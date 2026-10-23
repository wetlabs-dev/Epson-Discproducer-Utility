namespace DiscproducerDiagnostics.Core;

public record Fact(string Name, string Value, string Source, string Availability = "Available");
public record Device(string Name, string InstanceId, string? InterfacePath, string[] Ancestors,
    string Vid, string Pid, string[] HardwareIds, string[] CompatibleIds, string Manufacturer,
    string DriverProvider, string DriverVersion, string InterfaceNumber, uint ProblemCode, bool Simulated = false)
{
    public Dictionary<string,string> Properties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public override string ToString() => $"{(Simulated ? "[SIMULATED] " : "")}{Name} • {Vid}:{Pid} • {InterfaceNumber} • {InstanceId}";
}
public record QueueStatus(string Name, string Port, string Driver, uint RawStatus, uint Jobs, string? InstanceId);
public record Discovery(Device[] Devices, QueueStatus[] Queues, string[] Warnings);
public record StatusResult(string Source, DateTimeOffset Timestamp, string Raw, string Interpretation,
    string Availability, string? Error = null);
public record Exchange(string Command, byte[] Response, bool Success, int? ErrorCode, string? Error, double DurationMs, bool Simulated = false) { public bool ApiIssued { get; init; } }
public record LogEntry(DateTimeOffset Timestamp, string Transport, string Direction, string Command,
    string Hex, string Ascii, int Length, double DurationMs, bool Success, int? ErrorCode, string? Error, bool Simulated = false);
public record WorkerRequest(string Operation, string? Path = null, string? Command = null);
public record WorkerReply(Discovery? Discovery = null, Exchange? Exchange = null, string? Error = null, int? ErrorCode = null, DescriptorReport? Descriptors = null, PrinterDetail[]? Printers = null, Transaction? Progress = null, DiagnosticError? Failure = null);
public sealed record CommandDefinition(string Id, string Description, uint Ioctl, int OutputLength, string ResponseFormat, string Source)
{
    public string Transport => "Windows usbprint.sys DeviceIoControl";
    public CommandSafety Safety => CommandSafety.DocumentedReadOnly;
    public bool ReadOnly => Safety == CommandSafety.DocumentedReadOnly;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(5);
}
public static class CommandRegistry
{
    private static readonly Dictionary<string, CommandDefinition> Commands = new(StringComparer.Ordinal)
    {
        ["ieee1284"] = new("ieee1284", "Read IEEE 1284 identity", 0x220034, 4094, "Two-byte length prefix + IEEE 1284 fields", "https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/usbprint/ni-usbprint-ioctl_usbprint_get_1284_id"),
        ["port-status"] = new("port-status", "Read printer class status", 0x220030, 4, "DWORD; low byte has USB printer class status bits", "https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/usbprint/ni-usbprint-ioctl_usbprint_get_lpt_status")
    };
    public static CommandDefinition Authorize(string id) { DiagnosticCommands.Authorize(id); return Commands.TryGetValue(id, out var c) ? c : throw new NotSupportedException("Unknown native command rejected by the read-only registry."); }
}
public interface IDeviceTransport : IAsyncDisposable
{
    bool Connected { get; }
    Task ConnectAsync(CancellationToken token);
    Task DisconnectAsync();
    Task<Exchange> QueryAsync(string commandId, TimeSpan timeout, CancellationToken token);
}
public interface IPrinterProtocol
{
    Task<Fact[]> GetIdentityAsync(CancellationToken token);
    Task<StatusResult> GetStatusAsync(CancellationToken token);
}
public static class DeviceMatching
{
    public static bool IsEpson(string id) => System.Text.RegularExpressions.Regex.IsMatch(id, @"(?:^|[\\&])VID_04B8(?:&|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    public static bool QueueMatches(Device device, QueueStatus queue) => queue.InstanceId is { Length: > 0 } id &&
        (string.Equals(device.InstanceId, id, StringComparison.OrdinalIgnoreCase) || device.Ancestors.Contains(id, StringComparer.OrdinalIgnoreCase));
}
