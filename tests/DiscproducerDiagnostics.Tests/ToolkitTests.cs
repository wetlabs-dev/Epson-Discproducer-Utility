using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using DiscproducerDiagnostics.Core;
using DiscproducerDiagnostics.Diagnostics;
using DiscproducerDiagnostics.Cli;
using Xunit;
namespace DiscproducerDiagnostics.Tests;
public class ToolkitTests
{
    private static string Fixture=>Path.Combine(AppContext.BaseDirectory,"Fixtures","usb_u3v_sample.pcapng");
    private static CaptureReport Capture()=>EvidenceFiles.Capture(Fixture);
    private static string Temp(string extension)=>Path.Combine(Path.GetTempPath(),Guid.NewGuid()+extension);
    private static BundleSnapshot Snapshot()=>new(MockTransport.Device,[new("Model","PP-100","Simulated")],[],null,[],[],null,null,[],"");
    [Fact] public void ComparisonRejectsTooManyInputsBeforeOpeningFiles()=>Assert.Throws<InvalidDataException>(()=>EvidenceFiles.ComparisonInputs(Enumerable.Repeat("missing.pcapng",9)));
    [Fact] public void ComparisonLoadsTwoIndependentInputs()=>Assert.Equal(2,EvidenceFiles.ComparisonInputs([Fixture,Fixture]).Length);
    [Fact] public void EntropyAndFrequencyUseEvidenceCounts()
    {
        var p=ByteAnalysis.Profile([new byte[]{0xAA,1,9},new byte[]{0xAA,2,9},new byte[]{0xAA,1,9},new byte[]{0xAA,2,9}]);
        Assert.Equal(1,p.CommonPrefix);Assert.Equal(1,p.CommonSuffix);Assert.Equal(1,p.Offsets[1].Entropy);Assert.Equal(1,p.Offsets[1].ChangeFrequency);Assert.Equal(4,p.Offsets[1].Present);Assert.Equal(new[]{0,1,2},p.CandidateBoundaries);
    }
    [Fact] public void OneSampleDoesNotClaimRepeatability()=>Assert.All(ByteAnalysis.Profile([new byte[]{1,2}]).Offsets,o=>Assert.Equal("Single observation",o.Evidence));
    [Fact] public void MissingBytesHaveExplicitCounts(){var p=ByteAnalysis.Profile([new byte[]{1,2},new byte[]{1}]);Assert.Equal(1,p.Offsets[1].Missing);Assert.Equal(0,p.CommonSuffix);}
    [Fact] public void SideBySideHighlightsChangesAndLength(){var rows=ByteAnalysis.SideBySide([1,2],[1,3,4]);Assert.False(rows[0].Changed);Assert.True(rows[1].Changed);Assert.Equal("--",rows[2].Left);}
    [Fact] public void ByteProfilesAreBounded(){Assert.True(ByteAnalysis.Profile([new byte[2000]]).Truncated);Assert.Throws<InvalidDataException>(()=>ByteAnalysis.Profile(Enumerable.Repeat(Array.Empty<byte>(),513).ToArray()));}
    [Fact] public void ByteProfileCancellation(){using var c=new CancellationTokenSource();c.Cancel();Assert.Throws<OperationCanceledException>(()=>ByteAnalysis.Profile([new byte[10]],c.Token));}
    [Fact] public void FingerprintsAreDeterministicAndContextScoped()
    {
        var capture=Capture();var a=Fingerprinting.Analyze(capture);var b=Fingerprinting.Analyze(capture);
        Assert.Equal(JsonSerializer.Serialize(a),JsonSerializer.Serialize(b));Assert.All(a.Groups,g=>{Assert.Contains("section=",g.Context);Assert.Contains("Observation",g.Classification);});Assert.Equal(UsbCaptureImporter.ParserVersion,a.ParserVersion);Assert.NotEmpty(a.Sequences);
    }
    [Fact] public void FingerprintingRetainsIncompleteWarning()=>Assert.False(Fingerprinting.Analyze(Capture()).Complete);
    [Fact] public void MultipleCaptureComparisonHasEvidenceCounts(){var result=JsonSerializer.Serialize(Fingerprinting.Compare([Capture(),Capture(),Capture()]));Assert.Contains("Counts",result);Assert.Contains("Cross-session similarity hypothesis",result);}
    [Fact] public void ComparisonSamplesEverySessionEvenWhenBaselineIsLong()
    {
        var p=new CapturePacket(1,0,0,null,1,2,0x81,"IN","Bulk",1,0,false,null,[],[1],1,null);
        var a=new CaptureReport("baseline","A",0,Enumerable.Range(1,40).Select(i=>p with{Number=i,UrbId=(ulong)i}).ToArray(),[],true);
        var b=a with{FileName="error",Packets=[p with{Payload=[2]}]};
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(Fingerprinting.Compare([a,b])));
        Assert.Equal(2,json.RootElement.GetProperty("CandidateComparisons")[0].GetProperty("Bytes").GetProperty("Offsets")[0].GetProperty("Distinct").GetInt32());
    }
    [Fact] public void ContextsDoNotMergeIdenticalDevices()
    {
        var c=Capture();var p=c.Packets[0];var report=c with{Packets=[p,p with{Number=2,Address=99}]};Assert.Equal(2,Fingerprinting.Analyze(report).Groups.Length);
    }
    [Fact] public void MarkerOffsetNeverChangesOriginalTime()
    {
        var c=Capture();var time=DateTimeOffset.Parse("2026-01-01T00:00:00Z");var events=EvidenceTimeline.Build(c,[new(c.Sha256,time,"Printer error","Observed",2)],10);var m=Assert.Single(events,e=>e.Type=="Printer error");Assert.Equal(time,m.OriginalTime);Assert.Equal(time.AddSeconds(12),m.DisplayTime);Assert.StartsWith("User observation",m.Provenance);
    }
    [Fact] public void NoDisconnectionIsInferredFromMissingCompletion()=>Assert.DoesNotContain(EvidenceTimeline.Build(Capture()),e=>e.Type=="Device disconnection");
    [Fact] public void TimelineFiltersDoNotMixDevices(){var events=EvidenceTimeline.Build(Capture());Assert.Empty(EvidenceTimeline.Filter(events,device:"999:999"));Assert.All(EvidenceTimeline.Filter(events,type:"Control"),e=>Assert.Equal("Control",e.Type));}
    [Fact] public void TimelineCsvEscapesQuotesAndFormulaCells(){var e=new TimelineEvent("x",null,null,"","","User marker","=CMD(\"x\")",null,"User");var csv=EvidenceTimeline.Csv([e]);Assert.Contains("'",""+csv);Assert.Contains("\"\"x\"\"",csv);}
    [Theory][InlineData(double.NaN)][InlineData(double.PositiveInfinity)][InlineData(1e12)]public void InvalidOffsetsRejected(double offset)=>Assert.Throws<InvalidDataException>(()=>EvidenceTimeline.Build(Capture(),offsetSeconds:offset));
    [Fact] public void RegistryRoundTripPreservesRejectedHypothesis()
    {
        string path=Temp(".json");var registry=new FindingsRegistry{Findings=[new("f1","candidate","PP-100",["capture-hash"],["packet:2"],[1],["FF"],"Low",FindingStatus.RejectedHypothesis,"Not reproducible")]};
        try{FindingsStore.Save(path,registry);Assert.Equal(FindingStatus.RejectedHypothesis,FindingsStore.Load(path).Findings[0].Status);}finally{File.Delete(path);}
    }
    [Fact] public void RegistryRequiresReferencesForDocumentedStatus()=>Assert.Throws<InvalidDataException>(()=>FindingsStore.Validate(new(){Findings=[new("id","description","model",[],[],[],[],"High",FindingStatus.DocumentedProtocolBehavior,"")]}));
    [Theory][InlineData("{\"SchemaVersion\":99}")][InlineData("{\"SchemaVersion\":1,\"Findings\":null}")][InlineData("bad json")]
    public void MalformedRegistryRejected(string text){string p=Temp(".json");try{File.WriteAllText(p,text);Assert.ThrowsAny<Exception>(()=>FindingsStore.Load(p));}finally{File.Delete(p);}}
    [Fact] public void ErrorReferenceNeverGuessesUnknownCode(){Assert.Empty(Pp100ErrorReference.Search("0xDEADBEEF"));Assert.Single(Pp100ErrorReference.Search("0x01"));Assert.All(Pp100ErrorReference.All,e=>{Assert.Contains("411384703",e.Reference);Assert.Contains("not a verified live",e.SourceStatus);});}
    [Fact] public void HtmlEscapesImportedScriptsAndHasNoExternalDependencies()
    {
        var b=new OfflineBundle("x","FullForensic",[new("device/identity.json","<script>alert('bad')</script><img src=https://evil>")],[]);
        string html=HtmlReport.Generate(b);Assert.DoesNotContain("<script>",html);Assert.Contains("&lt;script&gt;",html);Assert.DoesNotContain("<img",html);Assert.Contains("default-src 'none'",html);Assert.Equal(html,HtmlReport.Generate(b));
    }
    [Theory][InlineData("RedactedSummary")][InlineData("TechnicalDiagnostics")]
    public void HtmlNeverAddsForensicAnalysisToRedactedSource(string mode)
    {string html=HtmlReport.Generate(new("x",mode,[],[]),[Fingerprinting.Analyze(Capture())],new{Secret="SECRET"});Assert.DoesNotContain("SECRET",html);Assert.DoesNotContain(Capture().Sha256,html);}
    [Theory][InlineData(ExportMode.RedactedSummary)][InlineData(ExportMode.TechnicalDiagnostics)][InlineData(ExportMode.FullForensic)]
    public void CliInspectValidateReportSupportAllPrivacyModes(ExportMode mode)
    {
        string zip=Temp(".zip"),html=Temp(".html");try{
            BundleExporter.Export(zip,Snapshot(),mode,true);
            foreach(string command in new[]{"inspect","validate"}){var output=new StringWriter();Assert.Equal(0,CliApplication.Run([command,zip,"--json"],output,new StringWriter()));using var json=JsonDocument.Parse(output.ToString());Assert.Equal(mode.ToString(),json.RootElement.GetProperty("Mode").GetString());}
            Assert.Equal(0,CliApplication.Run(["report",zip,"--output",html],new StringWriter(),new StringWriter()));Assert.Contains(mode.ToString(),File.ReadAllText(html));
        }finally{File.Delete(zip);File.Delete(html);}
    }
    [Fact] public void CliAnalyzeReportsIncompleteExitAndDeterministicJson()
    {var a=new StringWriter();var b=new StringWriter();Assert.Equal(5,CliApplication.Run(["analyze",Fixture,"--json"],a,new StringWriter()));Assert.Equal(5,CliApplication.Run(["analyze",Fixture,"--json"],b,new StringWriter()));Assert.Equal(a.ToString(),b.ToString());}
    [Fact] public void CliCompareSupportsThreeInputs()=>Assert.Equal(5,CliApplication.Run(["compare",Fixture,Fixture,Fixture,"--json"],new StringWriter(),new StringWriter()));
    [Fact] public void CliNoHardwareCommandExists()=>Assert.Equal(2,CliApplication.Run(["scan"],new StringWriter(),new StringWriter()));
    [Fact] public void CliMissingFileHasStableExit()=>Assert.Equal(4,CliApplication.Run(["inspect",Temp(".zip")],new StringWriter(),new StringWriter()));
    [Fact] public void CliCancellationHasStableExit(){using var c=new CancellationTokenSource();c.Cancel();Assert.Equal(130,CliApplication.Run(["analyze",Fixture],new StringWriter(),new StringWriter(),c.Token));}
    [Theory][InlineData("C:/evil")][InlineData("folder:stream")][InlineData("../escape")]
    public void ArchiveUnsafeNamesRejected(string name){string p=Temp(".zip");try{using(var zip=ZipFile.Open(p,ZipArchiveMode.Create))zip.CreateEntry(name);Assert.Equal(3,CliApplication.Run(["validate",p],new StringWriter(),new StringWriter()));}finally{File.Delete(p);}}
    [Fact] public void DuplicateArchivePathsRejected(){string p=Temp(".zip");try{using(var zip=ZipFile.Open(p,ZipArchiveMode.Create)){zip.CreateEntry("a");zip.CreateEntry("A");}Assert.Throws<InvalidDataException>(()=>BundleReader.Open(p));}finally{File.Delete(p);}}
    [Fact] public void CompressionBombRejected(){string p=Temp(".zip");try{using(var zip=ZipFile.Open(p,ZipArchiveMode.Create)){using var stream=zip.CreateEntry("bomb",CompressionLevel.SmallestSize).Open();stream.Write(new byte[4*1024*1024]);}Assert.Throws<InvalidDataException>(()=>BundleReader.Open(p));}finally{File.Delete(p);}}
    [Fact] public void Schema3RawEvidenceMustBeIndexed(){string p=Temp(".zip");try{BundleExporter.Export(p,Snapshot(),ExportMode.FullForensic,true);using(var zip=ZipFile.Open(p,ZipArchiveMode.Update)){using var stream=zip.CreateEntry("extra.bin").Open();stream.WriteByte(1);}Assert.Throws<InvalidDataException>(()=>BundleReader.Open(p));}finally{File.Delete(p);}}
    [Fact] public void IndependentLinuxFixtureIsExplicitlyUnsupported()
    {var c=EvidenceFiles.Capture(Path.Combine(AppContext.BaseDirectory,"Fixtures","usb-hid.pcapng"));Assert.Empty(c.Packets);Assert.False(c.Complete);Assert.Contains(c.Warnings,w=>w.Contains("Unsupported link type"));}
    [Fact] public void MultipleIndependentSectionsRetainSeparateContexts()
    {byte[] bytes=File.ReadAllBytes(Fixture);var report=UsbCaptureImporter.Parse(bytes.Concat(bytes).ToArray(),"two-sections");Assert.Equal(220,report.Packets.Length);Assert.Equal(new[]{0,1},report.Packets.Select(p=>p.Section).Distinct());}
    [Fact] public void CorruptEveryBlockLengthIsGraceful()
    {
        var original=File.ReadAllBytes(Fixture);for(int offset=0;offset<original.Length;)
        {uint length=BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(offset+4));var altered=original.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(altered.AsSpan(offset+4),uint.MaxValue);Assert.False(UsbCaptureImporter.Parse(altered,"corrupt").Complete);offset+=checked((int)length);}
    }
    [Fact] public async Task CliExecutableIntegrationRunsOffline()
    {
        string root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!,"..","..",".."));
        var info=new ProcessStartInfo(Path.Combine(root,OperatingSystem.IsWindows()?"dotnet.exe":"dotnet")){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"cli","discdiag-cli.dll"));info.ArgumentList.Add("errors");info.ArgumentList.Add("0x01");info.ArgumentList.Add("--json");
        using var p=Process.Start(info)!;var output=p.StandardOutput.ReadToEndAsync();await p.WaitForExitAsync();Assert.Equal(0,p.ExitCode);Assert.Contains("0x00000001",await output);
        Assert.DoesNotContain("DiscproducerDiagnostics.Usb",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"cli","discdiag-cli.deps.json")));
    }
}
