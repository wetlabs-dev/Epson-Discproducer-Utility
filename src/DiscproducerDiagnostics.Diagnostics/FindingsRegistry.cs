using System.Text.Json;
namespace DiscproducerDiagnostics.Diagnostics;
public enum FindingStatus { Observation,Hypothesis,IndependentlyReproduced,DocumentedProtocolBehavior,RejectedHypothesis }
public record ResearchFinding(string Id,string Description,string Model,string[] EvidenceReferences,string[] TransactionIds,
    int[] ByteOffsets,string[] ObservedValues,string Confidence,FindingStatus Status,string Notes);
public sealed record FindingsRegistry
{
    public int SchemaVersion {get;init;}=1;
    public ResearchFinding[] Findings {get;init;}=[];
    public TimelineMarker[] Markers {get;init;}=[];
}
public static class FindingsStore
{
    public static void Validate(FindingsRegistry registry)
    {
        if(registry.SchemaVersion!=1||registry.Findings==null||registry.Markers==null||registry.Findings.Length>10000||registry.Markers.Length>10000)throw new InvalidDataException("Unsupported or oversized findings registry");
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var f in registry.Findings)
        {
            if(f==null||string.IsNullOrWhiteSpace(f.Id)||!ids.Add(f.Id)||string.IsNullOrWhiteSpace(f.Description)||f.EvidenceReferences==null||f.TransactionIds==null||f.ByteOffsets==null||f.ObservedValues==null||!Enum.IsDefined(f.Status)||f.ByteOffsets.Any(i=>i<0)||f.ByteOffsets.Length!=f.ObservedValues.Length)throw new InvalidDataException("Malformed or duplicate finding");
            if(f.Status is FindingStatus.IndependentlyReproduced or FindingStatus.DocumentedProtocolBehavior && f.EvidenceReferences.Length==0)throw new InvalidDataException("Validation status requires supporting references");
        }
        foreach(var m in registry.Markers)if(m==null||m.CaptureHash==null||m.CaptureHash.Length!=64||!m.CaptureHash.All(Uri.IsHexDigit)||!double.IsFinite(m.OffsetSeconds)||Math.Abs(m.OffsetSeconds)>315576000)throw new InvalidDataException("Invalid timeline marker");
    }
    public static FindingsRegistry Load(string path)
    {
        var result=JsonSerializer.Deserialize<FindingsRegistry>(EvidenceFiles.ReadBounded(path,4*1024*1024),EvidenceFiles.Json)??throw new InvalidDataException("Empty registry");Validate(result);return result;
    }
    public static void Save(string path,FindingsRegistry registry)
    {
        Validate(registry);byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(registry,EvidenceFiles.Json);if(bytes.Length>4*1024*1024)throw new InvalidDataException("Registry exceeds 4 MiB");
        string temporary=path+".tmp-"+Guid.NewGuid().ToString("N");try{File.WriteAllBytes(temporary,bytes);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
public record ErrorReference(string HexCode,string Name,string Subsystem,string Description,string LikelyCauses,string Inspection,string Reference,string SourceStatus);
public static class Pp100ErrorReference
{
    public const string Source="Epson PP-100 Service Manual 411384703 Rev. D, printer mechanism table";
    public const string Url="https://www.scribd.com/document/808984453/PP-100-eng-sm-revD";
    private const string Status="Documented in original PP-100 manual; third-party-hosted copy; not a verified live response";
    private static ErrorReference Entry(string code,string name,string subsystem,string description,string causes,string page)=>new(code,name,subsystem,description,causes,"Record the reported code and inspect accessible areas visually while powered off. Do not force mechanisms, dismantle, adjust or reset; refer internal checks to Epson service.",Source+", p. "+page+"; "+Url,Status);
    // Short paraphrases of selected documented mappings; intentionally not a complete service table.
    public static IReadOnlyList<ErrorReference> All {get;}=Array.AsReadOnly(new[]{
        Entry("0x00000001","Carriage overspeed","Carriage / encoder","Carriage speed exceeds its control limit.","Obstruction or encoder feedback problem.","3-23"),
        Entry("0x000000F8","Tray locking fault","Disc transport","Tray PID control reports a lock.","Jamming, mechanical drag or encoder feedback.","3-24"),
        Entry("0x00000053","Tray homing fault","Disc transport","Tray home detection sequence failed.","Obstruction, encoder or drive problem.","3-25"),
        Entry("0x00000054","Disc not detected","Media sensing","Feed sequence did not detect media.","Media characteristics, sensor contamination or connection fault.","3-26"),
        Entry("0x00000080","Pressure lost","Ink pressure","Pressurization sequence failed.","Tubing leak, pressure sensing or pump problem.","3-26"),
        Entry("0x000000A0","Waste-fluid limit","Maintenance","Waste-fluid service threshold reached.","Maintenance component service life.","3-26"),
        Entry("0x000000DF","Driver overheating","Controller","Motor driver temperature is abnormal.","Driver circuit or motor load problem.","3-27")});
    public static ErrorReference[] Search(string query)
    {
        string normalized=query.Trim();if(normalized.StartsWith("0x",StringComparison.OrdinalIgnoreCase)&&uint.TryParse(normalized[2..],System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out var code))normalized=$"0x{code:X8}";
        return All.Where(e=>$"{e.HexCode} {e.Name} {e.Subsystem} {e.Description}".Contains(normalized,StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
