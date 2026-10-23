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
    public static OfflineBundle Open(string path)
    {
        using var zip=ZipFile.OpenRead(path);
        if(zip.Entries.Count>10000)throw new InvalidDataException("Too many ZIP entries");
        long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var e in zip.Entries)
        {
            total=checked(total+e.Length);
            if(total>MaxExpandedSize||e.Length>UsbCaptureImporter.MaxFileSize||!names.Add(e.FullName)||e.FullName.Contains("..")||e.FullName.StartsWith('/')||e.FullName.Contains('\\'))throw new InvalidDataException("Unsafe, duplicate or oversized ZIP entry");
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
        var warnings=new List<string>{"IMPORTED EVIDENCE — historical observations only; never a live connection. Redacted fields cannot be reconstructed."};
        if(zip.GetEntry("evidence-sha256.json") is { } hashes)
        {
            var expected=JsonSerializer.Deserialize<Dictionary<string,string>>(Read(hashes))??throw new InvalidDataException("Invalid evidence hashes");
            foreach(var (name,hash) in expected)
            {
                var entry=zip.GetEntry(name)??throw new InvalidDataException("Missing hashed evidence: "+name);
                using var stream=entry.Open();if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Evidence SHA-256 mismatch: "+name);
            }
        }
        else warnings.Add("Older bundle has no evidence hash index; integrity not verified.");
        var documents=zip.Entries.Where(e=>e.FullName.EndsWith(".json")||e.FullName.EndsWith(".md")||e.FullName.EndsWith(".txt")||e.FullName.EndsWith(".log"))
            .Select(e=>new OfflineDocument(e.FullName,e.Length>16*1024*1024?"Large document omitted from display; original retained in ZIP.":Read(e))).ToArray();
        string mode=json.RootElement.GetProperty("Mode").GetString()??"Unknown";
        var result=new OfflineBundle(path,mode,documents,warnings.ToArray());
        if(mode=="FullForensic")
        {
            T? ReadObject<T>(string name) => documents.FirstOrDefault(d=>d.Name==name) is { } document && !document.Text.StartsWith("Large document") ? JsonSerializer.Deserialize<T>(document.Text) : default;
            var device=ReadObject<Device>("device/windows-device-properties.json");
            var descriptors=ReadObject<DescriptorReport>("device/usb-descriptors.json");
            result=result with{Facts=ReadObject<Fact[]>("device/identity.json")??[],Transactions=ReadObject<Transaction[]>("communications/transactions.json")??[],Topology=device==null?[]:[DescriptorDecoder.Tree(device,descriptors,[device])]};
        }
        return result;
    }
}
