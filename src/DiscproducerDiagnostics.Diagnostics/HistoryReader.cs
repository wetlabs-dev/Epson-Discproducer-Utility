using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscproducerDiagnostics.Protocols;

namespace DiscproducerDiagnostics.Diagnostics;

public sealed record HistoryExchange(string Id, string RequestHex, string ResponseHex);
public sealed record HistoryEvidence(int SchemaVersion, string Model, string Firmware, string SourceSha256, HistoryExchange[] Exchanges);
public sealed record HistoryObservation(string Id, string RequestHex, string ResponseHex, string Outcome, string? Address, string? Value, string? Parameters, string? Warning);
public sealed record HistoryCandidate(string Address, string RoleHypothesis, string? Value, int EvidenceCount, string Status);
public sealed record HistoryReadiness(bool LiveReadEnabled, string Status, string[] Gaps);
public sealed record HistoryResult(int SchemaVersion, string ParserVersion, string EvidenceSha256, string SourceSha256,
    string Model, string Firmware, string Classification, HistoryReadiness Readiness, HistoryCandidate[] Candidates, HistoryObservation[] Observations);

/// <summary>Bounded, offline reader for manually extracted EEPROM application exchanges.</summary>
public static class HistoryReader
{
    public const int MaxFileBytes = 65536;
    public static HistoryReadiness Readiness => new(false, "Blocked: no verified original PP-100 live-read profile", [
        "Sourced candidate 0A5E / two-byte addressing exists; EEPROM hardware compatibility is unverified.",
        "Status-only D4 sequence succeeded in physical capture; full history retrieval stays blocked. GUI offers a separate experimental single-byte candidate read.",
        "Missing PP-100 addressed EEPROM reply fixture; current parser grammar is a cross-model hypothesis.",
        "Candidate history addresses are not reconciled with the service manual or validated for WLP00105."
    ]);

    public static HistoryResult Read(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (path.StartsWith("\\\\") || path.StartsWith("//") || path.Contains("://")) throw new ArgumentException("History input must be a local file.");
        using var stream = File.OpenRead(path);
        // Read a bounded buffer even if the input grows after opening.
        byte[] buffer = new byte[MaxFileBytes + 1];
        int length = 0, count;
        while (length < buffer.Length && (count = stream.Read(buffer, length, buffer.Length - length)) != 0)
        { length += count; token.ThrowIfCancellationRequested(); }
        if (length > MaxFileBytes) throw new InvalidDataException("History input exceeds 64 KiB.");
        return Parse(buffer.AsSpan(0, length), token);
    }

    public static HistoryResult Parse(ReadOnlySpan<byte> json, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (json.Length > MaxFileBytes) throw new InvalidDataException("History input exceeds 64 KiB.");
        var options = new JsonSerializerOptions { MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        using (var doc = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions { MaxDepth = 8 })) RejectDuplicates(doc.RootElement);
        var input = JsonSerializer.Deserialize<HistoryEvidence>(json, options) ?? throw new InvalidDataException("Missing history evidence.");
        if (input.SchemaVersion != 1 || input.Model != "PP-100" || input.Firmware != "WLP00105")
            throw new InvalidDataException("Only schema 1, original PP-100, firmware WLP00105 evidence is supported; identity is supplied, not authenticated.");
        if (input.SourceSha256 is null || input.SourceSha256.Length != 64 || !input.SourceSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("A source SHA-256 reference is required.");
        if (input.Exchanges is null || input.Exchanges.Length > 64) throw new InvalidDataException("At most 64 exchanges are accepted.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var observations = new List<HistoryObservation>();
        var values = new List<HistoryByte>();
        foreach (var exchange in input.Exchanges)
        {
            token.ThrowIfCancellationRequested();
            if (exchange is null || string.IsNullOrWhiteSpace(exchange.Id) || exchange.Id.Length > 128 || !ids.Add(exchange.Id) ||
                exchange.RequestHex is null || exchange.ResponseHex is null || exchange.RequestHex.Length > 128 || exchange.ResponseHex.Length > 512)
                throw new InvalidDataException("Invalid, duplicate or oversized exchange metadata.");
            try
            {
                var value = HistoryReply.Parse(Convert.FromHexString(exchange.RequestHex), Convert.FromHexString(exchange.ResponseHex));
                values.Add(value);
                observations.Add(new(exchange.Id, exchange.RequestHex, exchange.ResponseHex, "Candidate grammar matched; PP-100 semantics unverified",
                    $"0x{value.Address:X4}", $"0x{value.Value:X2}", value.ObservedReadParameters, null));
            }
            catch (FormatException e)
            {
                observations.Add(new(exchange.Id, exchange.RequestHex, exchange.ResponseHex, "Rejected; raw evidence retained", null, null, null, e.Message));
            }
        }
        bool mixed = values.Select(v => (v.ObservedReadParameters, v.AddressBytes)).Distinct().Count() > 1;
        var candidates = HistoryReply.CandidateAddresses.Select((address, index) => {
            var samples = values.Where(v => v.Address == address).ToArray();
            bool conflict = samples.Select(v => v.Value).Distinct().Count() > 1;
            return new HistoryCandidate($"0x{address:X4}", index == 0 ? "Latest code candidate" : index == 5 ? "History position candidate" : $"Older record {index} code candidate",
                samples.Length == 0 || conflict || mixed ? null : $"0x{samples[0].Value:X2}", samples.Length,
                mixed ? "Mixed parameters/address widths; no combined value" : conflict ? "Conflicting observations; no combined value" : samples.Length == 0 ? "Unavailable" : samples.Length == 1 ? "Single observation; meaning unverified" : "Repeated supplied value; not independent validation");
        }).ToArray();
        return new(1, "pp100-history-evidence-1", Convert.ToHexString(SHA256.HashData(json)), input.SourceSha256,
            input.Model, input.Firmware, "Offline observations only. Source hash and identity are supplied references, not verified provenance. No confirmed error-code meanings.",
            Readiness, candidates, observations.ToArray());
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!keys.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property."); RejectDuplicates(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
}
