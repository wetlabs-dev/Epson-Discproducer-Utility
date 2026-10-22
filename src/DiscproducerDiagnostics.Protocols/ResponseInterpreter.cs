using DiscproducerDiagnostics.Core;
namespace DiscproducerDiagnostics.Protocols;
public static class ResponseInterpreter
{
    public static (string Text,bool Valid) Interpret(string command,byte[] bytes)
    {
        if(command=="ieee1284")
        {
            var id=Ieee1284.Parse(bytes);return (string.Join("; ",id.Fields.Select(f=>$"{f.Key}={f.Value}"))+"\n"+string.Join("; ",id.Warnings),id.Fields.Count>0&&id.Warnings.Length==0);
        }
        if(command=="port-status")return bytes.Length==4?(StatusDecoder.Port(bytes[0]),true):("Expected a four-byte Windows DWORD; response is malformed",false);
        return ("Raw host API response retained; see structured inspector",false);
    }
}
