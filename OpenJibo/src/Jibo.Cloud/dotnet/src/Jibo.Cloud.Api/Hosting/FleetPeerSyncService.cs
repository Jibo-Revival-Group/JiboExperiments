using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Api.Hosting;

internal sealed class FleetPeerSyncService(
    IHttpClientFactory httpClientFactory,
    ICloudStateStore cloudStateStore,
    RobotPresenceRegistry robotPresenceRegistry,
    OpenJiboServerIdentity serverIdentity,
    FleetNetworkPresenceRegistry networkPresenceRegistry,
    IConfiguration configuration,
    ILogger<FleetPeerSyncService> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(Math.Clamp(
        configuration.GetValue("OpenJibo:FleetNetwork:SyncIntervalSeconds", 30), 10, 300));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Fleet peer presence publish failed");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        var sharedKey = configuration["OpenJibo:FleetNetwork:PeerSyncSharedKey"];
        if (string.IsNullOrWhiteSpace(sharedKey)) return;

        var now = DateTimeOffset.UtcNow;
        var devices = cloudStateStore.GetDevices();
        var connections = robotPresenceRegistry.GetLiveConnections();
        var robotIds = devices
            .Where(device => connections.Any(connection =>
                connection.RobotKeys.Contains(device.DeviceId) || connection.RobotKeys.Contains(device.RobotId)))
            .Select(device => device.DeviceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var payload = new FleetPeerPresencePayload(
            serverIdentity.ServerId,
            serverIdentity.CanonicalHost,
            serverIdentity.InstanceId,
            robotIds,
            connections.Count,
            now);
        networkPresenceRegistry.Report(new FleetServerPresenceReport(
            payload.ServerId, payload.CanonicalHost, payload.InstanceId, payload.ConnectedRobotIds,
            payload.ConnectionCount, payload.ReportedAtUtc, IsLocal: true));

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var payloadHash = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();
        var timestamp = now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = FleetPeerSyncAuthentication.Sign(payload.ServerId, timestamp, payloadHash, sharedKey);
        var peers = cloudStateStore.GetTrustedServers()
            .Where(server => server.IsActive && server.ParticipatesInCloudSync &&
                             !server.ServerId.Equals(serverIdentity.ServerId, StringComparison.OrdinalIgnoreCase) &&
                             !server.CanonicalHost.Equals(serverIdentity.CanonicalHost, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var peer in peers)
        {
            if (!Uri.TryCreate($"https://{peer.CanonicalHost}/api/network/fleet-presence", UriKind.Absolute,
                    out var endpoint))
            {
                logger.LogWarning("Fleet peer {Host} has an invalid canonical host", peer.CanonicalHost);
                continue;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new ByteArrayContent(payloadBytes)
            };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            request.Headers.Add(FleetPeerSyncAuthentication.ServerIdHeader, payload.ServerId);
            request.Headers.Add(FleetPeerSyncAuthentication.TimestampHeader, timestamp);
            request.Headers.Add(FleetPeerSyncAuthentication.PayloadHashHeader, payloadHash);
            request.Headers.Add(FleetPeerSyncAuthentication.SignatureHeader, signature);

            try
            {
                using var response = await httpClientFactory.CreateClient("OpenJiboFleetPeerSync")
                    .SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    logger.LogWarning("Fleet peer {Host} rejected presence report with {StatusCode}",
                        peer.CanonicalHost, (int)response.StatusCode);
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Fleet peer {Host} could not be reached", peer.CanonicalHost);
            }
        }
    }
}

internal static class FleetPeerSyncAuthentication
{
    internal const string ServerIdHeader = "X-OpenJibo-Peer-Server-Id";
    internal const string TimestampHeader = "X-OpenJibo-Peer-Timestamp";
    internal const string PayloadHashHeader = "X-OpenJibo-Peer-Payload-Sha256";
    internal const string SignatureHeader = "X-OpenJibo-Peer-Signature";

    internal static string Sign(string serverId, string timestamp, string payloadHash, string sharedKey)
    {
        var payload = Encoding.UTF8.GetBytes($"{serverId}\n{timestamp}\n{payloadHash}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedKey));
        return Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
    }

    internal static bool Verify(string serverId, string timestamp, string payloadHash, string signature,
        string sharedKey)
    {
        var expected = Sign(serverId, timestamp, payloadHash, sharedKey);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var signatureBytes = Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant());
        return expectedBytes.Length == signatureBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, signatureBytes);
    }
}
