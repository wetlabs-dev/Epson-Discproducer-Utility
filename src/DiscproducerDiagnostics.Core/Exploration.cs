using System.Buffers.Binary;
namespace DiscproducerDiagnostics.Core;

public sealed record DiagnosticCommandDefinition(string Id, string Name, string Description, string Protocol,
    string RequiredTransport, string RequestFormat, string ExpectedResponse, string DocumentationReference,
    string Requirements, bool Supported = true, CommandSafety Safety = CommandSafety.DocumentedReadOnly)
{
    public bool IsReadOnly => Safety == CommandSafety.DocumentedReadOnly;
    public string Availability => Supported ? "Available but untested on physical hardware" : "Unsupported by current implementation";
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);
    public override string ToString() => Name;
}
public static class DiagnosticCommands
{
    private const string Ms = "https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/";
    public static IReadOnlyList<DiagnosticCommandDefinition> All { get; } = Array.AsReadOnly(new[] {
        new DiagnosticCommandDefinition("ieee1284", "GET_DEVICE_ID / IEEE 1284", "Read printer identification through the installed printer driver.", "USB Printer Class", "usbprint.sys", "IOCTL 0x220034; empty input. USB setup packet is driver-owned and unobserved.", "Length-prefixed IEEE 1284 fields", Ms+"usbprint/ni-usbprint-ioctl_usbprint_get_1284_id", "Accessible GUID_DEVINTERFACE_USBPRINT"),
        new DiagnosticCommandDefinition("port-status", "GET_PORT_STATUS", "Read the documented printer-class status bits.", "USB Printer Class", "usbprint.sys", "IOCTL 0x220030; empty input. No bulk request bytes.", "Four-byte output buffer; one-byte or DWORD reply; class bits in low byte", Ms+"usbprint/ni-usbprint-ioctl_usbprint_get_lpt_status", "Accessible GUID_DEVINTERFACE_USBPRINT"),
        new DiagnosticCommandDefinition("descriptors", "GET_DESCRIPTOR — device, configurations and strings", "Read descriptors through the parent USB hub, validating the port's driver key first.", "USB standard", "Windows USB hub", "IOCTL 0x220410; USB_DESCRIPTOR_REQUEST. Fixed GET_DESCRIPTOR; indexes taken only from returned descriptors.", "Device/configuration/string descriptors; all actual host requests retained", Ms+"usbioctl/ns-usbioctl-_usb_descriptor_request", "Verified parent hub + connection index + matching driver key"),
        new DiagnosticCommandDefinition("pnp", "Windows device properties", "Enumerate current PnP metadata and driver bindings.", "Windows metadata", "SetupAPI / Configuration Manager", "Windows API call; no observed USB bytes", "Device properties and topology", "https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetdevicepropertyw", "Present Epson device"),
        new DiagnosticCommandDefinition("spooler", "Windows printer queues and jobs", "Inspect Epson candidate queues and read-only job/driver information.", "Windows spooler", "Winspool", "EnumPrinters/GetPrinterDriver/EnumJobs/GetPrinterDataEx; no USB setup packet", "Queue, driver, jobs and association evidence", "https://learn.microsoft.com/en-us/windows/win32/printdocs/enumprinters", "Local spooler accessible"),
        new DiagnosticCommandDefinition("standard-status", "USB standard GET_STATUS — unsupported on current transport", "Standard GET_STATUS needs a separately supported control-transfer API. The hub descriptor API cannot issue it.", "USB standard", "No compatible transport implemented", "Not sent. No raw control-transfer access through Epson's printer handle.", "Unavailable", "https://www.usb.org/document-library/usb-20-specification", "Would require a documented compatible interface; no driver replacement", false)
    });
    public static DiagnosticCommandDefinition Authorize(string id)
    {
        var c = All.SingleOrDefault(c => c.Id == id);
        if (c == null || !c.Supported || c.Safety != CommandSafety.DocumentedReadOnly)
            throw new NotSupportedException("Command is unsupported or not authorized as documented read-only");
        return c;
    }
}
public static class DescriptorRequests
{
    public static byte[] Setup(byte type, byte index, ushort language, ushort length)
    {
        if (type is not (1 or 2 or 3) || length == 0 || (type == 1 && index != 0)) throw new ArgumentException("Only device/configuration/string GET_DESCRIPTOR is permitted");
        byte[] b = [0x80, 0x06, index, type, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), language);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6), length); return b;
    }
}
public record UsbEndpoint(byte Configuration, byte Interface, byte Alternate, byte Address, string Direction, string TransferType, ushort MaximumPacketSize, byte Interval, string RawHex);
public record UsbInterface(byte Configuration, byte Number, byte Alternate, byte Class, byte Subclass, byte Protocol, byte DeclaredEndpoints);
public record DescriptorBlob(string Kind, byte Index, byte[] Bytes);
public record DescriptorReport(DescriptorBlob[] Descriptors, Transaction[] Transactions, string[] Errors);
public record InspectionNode(string Label, InspectionNode[] Children);
public record PrinterJob(uint Id, string Status, uint RawStatus, DateTimeOffset? SubmittedUtc, string Document, uint Size, uint Pages);
public record PrinterDetail(QueueStatus Queue, string DriverVersion, string Provider, string Monitor, string Processor, bool BidirectionalConfigured,
    bool Shared, bool Local, string? ContainerId, PrinterJob[] Jobs, string[] Errors, string Association = "Uncorrelated", string[]? Evidence = null);
public record Transaction(Guid Id, DateTimeOffset TimestampUtc, string DeviceId, string Transport, string Interface, string Endpoint,
    string Command, byte[] Request, byte[] Response, byte[]? SetupPacket, double DurationMs, double TimeoutMs,
    string Outcome, int? NativeError, string? Error, string Interpretation, string Provenance, bool Simulated = false)
{
    public DiagnosticError? Failure => Successful ? null : ErrorClassifier.Classify(
        Outcome == "Cancelled" ? new OperationCanceledException(Error) : Outcome == "Timeout" ? new TimeoutException(Error) :
        Outcome is "InvalidResponse" or "ResponseWarning" ? new FormatException(Error ?? "Malformed response") :
        NativeError.HasValue ? new System.ComponentModel.Win32Exception(NativeError.Value, Error) : new IOException(Error ?? "Operation incomplete"),Command);
    public bool ApiIssued { get; init; } = true;
    public bool ValidResponse { get; init; }
    public bool Interpreted { get; init; }
    public bool? WireAcknowledged => null; // Neither host API success nor a capture URB proves a bus ACK.
    public int RequestLength => Request.Length;
    public int ResponseLength => Response.Length;
    public bool Successful => Outcome == "Success";
}
public enum ExportMode { RedactedSummary, TechnicalDiagnostics, FullForensic }
public record DiagnosticStep(string Operation, string Outcome, string Detail, DateTimeOffset TimestampUtc);
public record CapturePacket(int Number, int Section, uint CaptureInterface, DateTimeOffset? TimestampUtc,
    ushort Bus, ushort Address, byte Endpoint, string Direction, string TransferType, ulong UrbId, uint Status,
    bool Completion, byte? Stage, byte[] SetupPacket, byte[] Payload, int OriginalLength, string? Warning,
    int? RelatedPacket = null, string Correlation = "Unmatched")
{
    public double? DurationMs { get; init; }
    public ushort? UsbInterface => SetupPacket.Length == 8 && (SetupPacket[0] & 31) == 1 ? BinaryPrimitives.ReadUInt16LittleEndian(SetupPacket.AsSpan(4)) : null;
    public string Device => $"{Bus}:{Address}";
    public string EndpointText => $"0x{Endpoint:X2}";
    public string StatusText => $"0x{Status:X8}";
}
public record CaptureReport(string FileName, string Sha256, long FileSize, CapturePacket[] Packets, string[] Warnings, bool Complete) { public string ParserVersion { get; init; } = "Unspecified (legacy evidence)"; }
public record CaptureEnvironment(string Name, string Status, string? Path, string? Version);
public static class QueueCorrelation
{
    public static PrinterDetail Correlate(PrinterDetail p, Device selected, IReadOnlyList<Device> devices)
    {
        var evidence = new List<string>();
        var matches = devices.Where(d => DeviceMatching.QueueMatches(d,p.Queue) || (ValidContainer(p.ContainerId) && string.Equals(d.Properties.GetValueOrDefault("ContainerId"),p.ContainerId,StringComparison.OrdinalIgnoreCase))).ToArray();
        if (devices.Any(d => SamePhysical(d,selected) && DeviceMatching.QueueMatches(d,p.Queue))) evidence.Add("Exact spooler PnP instance ID matches selected device or its ancestry");
        string? container = selected.Properties.GetValueOrDefault("ContainerId");
        if (ValidContainer(container) && ValidContainer(p.ContainerId) && string.Equals(container,p.ContainerId,StringComparison.OrdinalIgnoreCase))
            evidence.Add("Exact non-empty PnP container ID match");
        bool ambiguous = matches.Any(d => !SamePhysical(d, selected));
        return p with { Association = evidence.Count == 0 ? "Uncorrelated" : ambiguous ? "Ambiguous" : "Associated by Windows evidence", Evidence = evidence.ToArray() };
    }
    public static bool SamePhysical(Device a, Device b) => a.InstanceId.Equals(b.InstanceId,StringComparison.OrdinalIgnoreCase) ||
        a.Ancestors.Contains(b.InstanceId,StringComparer.OrdinalIgnoreCase) || b.Ancestors.Contains(a.InstanceId,StringComparer.OrdinalIgnoreCase) ||
        a.Ancestors.Intersect(b.Ancestors,StringComparer.OrdinalIgnoreCase).Any(id => DeviceMatching.IsEpson(id) && !id.Contains("&MI_",StringComparison.OrdinalIgnoreCase));
    public static bool ValidContainer(string? s) => Guid.TryParse(s,out Guid id) && id != Guid.Empty && id != new Guid("00000000-0000-0000-ffff-ffffffffffff");
}
