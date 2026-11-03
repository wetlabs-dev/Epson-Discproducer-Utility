using DiscproducerDiagnostics.Core;
namespace DiscproducerDiagnostics.Protocols;

// Fixed original-model hypothesis; never an address/key builder or an error-code decoder.
public static class EepromCandidate
{
    public static void ValidateIdentity(ReadOnlySpan<byte> response)
    {
        var id = Ieee1284.Parse(response);
        if ((id.Warnings.Length != 0 && !id.UsableWithWarning) || id.Find("MFG", "MANUFACTURER") != "EPSON" ||
            id.Find("MDL", "MODEL") != "PP-100PRN" ||
            !id.Find("BSFU").Split(',').Contains("WLP00105", StringComparer.Ordinal))
            throw new IOException("Requires driver-reported EPSON PP-100PRN / WLP00105 identity; identity may be cached and does not prove live compatibility");
    }

    public static EepromCandidateResult Interpret(D4SessionResult transport)
    {
        string? value = null, error = transport.Error;
        if (transport.Complete)
        {
            try { value = $"0x{HistoryReply.Parse(Convert.FromHexString("7C7C07000A5E41BEA03C00"), transport.Response).Value:X2}"; }
            catch (FormatException e) { error = e.Message; }
        }
        return new("Original PP-100 / WLP00105; candidate 0A5E, two-byte address", "0x003C", value,
            value == null ? "Unavailable; raw evidence retained; no fallback or retry" :
            "Addressed candidate byte observed; address meaning and error-code interpretation unverified; inspect cleanup separately",
            error, transport);
    }
}
