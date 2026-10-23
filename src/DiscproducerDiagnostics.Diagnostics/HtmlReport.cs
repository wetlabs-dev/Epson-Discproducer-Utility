using System.Net;
using System.Text;
using System.Text.Json;
namespace DiscproducerDiagnostics.Diagnostics;
public static class HtmlReport
{
    public static string Generate(OfflineBundle bundle, FingerprintReport[]? fingerprints=null,object? comparisons=null)
    {
        string E(string? text)=>WebUtility.HtmlEncode(text??"");
        var html=new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\"><title>Discproducer evidence report</title><style>body{font:16px system-ui;max-width:1100px;margin:2rem auto;padding:1rem;color:#182d4b}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f3f5f9;padding:1rem}h2{border-bottom:1px solid #ccc}mark{background:#fff0ad}</style><h1>Discproducer evidence report</h1>");
        html.Append("<p>Imported offline evidence. Privacy mode: ").Append(E(bundle.Mode)).Append(". No live hardware validation.</p>");
        foreach(var warning in bundle.Warnings)html.Append("<p>").Append(E(warning)).Append("</p>");
        var sections=new[]{("Device identification and provenance","device/identity.json"),("USB topology","device/interfaces.json"),("Device and driver metadata","device/windows-device-properties.json"),("Driver and spooler","printer/queue.json"),("Communication methods","analysis/supported-operations.md"),("Queries and native errors","communications/transactions.json"),("Capture summary","captures/capture-analysis.json"),("User observations","research/session.json"),("Recommended next steps","analysis/recommended-next-steps.md")};
        foreach(var (title,path) in sections)
        {
            string content=bundle.Documents.FirstOrDefault(d=>d.Name==path)?.Text??"Unavailable in this source bundle";
            if(content.Length>65536)content=content[..65536]+"\nDisplay truncated at 65536 characters; retain the original bundle.";
            html.Append("<h2>").Append(E(title)).Append("</h2><pre>").Append(E(content)).Append("</pre>");
        }
        if(bundle.Mode=="FullForensic" && comparisons==null)
            comparisons=bundle.Transactions.GroupBy(t=>new{t.DeviceId,t.Transport,t.Command}).Where(g=>g.Count()>1).Take(16)
                .Select(g=>new{g.Key.Command,Executions=g.Count(),Profile=ByteAnalysis.Profile(g.Take(32).Select(t=>t.Response).ToArray()),Meaning="Repeated driver-response observations; field semantics unknown"}).ToArray();
        // Only data from the same forensic source may be added. Privacy-safe modes never receive payload analysis.
        html.Append("<h2>Protocol fingerprints</h2><pre>");
        if(bundle.Mode=="FullForensic"&&fingerprints!=null)html.Append(E(JsonSerializer.Serialize(fingerprints.Select(f=>new{f.ParserVersion,f.CaptureHash,f.Complete,f.Warnings,Groups=f.Groups.Select(g=>new{g.Id,g.Context,g.Framing,g.EvidenceCount,g.MinimumMs,g.MeanMs,g.MaximumMs,g.Failures,g.Classification}),f.Sequences}),EvidenceFiles.Json)));
        else html.Append("Unavailable: no original capture supplied or privacy mode omits payload evidence.");
        html.Append("</pre><h2>Comparison findings</h2><pre>").Append(E(bundle.Mode=="FullForensic"&&comparisons!=null?JsonSerializer.Serialize(comparisons,EvidenceFiles.Json):"No independent comparison evidence supplied; no field meanings inferred.")).Append("</pre></html>");
        return html.ToString();
    }
}
