using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DiscproducerDiagnostics.Core;
namespace DiscproducerDiagnostics.Usb;
[SupportedOSPlatform("windows")]
internal static partial class PrinterQueues
{
    public static QueueStatus[] Read()
    {
        EnumPrintersW(2, null, 2, 0, 0, out uint needed, out _);
        if (needed == 0) { if (Marshal.GetLastWin32Error() is not (0 or 122)) throw new Win32Exception(); return []; }
        nint buffer = Marshal.AllocHGlobal(checked((int)needed));
        try
        {
            if (!EnumPrintersW(2, null, 2, buffer, needed, out _, out uint count)) throw new Win32Exception();
            var result = new List<QueueStatus>();
            for (int i = 0; i < count; i++)
            {
                var p = Marshal.PtrToStructure<PrinterInfo>(buffer + i * Marshal.SizeOf<PrinterInfo>());
                string name = S(p.Name), driver = S(p.Driver);
                // Candidate queues only. Names never establish physical-device association.
                if (!driver.Contains("Epson", StringComparison.OrdinalIgnoreCase) && !name.Contains("Epson", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(new(name, S(p.Port), driver, p.Status, p.Jobs, null));
            }
            return result.ToArray();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static string S(nint p) => Marshal.PtrToStringUni(p) ?? "Unavailable";
    [StructLayout(LayoutKind.Sequential)] private struct PrinterInfo
    {
        public nint Server, Name, Share, Port, Driver, Comment, Location, DevMode, Separator, Processor, DataType, Parameters, Security;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, Jobs, Average;
    }
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool EnumPrintersW(uint flags, string? name, uint level, nint buffer, uint size, out uint needed, out uint returned);
}
