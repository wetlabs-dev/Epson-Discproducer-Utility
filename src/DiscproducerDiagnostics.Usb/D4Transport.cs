using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DiscproducerDiagnostics.Core;
using Microsoft.Win32.SafeHandles;

namespace DiscproducerDiagnostics.Usb;

// Internal only: no arbitrary-byte API is exposed by the worker, GUI or CLI.
internal interface ID4Stream : IAsyncDisposable
{
    ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token);
    ValueTask<int> ReadAsync(Memory<byte> bytes, CancellationToken token);
}

[SupportedOSPlatform("windows")]
internal sealed class UsbPrintStream : ID4Stream
{
    private readonly FileStream stream;
    private UsbPrintStream(SafeFileHandle handle)
    {
        try { stream = new FileStream(handle, FileAccess.ReadWrite, 1, isAsync: true); }
        catch { handle.Dispose(); throw; }
    }
    internal static UsbPrintStream OpenExclusive(string path)
    {
        var device = NativeDiscovery.Scan(false).Devices.SingleOrDefault(d =>
            d.InterfacePath != null && d.InterfacePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (device == null || device.Simulated || device.Vid != "04B8" || device.Pid != "0007")
            throw new NotSupportedException("Only a currently enumerated original PP-100 printer interface is eligible.");
        // Zero sharing: never downgrade to a shared handle on failure. OPEN_EXISTING,
        // FILE_FLAG_OVERLAPPED. No driver/configuration change and no print job.
        var handle = CreateFileW(device.InterfacePath!, 0xC0000000, 0, 0, 3, 0x40000000, 0);
        if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        return new(handle);
    }
    public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) => stream.WriteAsync(bytes, token);
    public ValueTask<int> ReadAsync(Memory<byte> bytes, CancellationToken token) => stream.ReadAsync(bytes, token);
    public ValueTask DisposeAsync() => stream.DisposeAsync();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
}


// Two fixed operations only; no caller-supplied command, key or address.
internal sealed class D4Session(ID4Stream stream, Action<D4Trace>? progress = null)
{
    private readonly List<D4Trace> trace = [];
    private readonly List<byte> pending = [];
    private int packets, receivedBytes, sendCredit, receiveCredit, commandCredit = 0;
    private bool opened, trusted = true, used;
    private byte[]? earlyData;
    private int maxReceive = 256;
    private string stage = "Start";
    private static byte[] Hex(string value) => Convert.FromHexString(value);

    internal Task<D4SessionResult> QualifyAsync(TimeSpan timeout, CancellationToken token) => ExchangeAsync(false, timeout, token);
    internal Task<D4SessionResult> ReadCandidateAsync(TimeSpan timeout, CancellationToken token) => ExchangeAsync(true, timeout, token);

    private async Task<D4SessionResult> ExchangeAsync(bool candidate, TimeSpan timeout, CancellationToken token)
    {
        string request = candidate ? "0202001100017C7C07000A5E41BEA03C00" : "0202000B00017374010001";
        string operation = candidate ? "Candidate EEPROM" : "Status";
        if (used) throw new InvalidOperationException("A D4 session cannot be reused.");
        used = true;
        byte[] response = []; string? error = null; string cleanup = "No channel opened"; bool complete = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (timeout < TimeSpan.FromMilliseconds(10) || timeout > TimeSpan.FromSeconds(30))
        { await stream.DisposeAsync(); throw new ArgumentOutOfRangeException(nameof(timeout)); }
        deadline.CancelAfter(timeout);
        var ct = deadline.Token;
        try
        {
            ct.ThrowIfCancellationRequested();
            stage = "Enter";
            await Write("0000001B0140454A4C20313238342E340A40454A4C0A40454A4C0A", ct);
            var entry = await Packet(ct);
            if (!entry.AsSpan().SequenceEqual(Hex("000000080100C500"))) throw new IOException("Unexpected packet-mode acknowledgement");
            trusted = true;
            await Command("Init", "0000000801000010", "800010", ct, init:true);
            await Command("Resolve", "000000110100094550534F4E2D4354524C", "8900024550534F4E2D4354524C", ct);
            var open = await Command("Open", "0000001101000102020200020000000000", null, ct);
            trusted = false;
            if (open.Length != 16 || !open.AsSpan(6,4).SequenceEqual(Hex("81000202"))) throw new IOException("Invalid channel-open reply");
            int maxSend = BinaryPrimitives.ReadUInt16BigEndian(open.AsSpan(10));
            maxReceive = BinaryPrimitives.ReadUInt16BigEndian(open.AsSpan(12));
            if (maxSend < request.Length / 2 || maxSend > 512 || maxReceive < 7 || maxReceive > 512) throw new IOException("Unsupported negotiated packet size");
            sendCredit = BinaryPrimitives.ReadUInt16BigEndian(open.AsSpan(14)); opened = true; trusted = true;
            for (int attempt=0; sendCredit==0 && attempt<3; attempt++)
            {
                var grant = await Command("Send credit", "0000000D01000402020080FFFF", null, ct);
                trusted = false;
                if (grant.Length!=12 || !grant.AsSpan(6,4).SequenceEqual(Hex("84000202"))) throw new IOException("Invalid send-credit reply");
                sendCredit = BinaryPrimitives.ReadUInt16BigEndian(grant.AsSpan(10)); trusted = true;
                if(sendCredit==0 && attempt<2) await Task.Delay(100,ct);
            }
            if(sendCredit==0) throw new IOException("No send credit; request not sent");
            stage=operation+" request"; sendCredit--;
            await Write(request,ct);
            // One receive packet is authorized at a time; do not resend the query.
            var body = new List<byte>();
            for(int fragment=0;fragment<8;fragment++)
            {
                receiveCredit++;
                await Command("Reply credit", "0000000B01000302020001", "83000202",ct,allowEarlyData:true);
                stage=operation+" reply";trusted=false;
                var data=earlyData;earlyData=null;
                if(data==null) { data=await Packet(ct);AcceptData(data); }
                body.AddRange(data[6..]);
                if(body.Count>4096)throw new IOException("Reply exceeds bounded message limit");
                if((data[5]&1)!=0) { response=body.ToArray();trusted=true;complete=true;break; }
                trusted=false;
            }
            if(!complete)throw new IOException("Reply has no message-end marker within fragment limit");
        }
        catch(Exception e) when(e is IOException or OperationCanceledException or Win32Exception)
        { error=e is OperationCanceledException ? (token.IsCancellationRequested?"Cancelled":"Deadline exceeded") : e.Message; }
        finally
        {
            if(opened)
            {
                cleanup="Channel state uncertain or cancelled; no additional protocol traffic sent";
                if(trusted && !ct.IsCancellationRequested && pending.Count==0 && earlyData==null)
                {
                    try {await Command("Close", "0000000A010002020200", "82000202",ct);cleanup="Close acknowledged; D4 Exit not sent (unverified)";}
                    catch(Exception e) when(e is IOException or OperationCanceledException or Win32Exception)
                    { cleanup="Close failed: "+e.Message; }
                }
            }
            try { await stream.DisposeAsync(); }
            catch(Exception e) when(e is IOException or Win32Exception) { cleanup += "; handle disposal failed: " + e.Message; }
        }
        return new(response,complete,error,cleanup,trace.ToArray());
    }

    private void Record(D4Trace item)
    {
        trace.Add(item);
        progress?.Invoke(item); // streamed to parent before the next native I/O
    }
    private async Task Write(string hex,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();trusted=false;
        var bytes=Hex(hex);Record(new(stage,"OUT attempted",hex,DateTimeOffset.UtcNow));
        await stream.WriteAsync(bytes,token);
        Record(new(stage,"OUT completed",hex,DateTimeOffset.UtcNow));
    }
    private async Task<byte[]> Command(string name,string request,string? expected,CancellationToken token,bool init=false,bool allowEarlyData=false)
    {
        stage=name;
        if(pending.Count!=0 || earlyData!=null) { trusted=false;throw new IOException("Unexpected buffered data before command; no further request sent"); }
        if(!init && commandCredit<=0)throw new IOException("No command-channel credit");
        if(!init)commandCredit--;
        await Write(request,token);
        byte[] reply;
        while(true)
        {
            reply=await Packet(token);
            if(reply[0]==0 && reply[1]==0)break;
            if(!allowEarlyData || earlyData!=null)throw new IOException("Unexpected data while awaiting command reply");
            AcceptData(reply);earlyData=reply;
        }
        if(reply[5]!=0 || reply.Length<8 || reply[6]!=(Hex(request)[6]|0x80) || reply[7]!=0)
            throw new IOException("Unexpected or rejected D4 command reply");
        if(expected!=null && !reply.AsSpan(6).SequenceEqual(Hex(expected)))throw new IOException("D4 reply fields mismatch");
        commandCredit=checked(commandCredit+reply[4]);
        if(commandCredit>16)throw new IOException("Excessive command credit");
        trusted=true;return reply;
    }
    private void AcceptData(byte[] packet)
    {
        trusted=false;
        if(packet[0]!=2 || packet[1]!=2 || (packet[5]&~1)!=0 || packet.Length>maxReceive || receiveCredit<=0)
            throw new IOException("Wrong socket, control, size or unauthorized data packet");
        receiveCredit--;sendCredit=checked(sendCredit+packet[4]);
    }
    private async Task<byte[]> Packet(CancellationToken token)
    {
        if(++packets>32)throw new IOException("Packet budget exceeded");
        while(true)
        {
            token.ThrowIfCancellationRequested();
            if(pending.Count>=6)
            {
                int length=(pending[2]<<8)|pending[3];
                if(length<6 || length>512)throw new IOException("Invalid D4 packet length");
                if(pending.Count>=length)
                {var result=pending.GetRange(0,length).ToArray();pending.RemoveRange(0,length);return result;}
            }
            byte[] bytes=new byte[512];int read=await stream.ReadAsync(bytes,token);
            if(read<=0 || read>bytes.Length)throw new IOException("Disconnected or invalid read count");
            Record(new(stage,"IN",Convert.ToHexString(bytes.AsSpan(0,read)),DateTimeOffset.UtcNow));
            receivedBytes+=read;if(receivedBytes>8192)throw new IOException("Receive budget exceeded");
            pending.AddRange(bytes.AsSpan(0,read).ToArray());
        }
    }
}
