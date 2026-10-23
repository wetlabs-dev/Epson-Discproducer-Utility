using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using DiscproducerDiagnostics.Core;
using DiscproducerDiagnostics.Diagnostics;
using Microsoft.Win32;
namespace DiscproducerDiagnostics.App;
public sealed partial class MainViewModel
{
    private ResearchSession research=new();
    private string? capturePath;
    private CapturePacket? pinnedPacket;
    private Transaction? pinnedTransaction;
    private OfflineDocument? offlineDocument;
    private string researchOutput="Sessions contain observations and external capture references, never executable commands.",offlineSummary="No bundle opened.";
    public string RegisteredModules=>ProtocolModules.Default.All.Count==0?"No model-specific modules registered. Fatal history and waste counters are unsupported.":JsonSerializer.Serialize(ProtocolModules.Default.All.Select(m=>new{m.Name,m.Commands}),new JsonSerializerOptions{WriteIndented=true});
    public string SessionName {get;set;}="PP-100 investigation";
    public string SessionNotes {get;set;}="";
    public string SessionScenario {get;set;}="Unknown condition";
    public string[] ResearchScenarios=>["Idle/Ready","Internal printer error","Device startup","Status refresh","USB reconnection","Unknown condition"];
    public string Annotation {get;set;}="Unknown Epson command";
    public string CaptureControlFilter {get;set;}="";
    public string ResearchOutput {get=>researchOutput;private set{researchOutput=value;OnChanged();}}
    public string OfflineSummary {get=>offlineSummary;private set{offlineSummary=value;OnChanged();}}
    public ObservableCollection<Fact> OfflineFacts {get;}=[];
    public ObservableCollection<Transaction> OfflineTransactions {get;}=[];
    public ObservableCollection<InspectionNode> OfflineTopology {get;}=[];
    private Transaction? offlineTransaction;
    public Transaction? SelectedOfflineTransaction {get=>offlineTransaction;set{offlineTransaction=value;OnChanged();OnChanged(nameof(OfflinePayload));}}
    public string OfflinePayload=>SelectedOfflineTransaction==null?"Imported forensic responses only":$"IMPORTED • {SelectedOfflineTransaction.Provenance}\n{HexFormatter.Format(SelectedOfflineTransaction.Response)}";
    public ObservableCollection<OfflineDocument> OfflineDocuments {get;}=[];
    public OfflineDocument? SelectedOfflineDocument {get=>offlineDocument;set{offlineDocument=value;OnChanged();OnChanged(nameof(OfflineText));}}
    public string OfflineText=>SelectedOfflineDocument?.Text??"Select an imported evidence document.";
    public string CapabilityReport
    {
        get
        {
            var t=Transactions.Where(t=>t.DeviceId==SelectedDevice?.InstanceId).ToArray();
            return $"Selected node discovered: {SelectedDevice!=null}\nSimulation: {SelectedDevice?.Simulated ?? Simulation}\nPhysical Windows identity: {(SelectedDevice==null?"Unresolved":DeviceTopology.Describe(SelectedDevice,lastDiscovery?.Devices??[SelectedDevice],Printers,connected).PhysicalInstanceId)}\nQueue associated: {Printers.Any(p=>p.Association.StartsWith("Associated"))}\nDescriptor access: {descriptorReport?.Descriptors.Length>0}\nDriver handle opened: {t.Any(t=>t.Command=="interface-probe"&&t.Successful)}\nHost query API issued (known): {t.Any(t=>t.ApiIssued&&(t.Command is "ieee1284" or "port-status"))}\nValid response received: {t.Any(t=>t.ValidResponse)}\nResponse interpreted: {t.Any(t=>t.Interpreted)}\nPhysical USB transaction: unverified; imported host URBs are not bus acknowledgements\nModel-specific Epson status decoded: unsupported\nPP-100 hardware compatibility: unverified\nCompleted observations remain available after failure or cancellation.";
        }
    }
    public ICommand HardwareValidationCommand {get;private set;}=null!;
    public ICommand SaveResearchCommand {get;private set;}=null!;
    public ICommand OpenResearchCommand {get;private set;}=null!;
    public ICommand CompareSessionsCommand {get;private set;}=null!;
    public ICommand AssociateCaptureCommand {get;private set;}=null!;
    public ICommand ManualAssociationCommand {get;private set;}=null!;
    public ICommand AnnotateCommand {get;private set;}=null!;
    public ICommand PinPacketCommand {get;private set;}=null!;
    public ICommand DiffPacketCommand {get;private set;}=null!;
    public ICommand PinTransactionCommand {get;private set;}=null!;
    public ICommand DiffTransactionCommand {get;private set;}=null!;
    public ICommand ExportCaptureSelectionCommand {get;private set;}=null!;
    public ICommand OpenBundleCommand {get;private set;}=null!;
    public ICommand SaveResearchReportCommand {get;private set;}=null!;
    private void InitializeResearch()
    {
        HardwareValidationCommand=new ActionCommand(()=>_=Run(async token=>{if(Simulation)throw new InvalidOperationException("Turn Simulation mode off before hardware validation.");if(SelectedDevice==null)throw new InvalidOperationException("Scan, identify and select the intended physical printer first.");await FullDiagnostics(token);}),()=>!busy);
        SaveResearchCommand=new ActionCommand(SaveResearch,()=>!busy);
        OpenResearchCommand=new ActionCommand(OpenResearch,()=>!busy);
        CompareSessionsCommand=new ActionCommand(CompareSessions,()=>!busy);
        AssociateCaptureCommand=new ActionCommand(()=>ResearchOutput=JsonSerializer.Serialize(CaptureAnalysis.Associate(captureReport!,SelectedDevice!),new JsonSerializerOptions{WriteIndented=true}),()=>!busy&&captureReport!=null&&SelectedDevice!=null);
        ManualAssociationCommand=new ActionCommand(()=>{
            var p=SelectedPacket!;var a=new CaptureAssociation(captureReport!.Sha256,p.Section,p.CaptureInterface,p.Bus,p.Address,SelectedDevice!.InstanceId,"Manual — unverified",["User associated selected captured address with selected Windows node",Annotation],true);
            research=research with{Associations=research.Associations.Where(x=>!(x.CaptureSha256==a.CaptureSha256&&x.Section==a.Section&&x.CaptureInterface==a.CaptureInterface&&x.Bus==a.Bus&&x.Address==a.Address)).Append(a).ToArray()};
            ResearchOutput=JsonSerializer.Serialize(a,new JsonSerializerOptions{WriteIndented=true});
        },()=>!busy&&SelectedPacket!=null&&SelectedDevice!=null&&captureReport!=null);
        AnnotateCommand=new ActionCommand(()=>{research=research with{Annotations=research.Annotations.Append(new(captureReport!.Sha256,SelectedPacket!.Number,Annotation,DateTimeOffset.UtcNow)).ToArray()};ResearchOutput=$"User observation saved in session for packet {SelectedPacket.Number}: {Annotation}";},()=>!busy&&SelectedPacket!=null&&captureReport!=null);
        PinPacketCommand=new ActionCommand(()=>{pinnedPacket=SelectedPacket;ResearchOutput=$"Pinned capture packet {pinnedPacket!.Number}; select another packet and compare.";},()=>SelectedPacket!=null);
        DiffPacketCommand=new ActionCommand(()=>ResearchOutput=CaptureAnalysis.DiffText(pinnedPacket!.Payload,SelectedPacket!.Payload),()=>pinnedPacket!=null&&SelectedPacket!=null);
        PinTransactionCommand=new ActionCommand(()=>{pinnedTransaction=SelectedTransaction;ExplorerResult=$"Pinned {pinnedTransaction!.Command} execution {pinnedTransaction.Id}; select another execution of the same command.";},()=>SelectedTransaction!=null);
        DiffTransactionCommand=new ActionCommand(()=>ExplorerResult=CaptureAnalysis.DiffText(pinnedTransaction!.Response,SelectedTransaction!.Response),()=>pinnedTransaction!=null&&SelectedTransaction!=null&&pinnedTransaction.Command==SelectedTransaction.Command);
        ExportCaptureSelectionCommand=new ActionCommand(()=>_=Run(async token=>{var data=new{captureReport!.Sha256,Records=CapturePackets.ToArray(),Annotations=research.Annotations};var text=await Task.Run(()=>JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}),token);SaveText("Visible capture records (unredacted)|*.json",text);}),()=>!busy&&captureReport!=null);
        OpenBundleCommand=new ActionCommand(OpenBundle,()=>!busy);
        SaveResearchReportCommand=new ActionCommand(()=>SaveText("Research comparison report (unredacted)|*.txt",ResearchOutput),()=>!busy);
    }
    private ResearchSession CurrentResearch()
    {
        string Verified(string name)=>Facts.FirstOrDefault(f=>f.Name==name&&f.Value!="Unavailable"&&(f.Source.Contains("IEEE 1284")&&f.Availability=="Reported"||f.Source.StartsWith("USB ")))?.Value??"Unavailable / unverified";
        return research with{Name=SessionName,Notes=SessionNotes,Scenario=SessionScenario,Model=SelectedDevice==null?research.Model:Verified("Model"),Serial=SelectedDevice==null?research.Serial:Verified("Serial number"),
            DriverVersion=SelectedDevice?.DriverVersion??research.DriverVersion,Vid=SelectedDevice?.Vid??research.Vid,Pid=SelectedDevice?.Pid??research.Pid,Simulated=SelectedDevice?.Simulated??research.Simulated,
            Captures=captureReport==null||capturePath==null?research.Captures:[new(capturePath,captureReport.Sha256,captureReport.FileSize)]};
    }
    private void SaveResearch()
    {
        var picker=new SaveFileDialog{Filter="Research session (private observations)|*.json",FileName="research-session.json"};if(picker.ShowDialog()!=true)return;
        _=Run(async token=>{var value=CurrentResearch();await Task.Run(()=>ResearchSessionStore.Save(picker.FileName,value),token);research=value;ResearchOutput="Saved session metadata and capture references. Raw captures remain external; keep them alongside the session.";});
    }
    private void OpenResearch()
    {
        var picker=new OpenFileDialog{Filter="Research session|*.json"};if(picker.ShowDialog()!=true)return;
        _=Run(async token=>{
            var loaded=await Task.Run(()=>ResearchSessionStore.Load(picker.FileName),token);research=loaded;
            captureReport=null;originalCapture=null;capturePath=null;CapturePackets.Clear();SelectedPacket=null;pinnedPacket=null;
            SessionName=loaded.Name;SessionNotes=loaded.Notes;SessionScenario=loaded.Scenario;OnChanged(nameof(SessionName));OnChanged(nameof(SessionNotes));OnChanged(nameof(SessionScenario));
            ResearchOutput="Reopened user observations; no live hardware state changed. External captures are verified when comparing sessions.\n"+JsonSerializer.Serialize(loaded,new JsonSerializerOptions{WriteIndented=true});
            foreach(var c in loaded.Captures){try{if(!File.Exists(ResearchSessionStore.ResolveCapture(picker.FileName,c)))ResearchOutput+="\nMissing external capture: "+c.Path;}catch(Exception e){ResearchOutput+="\n"+e.Message;}}
        });
    }
    private void CompareSessions()
    {
        var a=new OpenFileDialog{Title="Choose baseline A research session",Filter="Research session|*.json"};if(a.ShowDialog()!=true)return;
        var b=new OpenFileDialog{Title="Choose baseline B research session",Filter="Research session|*.json"};if(b.ShowDialog()!=true)return;
        _=Run(async token=>ResearchOutput=await Task.Run(()=>{
            var sa=ResearchSessionStore.Load(a.FileName);var sb=ResearchSessionStore.Load(b.FileName);
            if(sa.Captures.Length!=1||sb.Captures.Length!=1)throw new InvalidDataException("Comparison requires exactly one capture reference per session; choose the intended capture explicitly.");
            return $"User condition A: {sa.Scenario}; B: {sb.Scenario}\n"+CaptureAnalysis.Compare(ResearchSessionStore.LoadCapture(a.FileName,sa.Captures[0],token),ResearchSessionStore.LoadCapture(b.FileName,sb.Captures[0],token));
        },token));
    }
    private void OpenBundle()
    {
        var picker=new OpenFileDialog{Filter="Diagnostic bundle|*.zip"};if(picker.ShowDialog()!=true)return;
        _=Run(async token=>{var bundle=await Task.Run(()=>BundleReader.Open(picker.FileName),token);OfflineFacts.Clear();foreach(var f in bundle.Facts)OfflineFacts.Add(f);OfflineTransactions.Clear();foreach(var t in bundle.Transactions)OfflineTransactions.Add(t);SelectedOfflineTransaction=OfflineTransactions.FirstOrDefault();OfflineTopology.Clear();foreach(var tree in bundle.Topology)OfflineTopology.Add(tree);OfflineDocuments.Clear();foreach(var d in bundle.Documents)OfflineDocuments.Add(d);SelectedOfflineDocument=OfflineDocuments.FirstOrDefault();OfflineSummary=$"IMPORTED EVIDENCE • {bundle.Mode} • {bundle.Source}\n"+string.Join("\n",bundle.Warnings);});
    }
}
