using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class RepeatLastCommandInteractionServiceTests
{
    [Fact]
    public async Task BuildDecisionAsync_DoThatAgain_ReplaysLastCommand()
    {
        var store = new RepeatLastCommandStore();
        var service = CreateService(store, new FixedIndexRandomizer(3));

        var first = await service.BuildDecisionAsync(CreateTurn("hey jibo roll a dice", "robot-a"));
        Assert.Equal("roll_dice", first.IntentName);

        var repeat = await service.BuildDecisionAsync(CreateTurn("hey jibo do that again", "robot-a"));
        Assert.Equal("roll_dice", repeat.IntentName);
        Assert.Equal("It landed on 4.", repeat.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_DoThatAgain_WithColdStore_ReturnsDontRemember()
    {
        var store = new RepeatLastCommandStore();
        var service = CreateService(store);

        var decision = await service.BuildDecisionAsync(CreateTurn("do that again", "robot-a"));

        Assert.Equal("repeat_last_command", decision.IntentName);
        Assert.Equal("I don't remember what you asked me to do. What would you like?", decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_DoThatAgain_DoesNotLeakAcrossRobots()
    {
        var store = new RepeatLastCommandStore();
        var service = CreateService(store, new FixedIndexRandomizer(3));

        await service.BuildDecisionAsync(CreateTurn("hey jibo roll a dice", "robot-a"));

        var robotB = await service.BuildDecisionAsync(CreateTurn("do that again", "robot-b"));

        Assert.Equal("repeat_last_command", robotB.IntentName);
        Assert.Equal("I don't remember what you asked me to do. What would you like?", robotB.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_DoThatAgainTwice_ReplaysOriginalCommandBothTimes()
    {
        var store = new RepeatLastCommandStore();
        var service = CreateService(store, new FixedIndexRandomizer(3));

        await service.BuildDecisionAsync(CreateTurn("hey jibo roll a dice", "robot-a"));

        var firstRepeat = await service.BuildDecisionAsync(CreateTurn("do that again", "robot-a"));
        var secondRepeat = await service.BuildDecisionAsync(CreateTurn("do that again", "robot-a"));

        Assert.Equal("roll_dice", firstRepeat.IntentName);
        Assert.Equal("roll_dice", secondRepeat.IntentName);
        Assert.Equal("It landed on 4.", firstRepeat.ReplyText);
        Assert.Equal("It landed on 4.", secondRepeat.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_DoThatAgain_AfterTtlExpires_ReturnsDontRemember()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new RepeatLastCommandStore(TimeSpan.FromMinutes(30), clock);
        var service = CreateService(store, new FixedIndexRandomizer(3));

        await service.BuildDecisionAsync(CreateTurn("hey jibo roll a dice", "robot-a"));

        clock.Advance(TimeSpan.FromMinutes(31));

        var decision = await service.BuildDecisionAsync(CreateTurn("do that again", "robot-a"));

        Assert.Equal("repeat_last_command", decision.IntentName);
        Assert.Equal("I don't remember what you asked me to do. What would you like?", decision.ReplyText);
    }

    [Fact]
    public async Task BuildDecisionAsync_DoThatAgain_WithinWindow_RenewsTtl()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new RepeatLastCommandStore(TimeSpan.FromMinutes(30), clock);
        var service = CreateService(store, new FixedIndexRandomizer(3));

        await service.BuildDecisionAsync(CreateTurn("hey jibo roll a dice", "robot-a"));

        // Advance almost to expiry, then repeat — that renews the window.
        clock.Advance(TimeSpan.FromMinutes(29));
        var midWindow = await service.BuildDecisionAsync(CreateTurn("do that again", "robot-a"));
        Assert.Equal("roll_dice", midWindow.IntentName);

        // Another 29 minutes from the renew should still hit.
        clock.Advance(TimeSpan.FromMinutes(29));
        var stillAlive = await service.BuildDecisionAsync(CreateTurn("do that again", "robot-a"));
        Assert.Equal("roll_dice", stillAlive.IntentName);
    }

    private static TurnContext CreateTurn(string transcript, string robotId)
    {
        return new TurnContext
        {
            SessionId = $"session-{robotId}",
            DeviceId = robotId,
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            Attributes = new Dictionary<string, object?>
            {
                ["robotId"] = robotId,
                ["friendlyId"] = robotId
            }
        };
    }

    private static JiboInteractionService CreateService(
        RepeatLastCommandStore store,
        IJiboRandomizer? randomizer = null)
    {
        return new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            randomizer ?? new FixedIndexRandomizer(0),
            new InMemoryPersonalMemoryStore(),
            repeatLastCommandStore: store);
    }

    private sealed class FixedIndexRandomizer(int index) : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items) => items[index];
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow += delta;
    }
}
