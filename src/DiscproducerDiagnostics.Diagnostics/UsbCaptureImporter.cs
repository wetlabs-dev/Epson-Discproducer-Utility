using System.Buffers.Binary;
using System.Security.Cryptography;
using DiscproducerDiagnostics.Core;
namespace DiscproducerDiagnostics.Diagnostics;

// PCAPNG container fields follow section byte order. USBPcap pseudoheaders are always little-endian.
public static class UsbCaptureImporter
{
    public const int MaxFileSize=128*1024*1024, MaxPackets=100000;
    private record CaptureInterface(ushort LinkType,double SecondsPerUnit,long OffsetSeconds);
    public static CaptureReport Parse(byte[] file,string name,CancellationToken token=default)
    {
        if(file.Length>MaxFileSize)throw new InvalidDataException("Capture exceeds the 128 MiB import limit; filter/split it in Wireshark first");
        var packets=new List<CapturePacket>(); var warnings=new List<string>(); var interfaces=new List<CaptureInterface>();
        int offset=0,section=-1; bool little=true,complete=true;
        void Warn(string text){complete=false;if(warnings.Count<200)warnings.Add(text);}
        while(offset<file.Length)
        {
            token.ThrowIfCancellationRequested();
            if(file.Length-offset<12){Warn("Truncated block header");break;}
            var block=file.AsSpan(offset);
            bool isSection=BinaryPrimitives.ReadUInt32LittleEndian(block)==0x0a0d0d0a;
            if(isSection)
            {
                uint magic=BinaryPrimitives.ReadUInt32LittleEndian(block[8..]);
                if(magic is not (0x1a2b3c4d or 0x4d3c2b1a)){Warn("Invalid section byte-order magic");break;}
                little=magic==0x1a2b3c4d;
            }
            if(section<0&&!isSection){Warn("Missing PCAPNG section header (classic PCAP must be saved as PCAPNG)");break;}
            uint type=U32(block,0,little),length=U32(block,4,little);
            if(length<12||length%4!=0||length>32*1024*1024||length>block.Length){Warn("Invalid or truncated PCAPNG block length");break;}
            block=block[..(int)length]; offset+=(int)length;
            if(U32(block,block.Length-4,little)!=length){Warn("Block length trailer mismatch");break;}
            if(isSection)
            {
                if(length<28||U16(block,12,little)!=1){Warn("Unsupported or truncated section header");break;}
                section++;interfaces.Clear();continue;
            }
            if(type==1)
            {
                if(length<20){Warn("Truncated interface description");continue;}
                double scale=1e-6;long secondsOffset=0;
                for(int pos=16;pos+4<=block.Length-4;)
                {
                    ushort code=U16(block,pos,little),size=U16(block,pos+2,little);pos+=4;
                    if(code==0)break;
                    if(pos+size>block.Length-4){Warn("Truncated interface option");break;}
                    if(code==9&&size==1){byte value=block[pos];scale=(value&128)!=0?Math.Pow(2,-(value&127)):Math.Pow(10,-value);}
                    if(code==14&&size==8)secondsOffset=unchecked((long)U64(block,pos,little));
                    pos+=(size+3)&~3;
                }
                interfaces.Add(new(U16(block,8,little),scale,secondsOffset));continue;
            }
            if(type==6)
            {
                if(length<32){Warn("Truncated enhanced packet block");continue;}
                uint index=U32(block,8,little),captured=U32(block,20,little),original=U32(block,24,little);
                if(index>=interfaces.Count||captured>length-32||original<captured){Warn("Invalid packet interface or captured length");continue;}
                var info=interfaces[(int)index];
                if(info.LinkType!=249){Warn($"Unsupported link type {info.LinkType}; USBPcap link type 249 required");continue;}
                ulong stamp=((ulong)U32(block,12,little)<<32)|U32(block,16,little);DateTimeOffset? time=null;
                try{double ticks=stamp*info.SecondsPerUnit*TimeSpan.TicksPerSecond+info.OffsetSeconds*(double)TimeSpan.TicksPerSecond;time=DateTimeOffset.UnixEpoch.AddTicks(checked((long)Math.Round(ticks)));}catch(Exception e)when(e is ArgumentOutOfRangeException or OverflowException){Warn("Timestamp outside supported range");}
                try
                {
                    var packet=DecodePacket(block.Slice(28,(int)captured),packets.Count+1,section,index,time,checked((int)original));
                    packets.Add(packet);if(packet.Warning!=null)Warn($"Packet {packet.Number}: {packet.Warning}");
                }
                catch(Exception e)when(e is InvalidDataException or OverflowException){Warn(e.Message);}
                if(packets.Count>=MaxPackets){Warn("Packet limit reached; remaining blocks not parsed");break;}
            }
            else if(type is 2 or 3)Warn("Legacy/simple packet block skipped; export Enhanced Packet Blocks from Wireshark for timestamps and USB analysis");
        }
        if(section<0)Warn("No valid PCAPNG section");
        Correlate(packets,warnings);
        complete &= !packets.Any(p=>p.Correlation is "Request without completion" or "Completion without request" or "Ambiguous repeated URB ID" or "Intermediate without request" or "Unmatched");
        return new(Path.GetFileName(name),Convert.ToHexString(SHA256.HashData(file)),file.Length,packets.ToArray(),warnings.ToArray(),complete);
    }
    private static CapturePacket DecodePacket(ReadOnlySpan<byte> data,int number,int section,uint iface,DateTimeOffset? time,int original)
    {
        if(data.Length<27)throw new InvalidDataException("Truncated USBPcap pseudoheader");
        int header=U16(data,0,true); if(header<27||header>data.Length)throw new InvalidDataException("Invalid USBPcap header length");
        byte transfer=data[22];if(transfer==2&&header<28)throw new InvalidDataException("Missing USBPcap control-stage byte");
        uint declared=U32(data,23,true);int available=data.Length-header;int count=(int)Math.Min(declared,(uint)available);
        string? warning=declared>available||original>data.Length?"Truncated captured payload":null;
        byte[] payload=data.Slice(header,count).ToArray(),setup=[];byte? stage=transfer==2?data[27]:null;
        if(transfer>3 || stage is >3) warning="Unsupported transfer type or control stage; raw bytes retained";
        byte endpoint=data[21]; string direction=(endpoint&128)!=0?"IN":"OUT";
        if(stage==0)
        {
            if(payload.Length<8)warning="Truncated control setup packet";
            else{setup=payload[..8];payload=payload[8..];direction=(setup[0]&128)!=0?"IN":"OUT";}
        }
        return new(number,section,iface,time,U16(data,17,true),U16(data,19,true),endpoint,direction,
            transfer switch{0=>"Isochronous",1=>"Interrupt",2=>"Control",3=>"Bulk",_=>$"Unsupported ({transfer:X2})"},U64(data,2,true),U32(data,10,true),(data[16]&1)!=0,stage,setup,payload,original,warning);
    }
    private static void Correlate(List<CapturePacket> packets,List<string> warnings)
    {
        var pending=new Dictionary<string,int>();var ambiguous=new HashSet<string>();
        for(int i=0;i<packets.Count;i++)
        {
            var p=packets[i];string key=$"{p.Section}/{p.CaptureInterface}/{p.Bus}/{p.Address}/{(p.TransferType=="Control"?p.Endpoint&127:p.Endpoint)}/{p.TransferType}/{p.UrbId}";
            bool end=p.Stage is 2 or 3 || (p.Stage==null&&p.Completion);
            if(p.Stage==1)
            {
                if(!ambiguous.Contains(key)&&pending.TryGetValue(key,out int begin))packets[i]=p with{RelatedPacket=packets[begin].Number,Correlation="Intermediate control data stage",Direction=packets[begin].Direction};
                else packets[i]=p with{Correlation="Intermediate without request"};
                continue;
            }
            if(end)
            {
                if(ambiguous.Remove(key)){if(pending.Remove(key,out int ambiguousStart))packets[ambiguousStart]=packets[ambiguousStart] with{Correlation="Ambiguous repeated URB ID"}; packets[i]=p with{Correlation="Ambiguous repeated URB ID"};continue;}
                if(pending.Remove(key,out int start))
                {
                    var request=packets[start];double? duration=p.TimestampUtc>=request.TimestampUtc?(p.TimestampUtc-request.TimestampUtc)?.TotalMilliseconds:null;
                    packets[start]=request with{RelatedPacket=p.Number,Correlation="Matched host-controller completion",DurationMs=duration};
                    packets[i]=p with{RelatedPacket=request.Number,Correlation="Matched host-controller request",DurationMs=duration,Direction=p.TransferType=="Control"?request.Direction:p.Direction};
                }
                else packets[i]=p with{Correlation="Completion without request"};
            }
            else if(!p.Completion)
            {
                if(pending.ContainsKey(key)){ambiguous.Add(key);packets[pending[key]]=packets[pending[key]] with{Correlation="Ambiguous repeated URB ID"};}
                pending[key]=i; packets[i]=p with{Correlation="Request without completion"};
            }
        }
        if(pending.Count>0&&warnings.Count<200)warnings.Add($"{pending.Count} request(s) lack a completion record; no on-wire success assumed");
    }
    private static ushort U16(ReadOnlySpan<byte>b,int p,bool le)=>le?BinaryPrimitives.ReadUInt16LittleEndian(b[p..]):BinaryPrimitives.ReadUInt16BigEndian(b[p..]);
    private static uint U32(ReadOnlySpan<byte>b,int p,bool le)=>le?BinaryPrimitives.ReadUInt32LittleEndian(b[p..]):BinaryPrimitives.ReadUInt32BigEndian(b[p..]);
    private static ulong U64(ReadOnlySpan<byte>b,int p,bool le)=>le?BinaryPrimitives.ReadUInt64LittleEndian(b[p..]):BinaryPrimitives.ReadUInt64BigEndian(b[p..]);
}
