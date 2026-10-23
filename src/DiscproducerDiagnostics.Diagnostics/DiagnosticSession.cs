using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiscproducerDiagnostics.Core;
using Serilog;
using System.Diagnostics;
using DiscproducerDiagnostics.Protocols;
using Serilog.Core;
using Serilog.Events;
namespace DiscproducerDiagnostics.Diagnostics;

public sealed class DiagnosticSession : ILogEventSink, IDisposable
{
    private readonly List<string> applicationLog = [];
    private bool disposed;
    public List<Transaction> Transactions { get; } = [];
    public event Action<Transaction>? TransactionAdded;
    public void RecordTransaction(Transaction transaction)
    {
        if (Transactions.Any(t => t.Id == transaction.Id)) return;
        Transactions.Add(transaction);
        Logger.Information("Transaction {Id} {Command} {Outcome} code={Code}", transaction.Id,transaction.Command,transaction.Outcome,transaction.NativeError);
        TransactionAdded?.Invoke(transaction);
    }
    public string ApplicationLog => string.Join(Environment.NewLine,applicationLog);
    public List<LogEntry> Communication { get; } = [];
    public event Action<LogEntry>? EntryAdded;
    public ILogger Logger { get; }
    public DiagnosticSession() => Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(this).CreateLogger();
    public void Emit(LogEvent logEvent) => applicationLog.Add(logEvent.Timestamp.ToString("O") + " " + logEvent.Level + " " + logEvent.RenderMessage());
    public void Record(LogEntry entry)
    {
        Communication.Add(entry);
        Logger.Information("{Transport} {Direction} {Command} success={Success} bytes={Length} durationMs={Duration} code={Code} error={Error}", entry.Transport, entry.Direction, entry.Command, entry.Success, entry.Length, entry.DurationMs, entry.ErrorCode, entry.Error);
        EntryAdded?.Invoke(entry);
    }
    public void Event(string command, string message, bool success = true) => Record(new(DateTimeOffset.UtcNow, "Application", "EVENT", command, "", "", 0, 0, success, null, message));
    public void Export(string file, Device? device, Fact[] facts, StatusResult[] statuses, bool redact)
    {
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        void Json(string name, object value)
        {
            JsonNode? node = JsonSerializer.SerializeToNode(value);
            if (redact) Redaction.Apply(node);
            using var stream = new StreamWriter(zip.CreateEntry("diagnostics/" + name).Open(), Encoding.UTF8);
            stream.Write(node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        Json("device-info.json", new { Device = device, Facts = facts, Simulated = device?.Simulated ?? false });
        Json("usb-descriptors.json", new { Availability = "Unavailable", Reason = "Raw USB descriptors are not retrieved through the installed printer interface. PnP hardware/compatible IDs are in device-info.json.", Descriptors = Array.Empty<object>() });
        Json("printer-status.json", statuses);
        Json("communication-log.json", Communication);
        Json("system-info.json", new { OS = Environment.OSVersion.VersionString, Runtime = Environment.Version.ToString(), Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), AppVersion = "1.0.0", ExportedAt = DateTimeOffset.UtcNow, Redacted = redact });
        using var log = new StreamWriter(zip.CreateEntry("diagnostics/application.log").Open(), Encoding.UTF8);
        log.Write(redact ? "Redacted export: free-form application messages omitted to prevent identifier leakage. See structured communication-log.json for timings, command IDs, lengths and result codes." : string.Join(Environment.NewLine, applicationLog));
    }
    public void Dispose()
    {
        // Serilog also disposes its sinks; guard the owner/sink re-entry.
        if (disposed) return;
        disposed = true;
        (Logger as IDisposable)?.Dispose();
    }
}
public static class Redaction
{
    // Conservative: omit complete payloads and free text. Never depend on finding every serial alias in raw data.
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
    { "InstanceId", "InterfacePath", "Ancestors", "HardwareIds", "CompatibleIds", "Hex", "Ascii", "Raw", "Response", "Error", "Name", "Value", "Port", "Interpretation", "Source", "Availability" };
    public static void Apply(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (string key in obj.Select(k => k.Key).ToArray())
            {
                if (Sensitive.Contains(key)) obj[key] = "[REDACTED]";
                else Apply(obj[key]);
            }
        else if (node is JsonArray array) foreach (var child in array) Apply(child);
    }
}
public sealed class LoggedTransport(IDeviceTransport inner, DiagnosticSession session, Device? device = null) : IDeviceTransport
{
    public bool Connected => inner.Connected;
    public async Task ConnectAsync(CancellationToken token)
    {
        var watch=Stopwatch.StartNew();var stamp=DateTimeOffset.UtcNow;
        try
        {
            await inner.ConnectAsync(token);
            session.RecordTransaction(new(Guid.NewGuid(),stamp,device?.InstanceId??"Unavailable","Windows printer interface probe",device?.InterfacePath??"Unavailable","Host API","interface-probe",[],[],null,watch.Elapsed.TotalMilliseconds,15000,"Success",null,null,"Interface handle opened; no identification or status response yet","Host handle probe",inner is MockTransport));
            session.Event("connect", "Interface opened successfully; request/response not yet verified.");
        }
        catch(Exception e)
        {
            session.RecordTransaction(new(Guid.NewGuid(),stamp,device?.InstanceId??"Unavailable","Windows printer interface probe",device?.InterfacePath??"Unavailable","Host API","interface-probe",[],[],null,watch.Elapsed.TotalMilliseconds,15000,e is OperationCanceledException?"Cancelled":e is TimeoutException?"Timeout":"Failed",e is System.ComponentModel.Win32Exception w?w.NativeErrorCode:null,e.Message,"Interface access not established","Host handle probe",inner is MockTransport));throw;
        }
    }
    public async Task DisconnectAsync() { await inner.DisconnectAsync(); session.Event("disconnect", "Disconnected"); }
    public async Task<Exchange> QueryAsync(string commandId, TimeSpan timeout, CancellationToken token)
    {
        var command = CommandRegistry.Authorize(commandId);
        var watch=Stopwatch.StartNew(); var timestamp=DateTimeOffset.UtcNow; var transactionId=Guid.NewGuid();
        // IOCTL is host API metadata, not invented USB setup bytes. Input payload is empty.
        session.Record(new(DateTimeOffset.UtcNow, command.Transport, "TX", $"{commandId} / IOCTL 0x{command.Ioctl:X8}; empty input", "", "", 0, 0, true, null, null, inner is MockTransport));
        try
        {
            var r = await inner.QueryAsync(commandId, timeout, token);
            string ascii = new(r.Response.Select(b => b >= 32 && b <= 126 ? (char)b : '.').ToArray());
            session.Record(new(DateTimeOffset.UtcNow, command.Transport, "RX", commandId, Convert.ToHexString(r.Response), ascii, r.Response.Length, r.DurationMs, r.Success, r.ErrorCode, r.Error, r.Simulated));
            var interpreted=ResponseInterpreter.Interpret(commandId,r.Response);
            session.RecordTransaction(new(transactionId,timestamp,device?.InstanceId??"Unavailable",command.Transport,device?.InterfacePath??"Unavailable","Driver-owned; EP0 unobserved",commandId,[],r.Response,null,watch.Elapsed.TotalMilliseconds,timeout.TotalMilliseconds,r.Success?(interpreted.Valid?"Success":interpreted.Partial?"ResponseWarning":"InvalidResponse"):r.ErrorCode==1460?"Timeout":"Failed",r.ErrorCode,r.Error??(r.Success&&!interpreted.Valid?interpreted.Text:null),interpreted.Text,r.Simulated?"SIMULATED":"Windows printer IOCTL; USB wire request unobserved",r.Simulated){ApiIssued=r.ApiIssued,ValidResponse=r.Success&&interpreted.Valid,Interpreted=r.Success&&(interpreted.Valid||interpreted.Partial)});
            return r;
        }
        catch (Exception e)
        {
            session.RecordTransaction(new(transactionId,timestamp,device?.InstanceId??"Unavailable",command.Transport,device?.InterfacePath??"Unavailable","Driver-owned",commandId,[],[],null,watch.Elapsed.TotalMilliseconds,timeout.TotalMilliseconds,e is OperationCanceledException?"Cancelled":"Failed",null,e.Message,"No response","Host API invocation; request issuance unknown, no wire evidence",inner is MockTransport){ApiIssued=false});
            session.Record(new(DateTimeOffset.UtcNow, command.Transport, "RX", commandId, "", "", 0, 0, false, null, e is OperationCanceledException ? "Cancelled" : e.Message, inner is MockTransport));
            throw;
        }
    }
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
