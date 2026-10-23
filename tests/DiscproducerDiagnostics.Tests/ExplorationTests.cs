using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using DiscproducerDiagnostics.Core;
using DiscproducerDiagnostics.Diagnostics;
using DiscproducerDiagnostics.Protocols;
using Xunit;
namespace DiscproducerDiagnostics.Tests;
public class ExplorationTests
{
    [Fact] public void DescriptorSetupIsExactAndReadOnly()
    {
        Assert.Equal(new byte[]{0x80,6,0,1,0,0,18,0},DescriptorRequests.Setup(1,0,0,18));
        Assert.Equal(new byte[]{0x80,6,3,3,9,4,255,0},DescriptorRequests.Setup(3,3,0x409,255));
        Assert.Throws<ArgumentException>(()=>DescriptorRequests.Setup(0xff,0,0,8));
        Assert.Throws<ArgumentException>(()=>DescriptorRequests.Setup(1,1,0,18));
    }
    [Theory][InlineData("soft-reset")][InlineData("vendor")][InlineData("eeprom")]
    public void ExplorerRejectsUnknownCommands(string id)=>Assert.Throws<NotSupportedException>(()=>DiagnosticCommands.Authorize(id));
    [Fact] public void StandardStatusIsExplicitlyUnsupported()=>Assert.Throws<NotSupportedException>(()=>DiagnosticCommands.Authorize("standard-status"));
    [Fact] public void DecodesInterfacesAndEndpointFieldsWithoutAssumingAccess()
    {
        byte[] config=[9,2,25,0,1,1,0,0x80,50,9,4,2,1,1,7,1,2,0,7,5,0x82,2,0,2,8];
        var decoded=DescriptorDecoder.Decode([new("Configuration",0,config)]);
        Assert.Empty(decoded.Warnings);var endpoint=Assert.Single(decoded.Endpoints);
        Assert.Equal("IN",endpoint.Direction);Assert.Equal("Bulk",endpoint.TransferType);Assert.Equal(512,endpoint.MaximumPacketSize);Assert.Equal(2,endpoint.Interface);Assert.Equal(1,endpoint.Alternate);
    }
    [Fact] public void MalformedDescriptorsAreReportedWithoutInventingEndpoints()
    {
        var decoded=DescriptorDecoder.Decode([new("Configuration",0,[9,2,1])]);Assert.Empty(decoded.Endpoints);Assert.NotEmpty(decoded.Warnings);
    }
    [Fact] public void QueueAssociationRequiresEvidenceAndFlagsSharedContainers()
    {
        var first=MockTransport.Device with{InstanceId=@"USB\VID_04B8&PID_1111\ONE",Properties=new(){["ContainerId"]="12345678-1234-1234-1234-123456789012"}};
        var second=first with{InstanceId=@"USB\VID_04B8&PID_1111\TWO"};
        var queue=new PrinterDetail(new("same name","USB001","Epson",0,0,null),"1","Epson","USB Monitor","winprint",true,false,true,first.Properties["ContainerId"],[],[]);
        Assert.StartsWith("Associated",QueueCorrelation.Correlate(queue,first,[first]).Association);
        Assert.Equal("Ambiguous",QueueCorrelation.Correlate(queue,first,[first,second]).Association);
        Assert.Equal("Uncorrelated",QueueCorrelation.Correlate(queue with{ContainerId=null},first,[first]).Association);
    }
    [Fact] public void HexViewerKeepsOffsetsAndBoundsRendering()
    {
        string text=HexFormatter.Format(Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOPQRST"),17);
        Assert.Contains("00000010",text);Assert.Contains("41 42 43",text);Assert.Contains("17 of 20",text);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void ImportsBothPcapngByteOrdersAndCorrelatesControl(bool big)
    {
        var file=Capture(big,Packet(false,[0xA1,0,0,0,2,0,4,0],0),Packet(true,[1,2,3,4],3));
        var report=UsbCaptureImporter.Parse(file,"fixture.pcapng");
        Assert.True(report.Complete);Assert.Equal(2,report.Packets.Length);Assert.Equal(2,report.Packets[0].RelatedPacket);Assert.Equal(1,report.Packets[1].RelatedPacket);
        Assert.Equal("IN",report.Packets[0].Direction);Assert.Equal("IN",report.Packets[1].Direction);Assert.Equal((ushort)2,report.Packets[0].UsbInterface);Assert.Equal(new byte[]{1,2,3,4},report.Packets[1].Payload);Assert.Equal(1,report.Packets[1].DurationMs);
    }
    [Fact] public void MissingCompletionRemainsExplicit()
    {
        var report=UsbCaptureImporter.Parse(Capture(false,Packet(false,[0x80,6,0,1,0,0,18,0],0)),"incomplete.pcapng");Assert.False(report.Complete);Assert.Contains("without completion",report.Packets[0].Correlation);
    }
    [Fact] public void TruncationAndUnsupportedLinkTypesAreGraceful()
    {
        var file=Capture(false,Packet(false,[],null));var report=UsbCaptureImporter.Parse(file[..^3],"truncated.pcapng");Assert.False(report.Complete);Assert.NotEmpty(report.Warnings);
        file=Capture(false,Packet(false,[],null));file[36]=1;file[37]=0;
        report=UsbCaptureImporter.Parse(file,"ethernet.pcapng");Assert.Empty(report.Packets);Assert.Contains(report.Warnings,w=>w.Contains("Unsupported link type"));
    }
    [Fact] public void ReusedUrbIdDoesNotCreateFalsePair()
    {
        var report=UsbCaptureImporter.Parse(Capture(false,Packet(false,[],null),Packet(false,[],null),Packet(true,[],null)),"ambiguous.pcapng");Assert.Contains("Ambiguous",report.Packets[2].Correlation);Assert.Null(report.Packets[2].RelatedPacket);
    }
    [Fact] public void CaptureSectionsNeverCrossCorrelate()
    {
        var bytes=Capture(false,Packet(false,[],null)).Concat(Capture(true,Packet(true,[],null))).ToArray();
        var report=UsbCaptureImporter.Parse(bytes,"sections.pcapng");Assert.All(report.Packets,p=>Assert.Null(p.RelatedPacket));
    }
    [Fact] public void CaptureCancellationIsHonored()
    {
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();Assert.Throws<OperationCanceledException>(()=>UsbCaptureImporter.Parse(Capture(false,Packet(false,[],null)),"fixture",cancellation.Token));
    }
    [Fact] public async Task CentralRecorderPreservesMockBytesAndCancellation()
    {
        using var session=new DiagnosticSession();await using var transport=new LoggedTransport(new MockTransport(MockScenario.Ready),session,MockTransport.Device);await transport.ConnectAsync(default);
        var result=await transport.QueryAsync("port-status",TimeSpan.FromSeconds(1),default);var t=Assert.Single(session.Transactions,t=>t.Command=="port-status");Assert.Equal(result.Response,t.Response);Assert.True(t.Simulated);Assert.True(t.ValidResponse);Assert.Null(t.SetupPacket);Assert.Null(t.WireAcknowledged);
    }
    [Theory][InlineData(ExportMode.RedactedSummary)][InlineData(ExportMode.TechnicalDiagnostics)]
    public void PrivacyModesExcludePayloadsAndPrivateStrings(ExportMode mode)
    {
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".zip");
        var transaction=new Transaction(Guid.NewGuid(),DateTimeOffset.UtcNow,"SECRET","USB","SECRET","EP0","ieee1284",Encoding.ASCII.GetBytes("SECRET"),Encoding.ASCII.GetBytes("SECRET"),null,1,5000,"Failed",5,"SECRET","SECRET","SECRET");
        var snapshot=new BundleSnapshot(MockTransport.Device with{Name="SECRET"},[new("SN","SECRET","IEEE1284")],[],null,[],[transaction],null,Encoding.ASCII.GetBytes("SECRET"),[],"SECRET");
        try
        {
            BundleExporter.Export(path,snapshot,mode,false);using var zip=ZipFile.OpenRead(path);Assert.Contains(zip.Entries,e=>e.FullName=="manifest.json");Assert.Contains(zip.Entries,e=>e.FullName=="analysis/recommended-next-steps.md");
            foreach(var entry in zip.Entries){Assert.DoesNotContain(".bin",entry.FullName);Assert.DoesNotContain(".pcapng",entry.FullName);using var reader=new StreamReader(entry.Open());Assert.DoesNotContain("SECRET",reader.ReadToEnd());}
        }
        finally{File.Delete(path);}
    }
    [Fact] public void ForensicExportRequiresConfirmationAndPreservesBytes()
    {
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".zip");var snapshot=new BundleSnapshot(null,[],[],null,[],[],null,[1,2,3],[],"");
        Assert.Throws<InvalidOperationException>(()=>BundleExporter.Export(path,snapshot,ExportMode.FullForensic,false));Assert.False(File.Exists(path));
        try{BundleExporter.Export(path,snapshot,ExportMode.FullForensic,true);using var zip=ZipFile.OpenRead(path);using var stream=zip.GetEntry("captures/original.pcapng")!.Open();using var output=new MemoryStream();stream.CopyTo(output);Assert.Equal(new byte[]{1,2,3},output.ToArray());}finally{File.Delete(path);}
    }
    private static byte[] Packet(bool complete,byte[] data,byte? stage)
    {
        int size=stage.HasValue?28:27;byte[] result=new byte[size+data.Length];BinaryPrimitives.WriteUInt16LittleEndian(result,(ushort)size);BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(2),42);result[16]=complete?(byte)1:(byte)0;result[17]=1;result[19]=7;result[21]=0x80;result[22]=stage.HasValue?(byte)2:(byte)3;BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(23),(uint)data.Length);if(stage.HasValue)result[27]=stage.Value;data.CopyTo(result,size);return result;
    }
    private static byte[] Capture(bool big,params byte[][] packets)
    {
        using var stream=new MemoryStream();
        void U16(byte[]b,int p,ushort v){if(big)BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(p),v);else BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p),v);}
        void U32(byte[]b,int p,uint v){if(big)BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(p),v);else BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p),v);}
        void Block(uint type,byte[]body){byte[] block=new byte[12+body.Length];U32(block,0,type);U32(block,4,(uint)block.Length);body.CopyTo(block,8);U32(block,block.Length-4,(uint)block.Length);stream.Write(block);}
        byte[] section=new byte[16];U32(section,0,0x1a2b3c4d);U16(section,4,1);for(int i=8;i<16;i++)section[i]=255;Block(0x0a0d0d0a,section);
        byte[] idb=new byte[8];U16(idb,0,249);U32(idb,4,65535);Block(1,idb);
        uint stamp=1000000;
        foreach(var packet in packets){byte[] body=new byte[20+((packet.Length+3)&~3)];U32(body,8,stamp);stamp+=1000;U32(body,12,(uint)packet.Length);U32(body,16,(uint)packet.Length);packet.CopyTo(body,20);Block(6,body);}
        return stream.ToArray();
    }
}
