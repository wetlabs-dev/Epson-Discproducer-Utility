using System.ComponentModel;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DiscproducerDiagnostics.Core;
using DiscproducerDiagnostics.Diagnostics;
using DiscproducerDiagnostics.Usb;
using Xunit;
using DiscproducerDiagnostics.Protocols;
namespace DiscproducerDiagnostics.Tests;
public class ResearchTests
{
    private static string Fixture=>Path.Combine(AppContext.BaseDirectory,"Fixtures","usb_u3v_sample.pcapng");
    private static CaptureReport Independent()=>UsbCaptureImporter.Parse(File.ReadAllBytes(Fixture),Fixture);
    private static string Temporary(string extension)=>Path.Combine(Path.GetTempPath(),Guid.NewGuid()+extension);
    [Fact] public void IndependentCaptureAgreesWithTsharkFields()
    {
        var report=Independent();var expected=File.ReadAllLines(Path.ChangeExtension(Fixture,"tshark.tsv"));
        Assert.Equal(110,report.Packets.Length);Assert.Equal(expected.Length,report.Packets.Length);
        for(int i=0;i<expected.Length;i++)
        {
            var columns=expected[i].Split('\t');var p=report.Packets[i];
            Assert.Equal(int.Parse(columns[0]),p.Number);Assert.Equal(ushort.Parse(columns[2]),p.Bus);Assert.Equal(ushort.Parse(columns[3]),p.Address);
            Assert.Equal(Convert.ToByte(columns[4][2..],16),p.Endpoint);
            Assert.Equal(columns[5]=="0x02"?"Control":"Bulk",p.TransferType);
            Assert.InRange(Math.Abs((p.TimestampUtc!.Value-DateTimeOffset.UnixEpoch).TotalSeconds-double.Parse(columns[1],CultureInfo.InvariantCulture)),0,0.000001);
            if(columns[7].Length>0)Assert.Equal(byte.Parse(columns[7]),p.Stage);
        }
        Assert.Equal("8006050300002600",Convert.ToHexString(report.Packets[0].SetupPacket));
        Assert.Equal(3,report.Packets[0].RelatedPacket);Assert.Equal(38,report.Packets[1].Payload.Length);
        Assert.False(report.Complete); // Sample contains truncated video payloads and unmatched URBs.
    }
    [Fact] public void ReconstructsLegacyThreeStageControl()
    {
        var first=CaptureAnalysis.Reconstruct(Independent())[0];Assert.Equal(3,first.Records.Length);Assert.Equal(38,first.Payload.Length);Assert.Equal((uint)0,first.CompletionStatus);
    }
    [Fact] public void DescriptorAssociationIsOnlyCandidate()
    {
        var report=Independent();var associations=CaptureAnalysis.Associate(report,MockTransport.Device);
        Assert.NotEmpty(associations);Assert.All(associations,a=>Assert.Equal("Conflicting VID/PID",a.Confidence));
        var transfer=CaptureAnalysis.Reconstruct(report).Single(t=>t.Request.SetupPacket.Length==8&&t.Request.SetupPacket[3]==1);
        var d=MockTransport.Device with{Vid=Convert.ToHexString(transfer.Payload[8..10].Reverse().ToArray()),Pid=Convert.ToHexString(transfer.Payload[10..12].Reverse().ToArray())};
        Assert.Contains(CaptureAnalysis.Associate(report,d),a=>a.Confidence.StartsWith("Candidate"));
    }
    [Fact] public void DiffIncludesChangedAddedAndRemovedBytes()
    {
        var diff=CaptureAnalysis.Diff([1,2,3],[1,5]);Assert.Equal(2,diff.Length);Assert.Equal(1,diff[0].Offset);Assert.Equal("05",diff[0].After);Assert.Equal("absent",diff[1].After);
        Assert.Equal(256,CaptureAnalysis.Diff(new byte[1000],Enumerable.Repeat((byte)1,1000).ToArray()).Length);
    }
    [Fact] public void ComparisonDetectsChangedPayloadAndDoesNotNameAnErrorField()
    {
        var a=Independent();var packets=a.Packets.ToArray();packets[1]=packets[1] with{Payload=[7,8,9]};var text=CaptureAnalysis.Compare(a,a with{Packets=packets});
        Assert.Contains("Occurrence 1",text);Assert.Contains("candidate alignment",text);Assert.Contains("not identified error codes",text);
    }
    [Fact] public void SessionRoundTripPreservesAnnotationsWithoutEmbeddingCaptures()
    {
        string path=Temporary(".json");var report=Independent();var s=new ResearchSession{Name="Error condition",Scenario="Internal printer error",Captures=[new(Fixture,report.Sha256,report.FileSize)],Annotations=[new(report.Sha256,1,"User observed error",DateTimeOffset.UtcNow)]};
        try{ResearchSessionStore.Save(path,s);var loaded=ResearchSessionStore.Load(path);Assert.Equal(s.SessionId,loaded.SessionId);Assert.Equal(s.Annotations,loaded.Annotations);Assert.DoesNotContain("Payload",File.ReadAllText(path));Assert.Equal(110,ResearchSessionStore.LoadCapture(path,loaded.Captures[0]).Packets.Length);}finally{File.Delete(path);}
    }
    [Fact] public void MissingCaptureDoesNotPreventOpeningSession()
    {
        string path=Temporary(".json");try{ResearchSessionStore.Save(path,new(){Captures=[new("missing.pcapng",new string('A',64),1)]});var s=ResearchSessionStore.Load(path);Assert.Throws<FileNotFoundException>(()=>ResearchSessionStore.LoadCapture(path,s.Captures[0]));}finally{File.Delete(path);}
    }
    [Fact] public void HashMismatchBlocksComparison()
    { Assert.Throws<InvalidDataException>(()=>ResearchSessionStore.LoadCapture(Temporary(".json"),new(Fixture,new string('A',64),new FileInfo(Fixture).Length))); }
    [Theory][InlineData(0)][InlineData(2)] public void UnknownSessionSchemaRejected(int version)
    {string path=Temporary(".json");try{File.WriteAllText(path,JsonSerializer.Serialize(new ResearchSession{SchemaVersion=version}));Assert.Throws<InvalidDataException>(()=>ResearchSessionStore.Load(path));}finally{File.Delete(path);}}
    [Fact] public void LargeSessionRejectedBeforeRead()
    {string path=Temporary(".json");try{using(var stream=File.Create(path))stream.SetLength(ResearchSessionStore.MaxSessionBytes+1);Assert.Throws<InvalidDataException>(()=>ResearchSessionStore.Load(path));}finally{File.Delete(path);}}
    [Theory][InlineData(ExportMode.RedactedSummary)][InlineData(ExportMode.TechnicalDiagnostics)][InlineData(ExportMode.FullForensic)]
    public void BundleOfflineRoundTrip(ExportMode mode)
    {
        string path=Temporary(".zip");var snapshot=new BundleSnapshot(MockTransport.Device,[],[],null,[],[],null,[1,2,3],[],"");
        try{BundleExporter.Export(path,snapshot,mode,true);var imported=BundleReader.Open(path);Assert.Equal(mode.ToString(),imported.Mode);Assert.Contains(imported.Documents,d=>d.Name=="manifest.json"&&d.Text.Contains(snapshot.SessionId.ToString()));Assert.Contains(imported.Warnings,w=>w.StartsWith("IMPORTED"));}finally{File.Delete(path);}
    }
    [Fact] public void BundleTamperDetected()
    {
        string path=Temporary(".zip");try{
            BundleExporter.Export(path,new(null,[],[],null,[],[],null,[1,2,3],[],""),ExportMode.FullForensic,true);
            using(var zip=ZipFile.Open(path,ZipArchiveMode.Update)){zip.GetEntry("captures/original.pcapng")!.Delete();using var stream=zip.CreateEntry("captures/original.pcapng").Open();stream.Write([4,5,6]);}
            Assert.Throws<InvalidDataException>(()=>BundleReader.Open(path));
        }finally{File.Delete(path);}
    }
    [Theory][InlineData("../escape")][InlineData("/absolute")][InlineData("a\\b")]
    public void UnsafeZipNamesRejected(string name)
    {string path=Temporary(".zip");try{using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))zip.CreateEntry(name);Assert.Throws<InvalidDataException>(()=>BundleReader.Open(path));}finally{File.Delete(path);}}
    [Fact] public void PrivacyOmitsAllJobMetadata()
    {
        string path=Temporary(".zip");var p=new PrinterDetail(new("secret","secret","secret",0,1,null),"secret","secret","secret","secret",false,false,true,null,[new(567,"secret",891,DateTimeOffset.UtcNow,"secret",1234567,987)],[]);
        try{BundleExporter.Export(path,new(null,[],[],null,[p],[],null,null,[],""),ExportMode.TechnicalDiagnostics,false);var bundle=BundleReader.Open(path);var jobs=bundle.Documents.Single(d=>d.Name=="printer/jobs.json").Text;Assert.DoesNotContain("1234567",jobs);Assert.DoesNotContain("Submitted",jobs);Assert.DoesNotContain("secret",jobs);}finally{File.Delete(path);}
    }
    [Theory][InlineData(5,DiagnosticErrorCategory.AccessDenied)][InlineData(1167,DiagnosticErrorCategory.DeviceDisconnected)][InlineData(50,DiagnosticErrorCategory.UnsupportedTransport)][InlineData(1722,DiagnosticErrorCategory.SpoolerUnavailable)]
    public void ClassifiesNativeErrorWithoutLosingCode(int code,DiagnosticErrorCategory category)
    {var error=ErrorClassifier.Classify(new Win32Exception(code));Assert.Equal(category,error.Category);Assert.Equal(code,error.NativeCode);Assert.NotEmpty(error.Recovery);}
    [Fact] public void SiblingInterfacesGroupButIdenticalDevicesRemainDistinct()
    {
        var a=MockTransport.Device with{InstanceId=@"USB\VID_04B8&PID_1111\ONE",Ancestors=[]};
        var child=a with{InstanceId=@"USB\VID_04B8&PID_1111&MI_00\A",Ancestors=[a.InstanceId],InterfacePath="path-a"};
        var b=a with{InstanceId=@"USB\VID_04B8&PID_1111\TWO",InterfacePath="path-b"};
        var topology=DeviceTopology.Describe(child,[a,child,b],[],false);Assert.Equal(a.InstanceId,topology.PhysicalInstanceId);Assert.Equal(2,topology.Devnodes.Length);Assert.DoesNotContain("path-b",topology.PrinterInterfaces);
    }
    [Theory][InlineData("standard-status")][InlineData("descriptors")][InlineData("spooler")][InlineData("captured-command")]
    public void NonPrinterIoctlsCannotReachNativeDispatch(string command)=>Assert.Throws<NotSupportedException>(()=>CommandRegistry.Authorize(command));
    [Theory][InlineData("reset")][InlineData("write")][InlineData("replay")]
    public void WorkerBoundaryRejectsUnknownOperations(string op)=>Assert.Throws<NotSupportedException>(()=>WorkerAuthorization.Authorize(new(op)));
    [Theory][InlineData("standard-status")][InlineData("vendor")][InlineData("descriptors")]
    public void WorkerBoundaryRejectsNonPrinterQuery(string command)=>Assert.Throws<NotSupportedException>(()=>WorkerAuthorization.Authorize(new("query","stale",command)));
    [Fact] public void WindowsStalePathCannotBeOpened()
    {
        if(!OperatingSystem.IsWindows())return;
        var reply=NativeWorker.Execute(new("query","nonexistent-epson-interface","ieee1284"));
        Assert.Equal(1167,reply.ErrorCode);Assert.Null(reply.Exchange);
    }
    [Fact] public void WindowsUnauthorizedCommandRejectedBeforeDiscovery()
    {
        if(!OperatingSystem.IsWindows())return;
        var reply=NativeWorker.Execute(new("query","nonexistent","vendor"));Assert.NotNull(reply.Error);Assert.Null(reply.ErrorCode);Assert.Null(reply.Exchange);
    }
    [Fact] public void EmptyAndNonAsciiIdentityAreInvalid()
    {
        Assert.False(ResponseInterpreter.Interpret("ieee1284",[0,2]).Valid);
        Assert.False(ResponseInterpreter.Interpret("ieee1284",[0,7,65,58,255,59,0]).Valid);
    }
    [Fact] public void ExtraStatusBytesAreNotAValidDword()=>Assert.False(ResponseInterpreter.Interpret("port-status",[24,0,0,0,1]).Valid);
    [Fact] public void OrphanIntermediateIsPreservedInLogicalOperations()
    {
        var sample=Independent();var intermediate=sample.Packets[1] with{RelatedPacket=null,Correlation="Intermediate without request"};
        Assert.Single(CaptureAnalysis.Reconstruct(sample with{Packets=[intermediate]}));
    }
    [Fact] public void LargeCaptureRejectedBeforeParsing()=>Assert.Throws<InvalidDataException>(()=>UsbCaptureImporter.Parse(new byte[UsbCaptureImporter.MaxFileSize+1],"oversize"));
    [Fact] public void OfflineForensicReconstructsIdentityAndTransactions()
    {
        string path=Temporary(".zip");var t=new Transaction(Guid.NewGuid(),DateTimeOffset.UtcNow,"unit","driver","path","ep","port-status",[],[24,0,0,0],null,1,1000,"Success",null,null,"decoded","original driver provenance");
        try{BundleExporter.Export(path,new(MockTransport.Device,[new("Model","PP-100","Mock simulation")],[],null,[],[t],null,null,[],""),ExportMode.FullForensic,true);var b=BundleReader.Open(path);Assert.Single(b.Facts);Assert.Single(b.Topology);Assert.Equal(t.Response,Assert.Single(b.Transactions).Response);Assert.Equal(t.Provenance,b.Transactions[0].Provenance);}finally{File.Delete(path);}
    }
    private static WorkerClient Worker(){string root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!,"..","..",".."));return new(Path.Combine(root,OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"),[Path.Combine(AppContext.BaseDirectory,"worker","DiscproducerDiagnostics.TestWorker.dll")]);}
    private sealed class ProgressCollector : IProgress<Transaction> { public List<Transaction> Items {get;}=[];public void Report(Transaction t){lock(Items)Items.Add(t);} }
    [Fact] public async Task TimeoutPreservesCompletedProgress()
    {var progress=new ProgressCollector();await Assert.ThrowsAsync<TimeoutException>(()=>Worker().RunAsync(new("partial"),TimeSpan.FromSeconds(2),default,progress));var t=Assert.Single(progress.Items);Assert.Equal(new byte[]{1,2,3},t.Response);Assert.Equal("completed-step",t.Command);}
    [Fact] public async Task RepeatedWorkersReleaseSerializationAfterFailure()
    {var worker=Worker();await Assert.ThrowsAsync<IOException>(()=>worker.RunAsync(new("crash"),TimeSpan.FromSeconds(10),default));for(int i=0;i<3;i++)Assert.Null((await worker.RunAsync(new("echo"),TimeSpan.FromSeconds(10),default)).Error);}
    [Fact] public async Task DuplicateFinalFrameRejected()=>await Assert.ThrowsAsync<IOException>(()=>Worker().RunAsync(new("duplicate-final"),TimeSpan.FromSeconds(10),default));
}
