using System.Text;
using DiscproducerDiagnostics.Protocols;
using Xunit;
namespace DiscproducerDiagnostics.Tests;
public class EepromCandidateTests
{
    private static byte[] Identity(string model="PP-100PRN",string firmware="WLP00105",string maker="EPSON")
    {
        var body=Encoding.ASCII.GetBytes($"MFG:{maker};MDL:{model};BSFU:0,18,{firmware},PP-100PRN.A;");
        int length=body.Length+2;return [(byte)(length>>8),(byte)length,..body];
    }
    [Fact] public void ExactOriginalModelIdentityAcceptedAsDriverReportedOnly() => EepromCandidate.ValidateIdentity(Identity());
    [Theory][InlineData("PP-50","WLP00105","EPSON")][InlineData("PP-100PRN","WLP00106","EPSON")][InlineData("PP-100PRN","WLP00105X","EPSON")][InlineData("PP-100PRN","WLP00105","OTHER")]
    public void OtherProfilesBlocked(string model,string firmware,string maker) => Assert.Throws<IOException>(()=>EepromCandidate.ValidateIdentity(Identity(model,firmware,maker)));
    [Fact] public void MissingIdentityBlocked() => Assert.Throws<IOException>(()=>EepromCandidate.ValidateIdentity([]));
}
