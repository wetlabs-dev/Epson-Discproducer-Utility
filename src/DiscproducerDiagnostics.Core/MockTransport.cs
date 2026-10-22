using System.Text;
namespace DiscproducerDiagnostics.Core;
public enum MockScenario { Ready, PrinterError, Timeout, Disconnected, Malformed, MissingSerial, Unsupported }
public sealed class MockTransport(MockScenario scenario) : IDeviceTransport
{
    public bool Connected { get; private set; }
    public static Device Device => new("Epson PP-100 mock", "SIMULATED\\VID_04B8&PID_FFFF\\DEMO", "mock://pp100", [], "04B8", "FFFF", [], [], "Epson (simulated)", "Mock", "1", "00 (simulated)", 0, true);
    public Task ConnectAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); Connected = true; return Task.CompletedTask; }
    public Task DisconnectAsync() { Connected = false; return Task.CompletedTask; }
    public async Task<Exchange> QueryAsync(string commandId, TimeSpan timeout, CancellationToken token)
    {
        CommandRegistry.Authorize(commandId);
        if (!Connected) throw new InvalidOperationException("Not connected");
        if (scenario == MockScenario.Timeout)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(timeout);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new(commandId, [], false, 1460, "Simulated timeout", timeout.TotalMilliseconds, true); }
        }
        token.ThrowIfCancellationRequested();
        if (scenario == MockScenario.Disconnected) { Connected = false; return new(commandId, [], false, 1167, "Simulated device removal", 0, true); }
        if (scenario == MockScenario.Unsupported) return new(commandId, [], false, 50, "Simulated unsupported query", 0, true);
        byte[] bytes;
        if (scenario == MockScenario.Malformed) bytes = [0, 50, 63];
        else if (commandId == "port-status") bytes = [scenario == MockScenario.PrinterError ? (byte)0x30 : (byte)0x18, 0, 0, 0];
        else
        {
            var text = Encoding.ASCII.GetBytes("MFG:EPSON;MDL:PP-100;CMD:SIMULATED;" + (scenario == MockScenario.MissingSerial ? "" : "SN:DEMO-ONLY-0001;"));
            bytes = new byte[text.Length + 2]; bytes[0] = (byte)(bytes.Length >> 8); bytes[1] = (byte)bytes.Length; text.CopyTo(bytes, 2);
        }
        return new(commandId, bytes, true, null, null, 1, true);
    }
    public ValueTask DisposeAsync() { Connected = false; return ValueTask.CompletedTask; }
}
