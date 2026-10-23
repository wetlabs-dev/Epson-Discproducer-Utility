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
    public static WorkerReply Execute(WorkerRequest request, Action<Transaction>? progress = null)
    {
        try
        {
            WorkerAuthorization.Authorize(request);
            if (request.Operation == "scan") return new(Discovery: NativeDiscovery.Scan(false));
            if (request.Operation == "descriptors") return new(Descriptors: HubDescriptorReader.Read(request.Path ?? "", progress));
            if (request.Operation == "printers") { DiagnosticCommands.Authorize("spooler"); return new(Printers: PrinterQueues.Inspect()); }
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
                return new(Exchange: new(command.Id, [], false, code, new Win32Exception(code).Message, watch.Elapsed.TotalMilliseconds){ApiIssued=true});
            }
            if (read < 0 || read > output.Length) throw new IOException("Driver returned an invalid byte count");
            return new(Exchange: new(command.Id, output[..read], true, null, null, watch.Elapsed.TotalMilliseconds){ApiIssued=true});
        }
        catch (Exception ex) { return new(Error: ex.Message, ErrorCode: ex is Win32Exception w ? w.NativeErrorCode : null, Failure: ErrorClassifier.Classify(ex,request.Operation)); }
    }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(SafeFileHandle file, uint ioctl, nint input, int inputLength, byte[] output, int outputLength, out int returned, nint overlapped);
}
public sealed class WorkerClient(string executable, IReadOnlyList<string>? hostArguments = null)
{
    private static readonly SemaphoreSlim Operations = new(1, 1); // Conservative application-wide serialization also covers sibling interfaces.
    public async Task<WorkerReply> RunAsync(WorkerRequest request, TimeSpan timeout, CancellationToken token, IProgress<Transaction>? progress = null)
    {
        if (timeout < TimeSpan.FromMilliseconds(10) || timeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(timeout);
        bool acquired = false;
        try
        {
            await Operations.WaitAsync(deadline.Token).ConfigureAwait(false); acquired = true;
            return await RunCoreAsync(request, timeout, deadline.Token, progress).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Diagnostic deadline exceeded, including queue time; no automatic retry."); }
        finally { if (acquired) Operations.Release(); }
    }
    private async Task<WorkerReply> RunCoreAsync(WorkerRequest request, TimeSpan timeout, CancellationToken token, IProgress<Transaction>? progress)
    {
        if (timeout < TimeSpan.FromMilliseconds(10) || timeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(timeout));
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (hostArguments != null) foreach (string argument in hostArguments) info.ArgumentList.Add(argument);
        info.ArgumentList.Add("--worker");
        using var process = new Process { StartInfo = info };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        Task<WorkerReply>? output = null; Task<string>? error = null;
        try
        {
            token.ThrowIfCancellationRequested();
            process.Start();
            // Cancellation cleanup must not depend on the WPF dispatcher remaining alive.
            using var stopOnCancellation = deadline.Token.Register(() => Stop(process));
            output = ReadFrames(process.StandardOutput, deadline.Token, progress);
            error = ReadBounded(process.StandardError, 65536, deadline.Token);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), deadline.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            var exited = process.WaitForExitAsync(deadline.Token);
            var first = await Task.WhenAny(exited, output, error).ConfigureAwait(false);
            if (first.IsFaulted) await first.ConfigureAwait(false);
            await exited.ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            string stderr = await error.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException($"Diagnostic worker exited with code {process.ExitCode}: {stderr}");
            return await output.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Diagnostic operation exceeded its time limit; worker stopped. No retry was sent."); }
        finally
        {
            deadline.Cancel(); Stop(process);
            // Observe reader failures even on crash/cancellation; they must not escape as abandoned tasks.
            if (output != null) try { await output.ConfigureAwait(false); } catch (Exception) { }
            if (error != null) try { await error.ConfigureAwait(false); } catch (Exception) { }
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader, int limit, CancellationToken token)
    {
        var text = new System.Text.StringBuilder(); char[] buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        { if (text.Length + count > limit) throw new IOException("Worker output exceeds limit"); text.Append(buffer, 0, count); }
        return text.ToString();
    }
    private static async Task<WorkerReply> ReadFrames(StreamReader reader, CancellationToken token, IProgress<Transaction>? progress)
    {
        var line = new System.Text.StringBuilder(); char[] buffer = new char[4096]; int total = 0, frames = 0, count; WorkerReply? final = null;
        void ParseLine()
        {
            if (line.Length == 0) return;
            if (++frames > 4096) throw new IOException("Worker frame limit exceeded");
            var reply = JsonSerializer.Deserialize<WorkerReply>(line.ToString()) ?? throw new IOException("Empty worker frame");
            line.Clear();
            if (final != null) throw new IOException("Worker emitted data after final reply");
            if (reply.Progress != null) progress?.Report(reply.Progress); else final = reply;
        }
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            total += count; if (total > 32 * 1024 * 1024) throw new IOException("Worker stream exceeds 32 MiB limit");
            for (int i = 0; i < count; i++) { if (buffer[i] == '\n') ParseLine(); else { line.Append(buffer[i]); if (line.Length > 16 * 1024 * 1024) throw new IOException("Worker frame too large"); } }
        }
        ParseLine(); return final ?? throw new IOException("Worker ended without final reply");
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
        if (r.Error != null) throw r.Failure != null ? new DiagnosticFailureException(r.Failure) : r.ErrorCode.HasValue ? new Win32Exception(r.ErrorCode.Value, r.Error) : new IOException(r.Error);
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
