using System.Net.WebSockets;
using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class HomeAssistantEntityAwareTests
{
    [Fact]
    public async Task SendCommandAndWaitAsync_ReturnsResult_WhenCommandResultArrives()
    {
        var registry = new HomeAssistantConnectionRegistry();
        var socket = new CapturingWebSocket(registry);
        registry.RegisterPairedConnection("ha-instance-1", socket);

        var waitTask = registry.SendCommandAndWaitAsync(
            "ha-instance-1",
            "lights_off_named",
            new Dictionary<string, string> { ["targetName"] = "zanes" });

        var result = await waitTask;

        Assert.NotNull(result);
        Assert.True(result!.IsOk);
        Assert.Equal("Zane's Lamp", result.MatchedName);
    }

    [Fact]
    public async Task BuildDecisionAsync_NamedLight_SpeaksMatchedName_WhenHaReturnsOk()
    {
        var (service, _) = CreateServiceWithRespondingHa(
            status: "ok",
            matchedName: "Bedroom Lamp");

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "turn off bedrom light",
            NormalizedTranscript = "turn off bedrom light",
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("ha_lights_off", decision.IntentName);
        Assert.Equal("Okay, turning off Bedroom Lamp.", decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_NamedLight_SpeaksNoLightNamed_WhenHaReturnsNotFound()
    {
        var (service, _) = CreateServiceWithRespondingHa(
            status: "not_found",
            heardName: "spaceship");

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "turn on spaceship light",
            NormalizedTranscript = "turn on spaceship light",
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("ha_lights_on", decision.IntentName);
        Assert.Equal("There is no light named spaceship.", decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_Climate_AsksWhichThermostat_WhenNeedsClarification()
    {
        var (service, pendingStore) = CreateServiceWithRespondingHa(
            status: "needs_clarification",
            candidates:
            [
                new HomeAssistantCommandCandidate("climate.hall", "Hallway"),
                new HomeAssistantCommandCandidate("climate.office", "Office")
            ]);

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "set the temperature to 70",
            NormalizedTranscript = "set the temperature to 70",
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("ha_climate_set_temp", decision.IntentName);
        Assert.Equal("Which thermostat should I use, Hallway or Office?", decision.ReplyText);
        Assert.NotNull(pendingStore.TryGet("BOJW-1000-0017-0820-0020", "Ghost-Instance-Onion-Silk"));
    }

    [Fact]
    public async Task BuildDecisionAsync_ClimateClarify_AppliesChosenEntity()
    {
        var (service, pendingStore) = CreateServiceWithRespondingHa(
            status: "ok",
            matchedName: "Hallway",
            autoRespondAction: "climate_apply_entity");

        pendingStore.Set(
            "Ghost-Instance-Onion-Silk",
            new HomeAssistantPendingClimateStore.PendingClimateAction(
                "set_temperature",
                [
                    new HomeAssistantCommandCandidate("climate.hall", "Hallway"),
                    new HomeAssistantCommandCandidate("climate.office", "Office")
                ],
                Temperature: "70"));

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = "hallway",
            NormalizedTranscript = "hallway",
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("ha_climate_clarify", decision.IntentName);
        Assert.Equal("Okay, setting the Hallway to 70 degrees.", decision.ReplyText);
        Assert.Null(pendingStore.TryGet("BOJW-1000-0017-0820-0020", "Ghost-Instance-Onion-Silk"));
    }

    [Fact]
    public void EntityNameMatcher_PicksClosestCandidate()
    {
        var match = HomeAssistantEntityNameMatcher.FindClosest(
            "hallwey",
            [
                new HomeAssistantCommandCandidate("climate.hall", "Hallway"),
                new HomeAssistantCommandCandidate("climate.office", "Office")
            ]);

        Assert.NotNull(match);
        Assert.Equal("Hallway", match!.Name);
    }

    private static (JiboInteractionService Service, HomeAssistantPendingClimateStore PendingStore)
        CreateServiceWithRespondingHa(
            string status,
            string? matchedName = null,
            string? heardName = null,
            IReadOnlyList<HomeAssistantCommandCandidate>? candidates = null,
            string? autoRespondAction = null)
    {
        var snapshotStore = new EncryptedUserDataSnapshotStore(
            Path.Combine(Path.GetTempPath(), $"openjibo-ha-aware-{Guid.NewGuid():N}.json"),
            new UserDataEncryptionService());
        var integrationStore = new InMemoryUserIntegrationStore(snapshotStore);
        integrationStore.AddHomeAssistantLink(
            "BOJW-1000-0017-0820-0020",
            "Ghost-Instance-Onion-Silk",
            "ha-instance-1");

        var cloudStateStore = new InMemoryCloudStateStore();
        cloudStateStore.UpdateRobot(new DeviceRegistration
        {
            DeviceId = "BOJW-1000-0017-0820-0020",
            RobotId = "Ghost-Instance-Onion-Silk",
            FriendlyName = "Test Robot"
        });

        var registry = new HomeAssistantConnectionRegistry();
        var socket = new CapturingWebSocket(
            registry,
            status,
            matchedName,
            heardName,
            candidates,
            autoRespondAction);
        registry.RegisterPairedConnection("ha-instance-1", socket);

        var pendingStore = new HomeAssistantPendingClimateStore();
        var commandService = new HomeAssistantCommandService(integrationStore, registry, cloudStateStore);
        var service = new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            new InMemoryPersonalMemoryStore(),
            cloudStateStore: cloudStateStore,
            userIntegrationStore: integrationStore,
            homeAssistantCommandService: commandService,
            homeAssistantPendingClimateStore: pendingStore);

        return (service, pendingStore);
    }

    private sealed class CapturingWebSocket : WebSocket
    {
        private readonly HomeAssistantConnectionRegistry _registry;
        private readonly string _status;
        private readonly string? _matchedName;
        private readonly string? _heardName;
        private readonly IReadOnlyList<HomeAssistantCommandCandidate>? _candidates;
        private readonly string? _autoRespondAction;

        public CapturingWebSocket(
            HomeAssistantConnectionRegistry registry,
            string status = "ok",
            string? matchedName = "Zane's Lamp",
            string? heardName = null,
            IReadOnlyList<HomeAssistantCommandCandidate>? candidates = null,
            string? autoRespondAction = null)
        {
            _registry = registry;
            _status = status;
            _matchedName = matchedName;
            _heardName = heardName;
            _candidates = candidates;
            _autoRespondAction = autoRespondAction;
        }

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;

        public override void Abort()
        {
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(buffer.Array!.AsMemory(buffer.Offset, buffer.Count));
            var root = document.RootElement;
            if (!root.TryGetProperty("requestId", out var requestIdElement))
                return Task.CompletedTask;

            var requestId = requestIdElement.GetString();
            if (string.IsNullOrWhiteSpace(requestId)) return Task.CompletedTask;

            var command = root.TryGetProperty("command", out var commandElement)
                ? commandElement.GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(_autoRespondAction) &&
                !string.Equals(command, _autoRespondAction, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(command, "lights_off_named", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(command, "lights_on_named", StringComparison.OrdinalIgnoreCase) &&
                command is not null &&
                !command.StartsWith("climate_", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            var payload = new Dictionary<string, object?>
            {
                ["type"] = "command_result",
                ["requestId"] = requestId,
                ["status"] = _status
            };
            if (!string.IsNullOrWhiteSpace(_matchedName))
                payload["matchedName"] = _matchedName;
            if (!string.IsNullOrWhiteSpace(_heardName))
                payload["heardName"] = _heardName;
            if (_candidates is { Count: > 0 })
                payload["candidates"] = _candidates
                    .Select(candidate => new { entityId = candidate.EntityId, name = candidate.Name })
                    .ToArray();

            var json = JsonSerializer.Serialize(payload);
            using var resultDoc = JsonDocument.Parse(json);
            _registry.TryCompleteCommandResult(resultDoc.RootElement.Clone());
            return Task.CompletedTask;
        }
    }

    private sealed class FirstItemRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items) => items[0];
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
    }
}
