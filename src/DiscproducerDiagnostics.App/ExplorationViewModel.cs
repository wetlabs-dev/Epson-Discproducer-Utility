using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using DiscproducerDiagnostics.Core;
using DiscproducerDiagnostics.Diagnostics;
using DiscproducerDiagnostics.Protocols;
using Microsoft.Win32;
namespace DiscproducerDiagnostics.App;
public sealed partial class MainViewModel
{
    private Discovery? lastDiscovery;
    private DescriptorReport? descriptorReport;
    private CaptureReport? captureReport;
    private byte[]? originalCapture;
    private Transaction? selectedTransaction;
    private CapturePacket? selectedPacket;
    private PrinterDetail? selectedPrinter;
    private DiagnosticCommandDefinition selectedCommand=DiagnosticCommands.All[0];
    private string explorerResult="Choose an allowlisted operation. No arbitrary commands are accepted.",progress="",captureResult="Import USBPcap PCAPNG to inspect host-controller records. Captures may contain private print data.";
    private ExportMode exportMode=ExportMode.TechnicalDiagnostics;
    private readonly UiPreferences preferences=UiPreferences.Load();
    public ObservableCollection<InspectionNode> InterfaceTree { get; }=[];
    public ObservableCollection<PrinterDetail> Printers { get; }=[];
    public ObservableCollection<PrinterJob> PrinterJobs { get; }=[];
    public ObservableCollection<Transaction> Transactions { get; }=[];
    public ObservableCollection<Transaction> VisibleTransactions { get; }=[];
    public ObservableCollection<CapturePacket> CapturePackets { get; }=[];
    public ObservableCollection<CaptureEnvironment> CaptureTools { get; }=[];
    public ObservableCollection<DiagnosticStep> DiagnosticSteps { get; }=[];
    public IReadOnlyList<DiagnosticCommandDefinition> Commands=>DiagnosticCommands.All;
    public IEnumerable<Transaction> CommandHistory=>Transactions.Where(t=>t.Command==SelectedCommand.Id).Reverse();
    public ExportMode[] ExportModes=>Enum.GetValues<ExportMode>();
    public string[] Directions=>["All","IN","OUT","Unknown"];
    public string[] CaptureTypes=>["All","Control","Bulk","Interrupt","Isochronous"];
    public string[] PayloadKinds=>["Response","Request"];
    private string payloadKind="Response";
    public string PayloadKind {get=>payloadKind;set{payloadKind=value;OnChanged();OnChanged(nameof(TransactionHex));}}
    public string LogFilter {get;set;}="";
    public string LogDirection {get;set;}="All";
    public string CaptureDeviceFilter {get;set;}="";
    public string CaptureEndpointFilter {get;set;}="";
    public string CaptureDirectionFilter {get;set;}="All";
    public string CaptureTypeFilter {get;set;}="All";
    public string CaptureFrom {get;set;}="";
    public string CaptureTo {get;set;}="";
    public string CaptureSearch {get;set;}="";
    public string Progress {get=>progress;private set{progress=value;OnChanged();}}
    public string ExplorerResult {get=>explorerResult;private set{explorerResult=value;OnChanged();}}
    public string CaptureResult {get=>captureResult;private set{captureResult=value;OnChanged();}}
    public ExportMode SelectedExportMode {get=>exportMode;set{exportMode=value;OnChanged();}}
    public DiagnosticCommandDefinition SelectedCommand {get=>selectedCommand;set{selectedCommand=value;OnChanged();OnChanged(nameof(CommandDetails));OnChanged(nameof(CommandHistory));CommandManager.InvalidateRequerySuggested();}}
    public string CommandDetails=>$"{SelectedCommand.Description}\nProtocol: {SelectedCommand.Protocol}\nTransport: {SelectedCommand.RequiredTransport}\nRequirements: {SelectedCommand.Requirements}\nRequest: {SelectedCommand.RequestFormat}\nExpected: {SelectedCommand.ExpectedResponse}\nSafety: {SelectedCommand.Safety}; {SelectedCommand.Availability}\nTimeout: {TimeoutSeconds} seconds; last outcome: {Transactions.LastOrDefault(t=>t.Command==SelectedCommand.Id)?.Outcome ?? "Not executed"}\nSource: {SelectedCommand.DocumentationReference}\nLast driver availability: {(Transactions.LastOrDefault(t=>t.Command==SelectedCommand.Id)?.NativeError is 1 or 50 ? "Unsupported by current driver" : "See execution history; no physical verification claimed")}";
    public Transaction? SelectedTransaction {get=>selectedTransaction;set{selectedTransaction=value;OnChanged();OnChanged(nameof(TransactionHex));OnChanged(nameof(TransactionDetails));CommandManager.InvalidateRequerySuggested();}}
    public string TransactionHex=>SelectedTransaction==null?"":HexFormatter.Format(SelectedPayload());
    public string TransactionDetails=>SelectedTransaction==null?"Select a transaction":$"{SelectedTransaction.TimestampUtc:O} • {SelectedTransaction.Id}\n{SelectedTransaction.Transport} • {SelectedTransaction.Interface} • {SelectedTransaction.Endpoint}\n{SelectedTransaction.Provenance}\nOutcome: {SelectedTransaction.Outcome}; Category: {SelectedTransaction.Failure?.Category}; Windows error: {SelectedTransaction.NativeError}; {SelectedTransaction.Error}\nRequest/response: {SelectedTransaction.RequestLength}/{SelectedTransaction.ResponseLength} bytes; {SelectedTransaction.DurationMs:F3} ms; timeout {SelectedTransaction.TimeoutMs} ms\nSetup (supplied, not on-wire): {(SelectedTransaction.SetupPacket==null?"Unavailable":Convert.ToHexString(SelectedTransaction.SetupPacket))}\nAPI issued: {SelectedTransaction.ApiIssued}; valid reply: {SelectedTransaction.ValidResponse}; interpreted: {SelectedTransaction.Interpreted}; bus acknowledgement: unobserved\n{SelectedTransaction.Interpretation}\n{SelectedTransaction.Failure?.Recovery}";
    public CapturePacket? SelectedPacket {get=>selectedPacket;set{selectedPacket=value;OnChanged();OnChanged(nameof(CaptureHex));OnChanged(nameof(CaptureDetails));CommandManager.InvalidateRequerySuggested();}}
    public string CaptureHex=>SelectedPacket==null?"":HexFormatter.Format(SelectedPacket.Payload);
    public string CaptureDetails=>SelectedPacket==null?"Select a capture record":$"Packet {SelectedPacket.Number}; {SelectedPacket.TimestampUtc:O}; URB 0x{SelectedPacket.UrbId:X}\nBus/address {SelectedPacket.Device}; {SelectedPacket.EndpointText}; {SelectedPacket.TransferType} {SelectedPacket.Direction}; status {SelectedPacket.StatusText}; USB interface (when setup recipient is interface): {SelectedPacket.UsbInterface}; pair elapsed {SelectedPacket.DurationMs:F3} ms\nSetup: {Convert.ToHexString(SelectedPacket.SetupPacket)}; stage: {SelectedPacket.Stage}\n{SelectedPacket.Correlation}; related packet: {SelectedPacket.RelatedPacket}\n{SelectedPacket.Warning}\nUSBPcap records are host-controller observations, not proof of successful wire transactions.";
    public PrinterDetail? SelectedPrinter {get=>selectedPrinter;set{selectedPrinter=value;PrinterJobs.Clear();foreach(var j in value?.Jobs??[])PrinterJobs.Add(j);OnChanged();OnChanged(nameof(PrinterDetails));}}
    public string PrinterDetails=>SelectedPrinter==null?"Select a queue":JsonSerializer.Serialize(SelectedPrinter with{Jobs=[]},new JsonSerializerOptions{WriteIndented=true});
    public ICommand InspectInterfacesCommand {get;private set;}=null!;
    public ICommand CopyInterfacesCommand {get;private set;}=null!;
    public ICommand ExportInterfacesCommand {get;private set;}=null!;
    public ICommand InspectPrintersCommand {get;private set;}=null!;
    public ICommand ExecuteProtocolCommand {get;private set;}=null!;
    public ICommand FullDiagnosticsCommand {get;private set;}=null!;
    public ICommand ImportCaptureCommand {get;private set;}=null!;
    public ICommand FilterCaptureCommand {get;private set;}=null!;
    public ICommand CompareCommand {get;private set;}=null!;
    public ICommand FilterLogsCommand {get;private set;}=null!;
    public ICommand CopyHexCommand {get;private set;}=null!;
    public ICommand CopyAsciiCommand {get;private set;}=null!;
    public ICommand CopyTransactionCommand {get;private set;}=null!;
    public ICommand SavePayloadCommand {get;private set;}=null!;
    public ICommand SaveTransactionCommand {get;private set;}=null!;
    public ICommand DetectCaptureToolsCommand {get;private set;}=null!;
    public ICommand CopyCaptureHexCommand {get;private set;}=null!;
    public ICommand SaveCapturePayloadCommand {get;private set;}=null!;
    private void InitializeExploration()
    {
        TimeoutSeconds=preferences.TimeoutSeconds;
        LogDirection=Directions.Contains(preferences.LogDirection)?preferences.LogDirection:"All";PayloadKind=PayloadKinds.Contains(preferences.PayloadKind)?preferences.PayloadKind:"Response";
        session.TransactionAdded+=t=>{Transactions.Add(t);VisibleTransactions.Add(t);SelectedTransaction=t;OnChanged(nameof(CommandDetails));OnChanged(nameof(CommandHistory));OnChanged(nameof(CapabilityReport));};
        InspectInterfacesCommand=new ActionCommand(()=>_=Run(InspectInterfaces),()=>!busy&&SelectedDevice!=null);
        CopyInterfacesCommand=new ActionCommand(()=>Copy(JsonSerializer.Serialize(InterfaceTree,new JsonSerializerOptions{WriteIndented=true})),()=>InterfaceTree.Count>0);
        ExportInterfacesCommand=new ActionCommand(()=>SaveText("USB interface tree|*.json",JsonSerializer.Serialize(InterfaceTree,new JsonSerializerOptions{WriteIndented=true})),()=>!busy&&InterfaceTree.Count>0);
        InspectPrintersCommand=new ActionCommand(()=>_=Run(InspectPrinters),()=>!busy);
        ExecuteProtocolCommand=new ActionCommand(()=>_=Run(ExecuteProtocol),()=>!busy&&SelectedDevice!=null&&SelectedCommand.Supported&&SelectedCommand.IsReadOnly);
        FullDiagnosticsCommand=new ActionCommand(()=>_=Run(FullDiagnostics),()=>!busy);
        ImportCaptureCommand=new ActionCommand(ChooseCapture,()=>!busy);
        FilterCaptureCommand=new ActionCommand(()=>_=Run(FilterCapture),()=>!busy&&captureReport!=null);
        CompareCommand=new ActionCommand(()=>CaptureResult=HexFormatter.Compare(SelectedTransaction!,SelectedPacket!),()=>SelectedTransaction!=null&&SelectedPacket!=null);
        FilterLogsCommand=new ActionCommand(FilterTransactions,()=>!busy);
        CopyHexCommand=new ActionCommand(()=>Copy(Convert.ToHexString(SelectedPayload())),()=>SelectedTransaction!=null);
        CopyAsciiCommand=new ActionCommand(()=>Copy(HexFormatter.Ascii(SelectedPayload())),()=>SelectedTransaction!=null);
        CopyTransactionCommand=new ActionCommand(()=>Copy(JsonSerializer.Serialize(SelectedTransaction,new JsonSerializerOptions{WriteIndented=true})),()=>SelectedTransaction!=null);
        SavePayloadCommand=new ActionCommand(SavePayload,()=>!busy&&SelectedTransaction!=null);
        SaveTransactionCommand=new ActionCommand(()=>SaveText("Transaction JSON|*.json",JsonSerializer.Serialize(SelectedTransaction,new JsonSerializerOptions{WriteIndented=true})),()=>!busy&&SelectedTransaction!=null);
        CopyCaptureHexCommand=new ActionCommand(()=>Copy(Convert.ToHexString(SelectedPacket!.Payload)),()=>SelectedPacket!=null);
        SaveCapturePayloadCommand=new ActionCommand(()=>SaveBytes(SelectedPacket!.Payload,"Captured payload (unredacted)|*.bin"),()=>!busy&&SelectedPacket!=null);
        DetectCaptureToolsCommand=new ActionCommand(DetectCaptureTools,()=>!busy);DetectCaptureTools();
    }
    private void ResetInspection()
    {
        descriptorReport=null;InterfaceTree.Clear();Printers.Clear();SelectedPrinter=null;DiagnosticSteps.Clear();
        if(SelectedDevice!=null)InterfaceTree.Add(DescriptorDecoder.Tree(SelectedDevice,null,lastDiscovery?.Devices.Where(d=>QueueCorrelation.SamePhysical(d,SelectedDevice))??[]));
    }
    private TimeSpan QueryTimeout()
    {
        if(!double.TryParse(TimeoutSeconds,out double seconds)||!double.IsFinite(seconds)||seconds<0.1||seconds>60)throw new ArgumentException("Timeout must be 0.1–60 seconds");return TimeSpan.FromSeconds(seconds);
    }
    private void Step(string name,string outcome,string detail){DiagnosticSteps.Add(new(name,outcome,detail,DateTimeOffset.UtcNow));Progress=$"{name}: {outcome} — {detail}";session.Event(name,detail,outcome=="Success");}
    private async Task<WorkerReply> RecordedWorker(WorkerRequest request,CancellationToken token)
    {
        var sw=Stopwatch.StartNew();var started=DateTimeOffset.UtcNow;var timeout=QueryTimeout();
        try
        {
            var reply=await worker.RunAsync(request,timeout,token,new Progress<Transaction>(t=>RecordDescriptorTransaction(t,timeout)));
            session.RecordTransaction(new(Guid.NewGuid(),started,SelectedDevice?.InstanceId??"Unavailable","Windows API worker",SelectedDevice?.InterfacePath??"Unavailable","Host API",request.Operation=="scan"?"pnp":request.Operation=="printers"?"spooler":request.Operation,[],[],null,sw.Elapsed.TotalMilliseconds,timeout.TotalMilliseconds,reply.Error!=null?"Failed":reply.Descriptors?.Errors.Length>0?"Partial":"Success",reply.ErrorCode,reply.Error,"Structured returned metadata is in the corresponding inspector; no USB bytes inferred","Windows metadata / host API"));
            if(reply.Error!=null)throw reply.Failure != null ? new DiagnosticFailureException(reply.Failure) : reply.ErrorCode.HasValue ? new System.ComponentModel.Win32Exception(reply.ErrorCode.Value,reply.Error) : new IOException(reply.Error);
            return reply;
        }
        catch(Exception e)when(e is TimeoutException or OperationCanceledException)
        {
            session.RecordTransaction(new(Guid.NewGuid(),started,SelectedDevice?.InstanceId??"Unavailable","Windows API worker","Unavailable","Host API",request.Operation,[],[],null,sw.Elapsed.TotalMilliseconds,timeout.TotalMilliseconds,e is OperationCanceledException?"Cancelled":"Timeout",e is TimeoutException?1460:null,e.Message,"Worker stopped; no partial response fabricated","Windows API"));throw;
        }
    }
    private void RecordDescriptorTransaction(Transaction t, TimeSpan timeout)
    {
        bool valid=false;
        if(t.Successful && t.SetupPacket is {Length:8} && t.Response.Length>12)
            try {valid=UsbDescriptors.Parse(t.Response.AsSpan(12)).FirstOrDefault()?.Type==t.SetupPacket[3];} catch(FormatException) { }
        session.RecordTransaction(t with {TimeoutMs=timeout.TotalMilliseconds,ValidResponse=valid,Interpreted=valid});
    }
    private async Task InspectInterfaces(CancellationToken token)
    {
        var d=lastDiscovery?.Devices.FirstOrDefault(x=>x.InstanceId==SelectedDevice?.InstanceId)??SelectedDevice!;
        descriptorReport=null;InterfaceTree.Clear();InterfaceTree.Add(DescriptorDecoder.Tree(d,null,[d]));
        if(d.Simulated){descriptorReport=new([],[],["Mock device has no live USB descriptors; no example endpoints are inserted."]);}
        else
        {
            var reply=await RecordedWorker(new("descriptors",d.InstanceId),token);descriptorReport=reply.Descriptors??new([],[],["No descriptor result"]);
            foreach(var t in descriptorReport.Transactions)
            {
                bool valid=false;
                if(t.Successful&&t.SetupPacket!=null&&t.Response.Length>12)
                {try{valid=UsbDescriptors.Parse(t.Response.AsSpan(12)).FirstOrDefault()?.Type==t.SetupPacket[3];}catch(FormatException){}}
                session.RecordTransaction(t with{TimeoutMs=QueryTimeout().TotalMilliseconds,ValidResponse=valid,Interpreted=valid,Interpretation=valid?"Descriptor response parsed; inspect the descriptor tree (configuration header reads may be partial)":t.Interpretation});
            }
        }
        InterfaceTree.Clear();InterfaceTree.Add(DescriptorDecoder.Tree(d,descriptorReport,lastDiscovery?.Devices.Where(x=>QueueCorrelation.SamePhysical(x,d))??[d]));
        var parsed=DescriptorDecoder.Decode(descriptorReport.Descriptors);
        foreach(var prior in Facts.Where(f=>f.Source.StartsWith("USB ")).ToArray())Facts.Remove(prior);
        foreach(var fact in parsed.DeviceFacts)Facts.Add(fact);
        ExplorerResult=$"Descriptors: {descriptorReport.Descriptors.Length}; interfaces: {parsed.Interfaces.Length}; endpoints: {parsed.Endpoints.Length}\n"+string.Join("\n",descriptorReport.Errors.Concat(parsed.Warnings));
        Step("USB descriptors",descriptorReport.Descriptors.Length>0?"Success":"Unavailable",ExplorerResult);
    }
    private async Task InspectPrinters(CancellationToken token)
    {
        Printers.Clear();SelectedPrinter=null;
        PrinterDetail[] details=Simulation?[]:(await RecordedWorker(new("printers"),token)).Printers??[];
        Printers.Clear();
        foreach(var p in details)Printers.Add(SelectedDevice==null?p:QueueCorrelation.Correlate(p,SelectedDevice,lastDiscovery?.Devices??[SelectedDevice]));
        SelectedPrinter=Printers.FirstOrDefault();
        foreach(var old in Statuses.Where(s=>s.Source.StartsWith("Spooler:")).ToArray())Statuses.Remove(old);
        foreach(var p in Printers)Statuses.Add(new("Spooler: "+p.Queue.Name,DateTimeOffset.UtcNow,$"0x{p.Queue.RawStatus:X8}",StatusDecoder.Spooler(p.Queue.RawStatus),p.Association));
        Step("Printer queues","Success",$"{Printers.Count} candidate queues; {Printers.Count(p=>p.Association.StartsWith("Associated"))} associated by Windows evidence. Names alone never establish physical association.");
    }
    private async Task ExecuteProtocol(CancellationToken token)
    {
        var command=DiagnosticCommands.Authorize(SelectedCommand.Id);
        if(!command.Supported){ExplorerResult=command.Description+"\n"+command.Requirements;Step(command.Id,"Unsupported",ExplorerResult);return;}
        if(command.Id=="descriptors"){await InspectInterfaces(token);return;}
        if(command.Id=="spooler"){await InspectPrinters(token);ExplorerResult=$"{Printers.Count} printer candidates inspected";return;}
        if(command.Id=="pnp")
        {
            lastDiscovery=Simulation?new([MockTransport.Device],[],[]):(await RecordedWorker(new("scan"),token)).Discovery;
            var current=lastDiscovery?.Devices.FirstOrDefault(d=>d.InstanceId==SelectedDevice?.InstanceId);
            ExplorerResult=current==null?"Selected device is absent":JsonSerializer.Serialize(current,new JsonSerializerOptions{WriteIndented=true});Step("PnP properties",current==null?"Unavailable":"Success",ExplorerResult);return;
        }
        if(transport==null||!transport.Connected)throw new InvalidOperationException("Connect to an accessible USB printer interface first. The existing driver must expose the documented IOCTL.");
        var r=await transport.QueryAsync(command.Id,QueryTimeout(),token);var parsed=ResponseInterpreter.Interpret(command.Id,r.Response);
        if(r.Success&&parsed.Valid)
        {
            if(command.Id=="ieee1284")
            {
                var identity=Ieee1284.Parse(r.Response);string source=r.Simulated?"SIMULATED IEEE 1284":"IEEE 1284 via Windows USB printer driver";
                foreach(var(name,value)in new[]{("Manufacturer",identity.Find("MFG","MANUFACTURER")),("Model",identity.Find("MDL","MODEL")),("Serial number",identity.Find("SN","SERN","SERIALNUMBER"))})
                {
                    foreach(var old in Facts.Where(f=>f.Name==name&&f.Source==source).ToArray())Facts.Remove(old);
                    Facts.Add(new(name,value,source,value=="Unavailable"?"Unavailable":"Reported"));
                }
            }
            else if(command.Id=="port-status")Statuses.Add(new(r.Simulated?"SIMULATED USB class":"USB printer class via Windows driver",DateTimeOffset.UtcNow,Convert.ToHexString(r.Response),parsed.Text,"Driver-reported; wire acknowledgement unobserved"));
        }
        ExplorerResult=$"API result: {(r.Success?"Success":"Failed")}; code {r.ErrorCode}; {r.Error}\nValid/interpreted reply: {parsed.Valid&&r.Success}\n{parsed.Text}\nPhysical transmission/acknowledgement is unobserved by the Windows API.";
        if(!transport.Connected){connected=false;Connection="Device disconnected / unavailable";}
        Step(command.Id,r.Success&&parsed.Valid?"Success":r.Success?"InvalidResponse":"Failed",ExplorerResult);
    }
    private async Task FullDiagnostics(CancellationToken token)
    {
        DiagnosticSteps.Clear();string? selected=SelectedDevice?.InstanceId;
        async Task Attempt(string name,Func<Task> action){token.ThrowIfCancellationRequested();Progress=name;try{await action();}catch(OperationCanceledException){throw;}catch(Exception e){var failure=ErrorClassifier.Classify(e,name);Step(name,failure.Category.ToString(),$"{failure.Detail}; native={failure.NativeCode}; HRESULT={failure.HResult}; {failure.Recovery}");}}
        await Attempt("Enumerate Epson devices",async()=>
        {
            lastDiscovery=Simulation?new([MockTransport.Device],[],[]):(await RecordedWorker(new("scan"),token)).Discovery;
            var found=lastDiscovery?.Devices??[];
            if(selected==null)
            {
                if(found.Length==0)throw new IOException("No Epson device discovered");
                if(found.Any(d=>!QueueCorrelation.SamePhysical(d,found[0])))throw new InvalidOperationException("Multiple physical devices: select one before running diagnostics");
                Devices.Clear();foreach(var d in found)Devices.Add(d);
                SelectedDevice=found.FirstOrDefault(d=>d.InterfacePath!=null)??found[0];
            }
            else if(!found.Any(d=>d.InstanceId==selected))throw new IOException("Selected device is disconnected; another device will not be substituted");
            Step("Device enumeration","Success","Selected Epson node is present. This is not request/response evidence.");
        });
        if(SelectedDevice==null||lastDiscovery?.Devices.Any(d=>d.InstanceId==SelectedDevice.InstanceId)!=true){ExplorerResult=BundleExporter.Summary(Snapshot(),true);await ExportBundle(token);return;}
        Facts.Clear();AddMetadata(lastDiscovery.Devices.First(d=>d.InstanceId==SelectedDevice.InstanceId));
        await Attempt("Inspect descriptors and drivers",()=>InspectInterfaces(token));
        await Attempt("Inspect printer queues",()=>InspectPrinters(token));
        await Attempt("Open printer interface",async()=>
        {
            if(transport==null||!transport.Connected)await OpenTransport(token);
            Step("Interface opened",transport?.Connected==true?"Success":"Unavailable","Handle probe only; no live wire acknowledgement established.");
        });
        foreach(var id in new[]{"ieee1284","port-status"})await Attempt(id,async()=>{SelectedCommand=DiagnosticCommands.Authorize(id);await ExecuteProtocol(token);});
        Step("USB standard GET_STATUS","Unsupported","No supported control-transfer API on the installed printer transport; no bypass attempted.");
        Progress="Read-only diagnostics complete. Generating export…";
        ExplorerResult=BundleExporter.Summary(Snapshot(),true);
        await ExportBundle(token);
    }
    private byte[] SelectedPayload()=>SelectedTransaction==null?[]:PayloadKind=="Request"?SelectedTransaction.Request:SelectedTransaction.Response;
    private void FilterTransactions()
    {
        VisibleTransactions.Clear();foreach(var t in Transactions)
        {
            string direction=t.SetupPacket==null?"Unknown":(t.SetupPacket[0]&128)!=0?"IN":"OUT";
            if((LogDirection=="All"||LogDirection==direction)&&$"{t.Command} {t.Endpoint} {t.Outcome}".Contains(LogFilter,StringComparison.OrdinalIgnoreCase))VisibleTransactions.Add(t);
        }
    }
    private void Copy(string text){try{Clipboard.SetText(text);}catch(Exception e){Notice=e.Message;}}
    private void SaveText(string filter,string text)
    {
        var picker=new SaveFileDialog{Filter=filter,InitialDirectory=preferences.ExportDirectory};if(picker.ShowDialog()!=true)return;
        try{File.WriteAllText(picker.FileName,text);preferences.ExportDirectory=Path.GetDirectoryName(picker.FileName)??"";}catch(Exception e){Notice=e.Message;}
    }
    private void SavePayload()=>SaveBytes(SelectedPayload(),"Raw payload (unredacted)|*.bin");
    private void SaveBytes(byte[] bytes,string filter)
    {
        var picker=new SaveFileDialog{Filter=filter,InitialDirectory=preferences.ExportDirectory};if(picker.ShowDialog()!=true)return;
        try{File.WriteAllBytes(picker.FileName,bytes);}catch(Exception e){Notice=e.Message;}
    }
    private void ChooseCapture()
    {
        var picker=new OpenFileDialog{Filter="USBPcap PCAPNG|*.pcapng",InitialDirectory=preferences.CaptureDirectory};if(picker.ShowDialog()!=true)return;
        _=Run(async token=>
        {
            var imported=await Task.Run(()=>{if(new FileInfo(picker.FileName).Length>UsbCaptureImporter.MaxFileSize)throw new InvalidDataException("Capture exceeds 128 MiB; split/filter in Wireshark first");var bytes=File.ReadAllBytes(picker.FileName);return (bytes,report:UsbCaptureImporter.Parse(bytes,picker.FileName,token));},token);
            originalCapture=imported.bytes;captureReport=imported.report;capturePath=picker.FileName;preferences.CaptureDirectory=Path.GetDirectoryName(picker.FileName)??"";
            await FilterCapture(token);
        });
    }
    private async Task FilterCapture(CancellationToken token)
    {
        if(captureReport==null)return;
        DateTimeOffset? from=null,to=null;
        if(CaptureFrom.Length>0){if(!DateTimeOffset.TryParse(CaptureFrom,out var value))throw new FormatException("Invalid start timestamp; enter ISO 8601 with timezone");from=value;}
        if(CaptureTo.Length>0){if(!DateTimeOffset.TryParse(CaptureTo,out var value))throw new FormatException("Invalid end timestamp; enter ISO 8601 with timezone");to=value;}
        if(from>to)throw new FormatException("Start must be before end");
        byte[] needle=CaptureSearch.StartsWith("hex:",StringComparison.OrdinalIgnoreCase)?Convert.FromHexString(CaptureSearch[4..].Replace(" ","")):System.Text.Encoding.UTF8.GetBytes(CaptureSearch);
        var filtered=await Task.Run(()=>captureReport.Packets.Where(p=>
        {
            token.ThrowIfCancellationRequested();return (CaptureControlFilter.Length==0||Convert.ToHexString(p.SetupPacket).Contains(CaptureControlFilter.Replace(" ",""),StringComparison.OrdinalIgnoreCase))&&p.Device.Contains(CaptureDeviceFilter,StringComparison.OrdinalIgnoreCase)&&p.EndpointText.Contains(CaptureEndpointFilter,StringComparison.OrdinalIgnoreCase)&&(CaptureDirectionFilter=="All"||p.Direction==CaptureDirectionFilter)&&(CaptureTypeFilter=="All"||p.TransferType==CaptureTypeFilter)&&(from==null||p.TimestampUtc>=from)&&(to==null||p.TimestampUtc<=to)&&(needle.Length==0||p.Payload.AsSpan().IndexOf(needle)>=0||p.SetupPacket.AsSpan().IndexOf(needle)>=0);
        }).Take(10000).ToArray(),token);
        CapturePackets.Clear();foreach(var p in filtered)CapturePackets.Add(p);SelectedPacket=CapturePackets.FirstOrDefault();
        CaptureResult=$"{captureReport.Packets.Length} records imported; showing first {filtered.Length} matches (10,000 display cap). Original retained in memory; SHA-256 {captureReport.Sha256}\n"+string.Join("\n",captureReport.Warnings.Take(20));
    }
    private void DetectCaptureTools()
    {
        try { DetectCaptureToolsCore(); }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException){CaptureTools.Add(new("Detection","Unavailable: "+e.Message,null,null));}
    }
    private void DetectCaptureToolsCore()
    {
        CaptureTools.Clear();
        foreach(var(name,relative)in new[]{("Wireshark",@"Wireshark\Wireshark.exe"),("TShark (optional)",@"Wireshark\tshark.exe"),("USBPcap command tool",@"USBPcap\USBPcapCMD.exe")})
        {
            string? path=new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}.Select(p=>Path.Combine(p,relative)).FirstOrDefault(File.Exists);
            CaptureTools.Add(new(name,path==null?"Not found in standard locations":"Detected",path,path==null?null:FileVersionInfo.GetVersionInfo(path).FileVersion));
        }
        using var service=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\USBPcap");
        CaptureTools.Add(new("USBPcap driver service",service==null?"Not registered":"Registered (not proof capture is running)",service?.GetValue("ImagePath")as string,null));
    }
    private BundleSnapshot Snapshot()=>new(lastDiscovery?.Devices.FirstOrDefault(d=>d.InstanceId==SelectedDevice?.InstanceId)??SelectedDevice,Facts.ToArray(),Statuses.ToArray(),descriptorReport,Printers.ToArray(),Transactions.ToArray(),captureReport,originalCapture,DiagnosticSteps.ToArray(),session.ApplicationLog){SessionId=research.SessionId,Research=CurrentResearch()};
    private async Task ExportBundle(CancellationToken token)
    {
        bool confirmed=SelectedExportMode!=ExportMode.FullForensic||MessageBox.Show("Full forensic export includes serial/device identifiers, printer job names, raw payloads and the original capture. Captures may contain print jobs, filenames and disc metadata. Export this sensitive data?","Confirm full forensic export",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
        if(!confirmed)return;
        var picker=new SaveFileDialog{Filter="Diagnostic ZIP|*.zip",FileName=$"DiscproducerDiagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.zip",InitialDirectory=preferences.ExportDirectory};if(picker.ShowDialog()!=true)return;
        var snapshot=Snapshot();var mode=SelectedExportMode;string temporary=picker.FileName+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{await Task.Run(()=>{token.ThrowIfCancellationRequested();BundleExporter.Export(temporary,snapshot,mode,confirmed);token.ThrowIfCancellationRequested();},token);File.Move(temporary,picker.FileName,true);preferences.ExportDirectory=Path.GetDirectoryName(picker.FileName)??"";Notice="Diagnostic bundle exported ("+mode+")";}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    internal async Task VerifySimulatedUiAsync()
    {
        Simulation=true;await Scan(default);await Connect(default);
        SelectedCommand=DiagnosticCommands.Authorize("ieee1284");await ExecuteProtocol(default);
        await InspectInterfaces(default);await InspectPrinters(default);
        if(!Transactions.Any(t=>t.Simulated&&t.ValidResponse)||!Facts.Any(f=>f.Name=="Model"&&f.Value=="PP-100"))throw new InvalidOperationException("Simulated UI path failed");
        await Disconnect(default);
        string temporary=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".zip");
        try
        {
            BundleExporter.Export(temporary,Snapshot(),ExportMode.FullForensic,true);
            var imported=await Task.Run(()=>BundleReader.Open(temporary));
            foreach(var fact in imported.Facts)OfflineFacts.Add(fact);
            foreach(var t in imported.Transactions)OfflineTransactions.Add(t);
            foreach(var tree in imported.Topology)OfflineTopology.Add(tree);
            foreach(var document in imported.Documents)OfflineDocuments.Add(document);
            SelectedOfflineDocument=OfflineDocuments.FirstOrDefault();SelectedOfflineTransaction=OfflineTransactions.FirstOrDefault();
            OfflineSummary="IMPORTED SIMULATED UI TEST — no physical hardware evidence";
            if(OfflineTransactions.Count==0)throw new InvalidOperationException("Offline view reconstruction failed");
        }
        finally{File.Delete(temporary);}
    }
    private void SavePreferences()
    {
        preferences.TimeoutSeconds=TimeoutSeconds;preferences.LogDirection=LogDirection;preferences.PayloadKind=PayloadKind;preferences.SelectedDeviceHash=SelectedDevice==null?"":Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(SelectedDevice.InstanceId)));preferences.Save();
    }
}
internal sealed class UiPreferences
{
    public string TimeoutSeconds{get;set;}="10";
    public string LogDirection{get;set;}="All";
    public string PayloadKind{get;set;}="Response";
    public string SelectedDeviceHash{get;set;}="";
    public string ExportDirectory{get;set;}="";
    public string CaptureDirectory{get;set;}="";
    private static string FileName=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DiscproducerDiagnostics","preferences.json");
    public static UiPreferences Load(){try{return JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(FileName))??new();}catch{return new();}}
    public void Save(){try{Directory.CreateDirectory(Path.GetDirectoryName(FileName)!);File.WriteAllText(FileName,JsonSerializer.Serialize(this));}catch(IOException){}catch(UnauthorizedAccessException){}}
}
