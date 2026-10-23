namespace DiscproducerDiagnostics.Core;

public enum CommandSafety { Unverified, DocumentedReadOnly, DisabledForSafety }
public enum EvidenceValidation { UnsupportedUnverified, Documented, ObservedInPassiveCapture, ReproducedOnPhysicalDevice, ParsedAndVerified }
public enum DiagnosticErrorCategory { DeviceNotFound, InterfaceUnavailable, AccessDenied, UnsupportedTransport, DriverRejectedRequest, DeviceDisconnected, Timeout, Cancelled, InvalidResponse, MalformedDescriptor, SpoolerUnavailable, AmbiguousAssociation, UnexpectedNativeError }
public record DiagnosticError(DiagnosticErrorCategory Category, int? NativeCode, int? HResult, string Detail, string Recovery);
public sealed class DiagnosticFailureException(DiagnosticError failure) : IOException(failure.Detail) { public DiagnosticError Failure { get; } = failure; }
public static class ErrorClassifier
{
    public static DiagnosticError Classify(Exception error, string operation = "")
    {
        if(error is DiagnosticFailureException known)return known.Failure;
        int? code = error is System.ComponentModel.Win32Exception w ? w.NativeErrorCode : null;
        var category = error switch {
            OperationCanceledException => DiagnosticErrorCategory.Cancelled,
            TimeoutException => DiagnosticErrorCategory.Timeout,
            NotSupportedException => DiagnosticErrorCategory.UnsupportedTransport,
            FormatException => operation == "descriptors" ? DiagnosticErrorCategory.MalformedDescriptor : DiagnosticErrorCategory.InvalidResponse,
            _ => code switch { 2 => DiagnosticErrorCategory.DeviceNotFound, 5 => DiagnosticErrorCategory.AccessDenied,
                6 or 433 or 1167 => DiagnosticErrorCategory.DeviceDisconnected, 50 or 1 => DiagnosticErrorCategory.UnsupportedTransport,
                1460 => DiagnosticErrorCategory.Timeout, 1722 => DiagnosticErrorCategory.SpoolerUnavailable,
                31 or 87 => DiagnosticErrorCategory.DriverRejectedRequest, _ => DiagnosticErrorCategory.UnexpectedNativeError }
        };
        string recovery = category switch {
            DiagnosticErrorCategory.DeviceNotFound or DiagnosticErrorCategory.DeviceDisconnected => "Scan again and select the intended device; do not substitute another identical printer.",
            DiagnosticErrorCategory.AccessDenied => "Close conflicting diagnostic applications and verify access permissions. Keep Epson's installed driver.",
            DiagnosticErrorCategory.Timeout => "Completed observations are retained. Check printer/cable state before manually retrying with a bounded timeout.",
            DiagnosticErrorCategory.Cancelled => "Collection stopped; completed records remain available for export.",
            DiagnosticErrorCategory.UnsupportedTransport or DiagnosticErrorCategory.DriverRejectedRequest => "Keep the Epson driver. Export the failure and consider a passive Total Disc Monitor capture.",
            DiagnosticErrorCategory.SpoolerUnavailable => "Check the Windows spooler separately; USB metadata can still be inspected.",
            _ => "Preserve the original code and export diagnostics; do not infer a printer fault from this host failure."
        };
        return new(category, code, error.HResult, error.Message, recovery);
    }
}
public record PhysicalIdentity(string PhysicalInstanceId, string ContainerId, string Vid, string Pid, Device[] Devnodes,
    string[] PrinterInterfaces, string[] Drivers, string[] QueueAssociations, string LogicalConnection);
public static class DeviceTopology
{
    public static PhysicalIdentity Describe(Device selected, IEnumerable<Device> all, IEnumerable<PrinterDetail> printers, bool connected)
    {
        var nodes = all.Where(d => QueueCorrelation.SamePhysical(d, selected)).ToArray();
        string physical = new[] { selected.InstanceId }.Concat(selected.Ancestors).FirstOrDefault(id =>
            id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) && DeviceMatching.IsEpson(id) && !id.Contains("&MI_", StringComparison.OrdinalIgnoreCase)) ?? "Unresolved";
        return new(physical, selected.Properties.GetValueOrDefault("ContainerId", "Unavailable"), selected.Vid, selected.Pid,
            nodes, nodes.Select(d => d.InterfacePath).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            nodes.Select(d => $"{d.DriverProvider} {d.DriverVersion}").Distinct().ToArray(),
            printers.Select(p => $"{p.Queue.Name}: {p.Association}").ToArray(), connected ? "Host interface probe succeeded; no handle retained" : "Disconnected");
    }
}
public record ProtocolEvidence(string Reference, EvidenceValidation Validation, string Notes);
public record ResearchCommand(string Id, byte[] ExactBytes, string Transport, string[] Models, string ResponseExpectation,
    CommandSafety Safety, ProtocolEvidence[] Evidence);
// Analysis-only extensions cannot register native dispatch or authorize captured bytes for replay.
public interface ICommandFraming { byte[] DescribeRequest(ResearchCommand command); }
public interface IResponseFraming { IReadOnlyList<byte[]> SplitObservedResponse(ReadOnlyMemory<byte> observed); }
public interface IResponseParsing { Fact[] ParseObservedResponse(ReadOnlyMemory<byte> observed); }
public interface IModelCompatibility { bool AppliesTo(string model, string? verifiedFirmware); }
public interface IProtocolModule : ICommandFraming, IResponseFraming, IResponseParsing, IModelCompatibility
{
    string Name { get; }
    IReadOnlyList<ResearchCommand> Commands { get; }
}
public sealed class ProtocolModules
{
    public static ProtocolModules Default {get;} = new();
    private readonly List<IProtocolModule> modules = [];
    public IReadOnlyList<IProtocolModule> All => modules.AsReadOnly();
    public void Register(IProtocolModule module) { if (modules.Any(m => m.Name == module.Name)) throw new ArgumentException("Duplicate module"); modules.Add(module); }
}
public record CaptureAssociation(string CaptureSha256, int Section, uint CaptureInterface, ushort Bus, ushort Address,
    string PhysicalDeviceId, string Confidence, string[] Evidence, bool Manual);
public record ResearchAnnotation(string CaptureSha256, int Packet, string Observation, DateTimeOffset TimestampUtc);
public record CaptureReference(string Path, string Sha256, long Size);
public sealed record ResearchSession
{
    public int SchemaVersion { get; init; } = 1;
    public Guid SessionId { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string ApplicationVersion { get; init; } = "1.2.0";
    public string Name { get; init; } = "Research session";
    public string Model { get; init; } = "Unverified";
    public string Firmware { get; init; } = "Unavailable / unverified";
    public string Serial { get; init; } = "Unavailable / unverified";
    public string WindowsVersion { get; init; } = Environment.OSVersion.VersionString;
    public string DriverVersion { get; init; } = "Unavailable";
    public string Vid { get; init; } = "Unavailable";
    public string Pid { get; init; } = "Unavailable";
    public string Scenario { get; init; } = "Unknown condition";
    public string Notes { get; init; } = "";
    public bool Simulated { get; init; }
    public string HardwareVerification { get; init; } = "Unverified";
    public CaptureReference[] Captures { get; init; } = [];
    public CaptureAssociation[] Associations { get; init; } = [];
    public ResearchAnnotation[] Annotations { get; init; } = [];
}

public static class WorkerAuthorization
{
    public static void Authorize(WorkerRequest request)
    {
        switch(request.Operation)
        {
            case "query": CommandRegistry.Authorize(request.Command ?? ""); break;
            case "probe": break; // Path is revalidated by the native boundary before CreateFile.
            case "scan": DiagnosticCommands.Authorize("pnp"); break;
            case "descriptors": DiagnosticCommands.Authorize("descriptors"); break;
            case "printers": DiagnosticCommands.Authorize("spooler"); break;
            default: throw new NotSupportedException("Worker operation is not authorized");
        }
    }
}
