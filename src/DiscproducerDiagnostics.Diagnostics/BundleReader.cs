using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DiscproducerDiagnostics.Core;
using DiscproducerDiagnostics.Protocols;
namespace DiscproducerDiagnostics.Diagnostics;
public record OfflineDocument(string Name,string Text);
public record OfflineBundle(string Source,string Mode,OfflineDocument[] Documents,string[] Warnings)
{
    public Fact[] Facts {get;init;}=[];
    public Transaction[] Transactions {get;init;}=[];
    public InspectionNode[] Topology {get;init;}=[];
}
public static class BundleReader
{
    public const long MaxExpandedSize=256L*1024*1024;
    public static OfflineBundle Open(string path, bool strict=false)
    {
        if(path.StartsWith("\\\\")||path.StartsWith("//")||path.Contains("://"))throw new InvalidDataException("Only local bundles are accepted");
        if(new FileInfo(path).Length>MaxExpandedSize)throw new InvalidDataException("Archive exceeds compressed size limit");
        using var zip=ZipFile.OpenRead(path);
        if(zip.Entries.Count>10000)throw new InvalidDataException("Too many ZIP entries");
        long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var e in zip.Entries)
        {
            total=checked(total+e.Length);
            if(strict && e.FullName.EndsWith(".json",StringComparison.OrdinalIgnoreCase)&&e.Length>16*1024*1024)throw new InvalidDataException("JSON exceeds validation size limit");
            if(total>MaxExpandedSize||e.Length>UsbCaptureImporter.MaxFileSize||!names.Add(e.FullName)||e.FullName.Contains("..")||e.FullName.StartsWith('/')||e.FullName.Contains('\\')||e.FullName.Contains(':')||e.FullName.Any(char.IsControl)||e.Length>1024*1024 && e.Length/Math.Max(1,e.CompressedLength)>1000)throw new InvalidDataException("Unsafe, duplicate or oversized ZIP entry");
        }
        string Read(ZipArchiveEntry e)
        {
            if(e.Length>16*1024*1024)throw new InvalidDataException("Text document exceeds 16 MiB limit");
            using var stream=e.Open();using var buffer=new MemoryStream();byte[] chunk=new byte[8192];int n;
            while((n=stream.Read(chunk))>0){if(buffer.Length+n>e.Length)throw new InvalidDataException("ZIP length mismatch");buffer.Write(chunk,0,n);}
            if(buffer.Length!=e.Length)throw new InvalidDataException("Truncated ZIP entry");return Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\uFEFF');
        }
        var manifest=zip.GetEntry("manifest.json")??throw new InvalidDataException("Missing diagnostic manifest");
        using var json=JsonDocument.Parse(Read(manifest));
        int schema=json.RootElement.GetProperty("ExportSchemaVersion").GetInt32();
        if(schema is not (2 or 3))throw new InvalidDataException("Unsupported bundle schema");
        string declaredMode=json.RootElement.GetProperty("Mode").GetString()??"";
        if(!Enum.TryParse<ExportMode>(declaredMode,out var parsedMode)||!Enum.IsDefined(parsedMode)||parsedMode.ToString()!=declaredMode)throw new InvalidDataException("Unknown privacy mode");
        var rawNames=zip.Entries.Where(e=>e.FullName.EndsWith(".bin",StringComparison.OrdinalIgnoreCase)||e.FullName.EndsWith(".pcapng",StringComparison.OrdinalIgnoreCase)).Select(e=>e.FullName).ToArray();
        if(parsedMode!=ExportMode.FullForensic && rawNames.Length>0)throw new InvalidDataException("Raw evidence conflicts with declared privacy mode");
        var warnings=new List<string>{"IMPORTED EVIDENCE — historical observations only; never a live connection. Redacted fields cannot be reconstructed."};
        if(zip.GetEntry("evidence-sha256.json") is { } hashes)
        {
            var expected=JsonSerializer.Deserialize<Dictionary<string,string>>(Read(hashes))??throw new InvalidDataException("Invalid evidence hashes");
            if(schema==3 && rawNames.Any(n=>!expected.ContainsKey(n)))throw new InvalidDataException("Unindexed raw evidence");
            foreach(var (name,hash) in expected)
            {
                var entry=zip.GetEntry(name)??throw new InvalidDataException("Missing hashed evidence: "+name);
                if(hash==null||hash.Length!=64||!hash.All(Uri.IsHexDigit))throw new InvalidDataException("Invalid SHA-256 value");
                using var stream=entry.Open();using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] chunk=new byte[8192];long read=0;int count;
                while((count=stream.Read(chunk))>0){read+=count;if(read>entry.Length)throw new InvalidDataException("Expanded size mismatch");digest.AppendData(chunk,0,count);}
                if(read!=entry.Length)throw new InvalidDataException("Truncated evidence");
                if(!Convert.ToHexString(digest.GetHashAndReset()).Equals(hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Evidence SHA-256 mismatch: "+name);
            }
        }
        else {if(schema==3 && rawNames.Length>0)throw new InvalidDataException("Missing raw evidence hash index");warnings.Add("No raw evidence hash index; integrity not verified.");}
        var documents=zip.Entries.Where(e=>e.FullName.EndsWith(".json")||e.FullName.EndsWith(".md")||e.FullName.EndsWith(".txt")||e.FullName.EndsWith(".log"))
            .OrderBy(e=>e.FullName,StringComparer.Ordinal).Select(e=>new OfflineDocument(e.FullName,e.Length>16*1024*1024?"Large document omitted from display; original retained in ZIP.":Read(e))).ToArray();
        foreach(var document in documents.Where(d=>d.Name.EndsWith(".json")&&!d.Text.StartsWith("Large document")))
        {using var parsed=JsonDocument.Parse(document.Text,new JsonDocumentOptions{MaxDepth=48});}
        if(schema==3 && (!json.RootElement.TryGetProperty("SessionId",out var sessionId)||!Guid.TryParse(sessionId.GetString(),out _)))throw new InvalidDataException("Schema 3 requires a valid session ID");
        foreach(string required in new[]{"device/identity.json","device/windows-device-properties.json","communications/transactions.json"})
            if(!names.Contains(required))throw new InvalidDataException("Missing bundle document: "+required);
        string mode=json.RootElement.GetProperty("Mode").GetString()??"Unknown";
        var result=new OfflineBundle(path,mode,documents,warnings.ToArray());
        if(mode=="FullForensic")
        {
            T? ReadObject<T>(string name) => documents.FirstOrDefault(d=>d.Name==name) is { } document && !document.Text.StartsWith("Large document") ? JsonSerializer.Deserialize<T>(document.Text) : default;
            var device=ReadObject<Device>("device/windows-device-properties.json");
            var descriptors=ReadObject<DescriptorReport>("device/usb-descriptors.json");
            if(device!=null && device.Properties==null || descriptors!=null && (descriptors.Descriptors==null||descriptors.Errors==null||descriptors.Descriptors.Any(d=>d==null||d.Bytes==null)))throw new InvalidDataException("Malformed topology records");
            result=result with{Facts=ReadObject<Fact[]>("device/identity.json")??[],Transactions=ReadObject<Transaction[]>("communications/transactions.json")??[],Topology=device==null?[]:[DescriptorDecoder.Tree(device,descriptors,[device])]};
        }
        if(result.Facts.Any(f=>f==null)||result.Transactions.Any(t=>t==null||t.Request==null||t.Response==null))throw new InvalidDataException("Malformed forensic records");
        return result;
    }
}
