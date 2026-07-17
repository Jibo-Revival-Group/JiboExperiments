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
}
