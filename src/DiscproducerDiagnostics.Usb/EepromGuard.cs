using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
namespace DiscproducerDiagnostics.Usb;

// Read-only SCM queries. No service stop/start, driver changes or process termination.
internal sealed class EepromGuard(ID4Stream inner, Action check) : ID4Stream
{
    [SupportedOSPlatform("windows")]
    internal static void RequireStoppedServices()
    {
        nint manager = OpenSCManagerW(null, null, 1);
        if (manager == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            foreach (string name in new[] { "EPSON Total Disc Control Service", "EPSON Total Disc Copy Service" })
            {
                nint service = OpenServiceW(manager, name, 4); // SERVICE_QUERY_STATUS only
                if (service == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot verify stopped service: " + name);
                try
                {
                    if (!QueryServiceStatus(service, out var status)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (status.State != 1) throw new IOException("Stop " + name + " before the candidate read. No service is changed automatically.");
                }
                finally { CloseServiceHandle(service); }
            }
        }
        finally { CloseServiceHandle(manager); }
    }
    public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        check(); // Includes setup and cleanup; fail closed if monitoring restarts.
        return inner.WriteAsync(bytes, token);
    }
    public ValueTask<int> ReadAsync(Memory<byte> bytes, CancellationToken token) => inner.ReadAsync(bytes, token);
    public ValueTask DisposeAsync() => inner.DisposeAsync();
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus
    { public uint Type, State, Controls, Win32Exit, ServiceExit, Checkpoint, WaitHint; }
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern nint OpenSCManagerW(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern nint OpenServiceW(nint manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError=true)] private static extern bool QueryServiceStatus(nint service, out ServiceStatus status);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(nint handle);
}
