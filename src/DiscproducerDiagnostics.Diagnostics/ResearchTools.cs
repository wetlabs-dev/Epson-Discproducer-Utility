using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using DiscproducerDiagnostics.Core;
namespace DiscproducerDiagnostics.Diagnostics;

public record ByteDifference(int Offset, string Before, string After);
public record LogicalTransfer(CapturePacket Request, CapturePacket[] Records, byte[] Payload, string Completeness, uint? CompletionStatus, double? DurationMs);
public static class CaptureAnalysis
{
    public static ByteDifference[] Diff(byte[] a, byte[] b, int limit = 256) => Enumerable.Range(0,Math.Max(a.Length,b.Length))
        .Where(i=>i>=a.Length||i>=b.Length||a[i]!=b[i]).Take(limit)
        .Select(i=>new ByteDifference(i,i<a.Length?$"{a[i]:X2}":"absent",i<b.Length?$"{b[i]:X2}":"absent")).ToArray();
    public static string DiffText(byte[] a, byte[] b) => $"Lengths {a.Length} → {b.Length}; first 256 changed offsets (hex values):\n"+
        string.Join("\n",Diff(a,b).Select(d=>$"0x{d.Offset:X6}: {d.Before} → {d.After}"))+"\nChanges are observations, not identified error codes or counters.";
    public static LogicalTransfer[] Reconstruct(CaptureReport report)
    {
        var children=report.Packets.Where(p=>p.RelatedPacket.HasValue).ToLookup(p=>p.RelatedPacket!.Value);
        var used=new HashSet<int>();var operations=new List<LogicalTransfer>();
        foreach(var p in report.Packets.Where(p=>!p.Completion && p.Stage is not (1 or 2 or 3)))
        {
            var records=new[]{p}.Concat(children[p.Number].Where(c=>c.Number!=p.Number && c.Correlation.StartsWith("Matched") || c.RelatedPacket==p.Number && c.Correlation=="Intermediate control data stage")).DistinctBy(c=>c.Number).OrderBy(c=>c.Number).ToArray();
            foreach(var record in records)used.Add(record.Number);
            var end=records.LastOrDefault(c=>c.Completion && c.Correlation=="Matched host-controller request");
            operations.Add(new(p,records,records.SelectMany(c=>c.Payload).ToArray(),end==null?"Incomplete or ambiguous":records.Any(c=>c.Warning!=null)?"Truncated":"Paired host records",end?.Status,end?.DurationMs));
        }
        foreach(var p in report.Packets.Where(p=>!used.Contains(p.Number)))operations.Add(new(p,[p],p.Payload,"Orphan / incomplete / ambiguous",p.Completion?p.Status:null,p.DurationMs));
        return operations.OrderBy(t=>t.Request.Number).ToArray();
    }
    public static CaptureAssociation[] Associate(CaptureReport report, Device device)
    {
        return Reconstruct(report).Where(t=>t.Request.SetupPacket is {Length:8} s && s[0]==0x80 && s[1]==6 && s[3]==1 && s[2]==0 && t.CompletionStatus==0)
            .Where(t=>t.Payload.Length>=18 && t.Payload[0]==18 && t.Payload[1]==1)
            .Select(t=>{
                string vid=$"{BinaryPrimitives.ReadUInt16LittleEndian(t.Payload.AsSpan(8)):X4}",pid=$"{BinaryPrimitives.ReadUInt16LittleEndian(t.Payload.AsSpan(10)):X4}";
                bool match=vid.Equals(device.Vid,StringComparison.OrdinalIgnoreCase)&&pid.Equals(device.Pid,StringComparison.OrdinalIgnoreCase);
                return new CaptureAssociation(report.Sha256,t.Request.Section,t.Request.CaptureInterface,t.Request.Bus,t.Request.Address,device.InstanceId,
                    match?"Candidate — VID/PID only":"Conflicting VID/PID",[$"Captured device descriptor {vid}:{pid}","Bus/address is capture-local; identical units and address reuse cannot be resolved from VID/PID", "Windows hub port number is not a USBPcap address"],false);
            }).DistinctBy(a=>(a.Section,a.CaptureInterface,a.Bus,a.Address,a.Confidence)).ToArray();
    }
    public static string Signature(CapturePacket p)=>$"{p.TransferType}/{p.EndpointText}/{p.Direction}/setup={Convert.ToHexString(p.SetupPacket)}";
    public static string Compare(CaptureReport a,CaptureReport b)
    {
        var left=Reconstruct(a);var right=Reconstruct(b);
        var lines=new List<string>{$"Baseline A {a.Sha256}\nBaseline B {b.Sha256}","Host URBs compared by transfer/endpoint/direction/exact setup, then occurrence order. This is a candidate alignment, not a proven protocol mapping."};
        var ga=left.GroupBy(t=>Signature(t.Request)).ToDictionary(g=>g.Key,g=>g.ToArray());
        var gb=right.GroupBy(t=>Signature(t.Request)).ToDictionary(g=>g.Key,g=>g.ToArray());
        foreach(string key in ga.Keys.Union(gb.Keys).Order().Take(1000))
        {
            var aa=ga.GetValueOrDefault(key)??[];var bb=gb.GetValueOrDefault(key)??[];
            lines.Add($"{key}: count {aa.Length} → {bb.Length}; payload bytes {aa.Sum(t=>t.Payload.Length)} → {bb.Sum(t=>t.Payload.Length)}; errors {aa.Count(t=>t.CompletionStatus is >0)} → {bb.Count(t=>t.CompletionStatus is >0)}; mean ms {Mean(aa)} → {Mean(bb)}");
            for(int i=0;i<Math.Min(Math.Min(aa.Length,bb.Length),5);i++)
                if(!aa[i].Payload.SequenceEqual(bb[i].Payload))lines.Add($"Occurrence {i+1}: "+DiffText(aa[i].Payload,bb[i].Payload));
        }
        lines.Add("Repeated adjacent sequence frequencies A:\n"+Sequences(left)+"\nB:\n"+Sequences(right));
        lines.Add("Report caps: 1,000 signatures, first five aligned occurrences, 256 byte differences each. Timing is host capture timing. No fatal-history or waste-counter field is identified.");
        return string.Join("\n",lines);
    }
    private static string Mean(LogicalTransfer[] t) { var values=t.Where(t=>t.DurationMs.HasValue).Select(t=>t.DurationMs!.Value).ToArray();return values.Length==0?"unavailable":values.Average().ToString("F3"); }
    private static string Sequences(LogicalTransfer[] t)=>string.Join("\n",t.Zip(t.Skip(1),(a,b)=>Signature(a.Request)+" → "+Signature(b.Request)).GroupBy(x=>x).Where(g=>g.Count()>1).OrderByDescending(g=>g.Count()).Take(100).Select(g=>$"{g.Count()} × {g.Key}"));
}
public static class ResearchSessionStore
{
    public const int MaxSessionBytes=4*1024*1024;
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,MaxDepth=32};
    public static void Save(string path,ResearchSession session)
    {
        Validate(session);byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(session,Options);
        if(bytes.Length>MaxSessionBytes)throw new InvalidDataException("Session exceeds 4 MiB limit");
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temporary,bytes);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static ResearchSession Load(string path)
    {
        EvidenceFiles.RequireLocal(path);
        if(new FileInfo(path).Length>MaxSessionBytes)throw new InvalidDataException("Session exceeds 4 MiB limit");
        var session=JsonSerializer.Deserialize<ResearchSession>(EvidenceFiles.ReadBounded(path,MaxSessionBytes),Options)??throw new InvalidDataException("Empty session");Validate(session);return session;
    }
    private static void Validate(ResearchSession s)
    {
        if(s.SchemaVersion!=1||s.Captures==null||s.Associations==null||s.Annotations==null||s.Captures.Length>100||s.Annotations.Length>10000)throw new InvalidDataException("Unsupported or malformed research session");
        foreach(var c in s.Captures)if(c==null||c.Sha256==null||c.Sha256.Length!=64||!c.Sha256.All(Uri.IsHexDigit)||c.Size<0||c.Size>UsbCaptureImporter.MaxFileSize)throw new InvalidDataException("Invalid capture reference");
    }
    public static string ResolveCapture(string sessionPath,CaptureReference reference)
    {
        if(string.IsNullOrWhiteSpace(reference.Path)||reference.Path.StartsWith("\\\\")||reference.Path.StartsWith("//")||reference.Path.Contains("://"))throw new InvalidDataException("Remote capture paths are not loaded");
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sessionPath))!,reference.Path));
    }
    public static CaptureReport LoadCapture(string sessionPath,CaptureReference reference,CancellationToken token=default)
    {
        string path=ResolveCapture(sessionPath,reference);var info=new FileInfo(path);
        if(!info.Exists)throw new FileNotFoundException("External capture missing. Re-import the original capture; the session remains readable.",path);
        if(info.Length!=reference.Size||info.Length>UsbCaptureImporter.MaxFileSize)throw new InvalidDataException("Capture size differs from saved evidence");
        byte[] bytes=EvidenceFiles.ReadBounded(path,UsbCaptureImporter.MaxFileSize);
        if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(reference.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Capture SHA-256 mismatch; comparison blocked");
        return UsbCaptureImporter.Parse(bytes,path,token);
    }
}
