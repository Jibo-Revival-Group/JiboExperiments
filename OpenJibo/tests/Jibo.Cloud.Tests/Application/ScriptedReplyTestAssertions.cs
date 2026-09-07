using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Content;

namespace Jibo.Cloud.Tests.Application;

internal static class ScriptedReplyTestAssertions
{
    private static readonly Lazy<Task<JiboExperienceCatalog>> SharedCatalog = new(async () =>
        await new InMemoryJiboExperienceContentRepository().GetCatalogAsync());

    internal static async Task AssertImportedScriptedReplyAsync(
        JiboInteractionDecision decision,
        string expectedIntent,
        string? expectedReplySnippet = null)
        => await AssertImportedScriptedReplyAsync(
            decision,
            expectedIntent,
            string.IsNullOrWhiteSpace(expectedReplySnippet) ? [] : [expectedReplySnippet]);

    internal static async Task AssertImportedScriptedReplyAsync(
        JiboInteractionDecision decision,
        string expectedIntent,
        params string[] expectedReplySnippets)
    {
        Assert.Equal(expectedIntent, decision.IntentName);

        var catalog = await SharedCatalog.Value;
        if (TryMatchesImportedMimReply(catalog, expectedIntent, decision.ReplyText))
            return;

        var alternatives = expectedReplySnippets
            .Where(snippet => !string.IsNullOrWhiteSpace(snippet))
            .ToArray();

        if (alternatives.Length > 0)
        {
            Assert.True(
                alternatives.Any(snippet =>
                    decision.ReplyText.Contains(snippet, StringComparison.OrdinalIgnoreCase)),
                $"Expected reply to contain one of: [{string.Join(", ", alternatives.Select(snippet => $"\"{snippet}\""))}], but was: \"{decision.ReplyText}\"");
        }
    }

    internal static void AssertImportedScriptedReply(
        JiboInteractionDecision decision,
        string expectedIntent,
        string? expectedReplySnippet = null)
        => AssertImportedScriptedReplyAsync(decision, expectedIntent, expectedReplySnippet).GetAwaiter().GetResult();

    internal static void AssertImportedScriptedReply(
        JiboInteractionDecision decision,
        string expectedIntent,
        params string[] expectedReplySnippets)
        => AssertImportedScriptedReplyAsync(decision, expectedIntent, expectedReplySnippets).GetAwaiter().GetResult();

    private static bool TryMatchesImportedMimReply(
        JiboExperienceCatalog catalog,
        string intentName,
        string replyText)
    {
        var mimReplies = LegacyMimIntentResolver.TryResolveReplies(catalog, intentName, explicitMimId: null);
        if (mimReplies is not { Count: > 0 }) return false;

        var normalizedActual = NormalizeReplyText(replyText);
        return mimReplies.Any(reply =>
            NormalizeReplyText(LegacyMimTemplateRenderer.Render(reply.Reply, displayName: null)) ==
            normalizedActual);
    }

    private static string NormalizeReplyText(string text)
    {
        var stripped = Regex.Replace(text, "<[^>]+>", " ");
        return Regex.Replace(stripped, "\\s+", " ").Trim();
    }
}
