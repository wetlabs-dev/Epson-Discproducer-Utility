using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DiscproducerDiagnostics.Core;
using DiscproducerDiagnostics.Protocols;
namespace DiscproducerDiagnostics.Diagnostics;
public record BundleSnapshot(Device? Device, Fact[] Facts,StatusResult[] Statuses,DescriptorReport? Descriptors,
    PrinterDetail[] Printers,Transaction[] Transactions,CaptureReport? Capture,byte[]? OriginalCapture,DiagnosticStep[] Steps,string ApplicationLog)
{
    public Guid SessionId { get; init; } = Guid.NewGuid();
    public ResearchSession? Research { get; init; }
}
public static class BundleExporter
{
    public const string Version="1.3.1";
    public static void Export(string file,BundleSnapshot data,ExportMode mode,bool forensicConfirmed)
    {
        if(mode==ExportMode.FullForensic&&!forensicConfirmed)throw new InvalidOperationException("Full forensic export requires explicit confirmation");
        bool full=mode==ExportMode.FullForensic;
        using var zip=ZipFile.Open(file,ZipArchiveMode.Create);
        void Text(string path,string text){using var writer=new StreamWriter(zip.CreateEntry(path).Open(),Encoding.UTF8);writer.Write(text);}
        void Json(string path,object? value)=>Text(path,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
        var hashes=new Dictionary<string,string>();
        void Binary(string path,byte[] bytes){hashes[path]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));using var stream=zip.CreateEntry(path).Open();stream.Write(bytes);}
        var decoded=DescriptorDecoder.Decode(data.Descriptors?.Descriptors??[]);
        var safeTransactions=data.Transactions.Select(t=>new{t.Id,t.TimestampUtc,t.Command,t.Outcome,t.DurationMs,t.TimeoutMs,t.NativeError,t.RequestLength,t.ResponseLength,t.Simulated,t.ApiIssued,t.ValidResponse,t.Interpreted,t.WireAcknowledged,ErrorCategory=t.Failure?.Category.ToString(),HResult=t.Failure?.HResult}).ToArray();
        var safeSteps=data.Steps.Select(s=>new{s.Operation,s.Outcome,s.TimestampUtc}).ToArray();
        Json("manifest.json",new{ApplicationVersion=Version,ExportSchemaVersion=3,SessionId=data.SessionId,HardwareVerification="Unverified",CaptureSha256=data.Capture?.Sha256,RawResponsesAvailable=full,Provenance="Windows metadata / driver requests / imported host capture; see individual records",OS=Environment.OSVersion.VersionString,ExportTimestampUtc=DateTimeOffset.UtcNow,Mode=mode.ToString(),Device=full?data.Device?.InstanceId:null,VID=data.Device?.Vid,PID=data.Device?.Pid,Simulation=data.Device?.Simulated??false,SelectedTransport=data.Transactions.LastOrDefault(t=>t.DeviceId==data.Device?.InstanceId)?.Transport??"Unavailable",CollectionCompleteness=safeSteps,FailedOperations=safeTransactions.Where(t=>t.Outcome!="Success"),CaptureIncluded=full&&data.OriginalCapture!=null,WireAcknowledgement="Not established by Windows API results or USBPcap URB records"});
        Json("device/identity.json",full?data.Facts:new{Availability="Sensitive identity values omitted",data.Device?.Vid,data.Device?.Pid});
        Json("device/windows-device-properties.json",full?data.Device:new{data.Device?.Vid,data.Device?.Pid,data.Device?.ProblemCode,Identifiers="Omitted"});
        Json("device/usb-descriptors.json",full?data.Descriptors:new{Count=data.Descriptors?.Descriptors.Length??0,Payloads="Omitted",FailedReads=data.Descriptors?.Errors.Length??0});
        Json("device/interfaces.json",mode==ExportMode.RedactedSummary?new{Count=decoded.Interfaces.Length}:decoded.Interfaces);
        Json("device/endpoints.json",full?decoded.Endpoints:mode==ExportMode.RedactedSummary?new{Count=decoded.Endpoints.Length}:decoded.Endpoints.Select(e=>new{e.Configuration,e.Interface,e.Alternate,e.Address,e.Direction,e.TransferType,e.MaximumPacketSize,e.Interval}));
        Json("printer/queue.json",full?data.Printers:data.Printers.Select(p=>new{p.Association,JobCount=p.Queue.Jobs,p.Queue.RawStatus,p.BidirectionalConfigured,p.Shared,p.Local,Errors=p.Errors.Length}));
        Json("printer/driver.json",full?data.Printers.Select(p=>new{p.Queue.Driver,p.DriverVersion,p.Provider,p.Monitor,p.Processor}):new{Availability="Driver strings omitted in privacy-preserving modes"});
        Json("printer/status.json",full?data.Statuses:data.Statuses.Select(s=>new{s.Timestamp,Available=s.Availability!="Unavailable",HasError=s.Error!=null}));
        Json("printer/jobs.json",full?data.Printers.SelectMany(p=>p.Jobs):new{Availability="All print-job metadata omitted"});
        Json("communications/transactions.json",full?data.Transactions:mode==ExportMode.RedactedSummary?new{Count=safeTransactions.Length,Success=safeTransactions.Count(t=>t.Outcome=="Success"),Failed=safeTransactions.Count(t=>t.Outcome!="Success")}:safeTransactions);
        Text("communications/transactions.txt",string.Join("\n",data.Transactions.Select(t=>$"{t.TimestampUtc:O} {t.Id} {t.Command} {t.Outcome}; {t.RequestLength}/{t.ResponseLength} bytes; {t.DurationMs:F3} ms; native={t.NativeError}; valid={t.ValidResponse}; simulated={t.Simulated}"+(full?$"\n{t.Provenance}\n{t.Error}\n{HexFormatter.Format(t.Response)}":""))));
        Json("captures/capture-analysis.json",full?data.Capture:new{PacketCount=data.Capture?.Packets.Length??0,WarningCount=data.Capture?.Warnings.Length??0,Metadata=mode==ExportMode.RedactedSummary?null:data.Capture?.Packets.Select(p=>new{p.Number,p.TimestampUtc,p.TransferType,p.Direction,p.Endpoint,p.Status,PayloadLength=p.Payload.Length,p.RelatedPacket,p.Correlation}),Payloads="Omitted; arbitrary binary data cannot safely be redacted"});
        if(full)
        {
            foreach(var t in data.Transactions){Binary($"communications/raw-payloads/{t.Id}-request.bin",t.Request);Binary($"communications/raw-payloads/{t.Id}-response.bin",t.Response);}
            if(data.OriginalCapture!=null)Binary("captures/original.pcapng",data.OriginalCapture);
        }
        Json("evidence-sha256.json",hashes);
        Json("research/session.json",full?data.Research:new{data.SessionId,Availability="User annotations and identifying research metadata omitted"});
        Text("logs/application.log",full?data.ApplicationLog:"Free-form application messages omitted to avoid identifier disclosure.");
        string summary=Summary(data,full);Text("summary.md",summary);
        Text("analysis/supported-operations.md",string.Join("\n",DiagnosticCommands.All.Select(c=>$"- {c.Name}: {c.RequiredTransport}. {(c.Supported?"Conditional support":"Unsupported")}. {c.DocumentationReference}")));
        Text("analysis/failed-operations.md",string.Join("\n",data.Transactions.Where(t=>!t.Successful).Select(t=>$"- {t.Command}: {t.Outcome}, native error {t.NativeError}"+(full?" — "+t.Error:""))));
        const string next="# Recommended next steps\n\n1. Inspect exact interface/driver and hub-access errors. Preserve Epson's driver.\n2. Compare independent spooler and USB-class observations; neither proves disc publishing readiness.\n3. If identification/status is unsupported, obtain Epson's documented read-only API or capture one controlled status refresh in Total Disc Monitor with USBPcap.\n4. Import the capture and compare framing, setup, payload and URB completion records. No packet replay is supported.\n5. Fatal-error history, EEPROM and waste-ink counters need model/firmware-specific framing, response fixtures and evidence of non-destructive semantics before registration. No speculative vendor commands should be sent.\n";
        Text("analysis/recommended-next-steps.md",next);Text("analysis/next-steps.md",next);
    }
    public static string Summary(BundleSnapshot data,bool includePrivate=false)
    {
        var lines=new List<string>{"# Diagnostic summary",$"Simulation: {data.Device?.Simulated??false}",$"Epson node selected: {data.Device!=null}",$"PP-100 model independently verified: no automatic claim",$"Descriptors retrieved: {data.Descriptors?.Descriptors.Length??0}",$"Windows-evidence queue associations: {data.Printers.Count(p=>p.Association.StartsWith("Associated"))}","Wire request transmitted / device acknowledgement: unobserved by Windows API calls. USBPcap records describe host-controller requests/completions."};
        foreach(var step in data.Steps)lines.Add($"- {step.Operation}: {step.Outcome}"+(includePrivate?" — "+step.Detail:""));
        foreach(var t in data.Transactions)lines.Add($"- {t.Command}: {t.Outcome}; API issued={t.ApiIssued}; valid reply={t.ValidResponse}; interpreted={t.Interpreted}; simulated={t.Simulated}; native={t.NativeError}");
        return string.Join("\n",lines);
    }
}
