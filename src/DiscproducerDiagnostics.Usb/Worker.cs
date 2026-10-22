using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using DiscproducerDiagnostics.Core;
namespace DiscproducerDiagnostics.Usb;

// Native calls run in a disposable child process. A wedged driver/spooler cannot block the UI.
[SupportedOSPlatform("windows")]
public static class NativeWorker
{
    public static WorkerReply Execute(WorkerRequest request)
    {
        try
        {
            if (request.Operation == "scan") return new(Discovery: NativeDiscovery.Scan());
            if (request.Operation is not ("probe" or "query")) throw new NotSupportedException("Unknown worker operation");
            // Re-enumerate to prevent stale paths or arbitrary CreateFile targets from being used.
            var device = NativeDiscovery.Scan(includeQueues: false).Devices.FirstOrDefault(d => d.InterfacePath != null && d.InterfacePath.Equals(request.Path, StringComparison.OrdinalIgnoreCase));
            if (device == null) throw new Win32Exception(1167, "Selected Epson USB printer interface is no longer present");
            CommandDefinition? command = request.Operation == "query" ? CommandRegistry.Authorize(request.Command ?? "") : null;
            using var handle = CreateFileW(device.InterfacePath!, 0x80000000, 3, 0, 3, 0, 0); // GENERIC_READ, shared R/W, OPEN_EXISTING
            if (handle.IsInvalid) throw new Win32Exception();
            if (command == null) return new();
            var watch = Stopwatch.StartNew();
            byte[] output = new byte[command.OutputLength];
            if (!DeviceIoControl(handle, command.Ioctl, 0, 0, output, output.Length, out int read, 0))
            {
                int code = Marshal.GetLastWin32Error();
                return new(Exchange: new(command.Id, [], false, code, new Win32Exception(code).Message, watch.Elapsed.TotalMilliseconds));
            }
            if (read < 0 || read > output.Length) throw new IOException("Driver returned an invalid byte count");
            return new(Exchange: new(command.Id, output[..read], true, null, null, watch.Elapsed.TotalMilliseconds));
        }
        catch (Exception ex) { return new(Error: ex.Message, ErrorCode: ex is Win32Exception w ? w.NativeErrorCode : null); }
    }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(SafeFileHandle file, uint ioctl, nint input, int inputLength, byte[] output, int outputLength, out int returned, nint overlapped);
}
public sealed class WorkerClient(string executable, IReadOnlyList<string>? hostArguments = null)
{
    public async Task<WorkerReply> RunAsync(WorkerRequest request, TimeSpan timeout, CancellationToken token)
    {
        if (timeout < TimeSpan.FromMilliseconds(10) || timeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(timeout));
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (hostArguments != null) foreach (string argument in hostArguments) info.ArgumentList.Add(argument);
        info.ArgumentList.Add("--worker");
        using var process = new Process { StartInfo = info };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        try
        {
            token.ThrowIfCancellationRequested();
            process.Start();
            // Cancellation cleanup must not depend on the WPF dispatcher remaining alive.
            using var stopOnCancellation = deadline.Token.Register(() => Stop(process));
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), deadline.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new IOException($"Diagnostic worker exited with code {process.ExitCode}: {await error.ConfigureAwait(false)}");
            return JsonSerializer.Deserialize<WorkerReply>(await output.ConfigureAwait(false)) ?? throw new IOException("Empty worker reply");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Diagnostic operation exceeded its time limit; worker stopped. No retry was sent."); }
        finally
        {
            Stop(process);
        }
    }
    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { } // Already exited or OS teardown raced with cancellation.
    }
}
public sealed class UsbPrinterClassTransport(WorkerClient worker, string path) : IDeviceTransport
{
    public bool Connected { get; private set; }
    public async Task ConnectAsync(CancellationToken token)
    {
        var r = await worker.RunAsync(new("probe", path), TimeSpan.FromSeconds(15), token);
        if (r.Error != null) throw new IOException($"{r.Error} (Windows code {r.ErrorCode})");
        Connected = true;
    }
    public Task DisconnectAsync() { Connected = false; return Task.CompletedTask; }
    public async Task<Exchange> QueryAsync(string commandId, TimeSpan timeout, CancellationToken token)
    {
        CommandRegistry.Authorize(commandId);
        if (!Connected) throw new InvalidOperationException("Not connected");
        var watch = Stopwatch.StartNew();
        try
        {
            var r = await worker.RunAsync(new("query", path, commandId), timeout, token);
            var exchange = r.Exchange ?? new(commandId, [], false, r.ErrorCode, r.Error ?? "No response", watch.Elapsed.TotalMilliseconds);
            if (exchange.ErrorCode is 2 or 6 or 31 or 433 or 1167) Connected = false;
            return exchange;
        }
        catch (TimeoutException e) { return new(commandId, [], false, 1460, e.Message, watch.Elapsed.TotalMilliseconds); }
        catch (IOException e) { Connected = false; return new(commandId, [], false, null, e.Message, watch.Elapsed.TotalMilliseconds); }
    }
    public ValueTask DisposeAsync() { Connected = false; return ValueTask.CompletedTask; }
}
