using System.Security.Cryptography;
using System.Diagnostics.Metrics;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class AwsSigV4ReplayObservationPublisherTests
{
    private static readonly AwsSigV4ReplayDigestKey Key = new(
        SHA256.HashData("replay-publisher-key"u8), 3);

    [Fact]
    public void TryPublish_DropsWhenBoundedQueueIsFull()
    {
        var outcomes = new List<(string Operation, string Outcome)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AwsSigV4ReplayObservationPublisher.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var values = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString());
            outcomes.Add((values["operation"]!, values["outcome"]!));
        });
        listener.Start();
        var publisher = new AwsSigV4ReplayObservationPublisher(
            new RecordingStore(),
            Key,
            1,
            NullLogger<AwsSigV4ReplayObservationPublisher>.Instance);
        var proof = CreateProof();

        Assert.True(publisher.TryPublish(proof, "Account.CreateHubToken"));
        Assert.False(publisher.TryPublish(proof, "Account.CreateHubToken"));
        Assert.Contains(("Account.CreateHubToken", "enqueued"), outcomes);
        Assert.Contains(("Account.CreateHubToken", "dropped"), outcomes);
    }

    [Fact]
    public async Task ExecuteAsync_PersistsOnlyOpaqueDigestAndContinuesAfterFailure()
    {
        var store = new RecordingStore(failFirst: true);
        var publisher = new AwsSigV4ReplayObservationPublisher(
            store,
            Key,
            4,
            NullLogger<AwsSigV4ReplayObservationPublisher>.Instance);
        var proof = CreateProof();
        var expectedDigest = proof.CreateReplayDigest(Key, "Notification.NewRobotToken");

        await publisher.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(publisher.TryPublish(proof, "Account.CreateHubToken"));
            Assert.True(publisher.TryPublish(proof, "Notification.NewRobotToken"));
            await store.SecondObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await publisher.StopAsync(CancellationToken.None);
            publisher.Dispose();
        }

        Assert.Equal(2, store.Observations.Count);
        var persisted = store.Observations[1];
        Assert.Equal(expectedDigest, persisted.Digest);
        Assert.Equal(3, persisted.KeyVersion);
        Assert.Equal("Notification.NewRobotToken", persisted.Operation);
    }

    [Fact]
    public void TryPublish_RejectsUnboundedOperationLabel()
    {
        var publisher = new AwsSigV4ReplayObservationPublisher(
            new RecordingStore(),
            Key,
            1,
            NullLogger<AwsSigV4ReplayObservationPublisher>.Instance);

        Assert.Throws<ArgumentException>(() =>
            publisher.TryPublish(CreateProof(), "attacker-controlled-operation"));
    }

    private static AwsSigV4VerifiedProof CreateProof() => new(
        "access-key-sentinel",
        "20260920",
        "api",
        "jibo",
        new DateTimeOffset(2026, 9, 20, 12, 34, 56, TimeSpan.Zero),
        SHA256.HashData("signature-sentinel"u8));

    private sealed class RecordingStore(bool failFirst = false) : IAwsSigV4ReplayObservationStore
    {
        private readonly bool _failFirst = failFirst;
        private int _calls;

        public List<(byte[] Digest, short KeyVersion, string Operation)> Observations { get; } = [];
        public TaskCompletionSource SecondObservation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AwsSigV4ReplayObservation> ObserveAsync(
            ReadOnlyMemory<byte> replayDigest,
            short keyVersion,
            string operation,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            Observations.Add((replayDigest.ToArray(), keyVersion, operation));
            if (_failFirst && call == 1)
                throw new InvalidOperationException("simulated persistence failure");
            if (call == 2) SecondObservation.TrySetResult();

            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new AwsSigV4ReplayObservation(
                AwsSigV4ReplayObservationStatus.FirstSeen,
                1,
                now,
                now,
                now.AddMinutes(15)));
        }
    }
}
