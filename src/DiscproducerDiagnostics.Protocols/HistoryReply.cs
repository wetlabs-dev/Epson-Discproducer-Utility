using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace DiscproducerDiagnostics.Protocols;

public record HistoryByte(ushort Address, byte Value, string ObservedReadParameters, int AddressBytes);

/// <summary>Offline application-message parser. No transport, request builder, or hardware authorization.</summary>
public static class HistoryReply
{
    // Hypotheses from the original PP-100 library, not a verified EEPROM map.
    public static IReadOnlyList<ushort> CandidateAddresses { get; } = Array.AsReadOnly<ushort>([0x3C, 0x109, 0x119, 0x129, 0x139, 0xF8]);

    public static HistoryByte Parse(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
    {
        if (request.Length is not (10 or 11) || request[0] != 0x7C || request[1] != 0x7C ||
            request[2] != request.Length - 4 || request[3] != 0 ||
            request[6] != 0x41 || request[7] != 0xBE || request[8] != 0xA0)
            throw new FormatException("Expected a single Ctl_ReplyEEP read application message; USB/D4 wrappers and other commands are not accepted.");
        ushort address = request.Length == 10 ? request[9] : BinaryPrimitives.ReadUInt16LittleEndian(request[9..]);
        if (!CandidateAddresses.Contains(address)) throw new FormatException("Address is outside the fixed candidate history set.");
        int width = request.Length - 9;
        // Explicit candidate grammar, based on other Epson models. No substring search,
        // padding removal, trailing-byte tolerance, or missing-response-to-zero conversion.
        byte[] prefix = "@BDC PS\r\nEE:"u8.ToArray();
        int digits = width * 2;
        if (response.Length != prefix.Length + digits + 4 || !response.StartsWith(prefix) ||
            response[^2] != (byte)';' || response[^1] != 0x0C)
            throw new FormatException("Reply does not match the candidate addressed EE grammar; retain raw evidence for review.");
        var hex = response.Slice(prefix.Length, digits + 2);
        foreach (byte b in hex)
            if (!(b is >= (byte)'0' and <= (byte)'9' or >= (byte)'A' and <= (byte)'F' or >= (byte)'a' and <= (byte)'f'))
                throw new FormatException("Reply contains non-hexadecimal address/data.");
        ushort echoed = ushort.Parse(Encoding.ASCII.GetString(hex[..digits]), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (echoed != address) throw new FormatException("Reply address does not match request.");
        byte value = byte.Parse(Encoding.ASCII.GetString(hex[digits..]), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new(address, value, Convert.ToHexString(request.Slice(4, 2)), width);
    }
}
