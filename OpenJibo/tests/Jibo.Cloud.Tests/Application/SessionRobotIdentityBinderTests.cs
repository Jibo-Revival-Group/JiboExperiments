using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Application;

public sealed class SessionRobotIdentityBinderTests
{
    [Fact]
    public void TryBindFromContextPayload_StampsRobotIdOntoPathTokenSession()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "SHARED-SINGLETON-DEVICE",
            RobotId = "Bootstrap-Robot"
        });

        var session = store.OpenSession("neo-hub-listen", null, "conn:abc", "192.168.7.142", "/v1/listen");
        Assert.Null(session.DeviceId);

        var bound = SessionRobotIdentityBinder.TryBindFromContextPayload(
            session,
            """{"general":{"accountID":"acct-1","robotID":"Ghost-Instance-Onion-Silk"}}""");

        Assert.True(bound);
        Assert.Equal("Ghost-Instance-Onion-Silk", session.DeviceId);
        Assert.Equal("Ghost-Instance-Onion-Silk", session.Metadata["friendlyId"]?.ToString());
    }

    [Fact]
    public void MapListenMessage_UsesContextRobotId_AsTurnDeviceId()
    {
        var session = new CloudSession
        {
            Kind = "neo-hub-listen",
            DeviceId = null,
            Token = "conn:xyz"
        };
        session.TurnState.ContextPayload =
            """{"general":{"accountID":"acct-1","robotID":"Jibo-Two"},"runtime":{"loop":{"users":[]}}}""";
        SessionRobotIdentityBinder.TryBindFromContextPayload(session, session.TurnState.ContextPayload);

        var turn = ProtocolToTurnContextMapper.MapListenMessage(
            new WebSocketMessageEnvelope { HostName = "host", ConnectionId = "c1" },
            session,
            "AUTO_FINALIZE");

        Assert.Equal("Jibo-Two", turn.DeviceId);
        Assert.Equal("Jibo-Two", turn.Attributes["friendlyId"]?.ToString());
    }

    [Fact]
    public void OpenSession_PathToken_DoesNotInheritSingletonDeviceId()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "SHARED-SINGLETON-DEVICE",
            RobotId = "Bootstrap-Robot"
        });

        var session = store.OpenSession("neo-hub-listen", null, "conn:robot-a", null, "/v1/listen");
        Assert.Null(session.DeviceId);
    }

    [Fact]
    public void TryBindFromContextPayload_UsesRuntimeLoopJiboId_WhenGeneralHasNoRobotId()
    {
        var store = new InMemoryCloudStateStore();
        store.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "SHARED-SINGLETON-DEVICE",
            RobotId = "Bootstrap-Robot"
        });

        var sessionOne = store.OpenSession("neo-hub-listen", null, "conn:robot-one", "192.168.7.142", "/v1/listen");
        var sessionTwo = store.OpenSession("neo-hub-listen", null, "conn:robot-two", "192.168.7.143", "/v1/listen");
        Assert.Null(sessionOne.DeviceId);
        Assert.Null(sessionTwo.DeviceId);

        // Real captured CONTEXT shape: general only has "release", identity is in runtime.loop.
        var boundOne = SessionRobotIdentityBinder.TryBindFromContextPayload(
            sessionOne,
            """{"runtime":{"loop":{"loopId":"loop-household-one","jibo":{"id":"jibo-unit-one","color":"WHITE"},"users":[]}},"general":{"release":"1.9.2"}}""");
        var boundTwo = SessionRobotIdentityBinder.TryBindFromContextPayload(
            sessionTwo,
            """{"runtime":{"loop":{"loopId":"loop-household-two","jibo":{"id":"jibo-unit-two","color":"BLACK"},"users":[]}},"general":{"release":"1.9.2"}}""");

        Assert.True(boundOne);
        Assert.True(boundTwo);
        Assert.Equal("jibo-unit-one", sessionOne.DeviceId);
        Assert.Equal("jibo-unit-two", sessionTwo.DeviceId);
        Assert.NotEqual(sessionOne.DeviceId, sessionTwo.DeviceId);
        Assert.Equal("jibo-unit-one", sessionOne.Metadata["friendlyId"]?.ToString());
        Assert.Equal("jibo-unit-two", sessionTwo.Metadata["friendlyId"]?.ToString());
        Assert.Equal("1.9.2", sessionOne.Metadata["firmwareVersion"]?.ToString());
        Assert.Equal("1.9.2", sessionTwo.Metadata["firmwareVersion"]?.ToString());
    }

    [Fact]
    public void TryBindFromContextPayload_StampsFirmwareVersionFromGeneralRelease()
    {
        var session = new CloudSession
        {
            Kind = "neo-hub-listen",
            DeviceId = null,
            Token = "conn:xyz"
        };

        var bound = SessionRobotIdentityBinder.TryBindFromContextPayload(
            session,
            """{"runtime":{"loop":{"loopId":"loop-1","jibo":{"id":"jibo-beam-unit"},"users":[]}},"general":{"release":"BEam.1.1.0"}}""");

        Assert.True(bound);
        Assert.Equal("BEam.1.1.0", session.Metadata["firmwareVersion"]?.ToString());
    }

    [Fact]
    public void MapListenMessage_UsesContextRelease_AsTurnFirmwareVersion()
    {
        var session = new CloudSession
        {
            Kind = "neo-hub-listen",
            DeviceId = null,
            Token = "conn:xyz"
        };
        session.TurnState.ContextPayload =
            """{"runtime":{"loop":{"loopId":"loop-1","jibo":{"id":"jibo-unit-one"},"users":[]}},"general":{"release":"1.9.2"}}""";
        SessionRobotIdentityBinder.TryBindFromContextPayload(session, session.TurnState.ContextPayload);

        var turn = ProtocolToTurnContextMapper.MapListenMessage(
            new WebSocketMessageEnvelope { HostName = "host", ConnectionId = "c1" },
            session,
            "AUTO_FINALIZE");

        Assert.Equal("1.9.2", turn.FirmwareVersion);
    }

    [Fact]
    public void TryReadRelease_ReadsGeneralRelease()
    {
        var ok = SessionRobotIdentityBinder.TryReadRelease(
            """{"general":{"release":"2.0.1"}}""",
            out var release);

        Assert.True(ok);
        Assert.Equal("2.0.1", release);
    }

    [Fact]
    public void MapListenMessage_UsesRuntimeLoopJiboId_AsTurnDeviceId()
    {
        var session = new CloudSession
        {
            Kind = "neo-hub-listen",
            DeviceId = null,
            Token = "conn:xyz"
        };
        session.TurnState.ContextPayload =
            """{"runtime":{"loop":{"loopId":"5c0b221fdf9d450019c5e253","jibo":{"id":"5c0b221fdf9d450019c5e254","color":"WHITE"},"users":[]}},"general":{"release":"1.9.2"}}""";
        SessionRobotIdentityBinder.TryBindFromContextPayload(session, session.TurnState.ContextPayload);

        var turn = ProtocolToTurnContextMapper.MapListenMessage(
            new WebSocketMessageEnvelope { HostName = "host", ConnectionId = "c1" },
            session,
            "AUTO_FINALIZE");

        Assert.Equal("5c0b221fdf9d450019c5e254", turn.DeviceId);
        Assert.Equal("5c0b221fdf9d450019c5e254", turn.Attributes["friendlyId"]?.ToString());
    }

    [Fact]
    public void TryBindFromContextPayload_DoesNotCreateAnInventoryLink()
    {
        var session = new CloudSession
        {
            Kind = "neo-hub-proactive",
            DeviceId = "Royal-Current-Sage-Canvas",
            Token = "hub-token"
        };

        var bound = SessionRobotIdentityBinder.TryBindFromContextPayload(
            session,
            """{"runtime":{"loop":{"jibo":{"id":"5c0b221fdf9d450019c5e254"}}}}""");

        Assert.True(bound);
        Assert.Equal("5c0b221fdf9d450019c5e254", session.DeviceId);
        Assert.False(session.Metadata.ContainsKey("registeredDeviceId"));
    }

    [Fact]
    public void TryReadGeneralRobotIdentity_FallsBackToLoopId_WhenJiboIdMissing()
    {
        var ok = SessionRobotIdentityBinder.TryReadGeneralRobotIdentity(
            """{"runtime":{"loop":{"loopId":"loop-only-id","users":[]}},"general":{"release":"1.9.2"}}""",
            out var robotId,
            out _);

        Assert.True(ok);
        Assert.Equal("loop-only-id", robotId);
    }
}
