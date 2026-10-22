using System.Text;
using DiscproducerDiagnostics.Core;
namespace DiscproducerDiagnostics.Diagnostics;
public static class HexFormatter
{
    public static string Ascii(ReadOnlySpan<byte> bytes) => new(bytes.ToArray().Select(b=>b>=32&&b<=126?(char)b:'.').ToArray());
    public static string Format(ReadOnlySpan<byte> data,int displayLimit=16384)
    {
        var result=new StringBuilder("Offset    Hexadecimal                                       ASCII\n");
        int count=Math.Min(data.Length,displayLimit);
        for(int pos=0;pos<count;pos+=16){var row=data.Slice(pos,Math.Min(16,count-pos));result.Append(pos.ToString("X8")).Append("  ").Append(string.Join(" ",row.ToArray().Select(b=>b.ToString("X2"))).PadRight(47)).Append("  ").AppendLine(Ascii(row));}
        if(count<data.Length)result.AppendLine($"Display limited to {count} of {data.Length} bytes. Binary/JSON export preserves the full payload.");return result.ToString();
    }
    public static string Compare(Transaction app,CapturePacket capture)
    {
        int common=Math.Min(app.Response.Length,capture.Payload.Length);int difference=Enumerable.Range(0,common).FirstOrDefault(i=>app.Response[i]!=capture.Payload[i],-1);
        string bytes=difference>=0?$"First response/payload difference at offset 0x{difference:X}":app.Response.Length==capture.Payload.Length?"Byte sequences equal (does not establish protocol equivalence)":"Common prefix equal; lengths differ";
        return $"Application: {app.Command}; {app.Transport}; {app.Provenance}\nInterface: {app.Interface}; endpoint/recipient: {app.Endpoint}\nSupplied setup: {(app.SetupPacket==null?"Unobserved / not applicable":Convert.ToHexString(app.SetupPacket))}\nCaptured: bus {capture.Bus}, device {capture.Address}, endpoint {capture.EndpointText}, {capture.TransferType} {capture.Direction}\nCaptured setup: {(capture.SetupPacket.Length==0?"Not in selected record":Convert.ToHexString(capture.SetupPacket))}\nApplication request/response: {app.RequestLength}/{app.ResponseLength} bytes; {app.DurationMs:F3} ms\nCapture payload: {capture.Payload.Length} bytes; time {capture.TimestampUtc:O}; paired elapsed {capture.DurationMs:F3} ms; {capture.Correlation}; status {capture.StatusText}\n{bytes}\nWindows IOCTL buffers are not USB payloads. Compare framing and stage before interpreting differences. No automatic device identity or timing association is inferred.";
    }
}
