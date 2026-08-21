using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jibo.Cloud.Tests.Application;

public sealed class OperationalMetricsTests
{
    [Fact]
    public async Task BinaryAudio_ReportsAcceptedBytesWithoutPassingSessionContext()
    {
        var metrics = new Mock<ITransportMetrics>(MockBehavior.Strict);
        metrics.Setup(item => item.BufferedAudioAccepted(4));
        var service = CreateService(metrics.Object);
        var session = ListeningSession();

        try
        {
            await service.HandleBinaryAudioAsync(session, new WebSocketMessageEnvelope { Binary = [1, 2, 3, 4] });
        }
        finally
        {
            WebSocketTurnFinalizationService.ReleaseBufferedAudio(session);
        }

        metrics.VerifyAll();
    }

    [Fact]
    public async Task OversizedBinaryAudio_ReportsLimitRejectionAndRejectedBytes()
    {
        var bytes = new byte[WebSocketTurnFinalizationService.MaximumAudioFrameBytes + 1];
        var metrics = new Mock<ITransportMetrics>(MockBehavior.Strict);
        metrics.Setup(item => item.BufferedAudioLimitRejected(bytes.Length));
        var service = CreateService(metrics.Object);

        var replies = await service.HandleBinaryAudioAsync(ListeningSession(),
            new WebSocketMessageEnvelope { Binary = bytes });

        Assert.Empty(replies);
        metrics.VerifyAll();
    }

    private static WebSocketTurnFinalizationService CreateService(ITransportMetrics metrics) => new(
        Mock.Of<IConversationBroker>(), Mock.Of<ISttStrategySelector>(), new NullTurnTelemetrySink(),
        NullLogger<WebSocketTurnFinalizationService>.Instance, null, null, null, null, metrics);

    private static CloudSession ListeningSession() => new()
    {
        SessionId = "not-exported",
        TurnState = { SawListen = true, TransId = "not-exported" }
    };
}
