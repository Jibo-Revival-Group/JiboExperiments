using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class AwsSigV4ReplayObservationOptions
{
    public bool Enabled { get; set; }
    public string? HmacKey { get; set; }
    public short KeyVersion { get; set; } = 1;
    public int Capacity { get; set; } = 256;
}

/// <summary>
/// Computes the replay HMAC on the request thread, then queues only the opaque
/// digest and bounded labels for best-effort cross-replica observation.
/// </summary>
public sealed class AwsSigV4ReplayObservationPublisher : BackgroundService,
    IAwsSigV4ReplayObservationPublisher
{
    public const string MeterName = "Jibo.Cloud.SigV4ReplayObservation";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>(
        "openjibo.sigv4_replay_observation.outcomes");
    private static readonly HashSet<string> SupportedOperations =
    [
        "Account.CreateHubToken",
        "Notification.NewRobotToken"
    ];

    private readonly IAwsSigV4ReplayObservationStore _store;
    private readonly AwsSigV4ReplayDigestKey _key;
    private readonly Channel<ObservationWorkItem> _channel;
    private readonly ILogger<AwsSigV4ReplayObservationPublisher> _logger;

    public AwsSigV4ReplayObservationPublisher(
        IAwsSigV4ReplayObservationStore store,
        AwsSigV4ReplayDigestKey key,
        int capacity,
        ILogger<AwsSigV4ReplayObservationPublisher> logger)
    {
        _store = store;
        _key = key;
        _logger = logger;
        _channel = Channel.CreateBounded<ObservationWorkItem>(new BoundedChannelOptions(
            Math.Clamp(capacity, 1, 4096))
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    public bool TryPublish(AwsSigV4VerifiedProof proof, string operation)
    {
        ArgumentNullException.ThrowIfNull(proof);
        if (!SupportedOperations.Contains(operation))
            throw new ArgumentException("Replay observation operation is not supported.", nameof(operation));

        var item = new ObservationWorkItem(
            proof.CreateReplayDigest(_key, operation),
            _key.Version,
            operation);
        var accepted = _channel.Writer.TryWrite(item);
        Record(operation, accepted ? "enqueued" : "dropped");
        return accepted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await _store.ObserveAsync(
                        item.Digest,
                        item.KeyVersion,
                        item.Operation,
                        stoppingToken);
                    Record(item.Operation, "persisted");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Record(item.Operation, "failed");
                    _logger.LogWarning(exception,
                        "Legacy SigV4 replay observation persistence failed operation={Operation} shadow=true",
                        item.Operation);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }

    private static void Record(string operation, string outcome) => Outcomes.Add(1,
        new KeyValuePair<string, object?>("operation", operation),
        new KeyValuePair<string, object?>("outcome", outcome));

    private sealed record ObservationWorkItem(byte[] Digest, short KeyVersion, string Operation);
}
