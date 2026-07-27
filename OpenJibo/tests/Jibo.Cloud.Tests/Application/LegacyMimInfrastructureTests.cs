using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Content;

namespace Jibo.Cloud.Tests.Application;

public sealed class LegacyMimInfrastructureTests
{
    [Fact]
    public void LegacyMimPromptNormalizer_PreservesBreakAndPhonemeAndExtractsEmotion()
    {
        var normalized = LegacyMimPromptNormalizer.Normalize(
            "<anim name='Greetings_02'/> Hey ${loopMember}. <break size='.3'/> <phoneme ph='test'>Life</phoneme> is good. <ssa cat='happy'/>",
            preservePlaceholders: true,
            preserveTtsMarkup: true);

        Assert.Equal("happy", normalized.Emotion);
        Assert.Contains("<break size='.3'/>", normalized.Text, StringComparison.Ordinal);
        Assert.Contains("<phoneme ph='test'>Life</phoneme>", normalized.Text, StringComparison.Ordinal);
        Assert.Contains("${loopMember}", normalized.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<anim", normalized.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<ssa", normalized.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("POD=='morning'", "morning", "morning", true)]
    [InlineData("PODclaim=='morning'", "morning", "afternoon", true)]
    [InlineData("loopMember", null, null, false)]
    [InlineData("loopMember", null, null, true)]
    [InlineData("jibo.emotion==\"NEUTRAL\"", null, null, true)]
    public void LegacyMimConditionEvaluator_MatchesExtendedConditions(
        string condition,
        string? podClaim,
        string? pod,
        bool hasSpeaker)
    {
        var context = new LegacyMimConditionEvaluator.Context(
            HolidayClaim: null,
            Holiday: null,
            CurrentDate: DateOnly.Parse("2026-07-27"),
            PodClaim: podClaim,
            Pod: pod,
            HasSpeaker: hasSpeaker,
            Emotion: "NEUTRAL");

        var expected = condition switch
        {
            "loopMember" => hasSpeaker,
            _ => true
        };

        Assert.Equal(expected, LegacyMimConditionEvaluator.Matches(condition, context));
    }

    [Fact]
    public void ImportCatalog_MapsGreetingMimBucketsWithMetadata()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "Greetings");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(catalog.ReactiveGreetingReplies, reply =>
            reply.Condition.Contains("POD=='morning'", StringComparison.OrdinalIgnoreCase) &&
            reply.MimId == "GenericMorningSalutation");
        Assert.Contains(catalog.WhatsUpReplies, reply =>
            reply.MimId == "WhatsUpResp" &&
            reply.Reply.Contains("Jibo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.GoodbyeReplies, reply =>
            reply.MimId == "GoodbyeRespCM" &&
            reply.Reply.Contains("Goodbye", StringComparison.OrdinalIgnoreCase));
        Assert.True(catalog.MimReplies.ContainsKey("WhatsUpResp"));
    }

    [Fact]
    public void LegacyMimReplySelector_AttachesPromptMetadata()
    {
        var replies = new[]
        {
            new JiboConditionedReply
            {
                Condition = string.Empty,
                Reply = "Hello ${loopMember}.",
                MimId = "WhatsUpResp",
                PromptId = "WhatsUpResp_AN_01",
                Weight = 1
            }
        };

        var selection = LegacyMimReplySelector.Select(
            replies,
            new FirstReplyRandomizer(),
            new LegacyMimConditionEvaluator.Context(null, null, DateOnly.Parse("2026-07-27"), HasSpeaker: true),
            "Alex",
            "Fallback.",
            "WhatsUpResp");

        Assert.Equal("WhatsUpResp", selection.MimId);
        Assert.Equal("WhatsUpResp_AN_01", selection.PromptId);
        Assert.Equal("Hello Alex.", selection.ReplyText);
    }

    [Fact]
    public void LegacyMimReplySelector_RandomlySelectsAmongEqualWeightMatches()
    {
        var replies = new[]
        {
            new JiboConditionedReply { Condition = string.Empty, Reply = "First.", Weight = 1, PromptId = "p1" },
            new JiboConditionedReply { Condition = string.Empty, Reply = "Second.", Weight = 1, PromptId = "p2" },
            new JiboConditionedReply { Condition = string.Empty, Reply = "Third.", Weight = 1, PromptId = "p3" }
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 30; i++)
        {
            var selection = LegacyMimReplySelector.Select(
                replies,
                new DefaultJiboRandomizer(),
                new LegacyMimConditionEvaluator.Context(null, null, DateOnly.Parse("2026-07-27")),
                displayName: null,
                "Fallback.",
                "TestMim");
            seen.Add(selection.ReplyText);
        }

        Assert.True(seen.Count > 1);
    }

    [Fact]
    public async Task ImportedCatalog_IndexesFavoriteColorMimWithMultiplePrompts()
    {
        var catalog = await new InMemoryJiboExperienceContentRepository().GetCatalogAsync();
        Assert.True(catalog.MimReplies.TryGetValue("RI_JBO_HasFavoriteColor", out var replies));
        Assert.True(replies.Count > 1);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 20; i++)
        {
            var selection = LegacyMimReplySelector.Select(
                replies,
                new DefaultJiboRandomizer(),
                new LegacyMimConditionEvaluator.Context(null, null, DateOnly.Parse("2026-07-27")),
                displayName: null,
                "Blue is my favorite color.",
                "RI_JBO_HasFavoriteColor");
            seen.Add(selection.ReplyText);
        }

        Assert.True(seen.Count > 1);
    }

    [Fact]
    public async Task TrySelectMimReply_RandomlySelectsAmongAllConditionedStoryPrompts()
    {
        var catalog = await new InMemoryJiboExperienceContentRepository().GetCatalogAsync();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 30; i++)
        {
            Assert.True(LegacyMimScriptedReplyBuilder.TrySelectMimReply(
                catalog,
                new DefaultJiboRandomizer(),
                "robot_story",
                new LegacyMimConditionEvaluator.Context(null, null, DateOnly.Parse("2026-07-27")),
                displayName: null,
                explicitMimId: "RA_JBO_Story",
                ["story, that sounds fun", "don't have any stories"],
                out var selection));

            seen.Add(selection!.ReplyText);
        }

        Assert.True(seen.Count > 1);
    }

    private sealed class FirstReplyRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items) => items[0];

        public double NextUnitInterval() => 0.0;
    }
}
