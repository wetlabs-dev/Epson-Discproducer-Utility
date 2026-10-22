using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using DiscproducerDiagnostics.Core;
namespace DiscproducerDiagnostics.Usb;
[SupportedOSPlatform("windows")]
internal static partial class PrinterQueues
{
    public static PrinterDetail[] Inspect()
    {
        var queues=Read(); var result=new List<PrinterDetail>();
        var monitors=PortMonitors();
        foreach(var q in queues)
        {
            var errors=new List<string>(); string version="Unavailable",provider="Unavailable",processor="Unavailable"; string? instance=null,container=null;
            uint attributes=0; PrinterJob[] jobs=[];
            if(!OpenPrinterW(q.Name,out nint handle,0)) errors.Add($"OpenPrinter: {Marshal.GetLastWin32Error()}");
            else try
            {
                byte[] data=new byte[16384];
                uint code=GetPrinterDataExW(handle,"PnPData","DeviceInstanceId",out uint type,data,(uint)data.Length,out uint size);
                if(code==0 && type is 1 or 2 && size<=data.Length) { instance=Encoding.Unicode.GetString(data,0,(int)size).TrimEnd('\0'); container=NativeDiscovery.ContainerForId(instance); }
                else errors.Add($"PnPData/DeviceInstanceId unavailable ({code}); queue association not assumed");
                GetPrinterW(handle,2,0,0,out uint needed);
                if(needed>0 && needed<4*1024*1024)
                {
                    nint buffer=Marshal.AllocHGlobal((int)needed);
                    try { if(GetPrinterW(handle,2,buffer,needed,out _)) {var p=Marshal.PtrToStructure<PrinterInfo>(buffer);processor=S(p.Processor);attributes=p.Attributes;} else errors.Add($"GetPrinter: {Marshal.GetLastWin32Error()}"); }
                    finally {Marshal.FreeHGlobal(buffer);}
                }
                GetPrinterDriverW(handle,null,6,0,0,out needed);
                if(needed>0 && needed<4*1024*1024)
                {
                    nint buffer=Marshal.AllocHGlobal((int)needed);
                    try
                    {
                        if(GetPrinterDriverW(handle,null,6,buffer,needed,out _))
                        {
                            var d=Marshal.PtrToStructure<DriverInfo6>(buffer); provider=S(d.Provider);
                            version=$"{d.DriverVersion>>48}.{(d.DriverVersion>>32)&65535}.{(d.DriverVersion>>16)&65535}.{d.DriverVersion&65535}";
                        }
                        else errors.Add($"GetPrinterDriver: {Marshal.GetLastWin32Error()}");
                    }
                    finally {Marshal.FreeHGlobal(buffer);}
                }
                else errors.Add($"Driver version/provider unavailable ({Marshal.GetLastWin32Error()})");
                try {jobs=Jobs(handle); } catch(Win32Exception e){errors.Add($"EnumJobs: {e.NativeErrorCode}");}
            }
            finally {ClosePrinter(handle);}
            result.Add(new(q with {InstanceId=instance},version,provider,monitors.GetValueOrDefault(q.Port,"Unavailable"),processor,(attributes&0x800)!=0,(attributes&8)!=0,(attributes&64)!=0,container,jobs,errors.ToArray()));
        }
        return result.ToArray();
    }
    private static PrinterJob[] Jobs(nint handle)
    {
        bool success=EnumJobsW(handle,0,5000,2,0,0,out uint size,out _);
        if(size==0) {if(!success&&Marshal.GetLastWin32Error()!=122)throw new Win32Exception();return [];}
        if(size>64*1024*1024)throw new Win32Exception(8,"Job metadata exceeds safety limit");
        nint buffer=Marshal.AllocHGlobal((int)size);
        try
        {
            if(!EnumJobsW(handle,0,5000,2,buffer,size,out _,out uint count))throw new Win32Exception();
            var jobs=new List<PrinterJob>();
            for(int i=0;i<count;i++)
            {
                var j=Marshal.PtrToStructure<JobInfo2>(buffer+i*Marshal.SizeOf<JobInfo2>()); DateTimeOffset? time=null;
                try{time=new DateTimeOffset(j.Submitted.Year,j.Submitted.Month,j.Submitted.Day,j.Submitted.Hour,j.Submitted.Minute,j.Submitted.Second,TimeSpan.Zero).AddMilliseconds(j.Submitted.Milliseconds);}catch(ArgumentOutOfRangeException){}
                jobs.Add(new(j.Id,S(j.StatusText),j.Status,time,S(j.Document),j.Size,j.TotalPages));
            }
            return jobs.ToArray();
        }
        finally{Marshal.FreeHGlobal(buffer);}
    }
    private static Dictionary<string,string> PortMonitors()
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        EnumPortsW(null,2,0,0,out uint size,out _); if(size==0||size>4*1024*1024)return result;
        nint buffer=Marshal.AllocHGlobal((int)size);
        try {if(EnumPortsW(null,2,buffer,size,out _,out uint count))for(int i=0;i<count;i++){var p=Marshal.PtrToStructure<PortInfo2>(buffer+i*Marshal.SizeOf<PortInfo2>());result[S(p.Name)]=S(p.Monitor);}}
        finally{Marshal.FreeHGlobal(buffer);} return result;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DriverInfo6 {public uint Version;public nint Name,Environment,DriverPath,DataFile,ConfigFile,HelpFile,DependentFiles,MonitorName,DefaultDataType,PreviousNames;public uint DateLow,DateHigh;public ulong DriverVersion;public nint Manufacturer,Url,HardwareId,Provider;}
    [StructLayout(LayoutKind.Sequential)] private struct SystemTime {public ushort Year,Month,DayOfWeek,Day,Hour,Minute,Second,Milliseconds;}
    [StructLayout(LayoutKind.Sequential)] private struct JobInfo2 {public uint Id;public nint Printer,Machine,User,Document,Notify,DataType,Processor,Parameters,Driver,DevMode,StatusText,Security;public uint Status,Priority,Position,StartTime,UntilTime,TotalPages,Size;public SystemTime Submitted;public uint Time,PagesPrinted;}
    [StructLayout(LayoutKind.Sequential)] private struct PortInfo2 {public nint Name,Monitor,Description;public uint Type,Reserved;}
    [DllImport("winspool.drv",SetLastError=true,CharSet=CharSet.Unicode)] private static extern bool OpenPrinterW(string name,out nint handle,nint defaults);
    [DllImport("winspool.drv",SetLastError=true)] private static extern bool ClosePrinter(nint handle);
    [DllImport("winspool.drv",CharSet=CharSet.Unicode)] private static extern uint GetPrinterDataExW(nint handle,string key,string value,out uint type,byte[] data,uint size,out uint needed);
    [DllImport("winspool.drv",SetLastError=true)] private static extern bool GetPrinterW(nint handle,uint level,nint data,uint size,out uint needed);
    [DllImport("winspool.drv",SetLastError=true,CharSet=CharSet.Unicode)] private static extern bool GetPrinterDriverW(nint handle,string? env,uint level,nint data,uint size,out uint needed);
    [DllImport("winspool.drv",SetLastError=true)] private static extern bool EnumJobsW(nint handle,uint first,uint count,uint level,nint data,uint size,out uint needed,out uint returned);
    [DllImport("winspool.drv",SetLastError=true,CharSet=CharSet.Unicode)] private static extern bool EnumPortsW(string? server,uint level,nint data,uint size,out uint needed,out uint returned);
}
