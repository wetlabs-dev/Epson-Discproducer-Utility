using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using DiscproducerDiagnostics.Core;
using Microsoft.Win32.SafeHandles;
namespace DiscproducerDiagnostics.Usb;
[SupportedOSPlatform("windows")]
internal static class HubDescriptorReader
{
    public static DescriptorReport Read(string instanceId)
    {
        DiagnosticCommands.Authorize("descriptors");
        var blobs = new List<DescriptorBlob>(); var traces = new List<Transaction>(); var errors = new List<string>();
        try
        {
            var devices=NativeDiscovery.Scan(false).Devices;
            var selected=devices.FirstOrDefault(d=>d.InstanceId.Equals(instanceId,StringComparison.OrdinalIgnoreCase)) ?? throw new Win32Exception(1167,"Selected Epson node is absent");
            var physical=devices.FirstOrDefault(d=>d.InstanceId.StartsWith("USB\\",StringComparison.OrdinalIgnoreCase)&&!d.InstanceId.Contains("&MI_",StringComparison.OrdinalIgnoreCase)&&QueueCorrelation.SamePhysical(d,selected)) ?? throw new NotSupportedException("Physical USB parent not identified");
            if(!uint.TryParse(physical.Properties.GetValueOrDefault("BusPortAddress"),out uint port)||port==0) throw new NotSupportedException("USB port address unavailable from Windows PnP");
            string parent=physical.Ancestors.FirstOrDefault()??"";
            string hub=NativeDiscovery.Interfaces(new Guid("f18a0e88-c30c-11d0-8815-00a0c906bed8")).FirstOrDefault(h=>h.Id.Equals(parent,StringComparison.OrdinalIgnoreCase)).Path ?? throw new NotSupportedException("Parent has no accessible USB hub interface");
            string driverKey=physical.Properties.GetValueOrDefault("DriverKey","");
            using var handle=CreateFileW(hub,0,3,0,3,0,0);
            if(handle.IsInvalid) throw new Win32Exception();
            byte[] Call(uint ioctl, byte[] input, string command, byte[]? setup)
            {
                byte[] output=new byte[input.Length]; var stamp=DateTimeOffset.UtcNow; var sw=Stopwatch.StartNew();
                bool ok=DeviceIoControl(handle,ioctl,input,input.Length,output,output.Length,out int count,0);
                int? code=ok?null:Marshal.GetLastWin32Error();
                if(count<0||count>output.Length) { ok=false; code=13; count=0; }
                byte[] result=ok?output[..count]:[];
                traces.Add(new(Guid.NewGuid(),stamp,instanceId,"Windows USB hub DeviceIoControl",hub,setup==null?"Host metadata":"EP0 / device recipient",command,input,result,setup,sw.Elapsed.TotalMilliseconds,0,ok?"Success":"Failed",code,code.HasValue?new Win32Exception(code.Value).Message:null,"Host API reply; bus acknowledgement not observed",setup==null?"Windows hub metadata":"USB descriptor API; supplied setup, not captured wire bytes"));
                if(!ok) throw new Win32Exception(code??13);
                return result;
            }
            void VerifyPort()
            {
                byte[] input=new byte[4096]; BinaryPrimitives.WriteUInt32LittleEndian(input,port);
                byte[] key=Call(0x220420,input,"hub-driver-key",null);
                if(key.Length<10||driverKey.Length==0||driverKey=="Unavailable"||!Encoding.Unicode.GetString(key,8,key.Length-8).TrimEnd('\0').Equals(driverKey,StringComparison.OrdinalIgnoreCase)) throw new IOException("Hub port driver key does not match selected physical device; descriptor query blocked");
            }
            byte[] Descriptor(byte type,byte index,ushort language,ushort length,string kind)
            {
                VerifyPort();
                byte[] setup=DescriptorRequests.Setup(type,index,language,length), input=new byte[12+length];
                BinaryPrimitives.WriteUInt32LittleEndian(input,port); setup.CopyTo(input,4);
                byte[] response=Call(0x220410,input,"GET_DESCRIPTOR / "+kind,setup);
                if(response.Length<14) throw new IOException("Truncated descriptor reply");
                return response[12..];
            }
            byte[] device=Descriptor(1,0,0,18,"device");
            if(device.Length!=18||device[0]!=18||device[1]!=1) throw new IOException("Invalid USB device descriptor");
            if($"{BinaryPrimitives.ReadUInt16LittleEndian(device.AsSpan(8)):X4}"!=physical.Vid||$"{BinaryPrimitives.ReadUInt16LittleEndian(device.AsSpan(10)):X4}"!=physical.Pid) throw new IOException("Descriptor VID/PID conflicts with selected device; collection stopped");
            blobs.Add(new("Device",0,device));
            for(int i=0;i<device[17];i++)
            {
                try
                {
                    var header=Descriptor(2,(byte)i,0,9,$"configuration {i} header");
                    if(header.Length<9||header[1]!=2) throw new IOException("Invalid configuration header");
                    ushort total=BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
                    if(total<9) throw new IOException("Invalid configuration length");
                    blobs.Add(new("Configuration",(byte)i,Descriptor(2,(byte)i,0,total,$"configuration {i}")));
                }
                catch(Exception e) when(e is IOException or Win32Exception) { errors.Add($"Configuration {i}: {e.Message}"); }
            }
            try
            {
                byte[] languages=Descriptor(3,0,0,255,"language IDs"); blobs.Add(new("Language IDs",0,languages));
                if(languages.Length<4||languages[1]!=3||languages[0]>languages.Length||languages[0]%2!=0) throw new IOException("Malformed language descriptor");
                ushort language=BinaryPrimitives.ReadUInt16LittleEndian(languages.AsSpan(2));
                foreach(var (index,name) in new[]{(device[14],"Manufacturer string"),(device[15],"Product string"),(device[16],"Serial number")})
                {
                    if(index==0) continue;
                    try { var b=Descriptor(3,index,language,255,name); if(b[0]<2||b[0]>b.Length||b[1]!=3) throw new IOException("Invalid string descriptor"); blobs.Add(new(name,index,b[..b[0]])); }
                    catch(Exception e) when(e is IOException or Win32Exception) { errors.Add(name+": "+e.Message); }
                }
            }
            catch(Exception e) when(e is IOException or Win32Exception) { errors.Add("Strings: "+e.Message); }
        }
        catch(Exception e) { errors.Add(e is Win32Exception w ? $"Windows error {w.NativeErrorCode}: {w.Message}" : e.Message); }
        return new(blobs.ToArray(),traces.ToArray(),errors.ToArray());
    }
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,nint security,uint creation,uint flags,nint template);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool DeviceIoControl(SafeFileHandle handle,uint code,byte[] input,int inputLength,byte[] output,int outputLength,out int bytes,nint overlapped);
}
