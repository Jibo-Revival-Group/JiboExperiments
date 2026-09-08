using System.IO.Compression;
using System.Text;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Tests.Application;

public sealed class RobotLogIdentityEvidenceTests
{
    private const string Name = "Black-Byte-Cookie-Crinkle";
    private const string Serial = "BOJB-1000-0017-0630-0018";
    private const string Header = "{\"system_clock\":1788905276226939674,\"name\":\"Black-Byte-Cookie-Crinkle\",\"serial_number\":\"BOJB-1000-0017-0630-0018\",\"health\":[{";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Extract_RecognizesTruncatedHealthHeaderInLargePlainOrGzipLog(bool compressed)
    {
        var bytes = Encoding.UTF8.GetBytes(Header + new string(' ', 300_000));
        if (compressed)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true)) gzip.Write(bytes);
            bytes = output.ToArray();
        }
        var evidence = RobotLogIdentityEvidence.Extract(bytes);
        Assert.Equal(Name, evidence.RobotName);
        Assert.Equal(Serial, evidence.SerialNumber);
        Assert.False(evidence.HasConflictingEvidence);
    }

    [Theory]
    [InlineData("{\"name\":\"Black-Byte-Cookie-Crinkle\"}\n{\"serial_number\":\"BOJB-1000-0017-0630-0018\"}")]
    [InlineData("{\"health\":[{\"name\":\"Black-Byte-Cookie-Crinkle\",\"serial_number\":\"BOJB-1000-0017-0630-0018\"}]}")]
    public void Extract_DoesNotPairSeparateRecordsOrReadNestedComponentNames(string log)
    {
        Assert.Null(RobotLogIdentityEvidence.Extract(Encoding.UTF8.GetBytes(log)).RobotName);
    }

    [Fact]
    public void Extract_DecodesJsonEscapesAndRejectsConflictingHeaderEvidence()
    {
        var escaped = "{\"name\":\"Black\\u002dByte-Cookie-Crinkle\",\"serial_number\":\"" + Serial + "\"}";
        Assert.Equal(Name, RobotLogIdentityEvidence.Extract(Encoding.UTF8.GetBytes(escaped)).RobotName);
        var conflicting = RobotLogIdentityEvidence.Extract(Encoding.UTF8.GetBytes(escaped +
            "\n{\"name\":\"Other-Robot-Name\",\"serial_number\":\"BOJB-1000-0000-0000-0000\"}"));
        Assert.True(conflicting.HasConflictingEvidence);
        Assert.Null(conflicting.ResolveDevice([Device("one", Name, Serial)]));
    }

    [Fact]
    public void ResolveDevice_UsesUniqueVerifiedSerialForGeneratedRegistration()
    {
        var device = Device("5a41326368dfd00019692602", "robot-generated", Serial);
        Assert.Same(device, Evidence().ResolveDevice([device]));
        Assert.Equal("robot-generated", device.RobotId);
    }

    [Fact]
    public void ResolveDevice_MatchesExactNameWithoutPromotingObservedSerialToVerified()
    {
        var device = Device("one", Name, null);
        Assert.Same(device, Evidence().ResolveDevice([device]));
        Assert.Null(device.VerifiedSerialNumber);
        Assert.Null(Evidence().ResolveDevice([Device("generated", "robot-generated", null)]));
    }

    [Fact]
    public void ResolveDevice_RejectsDuplicateAndContradictoryRegistrations()
    {
        Assert.Null(Evidence().ResolveDevice([Device("one", Name, Serial), Device("two", "Other-Robot-Name", Serial)]));
        Assert.Null(Evidence().ResolveDevice([Device("one", "Other-Robot-Name", Serial), Device("two", Name, null)]));
        Assert.Null(Evidence().ResolveDevice([Device("one", Name, "BOJB-1000-0000-0000-0000")]));
        Assert.Null(Evidence().ResolveDevice([Device("one", Name, null), Device("two", Name, null)]));
    }

    private static RobotLogIdentityEvidence Evidence() => new(Serial, Name, true, false);
    private static DeviceRegistration Device(string id, string name, string? serial) => new()
    {
        DeviceId = id, RobotId = name, FriendlyName = name, VerifiedSerialNumber = serial
    };
}
