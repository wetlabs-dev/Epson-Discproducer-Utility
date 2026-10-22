using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using DiscproducerDiagnostics.Core;
using System.Runtime.Versioning;
namespace DiscproducerDiagnostics.Usb;

[SupportedOSPlatform("windows")]
public static class NativeDiscovery
{
    private static readonly Guid PrinterInterface = new("28d78fad-5a12-11d1-ae5b-0000f803a8c2");
    public static Discovery Scan(bool includeQueues = true)
    {
        var warnings = new List<string>();
        var paths = Interfaces();
        var devices = new List<Device>();
        nint set = SetupDiGetClassDevsW(0, null, 0, 6); // PRESENT | ALLCLASSES
        if (set == -1) throw new Win32Exception();
        try
        {
            for (uint i = 0; ; i++)
            {
                var data = NewDeviceInfo();
                if (!SetupDiEnumDeviceInfo(set, i, ref data)) { if (Marshal.GetLastWin32Error() != 259) throw new Win32Exception(); break; }
                string id = InstanceId(set, ref data);
                var parents = Parents(data.DevInst);
                string? usbId = new[] { id }.Concat(parents).FirstOrDefault(DeviceMatching.IsEpson);
                if (usbId == null) continue;
                string prop(uint key) => Property(set, data, key);
                string driver = prop(9), provider = "Unavailable", version = "Unavailable";
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + driver, false);
                    provider = key?.GetValue("ProviderName") as string ?? provider;
                    version = key?.GetValue("DriverVersion") as string ?? version;
                }
                catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException) { warnings.Add("Driver registry metadata inaccessible"); }
                uint problem = 0;
                if (CM_Get_DevNode_Status(out _, out problem, data.DevInst, 0) != 0) problem = uint.MaxValue;
                string name = prop(12); if (name == "Unavailable") name = prop(0);
                string vid = Match(usbId, @"VID_([0-9A-F]{4})"), pid = Match(usbId, @"PID_([0-9A-F]{4})");
                string mi = Match(usbId, @"MI_([0-9A-F]{2})");
                var matching = paths.Where(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matching.Length == 0) matching = [(id, (string?)null)];
                foreach (var path in matching)
                    devices.Add(new(name, id, path.Path, parents, vid, pid, Split(prop(1)), Split(prop(2)), prop(11), provider, version, mi, problem));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        QueueStatus[] queues;
        try { queues = includeQueues ? PrinterQueues.Read() : []; }
        catch (Win32Exception e) { queues = []; warnings.Add($"Spooler enumeration failed ({e.NativeErrorCode}): {e.Message}"); }
        return new(devices.ToArray(), queues, warnings.ToArray());
    }
    private static string[] Split(string s) => s.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    private static string Match(string s, string pattern) { var m = Regex.Match(s, pattern, RegexOptions.IgnoreCase); return m.Success ? m.Groups[1].Value.ToUpperInvariant() : "Unavailable"; }
    private static DevInfo NewDeviceInfo() => new() { Size = Marshal.SizeOf<DevInfo>() };
    private static string InstanceId(nint set, ref DevInfo info)
    {
        var text = new StringBuilder(1024);
        return SetupDiGetDeviceInstanceIdW(set, ref info, text, text.Capacity, out _) ? text.ToString() : "Unavailable";
    }
    private static string Property(nint set, DevInfo info, uint property)
    {
        var buffer = new byte[16384];
        return SetupDiGetDeviceRegistryPropertyW(set, ref info, property, out _, buffer, buffer.Length, out uint size)
            ? Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0') : "Unavailable";
    }
    private static string[] Parents(uint node)
    {
        var ids = new List<string>();
        for (int i = 0; i < 16 && CM_Get_Parent(out uint parent, node, 0) == 0; i++)
        {
            var text = new StringBuilder(1024);
            if (CM_Get_Device_IDW(parent, text, text.Capacity, 0) != 0) break;
            ids.Add(text.ToString()); node = parent;
        }
        return ids.ToArray();
    }
    private static List<(string Id, string? Path)> Interfaces()
    {
        Guid guid = PrinterInterface;
        nint set = SetupDiGetClassDevsGuid(ref guid, null, 0, 0x12);
        if (set == -1) throw new Win32Exception();
        var result = new List<(string, string?)>();
        try
        {
            for (uint i = 0; ; i++)
            {
                var info = new InterfaceInfo { Size = Marshal.SizeOf<InterfaceInfo>() };
                if (!SetupDiEnumDeviceInterfaces(set, 0, ref guid, i, ref info)) { if (Marshal.GetLastWin32Error() != 259) throw new Win32Exception(); break; }
                SetupDiGetDeviceInterfaceDetailW(set, ref info, 0, 0, out uint needed, 0);
                if (needed < 8 || needed > 1048576) throw new InvalidOperationException("Invalid interface detail size");
                nint detail = Marshal.AllocHGlobal((int)needed);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    var data = NewDeviceInfo();
                    if (!SetupDiGetDeviceInterfaceDetailWithInfo(set, ref info, detail, needed, out _, ref data)) throw new Win32Exception();
                    result.Add((InstanceId(set, ref data), Marshal.PtrToStringUni(detail + 4)));
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DevInfo { public int Size; public Guid ClassGuid; public uint DevInst; public nuint Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct InterfaceInfo { public int Size; public Guid ClassGuid; public uint Flags; public nuint Reserved; }
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern nint SetupDiGetClassDevsW(nint guid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true, CharSet = CharSet.Unicode)] private static extern nint SetupDiGetClassDevsGuid(ref Guid guid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInfo(nint set, uint index, ref DevInfo info);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInstanceIdW(nint set, ref DevInfo info, StringBuilder id, int size, out uint needed);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiGetDeviceRegistryPropertyW(nint set, ref DevInfo info, uint property, out uint type, byte[] buffer, int size, out uint needed);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint device, ref Guid guid, uint index, ref InterfaceInfo info);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetailW(nint set, ref InterfaceInfo info, nint detail, uint size, out uint needed, nint device);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetailWithInfo(nint set, ref InterfaceInfo info, nint detail, uint size, out uint needed, ref DevInfo device);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_IDW(uint node, StringBuilder buffer, int size, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);
}
