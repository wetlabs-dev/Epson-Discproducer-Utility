using System.Text;
using System.Text.Json;
using DiscproducerDiagnostics.Cli;
using DiscproducerDiagnostics.Core;
using DiscproducerDiagnostics.Diagnostics;
using DiscproducerDiagnostics.Protocols;
using Xunit;
namespace DiscproducerDiagnostics.Tests;

public class HistoryReaderTests
{
    private static HistoryExchange Exchange(string id="frame:1/2",string reply="@BDC PS\r\nEE:003C01;\f") =>
        new(id,"7C7C0700123441BEA03C00",Convert.ToHexString(Encoding.ASCII.GetBytes(reply)));
    private static byte[] Evidence(params HistoryExchange[] exchanges)=>JsonSerializer.SerializeToUtf8Bytes(
        new HistoryEvidence(1,"PP-100","WLP00105",new string('A',64),exchanges));
    [Fact] public void ReadinessCannotAuthorizeHardware()
    {
        Assert.False(HistoryReader.Readiness.LiveReadEnabled);
        Assert.Equal(4,HistoryReader.Readiness.Gaps.Length);
        Assert.Throws<NotSupportedException>(()=>DiagnosticCommands.Authorize("history"));
    }
    [Fact] public void CandidateValueNeverBecomesConfirmedError()
    {
        var result=HistoryReader.Parse(Evidence(Exchange()));
        Assert.Equal("0x01",result.Candidates[0].Value);
        Assert.Contains("unverified",result.Candidates[0].Status);
        Assert.All(result.Candidates.Skip(1),c=>Assert.Null(c.Value));
        Assert.False(result.Readiness.LiveReadEnabled);
    }
    [Fact] public void SingleByteAddressReplySupportedAsCandidate()
    {
        var r=HistoryReply.Parse(Convert.FromHexString("7C7C0600123441BEA03C"),"@BDC PS\r\nEE:3C00;\f"u8);
        Assert.Equal(0,r.Value);Assert.Equal(1,r.AddressBytes);
    }
    [Theory]
    [InlineData("@BDC PS\r\nEE:003D01;\f")]
    [InlineData("@BDC PS\r\nEE:003C01;")]
    [InlineData("@BDC PS\r\nEE:003C0G;\f")]
    [InlineData("junk@BDC PS\r\nEE:003C01;\f")]
    [InlineData("@BDC PS\r\nEE:003C01;\fextra")]
    [InlineData("||:41:NA;\f")]
    [InlineData("")]
    public void InvalidRepliesRetainEvidenceAndNeverBecomeZero(string reply)
    {
        var result=HistoryReader.Parse(Evidence(Exchange(reply:reply)));
        Assert.Null(result.Candidates[0].Value);Assert.Equal(0,result.Candidates[0].EvidenceCount);
        Assert.Equal(Convert.ToHexString(Encoding.ASCII.GetBytes(reply)),result.Observations[0].ResponseHex);
        Assert.NotNull(result.Observations[0].Warning);
    }
    [Theory]
    [InlineData("7C7C0700123442BD213C00")]
    [InlineData("7C7C0700123441BEA00000")]
    [InlineData("7C7C0600123441BEA03C00")]
    [InlineData("0202001100007C7C0700123441BEA03C00")]
    [InlineData("ZZ")]
    public void OtherCommandsAddressesAndWrappersRejected(string request)
    {
        var result=HistoryReader.Parse(Evidence(Exchange() with{RequestHex=request}));
        Assert.Null(result.Candidates[0].Value);Assert.NotNull(result.Observations[0].Warning);
    }
    [Fact] public void ConflictsAndMixedParametersNeverProduceCombinedValues()
    {
        var conflict=HistoryReader.Parse(Evidence(Exchange(),Exchange("other","@BDC PS\r\nEE:003C02;\f")));
        Assert.Null(conflict.Candidates[0].Value);Assert.Equal(2,conflict.Candidates[0].EvidenceCount);
        var mixed=HistoryReader.Parse(Evidence(Exchange(),Exchange("other") with{RequestHex="7C7C0700567841BEA03C00"}));
        Assert.Null(mixed.Candidates[0].Value);Assert.Contains("Mixed",mixed.Candidates[0].Status);
    }
    [Fact] public void RepeatedValuesHaveCountsNotIndependentValidation()
    {
        var result=HistoryReader.Parse(Evidence(Exchange(),Exchange("other")));
        Assert.Equal(2,result.Candidates[0].EvidenceCount);Assert.Contains("not independent",result.Candidates[0].Status);
    }
    [Fact] public void DuplicateIdsAndOversizedCollectionsRejected()
    {
        Assert.Throws<InvalidDataException>(()=>HistoryReader.Parse(Evidence(Exchange(),Exchange())));
        Assert.Throws<InvalidDataException>(()=>HistoryReader.Parse(Evidence(Enumerable.Range(0,65).Select(i=>Exchange(i.ToString())).ToArray())));
        Assert.Throws<InvalidDataException>(()=>HistoryReader.Parse(new byte[HistoryReader.MaxFileBytes+1]));
    }
    [Theory]
    [InlineData("{\"SchemaVersion\":1,\"SchemaVersion\":2}")]
    [InlineData("null")]
    [InlineData("{\"SchemaVersion\":1,\"Model\":\"PP-100N\"}")]
    [InlineData("{\"EnableLive\":true}")]
    public void BadSchemasFailClosed(string text)=>Assert.ThrowsAny<Exception>(()=>HistoryReader.Parse(Encoding.UTF8.GetBytes(text)));
    [Fact] public void DeterministicWithCancellation()
    {
        var data=Evidence(Exchange());Assert.Equal(JsonSerializer.Serialize(HistoryReader.Parse(data)),JsonSerializer.Serialize(HistoryReader.Parse(data)));
        Assert.Throws<OperationCanceledException>(()=>HistoryReader.Parse(data,new CancellationToken(true)));
    }
    [Fact] public void CliReadinessAndOfflineReaderReturnUnverifiedExitCode()
    {
        using var output=new StringWriter();using var error=new StringWriter();
        Assert.Equal(5,CliApplication.Run(["history","--json"],output,error));Assert.Contains("false",output.ToString());
        string path=Path.GetTempFileName();
        try {File.WriteAllBytes(path,Evidence(Exchange()));output.GetStringBuilder().Clear();
            Assert.Equal(5,CliApplication.Run(["history",path,"--json"],output,error));Assert.Contains("0x01",output.ToString());
            File.WriteAllText(path,"bad");Assert.Equal(3,CliApplication.Run(["history",path],output,error));
        }finally{File.Delete(path);}
    }
}
