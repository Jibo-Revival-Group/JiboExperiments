using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Services;

public sealed class RobotLaunchRuleParserTests
{
    [Fact]
    public void Parse_ExtractsLiteralTokensAndSkillEntity()
    {
        const string content = """
            TopRule = ($* open the radio {%skill='@be/radio'%} $*);
            GalleryRule = (open gallery {%skill='@be/gallery'%});
            """;

        var rules = RobotLaunchRuleParser.Parse("launch.rule", content);

        Assert.Equal(2, rules.Count);

        var radio = rules.Single(rule => rule.RuleName == "TopRule");
        Assert.Equal(["open", "the", "radio"], radio.LiteralTokens);
        Assert.Equal("@be/radio", radio.Entities["skill"]);

        var gallery = rules.Single(rule => rule.RuleName == "GalleryRule");
        Assert.Equal(["open", "gallery"], gallery.LiteralTokens);
        Assert.Equal("@be/gallery", gallery.Entities["skill"]);
    }
}

public sealed class RobotLaunchRuleMatcherTests
{
    [Fact]
    public void TryMatch_MatchesTranscriptSubsequence()
    {
        var rules = new[]
        {
            new ParsedLaunchRule
            {
                RuleName = "TopRule",
                SourceFile = "launch.rule",
                LiteralTokens = ["open", "the", "radio"],
                Entities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["skill"] = "@be/radio",
                    ["intent"] = "menu"
                }
            }
        };

        var match = RobotLaunchRuleMatcher.TryMatch("hey jibo open the radio please", rules);

        Assert.NotNull(match);
        Assert.Equal("@be/radio", match!.SkillId);
        Assert.Equal("menu", match.Intent);
    }

    [Fact]
    public void TryMatch_PrefersLongerRule()
    {
        var rules = new[]
        {
            new ParsedLaunchRule
            {
                RuleName = "ShortRule",
                SourceFile = "launch.rule",
                LiteralTokens = ["open"],
                Entities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["skill"] = "@be/settings"
                }
            },
            new ParsedLaunchRule
            {
                RuleName = "LongRule",
                SourceFile = "launch.rule",
                LiteralTokens = ["open", "gallery"],
                Entities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["skill"] = "@be/gallery"
                }
            }
        };

        var match = RobotLaunchRuleMatcher.TryMatch("open gallery now", rules);

        Assert.NotNull(match);
        Assert.Equal("@be/gallery", match!.SkillId);
        Assert.Equal("LongRule", match.Rule.RuleName);
    }
}

public sealed class RobotLaunchRuleOrchestratorTests
{
    [Fact]
    public async Task TryBuildDecisionAsync_ReturnsSkillRedirectDecisionForLaunchTurn()
    {
        const string robotName = "Royal-Current-Sage-Canvas";
        const string content = "TopRule = ($* open gallery {%skill='@be/gallery'%} $*);";
        var store = new InMemoryRobotLaunchRuleStore();
        store.Save(robotName, "launch.rule", content);
        var orchestrator = new RobotLaunchRuleOrchestrator(store, new RobotLaunchRuleHostSettings());
        var turn = new TurnContext
        {
            DeviceId = robotName,
            RawTranscript = "open gallery",
            NormalizedTranscript = "open gallery",
            Attributes = new Dictionary<string, object?>
            {
                ["listenRules"] = new[] { "launch" }
            }
        };

        var decision = await orchestrator.TryBuildDecisionAsync(turn, "open gallery", null, CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal("@be/gallery", decision!.SkillName);
        Assert.Equal("menu", decision.IntentName);
        Assert.Equal("true", decision.SkillPayload!["launchRuleMatch"]?.ToString());
    }

    [Fact]
    public async Task TryBuildDecisionAsync_UsesSingleRobotFallbackWhenIdentityMissing()
    {
        const string robotName = "Royal-Current-Sage-Canvas";
        var store = new InMemoryRobotLaunchRuleStore();
        store.Save(robotName, "launch.rule",
            "GalleryRule = ($* open gallery {%skill='@be/gallery'%} $*);");
        var orchestrator = new RobotLaunchRuleOrchestrator(store, new RobotLaunchRuleHostSettings());
        var turn = new TurnContext
        {
            DeviceId = "my-robot-serial-number",
            RawTranscript = "open gallery",
            NormalizedTranscript = "open gallery",
            Attributes = new Dictionary<string, object?>
            {
                ["listenHotphrase"] = true
            }
        };

        var decision = await orchestrator.TryBuildDecisionAsync(turn, "hey jibo open gallery", null, CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal("@be/gallery", decision!.SkillName);
    }

    [Fact]
    public async Task TryBuildDecisionAsync_SkipsNonLaunchTurn()
    {
        const string robotName = "Royal-Current-Sage-Canvas";
        var store = new InMemoryRobotLaunchRuleStore();
        store.Save(robotName, "launch.rule", "TopRule = (open gallery {%skill='@be/gallery'%});");
        var orchestrator = new RobotLaunchRuleOrchestrator(store, new RobotLaunchRuleHostSettings());
        var turn = new TurnContext
        {
            DeviceId = robotName,
            RawTranscript = "open gallery",
            Attributes = new Dictionary<string, object?>
            {
                ["listenRules"] = new[] { "chitchat" }
            }
        };

        var decision = await orchestrator.TryBuildDecisionAsync(turn, "open gallery", null, CancellationToken.None);

        Assert.Null(decision);
    }
}

public sealed class RobotLaunchRuleResponseMapperTests
{
    [Fact]
    public void Map_EmitsSkillRedirectForLaunchRuleDecision()
    {
        var turn = new TurnContext
        {
            Attributes = new Dictionary<string, object?>
            {
                ["listenRules"] = new[] { "launch" },
                ["transID"] = "trans-123"
            },
            RawTranscript = "open gallery",
            NormalizedTranscript = "open gallery"
        };
        var session = new CloudSession { LastTransId = "trans-123" };
        var plan = new ResponsePlan
        {
            IntentName = "menu",
            Actions =
            {
                new SpeakAction { Text = string.Empty },
                new InvokeNativeSkillAction
                {
                    SkillName = "@be/gallery",
                    Payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["launchRuleMatch"] = "true",
                        ["launchRuleIntent"] = "menu",
                        ["skillId"] = "@be/gallery",
                        ["skill"] = "@be/gallery"
                    }
                }
            }
        };

        var messages = ResponsePlanToSocketMessagesMapper.Map(plan, turn, session, emitSkillActions: true);

        Assert.Contains(messages, message => message.Text.Contains("SKILL_REDIRECT", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Text.Contains("@be/gallery", StringComparison.Ordinal));
    }
}

internal sealed class InMemoryRobotLaunchRuleStore : IRobotLaunchRuleStore
{
    private readonly Dictionary<string, Dictionary<string, RobotLaunchRuleFile>> _files = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<RobotLaunchRuleFile> List(string robotFriendlyName)
    {
        return _files.TryGetValue(robotFriendlyName, out var robotFiles)
            ? robotFiles.Values.OrderBy(file => file.FileName).ToArray()
            : [];
    }

    public IReadOnlyList<string> ListRobotFriendlyNames()
    {
        return _files.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public RobotLaunchRuleFile? Get(string robotFriendlyName, string fileName)
    {
        return _files.TryGetValue(robotFriendlyName, out var robotFiles) &&
               robotFiles.TryGetValue(fileName, out var file)
            ? file
            : null;
    }

    public RobotLaunchRuleFile Save(string robotFriendlyName, string fileName, string content)
    {
        if (!_files.TryGetValue(robotFriendlyName, out var robotFiles))
        {
            robotFiles = new Dictionary<string, RobotLaunchRuleFile>(StringComparer.OrdinalIgnoreCase);
            _files[robotFriendlyName] = robotFiles;
        }

        var record = new RobotLaunchRuleFile
        {
            RobotFriendlyName = robotFriendlyName,
            FileName = fileName,
            Content = content,
            SizeBytes = content.Length,
            UploadedUtc = DateTimeOffset.UtcNow
        };
        robotFiles[fileName] = record;
        return record;
    }

    public bool Delete(string robotFriendlyName, string fileName)
    {
        return _files.TryGetValue(robotFriendlyName, out var robotFiles) && robotFiles.Remove(fileName);
    }
}

internal static class RobotLaunchRuleTestSupport
{
    public static RobotLaunchRuleOrchestrator CreateOrchestrator(
        IRobotLaunchRuleStore? store = null,
        RobotLaunchRuleHostSettings? hostSettings = null)
    {
        return new RobotLaunchRuleOrchestrator(
            store ?? new InMemoryRobotLaunchRuleStore(),
            hostSettings ?? new RobotLaunchRuleHostSettings());
    }
}
