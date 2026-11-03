using DiscproducerDiagnostics.Core;
using DiscproducerDiagnostics.Usb;
using Xunit;
namespace DiscproducerDiagnostics.Tests;

public class D4TransportTests
{
    private sealed class Script : ID4Stream
    {
        public List<string> Writes {get;}=[];
        public bool Disposed;
        public bool Split, Early, Hang, RejectClose, Zero, Malformed, WrongSocket, WrongRevision, Oversized, NoEnd, NoCommandCredit;
        private readonly Queue<byte[]> reads=[];
        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();string hex=Convert.ToHexString(bytes.Span);Writes.Add(hex);
            string reply=hex switch {
                "0000001B0140454A4C20313238342E340A40454A4C0A40454A4C0A"=>"000000080100C500",
                "0000000801000010"=>"000000090100800010",
                "000000110100094550534F4E2D4354524C"=>"0000001301008900024550534F4E2D4354524C",
                "0000001101000102020200020000000000"=>"00000010010081000202004001000000",
                "0000000D01000402020080FFFF"=>Zero?"0000000C0100840002020000":"0000000C0100840002020001",
                "0202000B00017374010001"=>"",
                "0000000B01000302020001"=>"0000000A010083000202",
                "0000000A010002020200"=>RejectClose?"0000000A010082010202":"0000000A010082000202",
                _=>throw new InvalidOperationException("Unexpected outgoing bytes: "+hex)
            };
            if(NoCommandCredit && hex=="0000000801000010")reply="000000090000800010";
            if(WrongRevision && hex=="0000000801000010")reply="000000090100800020";
            if(Oversized && Writes.Count==1)reply="000002010100";
            if(Malformed && Writes.Count==1)reply="000000050100";
            if(hex=="0000000B01000302020001" && !Hang)
            {
                string data=WrongSocket?"030300080001AABB":"020200080001AABB";
                if(NoEnd)data="020200080000AABB";
                reply=Early?data+reply:reply+data;
            }
            var b=Convert.FromHexString(reply);
            if(Split)foreach(var v in b)reads.Enqueue([v]);else if(b.Length>0)reads.Enqueue(b);
            return ValueTask.CompletedTask;
        }
        public async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token)
        {
            if(reads.Count==0){await Task.Delay(Timeout.Infinite,token);return 0;}
            var bytes=reads.Dequeue();bytes.CopyTo(buffer);return bytes.Length;
        }
        public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}
    }
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task HandlesSplitCoalescedAndEarlyData(bool split,bool early)
    {
        var stream=new Script{Split=split,Early=early};var result=await new D4Session(stream).QualifyAsync(TimeSpan.FromSeconds(2),default);
        Assert.True(result.Complete,result.Error);Assert.Equal(new byte[]{0xAA,0xBB},result.Response);
        Assert.StartsWith("Close acknowledged",result.Cleanup);Assert.True(stream.Disposed);
        Assert.Single(stream.Writes,x=>x.StartsWith("0202"));Assert.DoesNotContain(stream.Writes,x=>x.Contains("41BEA0"));
    }
    [Fact] public async Task ZeroCreditNeverSendsDataAndClosesOwnedChannel()
    {
        var stream=new Script{Zero=true};var result=await new D4Session(stream).QualifyAsync(TimeSpan.FromSeconds(2),default);
        Assert.False(result.Complete);Assert.DoesNotContain(stream.Writes,x=>x.StartsWith("0202"));
        Assert.Equal(3,stream.Writes.Count(x=>x=="0000000D01000402020080FFFF"));Assert.StartsWith("Close acknowledged",result.Cleanup);
    }
    [Fact] public async Task TimeoutStopsWithoutResetOrReplay()
    {
        var stream=new Script{Hang=true};var result=await new D4Session(stream).QualifyAsync(TimeSpan.FromMilliseconds(50),default);
        Assert.Equal("Deadline exceeded",result.Error);Assert.True(stream.Disposed);
        Assert.DoesNotContain("0000000A010002020200",stream.Writes);Assert.Single(stream.Writes,x=>x.StartsWith("0202"));
    }
    [Fact] public async Task AlreadyCancelledSendsNothing()
    {
        var stream=new Script();var result=await new D4Session(stream).QualifyAsync(TimeSpan.FromSeconds(1),new CancellationToken(true));
        Assert.Equal("Cancelled",result.Error);Assert.Empty(stream.Writes);Assert.True(stream.Disposed);
    }
    [Fact] public async Task CloseFailureRetainsSuccessfulReply()
    {
        var stream=new Script{RejectClose=true};var result=await new D4Session(stream).QualifyAsync(TimeSpan.FromSeconds(1),default);
        Assert.True(result.Complete);Assert.Equal(2,result.Response.Length);Assert.StartsWith("Close failed",result.Cleanup);Assert.True(stream.Disposed);
    }
    [Fact] public async Task InvalidLengthAbortsBeforeInitialization()
    {
        var stream=new Script{Malformed=true};var result=await new D4Session(stream).QualifyAsync(TimeSpan.FromSeconds(1),default);
        Assert.False(result.Complete);Assert.Single(stream.Writes);Assert.True(stream.Disposed);Assert.NotEmpty(result.Trace);
    }
    [Fact] public async Task WrongSocketIsNotAcceptedOrFollowedByCleanupTraffic()
    {
        var stream=new Script{WrongSocket=true};var result=await new D4Session(stream).QualifyAsync(TimeSpan.FromSeconds(1),default);
        Assert.False(result.Complete);Assert.DoesNotContain("0000000A010002020200",stream.Writes);
    }
    [Fact] public async Task SessionCannotBeReused()
    {
        var session=new D4Session(new Script());await session.QualifyAsync(TimeSpan.FromSeconds(1),default);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>session.QualifyAsync(TimeSpan.FromSeconds(1),default));
    }
    [Fact] public async Task NoEndMarkerCannotBecomeSuccessfulReply()
    {
        var stream=new Script{NoEnd=true};var result=await new D4Session(stream).QualifyAsync(TimeSpan.FromSeconds(1),default);
        Assert.False(result.Complete);Assert.Single(stream.Writes,x=>x.StartsWith("0202"));Assert.True(stream.Disposed);
    }
    [Fact] public async Task WrongRevisionDoesNotFallBack()
    {
        var stream=new Script{WrongRevision=true};var result=await new D4Session(stream).QualifyAsync(TimeSpan.FromSeconds(1),default);
        Assert.False(result.Complete);Assert.Equal(2,stream.Writes.Count);Assert.True(stream.Disposed);
    }
    [Fact] public async Task OversizedPacketIsRejectedBeforeBufferGrowth()
    {
        var stream=new Script{Oversized=true};var result=await new D4Session(stream).QualifyAsync(TimeSpan.FromSeconds(1),default);
        Assert.False(result.Complete);Assert.Single(stream.Writes);Assert.True(stream.Disposed);
    }
    [Fact] public async Task InitWithoutGrantedCommandCreditCannotResolveSocket()
    {
        var stream=new Script{NoCommandCredit=true};var result=await new D4Session(stream).QualifyAsync(TimeSpan.FromSeconds(1),default);
        Assert.False(result.Complete);Assert.Equal(2,stream.Writes.Count);Assert.Contains("command-channel credit",result.Error!);
    }
    [Fact] public async Task ProgressSurvivesIncompleteExchangeAndDtoRoundTrips()
    {
        var events=new List<D4Trace>();var stream=new Script{Hang=true};
        var result=await new D4Session(stream,events.Add).QualifyAsync(TimeSpan.FromMilliseconds(100),default);
        Assert.False(result.Complete);Assert.NotEmpty(events);Assert.Equal(events.Count,result.Trace.Length);
        Assert.Contains(events,e=>e.Direction=="OUT attempted");Assert.Contains(events,e=>e.Direction=="IN");
        var reply=new WorkerReply(D4Qualification:result);
        var decoded=System.Text.Json.JsonSerializer.Deserialize<WorkerReply>(System.Text.Json.JsonSerializer.Serialize(reply));
        Assert.Equal(result.Trace.Length,decoded!.D4Qualification!.Trace.Length);
    }
    [Fact] public void OnlyFixedQualificationOperationIsAuthorized()
    {
        WorkerAuthorization.Authorize(new("d4-status-qualification"));
        Assert.Throws<NotSupportedException>(()=>WorkerAuthorization.Authorize(new("d4-status-qualification",Command:"history")));
        Assert.Throws<NotSupportedException>(()=>WorkerAuthorization.Authorize(new("d4-status-qualification",Command:"7C7C")));
        Assert.Throws<NotSupportedException>(()=>WorkerAuthorization.Authorize(new("query",Command:"d4-status-qualification")));
    }
    [Fact] public void WorkerOnlyAllowsPayloadFreeOwnershipProbe()
    {
        WorkerAuthorization.Authorize(new("transport-ownership"));
        Assert.Throws<NotSupportedException>(()=>WorkerAuthorization.Authorize(new("d4")));
        Assert.Throws<NotSupportedException>(()=>WorkerAuthorization.Authorize(new("query",Command:"history")));
    }
}
