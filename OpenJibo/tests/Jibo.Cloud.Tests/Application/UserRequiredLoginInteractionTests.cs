using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class UserRequiredLoginInteractionTests
{
    [Fact]
    public async Task RequiredLoginBlocksUnpairedRobotButAllowsVerificationCode()
    {
        var previous = Environment.GetEnvironmentVariable("OPENJIBO_USER_REQUIRED_LOGIN");
        Environment.SetEnvironmentVariable("OPENJIBO_USER_REQUIRED_LOGIN", "true");
        try
        {
            var verification = new JiboVerificationService();
            var service = CreateService(new InMemoryCloudStateStore(
                Path.Combine(Path.GetTempPath(), $"openjibo-required-login-{Guid.NewGuid():N}.json")),
                verification);

            var blocked = await service.BuildDecisionAsync(new TurnContext
            {
                DeviceId = "unpaired-device",
                RawTranscript = "hey jibo tell me a joke",
                NormalizedTranscript = "hey jibo tell me a joke"
            });
            Assert.Equal("account_required", blocked.IntentName);

            var verificationDecision = await service.BuildDecisionAsync(new TurnContext
            {
                DeviceId = "unpaired-device",
                RawTranscript = "hey jibo verify me",
                NormalizedTranscript = "hey jibo verify me"
            });
            Assert.Equal("verify_me", verificationDecision.IntentName);
            Assert.Contains("verification code", verificationDecision.ReplyText,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENJIBO_USER_REQUIRED_LOGIN", previous);
        }
    }

    [Fact]
    public async Task UnsetRequiredLoginPreservesOpenRobotAccess()
    {
        var previous = Environment.GetEnvironmentVariable("OPENJIBO_USER_REQUIRED_LOGIN");
        Environment.SetEnvironmentVariable("OPENJIBO_USER_REQUIRED_LOGIN", null);
        try
        {
            var service = CreateService(new InMemoryCloudStateStore(
                Path.Combine(Path.GetTempPath(), $"openjibo-optional-login-{Guid.NewGuid():N}.json")),
                new JiboVerificationService());

            var decision = await service.BuildDecisionAsync(new TurnContext
            {
                DeviceId = "unpaired-device",
                RawTranscript = "hey jibo tell me a joke",
                NormalizedTranscript = "hey jibo tell me a joke"
            });

            Assert.NotEqual("account_required", decision.IntentName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENJIBO_USER_REQUIRED_LOGIN", previous);
        }
    }

    private static JiboInteractionService CreateService(
        ICloudStateStore store, JiboVerificationService verification) =>
        new(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            new InMemoryPersonalMemoryStore(),
            cloudStateStore: store,
            jiboVerificationService: verification);

    private sealed class FirstItemRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items) => items[0];
    }
}
