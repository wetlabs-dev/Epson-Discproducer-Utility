using System.Text;
using DiscproducerDiagnostics.Core;
using DiscproducerDiagnostics.Protocols;
using DiscproducerDiagnostics.Diagnostics;
using Xunit;
namespace DiscproducerDiagnostics.Tests;
public class ResponseCompatibilityTests
{
    // Synthetic non-identifying text reproduces the observed prefix mismatch. Raw customer files are not distributed.
    private static byte[] Identity(bool malformed)
    {
        byte[] body=Encoding.ASCII.GetBytes("MFG:EPSON;MDL:PP-100PRN;CLS:PRINTER;");
        return new byte[]{malformed?(byte)(body.Length+2):(byte)0,(byte)(body.Length+2)}.Concat(body).ToArray();
    }
    [Theory][InlineData(0x10)][InlineData(0x18)][InlineData(0x30)]
    public void OneByteAndDwordHaveSameStatusBits(byte value)
    {
        var single=ResponseInterpreter.Interpret("port-status",[value]);var full=ResponseInterpreter.Interpret("port-status",[value,0,0,0]);
        Assert.True(single.Valid);Assert.True(full.Valid);Assert.Contains(StatusDecoder.Port(value),single.Text);Assert.Contains(StatusDecoder.Port(value),full.Text);
        Assert.Equal(4,CommandRegistry.Authorize("port-status").OutputLength);
    }
    [Theory][InlineData(0)][InlineData(2)][InlineData(3)][InlineData(5)]
    public void InvalidStatusLengthsStayInvalid(int length)=>Assert.False(ResponseInterpreter.Interpret("port-status",new byte[length]).Valid);
    [Fact] public void InvalidIdentityPrefixIsNotSilentlyRepaired()
    {
        byte[] raw=Identity(true),before=raw.ToArray();var result=ResponseInterpreter.Interpret("ieee1284",raw);
        Assert.False(result.Valid);Assert.True(result.Partial);Assert.Contains("length prefix",result.Text);Assert.Contains("MDL=PP-100PRN",result.Text);Assert.Equal(before,raw);
        Assert.True(ResponseInterpreter.Interpret("ieee1284",Identity(false)).Valid);
    }
    [Fact] public void MalformedTextCannotBecomeAPartialIdentity()
    {
        foreach(var text in new[]{"MFG:EPSON;MDL:PP-100;bad;","MFG:EPSON;MDL:PP-100", "MFG:EPSON;MDL:PP-100;MDL:OTHER;","MFG:EPSON;"})
            Assert.False(ResponseInterpreter.Interpret("ieee1284",new byte[]{255,255}.Concat(Encoding.ASCII.GetBytes(text)).ToArray()).Partial);
    }
    [Fact] public async Task LoggerAndGuiProtocolAgreeAndPreserveEvidence()
    {
        using var session=new DiagnosticSession();await using var transport=new LoggedTransport(new Replies(),session);
        var protocol=new PrinterProtocol(transport,TimeSpan.FromSeconds(1));var facts=await protocol.GetIdentityAsync(default);var status=await protocol.GetStatusAsync(default);
        Assert.Contains(facts,f=>f.Name=="Model"&&f.Value=="PP-100PRN"&&f.Availability.Contains("unverified"));
        var id=Assert.Single(session.Transactions,t=>t.Command=="ieee1284");Assert.Equal("ResponseWarning",id.Outcome);Assert.False(id.ValidResponse);Assert.True(id.Interpreted);Assert.Null(id.NativeError);Assert.Equal(DiagnosticErrorCategory.InvalidResponse,id.Failure!.Category);Assert.Equal(Identity(true),id.Response);
        Assert.Contains("Error asserted",status.Interpretation);Assert.Contains("Driver-reported",status.Availability);Assert.Null(status.Error);
        var port=Assert.Single(session.Transactions,t=>t.Command=="port-status");Assert.Equal("Success",port.Outcome);Assert.True(port.ValidResponse);Assert.True(port.Interpreted);Assert.Equal(new byte[]{0x10},port.Response);Assert.Null(port.WireAcknowledged);
    }
    [Fact] public async Task FailedApiWithBytesDoesNotBecomeSuccessful()
    {
        using var session=new DiagnosticSession();await using var transport=new LoggedTransport(new Replies(false),session);
        var status=await new PrinterProtocol(transport,TimeSpan.FromSeconds(1)).GetStatusAsync(default);
        Assert.Equal("Unavailable",status.Availability);Assert.False(Assert.Single(session.Transactions).ValidResponse);
    }
    private sealed class Replies(bool success=true):IDeviceTransport
    {
        public bool Connected=>true;
        public Task ConnectAsync(CancellationToken token)=>Task.CompletedTask;
        public Task DisconnectAsync()=>Task.CompletedTask;
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
        public Task<Exchange> QueryAsync(string commandId,TimeSpan timeout,CancellationToken token)=>Task.FromResult(new Exchange(commandId,commandId=="ieee1284"?Identity(true):[0x10],success,success?null:5,success?null:"Denied",1){ApiIssued=true});
    }
}
