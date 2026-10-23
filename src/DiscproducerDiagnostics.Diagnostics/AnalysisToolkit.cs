using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DiscproducerDiagnostics.Core;
namespace DiscproducerDiagnostics.Diagnostics;
public record OffsetStatistics(int Offset,int Present,int Missing,int Distinct,double Entropy,double ChangeFrequency,string Values,string Evidence);
public record ByteProfile(int Samples,int[] Lengths,int CommonPrefix,int CommonSuffix,OffsetStatistics[] Offsets,int[] CandidateBoundaries,bool Truncated,string Disclaimer);
public record SideBySideRow(int Offset,string Left,string Right,bool Changed);
public static class ByteAnalysis
{
    public const int MaxSamples=512,MaxOffsets=1024;
    public static ByteProfile Profile(IReadOnlyList<byte[]> samples,CancellationToken token=default)
    {
        if(samples.Count==0) return new(0,[],0,0,[],[],false,"No samples");
        if(samples.Count>MaxSamples)throw new InvalidDataException("At most 512 samples per byte profile");
        int minimum=samples.Min(b=>b.Length),maximum=samples.Max(b=>b.Length),prefix=0,suffix=0;
        while(prefix<minimum && samples.All(b=>b[prefix]==samples[0][prefix])){token.ThrowIfCancellationRequested();prefix++;}
        while(suffix<minimum-prefix && samples.All(b=>b[b.Length-1-suffix]==samples[0][samples[0].Length-1-suffix])){token.ThrowIfCancellationRequested();suffix++;}
        var offsets=new List<OffsetStatistics>();var boundaries=new List<int>();bool? lastVariable=null;
        for(int i=0;i<Math.Min(maximum,MaxOffsets);i++)
        {
            token.ThrowIfCancellationRequested();var counts=samples.Where(b=>b.Length>i).GroupBy(b=>b[i]).OrderBy(g=>g.Key).Select(g=>(Value:g.Key,Count:g.Count())).ToArray();
            int present=counts.Sum(c=>c.Count),changes=0,pairs=0;
            for(int j=1;j<samples.Count;j++)if(samples[j].Length>i&&samples[j-1].Length>i){pairs++;if(samples[j][i]!=samples[j-1][i])changes++;}
            double entropy=counts.Sum(c=>{double p=(double)c.Count/present;return -p*Math.Log2(p);});
            bool variable=counts.Length>1||present<samples.Count;
            if(lastVariable!=variable)boundaries.Add(i);lastVariable=variable;
            offsets.Add(new(i,present,samples.Count-present,counts.Length,Math.Round(entropy,6),pairs==0?0:Math.Round((double)changes/pairs,6),string.Join(" ",counts.Select(c=>$"{c.Value:X2}:{c.Count}")),present<2?"Single observation":variable?"Observed variation":"Repeated stable observation"));
        }
        return new(samples.Count,samples.Select(b=>b.Length).ToArray(),prefix,suffix,offsets.ToArray(),boundaries.ToArray(),maximum>MaxOffsets,"Statistical region boundaries only; no field semantics inferred. Frequencies compare adjacent supplied samples; captures are not assumed synchronized.");
    }
    public static SideBySideRow[] SideBySide(byte[] a,byte[] b)=>Enumerable.Range(0,Math.Min(Math.Max(a.Length,b.Length),MaxOffsets))
        .Select(i=>new SideBySideRow(i,i<a.Length?$"{a[i]:X2}":"--",i<b.Length?$"{b[i]:X2}":"--",i>=a.Length||i>=b.Length||a[i]!=b[i])).ToArray();
}
public record Fingerprint(string Id,string Context,string Framing,int EvidenceCount,string Prefix,int[] PayloadLengths,double? MinimumMs,double? MeanMs,double? MaximumMs,int Failures,ByteProfile Bytes,string Classification);
public record RepeatedSequence(string Context,string Sequence,int Occurrences,string Classification);
public record FingerprintReport(string ParserVersion,string CaptureHash,bool Complete,string[] Warnings,Fingerprint[] Groups,RepeatedSequence[] Sequences,string Limits);
public static class Fingerprinting
{
    public static string Context(CapturePacket p)=>$"section={p.Section}/interface={p.CaptureInterface}/device={p.Bus}:{p.Address}";
    public static string Framing(LogicalTransfer t)=>$"USBPcap/{t.Request.TransferType}/{t.Request.EndpointText}/{t.Request.Direction}/setup={Convert.ToHexString(t.Request.SetupPacket)}/length={t.Payload.Length}/prefix={Convert.ToHexString(t.Payload.AsSpan(0,Math.Min(2,t.Payload.Length)))}";
    public static FingerprintReport Analyze(CaptureReport report,CancellationToken token=default)
    {
        var operations=CaptureAnalysis.Reconstruct(report);
        var groups=new List<Fingerprint>();
        foreach(var group in operations.GroupBy(t=>Context(t.Request)+"/"+Framing(t)).OrderBy(g=>g.Key,StringComparer.Ordinal).Take(128))
        {
            token.ThrowIfCancellationRequested();var values=group.ToArray();var first=values[0];var timing=values.Where(t=>t.DurationMs.HasValue).Select(t=>t.DurationMs!.Value).ToArray();
            string id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(group.Key)))[..16];
            var profile=ByteAnalysis.Profile(values.Take(32).Select(t=>t.Payload).ToArray(),token);
            groups.Add(new(id,Context(first.Request),Framing(first),values.Length,Convert.ToHexString(first.Payload.AsSpan(0,Math.Min(8,profile.CommonPrefix))),values.Select(t=>t.Payload.Length).Distinct().Order().ToArray(),timing.Length==0?null:timing.Min(),timing.Length==0?null:Math.Round(timing.Average(),6),timing.Length==0?null:timing.Max(),values.Count(t=>t.CompletionStatus is >0),profile,"Observation; prefix grouping is a hypothesis, not an Epson command definition"));
        }
        var sequences=new List<RepeatedSequence>();
        foreach(var context in operations.GroupBy(t=>Context(t.Request)).OrderBy(g=>g.Key,StringComparer.Ordinal))
        {
            var ordered=context.OrderBy(t=>t.Request.Number).ToArray();
            sequences.AddRange(ordered.Zip(ordered.Skip(1),(a,b)=>Framing(a)+" → "+Framing(b)).GroupBy(x=>x).Where(g=>g.Count()>1).OrderBy(g=>g.Key,StringComparer.Ordinal).Take(100)
                .Select(g=>new RepeatedSequence(context.Key,g.Key,g.Count(),"Repeated adjacent host operations; status-refresh association requires user markers")));
        }
        return new(report.ParserVersion,report.Sha256,report.Complete,report.Warnings,groups.ToArray(),sequences.Take(128).ToArray(),"First 128 groups/sequences; 32 samples and 1024 offsets per group. Prefix bytes are observations; do not replay.");
    }
    public static object Compare(IReadOnlyList<CaptureReport> reports,CancellationToken token=default)
    {
        if(reports.Count is <2 or >8)throw new InvalidDataException("Compare 2–8 captures/sessions");
        var analyses=reports.Select(r=>Analyze(r,token)).ToArray();
        // Never merge device addresses across captures as identity evidence.
        var candidates=reports.SelectMany((r,i)=>CaptureAnalysis.Reconstruct(r).Select(t=>(Capture:i,Transfer:t)))
            .GroupBy(x=>CaptureAnalysis.Signature(x.Transfer.Request)).OrderBy(g=>g.Key,StringComparer.Ordinal).Take(128)
            .Select(g=>new {Framing=g.Key,Counts=Enumerable.Range(0,reports.Count).Select(i=>g.Count(x=>x.Capture==i)).ToArray(),
                Bytes=ByteAnalysis.Profile(g.Take(32).Select(x=>x.Transfer.Payload).ToArray(),token),
                Status="Cross-session similarity hypothesis only; device identity and clocks require independent association"}).ToArray();
        return new {SchemaVersion=1,Captures=analyses,CandidateComparisons=candidates};
    }
}
public record TimelineMarker(string CaptureHash,DateTimeOffset Time,string Type,string Text,double OffsetSeconds=0);
public record TimelineEvent(string Context,DateTimeOffset? OriginalTime,DateTimeOffset? DisplayTime,string Device,string Endpoint,string Type,string Detail,int? Packet,string Provenance);
public static class EvidenceTimeline
{
    public static TimelineEvent[] Build(CaptureReport capture,IEnumerable<TimelineMarker>? markers=null,double offsetSeconds=0)
    {
        if(!double.IsFinite(offsetSeconds)||Math.Abs(offsetSeconds)>315576000)throw new InvalidDataException("Timestamp offset out of bounds");
        DateTimeOffset? Shift(DateTimeOffset? time,double seconds)=>time?.AddSeconds(seconds);
        var events=capture.Packets.Select(p=>new TimelineEvent(Fingerprinting.Context(p),p.TimestampUtc,Shift(p.TimestampUtc,offsetSeconds),p.Device,p.EndpointText,p.Status!=0?"Transfer failure":p.TransferType,$"URB {p.UrbId:X}; {p.Correlation}; status {p.StatusText}",p.Number,"USBPcap host observation")).ToList();
        foreach(var section in capture.Packets.GroupBy(p=>p.Section))
        {
            var first=section.First();events.Add(new($"section={section.Key}",first.TimestampUtc,Shift(first.TimestampUtc,offsetSeconds),"","","Session boundary","Capture section start; clock alignment unverified",first.Number,"Capture structure"));
        }
        foreach(var m in markers??[])
        {
            if(m.CaptureHash!=capture.Sha256)continue;
            if(!double.IsFinite(m.OffsetSeconds)||Math.Abs(m.OffsetSeconds)>315576000)throw new InvalidDataException("Marker timestamp offset out of bounds");
            events.Add(new(capture.Sha256,m.Time,Shift(m.Time,offsetSeconds+m.OffsetSeconds),"","",m.Type,m.Text,null,"User observation; not independently verified"));
        }
        return events.OrderBy(e=>e.DisplayTime).ThenBy(e=>e.Packet).ThenBy(e=>e.Type,StringComparer.Ordinal).ToArray();
    }
    public static TimelineEvent[] Filter(IEnumerable<TimelineEvent> events,string device="",string endpoint="",string type="",DateTimeOffset? from=null,DateTimeOffset? to=null)=>events.Where(e=>(device.Length==0||e.Device==device)&&(endpoint.Length==0||e.Endpoint.Equals(endpoint,StringComparison.OrdinalIgnoreCase))&&(type.Length==0||e.Type.Equals(type,StringComparison.OrdinalIgnoreCase))&&(from==null||e.DisplayTime>=from)&&(to==null||e.DisplayTime<=to)).ToArray();
    public static string Csv(IEnumerable<TimelineEvent> events)
    {
        string Cell(string text)=>"\""+(text.StartsWith('=')||text.StartsWith('+')||text.StartsWith('-')||text.StartsWith('@')?"'":"")+text.Replace("\"","\"\"")+"\"";
        return "OriginalTime,DisplayTime,Device,Endpoint,Type,Detail,Provenance\n"+string.Join("\n",events.Select(e=>string.Join(",",new[]{e.OriginalTime?.ToString("O",CultureInfo.InvariantCulture)??"",e.DisplayTime?.ToString("O",CultureInfo.InvariantCulture)??"",e.Device,e.Endpoint,e.Type,e.Detail,e.Provenance}.Select(Cell))));
    }
}
