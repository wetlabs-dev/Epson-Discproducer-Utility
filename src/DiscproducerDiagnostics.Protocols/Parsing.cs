using System.Buffers.Binary;
using System.Text;
using DiscproducerDiagnostics.Core;
namespace DiscproducerDiagnostics.Protocols;
public record Identification(Dictionary<string,string> Fields, string Raw, string[] Warnings)
{
    public string Find(params string[] aliases) => aliases.Select(a => Fields.GetValueOrDefault(a)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "Unavailable";
}
public static class Ieee1284
{
    public static Identification Parse(ReadOnlySpan<byte> bytes)
    {
        var warnings = new List<string>();
        if (bytes.Length < 2) return new(new(StringComparer.OrdinalIgnoreCase), "", ["Missing length prefix"]);
        int declared = BinaryPrimitives.ReadUInt16BigEndian(bytes);
        if (declared < 2 || declared > bytes.Length) warnings.Add("Invalid or truncated length prefix; retained available bytes");
        int end = declared >= 2 && declared <= bytes.Length ? declared : bytes.Length;
        string raw = Encoding.ASCII.GetString(bytes[2..end]).TrimEnd('\0');
        var fields = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = part.IndexOf(':');
            if (colon <= 0) { warnings.Add("Malformed field"); continue; }
            string key = part[..colon].Trim(), value = part[(colon + 1)..].Trim();
            if (!fields.TryAdd(key, value)) warnings.Add("Duplicate field: " + key);
        }
        return new(fields, raw, warnings.ToArray());
    }
}
public static class StatusDecoder
{
    public static string Port(byte status)
    {
        var flags = new List<string>();
        if ((status & 8) == 0) flags.Add("Error asserted");
        flags.Add((status & 16) != 0 ? "Selected / online" : "Not selected / offline");
        if ((status & 32) != 0) flags.Add("Paper/media empty (generic class bit)");
        if ((status & 8) != 0) flags.Add("No class error asserted");
        return string.Join("; ", flags) + "; busy and PP-100 mechanism state unavailable";
    }
    public static string Spooler(uint status) => status == 0 ? "No queue status flags (not proof of hardware readiness)" : string.Join(", ", Flags.Where(f => (status & f.Item1) != 0).Select(f => f.Item2).Append($"raw 0x{status:X8}"));
    private static readonly (uint,string)[] Flags = [(1,"Paused"),(2,"Error"),(4,"Pending deletion"),(8,"Paper jam"),(16,"Paper out"),(32,"Manual feed"),(64,"Paper problem"),(128,"Offline"),(256,"I/O active"),(512,"Busy"),(1024,"Printing"),(2048,"Output bin full"),(4096,"Unavailable"),(8192,"Waiting"),(16384,"Processing"),(32768,"Initializing"),(65536,"Warming up"),(131072,"Toner low"),(262144,"No toner"),(524288,"Page punt"),(1048576,"User intervention"),(2097152,"Out of memory"),(4194304,"Door open"),(8388608,"Server unknown"),(16777216,"Power save")];
}
public record UsbDescriptor(byte Type, string Hex);
public static class UsbDescriptors
{
    // Pure parser for future retrieved descriptor streams; discovery metadata is never passed as USB bytes.
    public static UsbDescriptor[] Parse(ReadOnlySpan<byte> bytes)
    {
        var result = new List<UsbDescriptor>();
        while (!bytes.IsEmpty)
        {
            if (bytes.Length < 2 || bytes[0] < 2 || bytes[0] > bytes.Length) throw new FormatException("Truncated or invalid USB descriptor");
            int length = bytes[0];
            if ((bytes[1] == 1 && length < 18) || (bytes[1] is 2 or 4 && length < 9) || (bytes[1] == 5 && length < 7)) throw new FormatException("Short standard USB descriptor");
            result.Add(new(bytes[1], Convert.ToHexString(bytes[..length]))); bytes = bytes[length..];
        }
        return result.ToArray();
    }
}
public sealed class PrinterProtocol(IDeviceTransport transport, TimeSpan timeout) : IPrinterProtocol
{
    public async Task<Fact[]> GetIdentityAsync(CancellationToken token)
    {
        var response = await transport.QueryAsync("ieee1284", timeout, token);
        string source = response.Simulated ? "SIMULATED IEEE 1284" : "IEEE 1284 via Windows USB printer driver";
        if (!response.Success) return [new("IEEE 1284 identification", "Unavailable", source, response.Error ?? "Failed")];
        var id = Ieee1284.Parse(response.Response);
        var result = new List<Fact>();
        foreach (var (name,value) in new[] { ("Manufacturer",id.Find("MFG","MANUFACTURER")),("Model",id.Find("MDL","MODEL")),("Serial number",id.Find("SN","SERN","SERIALNUMBER","SERIAL NUMBER")),("Command languages",id.Find("CMD","COMMAND SET")),("Description",id.Find("DES","DESCRIPTION")),("Class",id.Find("CLS","CLASS")) })
            result.Add(new(name, value, source, value == "Unavailable" ? "Unavailable" : "Reported"));
        result.Add(new("Identity parsing", id.Warnings.Length == 0 ? "Valid fields parsed" : string.Join("; ",id.Warnings), source));
        return result.ToArray();
    }
    public async Task<StatusResult> GetStatusAsync(CancellationToken token)
    {
        var r = await transport.QueryAsync("port-status", timeout, token);
        string source = r.Simulated ? "SIMULATED USB printer class" : "USB printer class via Windows USB printer driver";
        return new(source, DateTimeOffset.Now, Convert.ToHexString(r.Response), r.Success && r.Response.Length >= 4 ? StatusDecoder.Port(r.Response[0]) : "Unavailable", r.Success && r.Response.Length >= 4 ? "Driver-reported; may be cached" : "Unavailable", r.Error ?? (r.Response.Length < 4 ? "Missing DWORD status response" : null));
    }
}
