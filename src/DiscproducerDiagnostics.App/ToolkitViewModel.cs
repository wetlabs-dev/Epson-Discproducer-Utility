using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using DiscproducerDiagnostics.Core;
using DiscproducerDiagnostics.Diagnostics;
using Microsoft.Win32;
namespace DiscproducerDiagnostics.App;
public sealed partial class MainViewModel
{
    private FindingsRegistry findings=new();
    private string toolkitResult="Import a USB capture to analyze evidence. No traffic is replayed.",toolkitJson="";
    private TimelineEvent? selectedTimelineEvent;
    private ResearchFinding? selectedFinding;
    public string ToolkitResult {get=>toolkitResult;private set{toolkitResult=value;OnChanged();}}
    public string TimelineDevice {get;set;}="";
    public string TimelineEndpoint {get;set;}="";
    public string TimelineType {get;set;}="";
    public string TimelineFrom {get;set;}="";
    public string TimelineTo {get;set;}="";
    public string TimelineOffset {get;set;}="0";
    public string MarkerTime {get;set;}="";
    public string MarkerOffset {get;set;}="0";
    public string MarkerType {get;set;}="Status refresh";
    public string MarkerText {get;set;}="User observation";
    public string[] MarkerTypes=>["Status refresh","Device disconnection","Printer error","User marker"];
    public ObservableCollection<TimelineEvent> TimelineEvents {get;}=[];
    public TimelineEvent? SelectedTimelineEvent {get=>selectedTimelineEvent;set{selectedTimelineEvent=value;OnChanged();OnChanged(nameof(TimelineDetails));}}
    public string TimelineDetails=>SelectedTimelineEvent==null?"Click a timeline point or select an event. Separate capture clocks are not assumed synchronized.":JsonSerializer.Serialize(SelectedTimelineEvent,EvidenceFiles.Json);
    public ObservableCollection<SideBySideRow> DiffRows {get;}=[];
    public ObservableCollection<ResearchFinding> Findings {get;}=[];
    public string FindingDescription {get;set;}="";
    public string FindingModel {get;set;}="PP-100 (unverified)";
    public string FindingReferences {get;set;}="";
    public string FindingTransactions {get;set;}="";
    public string FindingOffsets {get;set;}="";
    public string FindingValues {get;set;}="";
    public string FindingConfidence {get;set;}="Low";
    public string FindingNotes {get;set;}="";
    public FindingStatus FindingValidation {get;set;}=FindingStatus.Hypothesis;
    public FindingStatus[] FindingStatuses=>Enum.GetValues<FindingStatus>();
    public ResearchFinding? SelectedFinding
    {
        get=>selectedFinding;set
        {
            selectedFinding=value;OnChanged();if(value==null)return;
            FindingDescription=value.Description;FindingModel=value.Model;FindingReferences=string.Join(";",value.EvidenceReferences);FindingTransactions=string.Join(";",value.TransactionIds);FindingOffsets=string.Join(";",value.ByteOffsets);FindingValues=string.Join(";",value.ObservedValues);FindingConfidence=value.Confidence;FindingNotes=value.Notes;FindingValidation=value.Status;
            foreach(string name in new[]{nameof(FindingDescription),nameof(FindingModel),nameof(FindingReferences),nameof(FindingTransactions),nameof(FindingOffsets),nameof(FindingValues),nameof(FindingConfidence),nameof(FindingNotes),nameof(FindingValidation)})OnChanged(name);
        }
    }
    public string ErrorSearch {get;set;}="";
    public ObservableCollection<ErrorReference> ErrorReferences {get;}=[];
    private ErrorReference? selectedErrorReference;
    public ErrorReference? SelectedErrorReference {get=>selectedErrorReference;set{selectedErrorReference=value;OnChanged();OnChanged(nameof(ErrorReferenceDetails));}}
    public string ErrorReferenceDetails=>SelectedErrorReference==null?"Selected documented mappings for original PP-100 only. No device codes are queried.":JsonSerializer.Serialize(SelectedErrorReference,EvidenceFiles.Json);
    public ICommand FingerprintCommand {get;private set;}=null!;
    public ICommand MultiCompareCommand {get;private set;}=null!;
    public ICommand ByteProfileCommand {get;private set;}=null!;
    public ICommand SideBySideCommand {get;private set;}=null!;
    public ICommand TimelineFilterCommand {get;private set;}=null!;
    public ICommand AddMarkerCommand {get;private set;}=null!;
    public ICommand ExportTimelineJsonCommand {get;private set;}=null!;
    public ICommand ExportTimelineCsvCommand {get;private set;}=null!;
    public ICommand SaveToolkitCommand {get;private set;}=null!;
    public ICommand SaveFindingCommand {get;private set;}=null!;
    public ICommand NewFindingCommand {get;private set;}=null!;
    public ICommand ImportFindingsCommand {get;private set;}=null!;
    public ICommand ExportFindingsCommand {get;private set;}=null!;
    public ICommand SearchErrorsCommand {get;private set;}=null!;
    public ICommand GenerateHtmlCommand {get;private set;}=null!;
    private void ShowToolkit(object result)
    {
        toolkitJson=JsonSerializer.Serialize(result,EvidenceFiles.Json);ToolkitResult=toolkitJson.Length<=131072?toolkitJson:toolkitJson[..131072]+"\nPreview truncated; export analysis JSON for all bounded results.";
    }
    private void InitializeToolkit()
    {
        FingerprintCommand=new ActionCommand(()=>_=Run(async token=>{var report=captureReport!;ShowToolkit(await Task.Run(()=>Fingerprinting.Analyze(report,token),token));}),()=>!busy&&captureReport!=null);
        MultiCompareCommand=new ActionCommand(CompareMany,()=>!busy);
        ByteProfileCommand=new ActionCommand(()=>_=Run(async token=>{var samples=CapturePackets.Take(ByteAnalysis.MaxSamples).Select(p=>p.Payload).ToArray();ShowToolkit(await Task.Run(()=>ByteAnalysis.Profile(samples,token),token));}),()=>!busy&&CapturePackets.Count>0);
        SideBySideCommand=new ActionCommand(()=>{DiffRows.Clear();foreach(var row in ByteAnalysis.SideBySide(pinnedPacket!.Payload,SelectedPacket!.Payload))DiffRows.Add(row);},()=>!busy&&pinnedPacket!=null&&SelectedPacket!=null);
        TimelineFilterCommand=new ActionCommand(()=>_=Run(BuildTimeline),()=>!busy&&captureReport!=null);
        AddMarkerCommand=new ActionCommand(()=>_=Run(async token=>{
            var marker=new TimelineMarker(captureReport!.Sha256,ParseTime(MarkerTime)??throw new FormatException("Enter marker timestamp including UTC offset"),MarkerType,MarkerText,ParseOffset(MarkerOffset));
            var candidate=findings with{Markers=findings.Markers.Append(marker).ToArray()};FindingsStore.Validate(candidate);findings=candidate;await BuildTimeline(token);
        }),()=>!busy&&captureReport!=null);
        ExportTimelineJsonCommand=new ActionCommand(()=>SaveText("Timeline JSON|*.json",JsonSerializer.Serialize(TimelineEvents,EvidenceFiles.Json)),()=>!busy&&TimelineEvents.Count>0);
        ExportTimelineCsvCommand=new ActionCommand(()=>SaveText("Timeline CSV|*.csv",EvidenceTimeline.Csv(TimelineEvents)),()=>!busy&&TimelineEvents.Count>0);
        SaveToolkitCommand=new ActionCommand(()=>SaveText("Evidence analysis JSON (unredacted)|*.json",toolkitJson),()=>!busy&&toolkitJson.Length>0);
        SaveFindingCommand=new ActionCommand(()=>_=Run(token=>{
            string[] Split(string s)=>s.Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
            var offsets=Split(FindingOffsets).Select(s=>s.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?int.Parse(s[2..],NumberStyles.HexNumber,CultureInfo.InvariantCulture):int.Parse(s,CultureInfo.InvariantCulture)).ToArray();
            var value=new ResearchFinding(SelectedFinding?.Id??Guid.NewGuid().ToString("N"),FindingDescription,FindingModel,Split(FindingReferences),Split(FindingTransactions),offsets,Split(FindingValues),FindingConfidence,FindingValidation,FindingNotes);
            var candidate=findings with{Findings=findings.Findings.Where(f=>f.Id!=value.Id).Append(value).ToArray()};FindingsStore.Validate(candidate);findings=candidate;RefreshFindings();SelectedFinding=Findings.First(f=>f.Id==value.Id);return Task.CompletedTask;
        }),()=>!busy);
        NewFindingCommand=new ActionCommand(()=>{SelectedFinding=null;FindingDescription="";OnChanged(nameof(FindingDescription));},()=>!busy);
        ImportFindingsCommand=new ActionCommand(()=>{
            var picker=new OpenFileDialog{Filter="Findings registry|*.json"};if(picker.ShowDialog()!=true)return;
            _=Run(async token=>{findings=await Task.Run(()=>FindingsStore.Load(picker.FileName),token);RefreshFindings();});
        },()=>!busy);
        ExportFindingsCommand=new ActionCommand(()=>{
            var picker=new SaveFileDialog{Filter="Findings registry (private observations)|*.json",FileName="findings.json"};if(picker.ShowDialog()!=true)return;
            _=Run(token=>Task.Run(()=>FindingsStore.Save(picker.FileName,findings),token));
        },()=>!busy);
        SearchErrorsCommand=new ActionCommand(()=>{ErrorReferences.Clear();foreach(var e in Pp100ErrorReference.Search(ErrorSearch))ErrorReferences.Add(e);SelectedErrorReference=ErrorReferences.FirstOrDefault();},()=>!busy);
        foreach(var e in Pp100ErrorReference.All)ErrorReferences.Add(e);
        GenerateHtmlCommand=new ActionCommand(GenerateHtml,()=>!busy);
    }
    internal async Task VerifyToolkitUiAsync()
    {
        var time=DateTimeOffset.Parse("2026-01-01T00:00:00Z",CultureInfo.InvariantCulture);
        var packet=new CapturePacket(1,0,0,time,1,2,0x81,"IN","Bulk",1,0,false,null,[],[0xAA,1],2,null);
        captureReport=new("SIMULATED",new string('A',64),0,[packet,packet with{Number=2,TimestampUtc=time.AddSeconds(1),Payload=[0xAA,2]}],[],true);
        ShowToolkit(Fingerprinting.Analyze(captureReport));
        pinnedPacket=packet;SelectedPacket=captureReport.Packets[1];SideBySideCommand.Execute(null);
        findings=findings with{Markers=[new(captureReport.Sha256,time,"Printer error","SIMULATED observation")]};
        await BuildTimeline(default);SearchErrorsCommand.Execute(null);
        if(!DiffRows.Any(r=>r.Changed)||TimelineEvents.Count<3||ErrorReferences.Count==0)throw new InvalidOperationException("Toolkit populated UI smoke failed");
        SelectedTimelineEvent=TimelineEvents.First();SelectedErrorReference=ErrorReferences.First();
    }
    private void RefreshFindings(){Findings.Clear();foreach(var f in findings.Findings)Findings.Add(f);SelectedFinding=Findings.FirstOrDefault();}
    private static DateTimeOffset? ParseTime(string text)
    {
        if(string.IsNullOrWhiteSpace(text))return null;
        if(!System.Text.RegularExpressions.Regex.IsMatch(text,@"(Z|[+-]\d{2}:\d{2})$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))throw new FormatException("Timestamp must include Z or a UTC offset");
        return DateTimeOffset.Parse(text,CultureInfo.InvariantCulture);
    }
    private static double ParseOffset(string text)=>double.Parse(text,CultureInfo.InvariantCulture);
    private async Task BuildTimeline(CancellationToken token)
    {
        var from=ParseTime(TimelineFrom);var to=ParseTime(TimelineTo);if(from>to)throw new FormatException("Timeline start must precede end");
        double offset=ParseOffset(TimelineOffset);var report=captureReport!;var markers=findings.Markers.Concat(research.Annotations.Where(a=>a.CaptureSha256==report.Sha256).Select(a=>new TimelineMarker(a.CaptureSha256,a.TimestampUtc,"User marker",a.Observation))).ToArray();
        var events=await Task.Run(()=>EvidenceTimeline.Filter(EvidenceTimeline.Build(report,markers,offset),TimelineDevice,TimelineEndpoint,TimelineType,from,to).Take(10000).ToArray(),token);
        TimelineEvents.Clear();foreach(var e in events)TimelineEvents.Add(e);OnChanged(nameof(TimelineEvents));SelectedTimelineEvent=TimelineEvents.FirstOrDefault();
    }
    private void CompareMany()
    {
        var picker=new OpenFileDialog{Filter="Captures, sessions or bundles|*.pcapng;*.json;*.zip",Multiselect=true};if(picker.ShowDialog()!=true)return;
        _=Run(async token=>ShowToolkit(await Task.Run(()=>Fingerprinting.Compare(EvidenceFiles.ComparisonInputs(picker.FileNames,token),token),token)));
    }
    private void GenerateHtml()
    {
        var source=new OpenFileDialog{Filter="Diagnostic bundle|*.zip"};if(source.ShowDialog()!=true)return;
        var destination=new SaveFileDialog{Filter="Offline HTML report|*.html",FileName="report.html"};if(destination.ShowDialog()!=true)return;
        _=Run(async token=>{
            if(Path.GetFullPath(source.FileName).Equals(Path.GetFullPath(destination.FileName),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Cannot overwrite evidence");
            await Task.Run(()=>{
                var bundle=BundleReader.Open(source.FileName);FingerprintReport[]? analysis=null;
                using(var zip=System.IO.Compression.ZipFile.OpenRead(source.FileName))if(bundle.Mode=="FullForensic"&&zip.GetEntry("captures/original.pcapng")!=null)analysis=EvidenceFiles.Captures(source.FileName,token).Select(c=>Fingerprinting.Analyze(c,token)).ToArray();
                File.WriteAllText(destination.FileName,HtmlReport.Generate(bundle,analysis),new System.Text.UTF8Encoding(false));
            },token);Notice="Offline HTML report saved; source privacy omissions retained.";
        });
    }
}
