using System.Text.Json;
using DiscproducerDiagnostics.Core;
namespace DiscproducerDiagnostics.Diagnostics;
public static class EvidenceFiles
{
    public static void RequireLocal(string path)
    {
        if(path.StartsWith("\\\\",StringComparison.Ordinal)||path.StartsWith("//",StringComparison.Ordinal)||path.Contains("://",StringComparison.Ordinal))throw new InvalidDataException("Only local evidence files are accepted");
    }
    public static byte[] ReadBounded(string path,long maximum)
    {
        RequireLocal(path);
        using var stream=File.OpenRead(path);
        if(stream.Length>maximum)throw new InvalidDataException("Evidence exceeds size limit");
        using var output=new MemoryStream();byte[] buffer=new byte[8192];int n;
        while((n=stream.Read(buffer))>0){if(output.Length+n>maximum)throw new InvalidDataException("Evidence grew beyond size limit");output.Write(buffer,0,n);}
        return output.ToArray();
    }
    public static CaptureReport Capture(string path,CancellationToken token=default)=>UsbCaptureImporter.Parse(ReadBounded(path,UsbCaptureImporter.MaxFileSize),Path.GetFileName(path),token);
    public static CaptureReport[] Captures(string path,CancellationToken token=default)
    {
        if(Path.GetExtension(path).Equals(".pcapng",StringComparison.OrdinalIgnoreCase))return [Capture(path,token)];
        if(Path.GetExtension(path).Equals(".zip",StringComparison.OrdinalIgnoreCase))
        {
            var bundle=BundleReader.Open(path);if(bundle.Mode!="FullForensic")throw new InvalidDataException("Redacted bundles omit capture bytes");
            using var zip=System.IO.Compression.ZipFile.OpenRead(path);var entry=zip.GetEntry("captures/original.pcapng");
            if(entry==null)throw new InvalidDataException("No original capture in bundle");
            using var stream=entry.Open();using var memory=new MemoryStream();stream.CopyTo(memory);
            return [UsbCaptureImporter.Parse(memory.ToArray(),"original.pcapng",token)];
        }
        var session=ResearchSessionStore.Load(path);
        if(session.Captures.Length is 0 or >8)throw new InvalidDataException("Comparison requires 1–8 capture references per session");
        return session.Captures.Select(c=>ResearchSessionStore.LoadCapture(path,c,token)).ToArray();
    }
    public static CaptureReport[] ComparisonInputs(IEnumerable<string> paths,CancellationToken token=default)
    {
        var inputs=paths.Take(9).ToArray();
        if(inputs.Length is <2 or >8)throw new InvalidDataException("Compare requires 2–8 inputs");
        long budget=0;int count=0;
        foreach(var path in inputs)
        {
            token.ThrowIfCancellationRequested();RequireLocal(path);
            if(Path.GetExtension(path).Equals(".json",StringComparison.OrdinalIgnoreCase))
            {
                var session=ResearchSessionStore.Load(path);
                count+=session.Captures.Length;
                foreach(var reference in session.Captures)budget=checked(budget+new FileInfo(ResearchSessionStore.ResolveCapture(path,reference)).Length);
            }
            else {count++;budget=checked(budget+(Path.GetExtension(path).Equals(".zip",StringComparison.OrdinalIgnoreCase)?UsbCaptureImporter.MaxFileSize:new FileInfo(path).Length));}
            if(count>8||budget>256L*1024*1024)throw new InvalidDataException("Comparison exceeds eight captures or 256 MiB input budget");
        }
        return inputs.SelectMany(p=>Captures(p,token)).ToArray();
    }
    public static readonly JsonSerializerOptions Json=new(){WriteIndented=true,MaxDepth=48,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
}
