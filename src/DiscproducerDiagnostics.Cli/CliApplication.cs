using System.Text.Json;
using DiscproducerDiagnostics.Diagnostics;
namespace DiscproducerDiagnostics.Cli;
public static class CliApplication
{
    public const string Usage="discdiag-cli inspect|validate <bundle.zip> [--json]\ndiscdiag-cli analyze <capture.pcapng> [--json]\ndiscdiag-cli compare <baseline> <error-session> [more sessions, max 8] [--json]\ndiscdiag-cli report <bundle.zip> --output report.html [--json]\ndiscdiag-cli errors [hex-code or text] [--json]";
    public static int Run(string[] args,TextWriter output,TextWriter error,CancellationToken token=default)
    {
        bool json=args.Contains("--json",StringComparer.Ordinal);var positional=args.Where(a=>a!="--json").ToArray();
        void Emit(object value){if(json)output.WriteLine(JsonSerializer.Serialize(value,EvidenceFiles.Json));else {using var doc=JsonDocument.Parse(JsonSerializer.Serialize(value,EvidenceFiles.Json));Human(doc.RootElement,output,"");}}
        try
        {
            token.ThrowIfCancellationRequested();
            if(positional.Length==0||positional[0] is "--help" or "help"){output.WriteLine(Usage);return positional.Length==0?2:0;}
            string command=positional[0];object result;int exit=0;
            if(command=="errors"&&positional.Length<=2)result=Pp100ErrorReference.Search(positional.ElementAtOrDefault(1)??"");
            else if(command is "inspect" or "validate" && positional.Length==2)
            {
                var bundle=BundleReader.Open(positional[1],strict:command=="validate");result=new{SchemaVersion=1,Mode=bundle.Mode,Validation="Accepted; hashes checked where supplied, not authenticated authorship",bundle.Warnings,Documents=command=="inspect"?bundle.Documents:null};
            }
            else if(command=="analyze"&&positional.Length==2)
            {
                var capture=EvidenceFiles.Capture(positional[1],token);result=Fingerprinting.Analyze(capture,token);if(!capture.Complete)exit=5;
            }
            else if(command=="compare"&&positional.Length is >=3 and <=9)
            {
                var captures=EvidenceFiles.ComparisonInputs(positional.Skip(1),token);result=Fingerprinting.Compare(captures,token);if(captures.Any(c=>!c.Complete))exit=5;
            }
            else if(command=="report"&&positional.Length==4&&positional[2]=="--output")
            {
                if(positional[3].StartsWith("\\\\")||positional[3].StartsWith("//")||positional[3].Contains("://"))throw new ArgumentException("Output must be local");
                if(Path.GetFullPath(positional[1]).Equals(Path.GetFullPath(positional[3]),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new ArgumentException("Report must not overwrite its source bundle");
                var bundle=BundleReader.Open(positional[1]);FingerprintReport[]? fingerprints=null;
                if(bundle.Mode=="FullForensic"&&bundle.Documents.Any(d=>d.Name=="captures/capture-analysis.json"&&d.Text!="null"))
                {
                    using var zip=System.IO.Compression.ZipFile.OpenRead(positional[1]);if(zip.GetEntry("captures/original.pcapng")!=null)fingerprints=EvidenceFiles.Captures(positional[1],token).Select(c=>Fingerprinting.Analyze(c,token)).ToArray();
                }
                string html=HtmlReport.Generate(bundle,fingerprints);File.WriteAllText(positional[3],html,new System.Text.UTF8Encoding(false));result=new{Report="Written",PrivacyMode=bundle.Mode};
                if(fingerprints?.Any(f=>!f.Complete)==true)exit=5;
            }
            else {error.WriteLine(Usage);return 2;}
            Emit(result);return exit;
        }
        catch(OperationCanceledException){error.WriteLine("Cancelled");return 130;}
        catch(Exception e) when(e is InvalidDataException or JsonException or FormatException or OverflowException or KeyNotFoundException or InvalidOperationException){error.WriteLine("Invalid evidence: "+e.Message);return 3;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){error.WriteLine("File access failed: "+e.Message);return 4;}
        catch(ArgumentException e){error.WriteLine(e.Message);return 2;}
        catch(Exception e){error.WriteLine("Analysis failed: "+e.GetType().Name);return 6;}
    }
    private static void Human(JsonElement element,TextWriter writer,string indent)
    {
        if(element.ValueKind==JsonValueKind.Object)foreach(var p in element.EnumerateObject()){writer.WriteLine(indent+p.Name+":");Human(p.Value,writer,indent+"  ");}
        else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray()){writer.WriteLine(indent+"-");Human(item,writer,indent+"  ");}
        else writer.WriteLine(indent+(element.ValueKind==JsonValueKind.String?element.GetString():element.ToString()));
    }
}
