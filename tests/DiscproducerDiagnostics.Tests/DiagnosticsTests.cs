using System.Text;
using System.Text.Json.Nodes;
using System.IO.Compression;
using DiscproducerDiagnostics.Core;
using DiscproducerDiagnostics.Protocols;
using DiscproducerDiagnostics.Diagnostics;
using Xunit;
using DiscproducerDiagnostics.Usb;
namespace DiscproducerDiagnostics.Tests;
public class DiagnosticsTests
{
    private static byte[] Identity(string text)
    {
        byte[] body = Encoding.ASCII.GetBytes(text), result = new byte[body.Length + 2];
        result[0] = (byte)(result.Length >> 8); result[1] = (byte)result.Length; body.CopyTo(result, 2); return result;
    }
    [Fact] public void ParsesAliasesAndPreservesRaw()
    {
        var p = Ieee1284.Parse(Identity("MANUFACTURER:EPSON;MODEL:PP-100;SN:ABC:123;"));
        Assert.Equal("EPSON",p.Find("MFG","MANUFACTURER")); Assert.Equal("ABC:123",p.Find("SN")); Assert.Contains("MODEL:PP-100", p.Raw); Assert.Empty(p.Warnings);
    }
    [Fact] public void MalformedFieldsDoNotCrashOrInventValues()
    {
        var p = Ieee1284.Parse([0,255,63]); Assert.NotEmpty(p.Warnings); Assert.Equal("Unavailable",p.Find("SN"));
        p = Ieee1284.Parse(Identity("MFG:EPSON;bad;MFG:OTHER;")); Assert.Equal("EPSON",p.Find("MFG")); Assert.Equal(2,p.Warnings.Length);
        Assert.NotEmpty(Ieee1284.Parse([]).Warnings);
    }
    [Fact] public void DeclaredLengthExcludesTrailingGarbage()
    {
        var good = Identity("SN:VERIFIED;"); Assert.Equal("VERIFIED", Ieee1284.Parse(good.Concat(Encoding.ASCII.GetBytes("SN:WRONG;")).ToArray()).Find("SN"));
    }
    [Fact] public void DescriptorParserHandlesSequences()
    {
        var p = UsbDescriptors.Parse([9,4,0,0,1,7,1,2,0,7,5,0x81,2,64,0,0]); Assert.Equal(2,p.Length); Assert.Equal(5,p[1].Type);
    }
    [Theory] [InlineData(new byte[]{0,4})] [InlineData(new byte[]{9,4,1})] [InlineData(new byte[]{2,1})]
    public void DescriptorParserRejectsInvalidLengths(byte[] data) => Assert.Throws<FormatException>(() => UsbDescriptors.Parse(data));
    [Fact] public void PortStatusUsesOnlyDocumentedBits()
    {
        Assert.Contains("No class error", StatusDecoder.Port(0x18)); Assert.Contains("Error asserted",StatusDecoder.Port(0x30)); Assert.Contains("empty",StatusDecoder.Port(0x30)); Assert.Contains("offline",StatusDecoder.Port(0));
        Assert.Equal(StatusDecoder.Port(0x18),StatusDecoder.Port(0xDF)); Assert.Contains("not proof",StatusDecoder.Spooler(0)); Assert.Contains("Offline",StatusDecoder.Spooler(128));
    }
    [Theory] [InlineData("USB\\VID_04B8&PID_1234",true)] [InlineData("USB\\VID_04b8&PID_ABCD&MI_01",true)] [InlineData("USB\\VID_04B80&PID_1234",false)] [InlineData("EPSON PP-100",false)]
    public void MatchingUsesActualVid(string id, bool expected) => Assert.Equal(expected,DeviceMatching.IsEpson(id));
    [Fact] public void QueueNamesNeverProveAssociation() => Assert.False(DeviceMatching.QueueMatches(MockTransport.Device,new("Epson PP-100","USB001","Epson",0,0,null)));
    [Theory] [InlineData("reset")] [InlineData("write-eeprom")] [InlineData("0x220038")] [InlineData("")]
    public void UnknownCommandsRejected(string command) => Assert.Throws<NotSupportedException>(() => CommandRegistry.Authorize(command));
    [Fact] public async Task TransportAlsoRejectsUnknownCommands()
    {
        await using var t = new MockTransport(MockScenario.Ready); await t.ConnectAsync(default);
        await Assert.ThrowsAsync<NotSupportedException>(() => t.QueryAsync("clean",TimeSpan.FromSeconds(1),default));
    }
    [Fact] public async Task TimeoutIsBoundedAndCancellationPropagates()
    {
        await using var t = new MockTransport(MockScenario.Timeout); await t.ConnectAsync(default);
        var result = await t.QueryAsync("ieee1284",TimeSpan.FromMilliseconds(20),default); Assert.Equal(1460,result.ErrorCode);
        using var c = new CancellationTokenSource(); c.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t.QueryAsync("ieee1284",TimeSpan.FromSeconds(1),c.Token));
    }
    [Theory] [InlineData(MockScenario.Disconnected,1167)] [InlineData(MockScenario.Unsupported,50)]
    public async Task FailuresAreExplicit(MockScenario scenario, int code)
    {
        await using var t = new MockTransport(scenario); await t.ConnectAsync(default); var r = await t.QueryAsync("port-status",TimeSpan.FromSeconds(1),default);
        Assert.False(r.Success); Assert.Equal(code,r.ErrorCode); Assert.True(r.Simulated); if (scenario == MockScenario.Disconnected) Assert.False(t.Connected);
    }
    [Fact] public async Task MissingSerialStaysUnavailable()
    {
        await using var t = new MockTransport(MockScenario.MissingSerial); await t.ConnectAsync(default);
        var facts = await new PrinterProtocol(t,TimeSpan.FromSeconds(1)).GetIdentityAsync(default);
        Assert.Equal("Unavailable",facts.Single(f => f.Name == "Serial number").Value); Assert.All(facts,f => Assert.StartsWith("SIMULATED",f.Source));
    }
    [Fact] public async Task MalformedStatusIsNotReportedAsHardwareStatus()
    {
        await using var t = new MockTransport(MockScenario.Malformed); await t.ConnectAsync(default);
        Assert.Equal("Unavailable",(await new PrinterProtocol(t,TimeSpan.FromSeconds(1)).GetStatusAsync(default)).Availability);
    }
    [Fact] public void RedactionRemovesEncodedAndFreeTextIdentifiers()
    {
        var node = JsonNode.Parse("""{"InstanceId":"secret","Raw":"534543524554","Facts":[{"Name":"Serial","Value":"secret"}],"Error":"secret in failure","Success":true,"ErrorCode":5}""");
        Redaction.Apply(node); string json = node!.ToJsonString(); Assert.DoesNotContain("secret",json); Assert.DoesNotContain("534543524554",json); Assert.Contains("\"ErrorCode\":5",json);
    }
    [Fact] public void SessionDisposalIsIdempotent()
    {
        var session = new DiagnosticSession(); session.Event("test", "event"); session.Dispose(); session.Dispose();
    }
    [Fact] public void RedactionRemovesQueueNamesFromProvenanceAndFailureText()
    {
        var node = JsonNode.Parse("""{"Source":"Spooler: Private serial123","Availability":"Failed at serial123"}""");
        Redaction.Apply(node); Assert.DoesNotContain("serial123",node!.ToJsonString());
    }
    [Fact] public void RedactedBundleContainsSixFilesAndNoRawSerial()
    {
        string path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".zip");
        try
        {
            using var session = new DiagnosticSession(); session.Event("query","secret123"); session.Record(new(DateTimeOffset.Now,"test","RX","ieee1284","736563726574313233","secret123",9,1,true,null,null));
            session.Export(path,MockTransport.Device,[new("Serial number","secret123","IEEE1284")],[],true);
            using var zip = ZipFile.OpenRead(path); Assert.Equal(6,zip.Entries.Count);
            foreach (var entry in zip.Entries) { using var reader = new StreamReader(entry.Open()); string s = reader.ReadToEnd(); Assert.DoesNotContain("secret123",s); Assert.DoesNotContain("736563726574313233",s); Assert.DoesNotContain("SIMULATED\\\\VID_",s); }
        }
        finally { File.Delete(path); }
    }
    private static WorkerClient ProcessFixture()
    {
        string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string root = Path.GetFullPath(Path.Combine(runtime,"..","..",".."));
        string dotnet = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return new(dotnet, [Path.Combine(AppContext.BaseDirectory,"worker","DiscproducerDiagnostics.TestWorker.dll")]);
    }
    [Fact] public async Task RealWorkerIpcRoundTrips()
    {
        var reply = await ProcessFixture().RunAsync(new("echo"),TimeSpan.FromSeconds(10),default);
        Assert.Null(reply.Error);
    }
    [Fact] public async Task RealHungWorkerHasBoundedTimeout()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() => ProcessFixture().RunAsync(new("hang"),TimeSpan.FromMilliseconds(500),default));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }
    [Fact] public async Task RealWorkerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessFixture().RunAsync(new("hang"),TimeSpan.FromSeconds(10),cancellation.Token));
    }
    [Fact] public async Task WorkerCrashIsReported()
    {
        var error = await Assert.ThrowsAsync<IOException>(() => ProcessFixture().RunAsync(new("crash"),TimeSpan.FromSeconds(10),default));
        Assert.Contains("code 7",error.Message);
    }
}
