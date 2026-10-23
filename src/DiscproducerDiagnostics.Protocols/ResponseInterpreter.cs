using DiscproducerDiagnostics.Core;
namespace DiscproducerDiagnostics.Protocols;
public static class ResponseInterpreter
{
    public sealed record Interpretation(string Text, bool Valid, bool Partial = false);
    public static Interpretation Interpret(string command,byte[] bytes)
    {
        if(command=="ieee1284")
        {
            var id=Ieee1284.Parse(bytes);return new(string.Join("; ",id.Fields.Select(f=>$"{f.Key}={f.Value}"))+"\n"+string.Join("; ",id.Warnings),id.Fields.Count>0&&id.Warnings.Length==0,id.UsableWithWarning);
        }
        if(command=="port-status")return StatusDecoder.IsSupportedReply(bytes)
            ?new(StatusDecoder.Port(bytes[0])+(bytes.Length==1?"; one-byte driver reply (observed compatibility form; four-byte output buffer requested)":"; four-byte driver reply"),true)
            :new("Expected one returned status byte or a four-byte Windows DWORD; response retained but not decoded",false);
        return new("Raw host API response retained; see structured inspector",false);
    }
}
