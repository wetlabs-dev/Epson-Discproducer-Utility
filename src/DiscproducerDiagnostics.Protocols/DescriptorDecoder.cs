using System.Buffers.Binary;
using System.Text;
using DiscproducerDiagnostics.Core;
namespace DiscproducerDiagnostics.Protocols;
public record DecodedDescriptors(Fact[] DeviceFacts, UsbInterface[] Interfaces, UsbEndpoint[] Endpoints, string[] Warnings);
public static class DescriptorDecoder
{
    private static ushort U16(byte[] b, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset));
    public static DecodedDescriptors Decode(IEnumerable<DescriptorBlob> blobs)
    {
        var facts = new List<Fact>(); var interfaces = new List<UsbInterface>(); var endpoints = new List<UsbEndpoint>(); var warnings = new List<string>();
        foreach (var blob in blobs)
        {
            byte config = 0; UsbInterface? current = null;
            UsbDescriptor[] descriptors;
            try { descriptors = UsbDescriptors.Parse(blob.Bytes); }
            catch (FormatException e) { warnings.Add($"{blob.Kind}: {e.Message}"); continue; }
            foreach (var descriptor in descriptors)
            {
                byte[] b = Convert.FromHexString(descriptor.Hex);
                if (descriptor.Type == 1)
                {
                    foreach (var (key,value) in new[] { ("bcdUSB",$"0x{U16(b,2):X4}"),("EP0 max packet field",b[7].ToString()),("Device class",$"0x{b[4]:X2}"),("Device subclass",$"0x{b[5]:X2}"),("Device protocol",$"0x{b[6]:X2}"),("VID",$"{U16(b,8):X4}"),("PID",$"{U16(b,10):X4}"),("bcdDevice",$"0x{U16(b,12):X4}"),("Configuration count",b[17].ToString()) }) facts.Add(new(key,value,"USB device descriptor via Windows hub"));
                }
                else if (descriptor.Type == 2)
                {
                    config = b[5]; current = null;
                    if (U16(b,2) != blob.Bytes.Length) warnings.Add($"Configuration {config}: wTotalLength mismatch");
                }
                else if (descriptor.Type == 4)
                {
                    current = new(config,b[2],b[3],b[5],b[6],b[7],b[4]); interfaces.Add(current);
                }
                else if (descriptor.Type == 5)
                {
                    if (current == null) { warnings.Add("Endpoint descriptor without an interface"); continue; }
                    endpoints.Add(new(config,current.Number,current.Alternate,b[2],(b[2]&128)!=0?"IN":"OUT",(b[3]&3) switch {0=>"Control",1=>"Isochronous",2=>"Bulk",_=>"Interrupt"},(ushort)(U16(b,4)&0x7ff),b[6],descriptor.Hex));
                }
                else if (descriptor.Type == 3 && blob.Index != 0)
                {
                    if (b.Length%2!=0) warnings.Add("Odd-length string descriptor");
                    else facts.Add(new(blob.Kind,Encoding.Unicode.GetString(b,2,b.Length-2),"USB string descriptor via Windows hub"));
                }
            }
        }
        facts.Add(new("Active configuration","Unavailable","No supported GET_CONFIGURATION transport","Unavailable"));
        foreach (var i in interfaces)
            if (endpoints.Count(e=>e.Configuration==i.Configuration&&e.Interface==i.Number&&e.Alternate==i.Alternate)!=i.DeclaredEndpoints) warnings.Add($"Interface {i.Number}/{i.Alternate}: endpoint count mismatch");
        return new(facts.ToArray(),interfaces.ToArray(),endpoints.ToArray(),warnings.ToArray());
    }
    public static InspectionNode Tree(Device d, DescriptorReport? report, IEnumerable<Device> related)
    {
        var nodes = new List<InspectionNode> {new($"PnP: {d.InstanceId}",d.Properties.Select(p=>new InspectionNode($"{p.Key}: {p.Value}",[])).ToArray())};
        foreach(var other in related) nodes.Add(new($"PnP interface {other.InterfaceNumber}: {other.InstanceId}", [new($"Driver {other.DriverProvider} {other.DriverVersion}; {other.Properties.GetValueOrDefault("Service","Unavailable")}",[]),new($"Printer API path: {other.InterfacePath??"Unavailable"}; accessibility requires explicit probe",[])]));
        if (report==null) nodes.Add(new("Descriptors not collected. Endpoint availability is unknown.",[]));
        else
        {
            var parsed=Decode(report.Descriptors);
            nodes.AddRange(parsed.DeviceFacts.Select(f=>new InspectionNode($"{f.Name}: {f.Value} [{f.Source}]",[])));
            foreach(var group in parsed.Interfaces.GroupBy(i=>i.Configuration)) nodes.Add(new($"Configuration {group.Key} (descriptor, not proof of active configuration)",group.Select(i=>new InspectionNode($"Interface {i.Number}, alternate {i.Alternate}; class {i.Class:X2}/{i.Subclass:X2}/{i.Protocol:X2}",parsed.Endpoints.Where(e=>e.Configuration==i.Configuration&&e.Interface==i.Number&&e.Alternate==i.Alternate).Select(e=>new InspectionNode($"Endpoint 0x{e.Address:X2} {e.Direction} {e.TransferType}; max packet {e.MaximumPacketSize}; interval {e.Interval} (raw units); access not established",[])).ToArray())).ToArray()));
            nodes.AddRange(report.Errors.Concat(parsed.Warnings).Select(e=>new InspectionNode("Limitation: "+e,[])));
        }
        return new(d.Name+" — "+d.Vid+":"+d.Pid,nodes.ToArray());
    }
}
