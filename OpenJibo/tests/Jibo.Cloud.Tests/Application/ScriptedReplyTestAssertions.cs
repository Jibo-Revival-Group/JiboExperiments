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
    {
        Assert.Equal(expectedIntent, decision.IntentName);

        var catalog = await SharedCatalog.Value;
        if (TryMatchesImportedMimReply(catalog, expectedIntent, decision.ReplyText))
            return;

        if (!string.IsNullOrWhiteSpace(expectedReplySnippet))
            Assert.Contains(expectedReplySnippet, decision.ReplyText, StringComparison.OrdinalIgnoreCase);
    }

    internal static void AssertImportedScriptedReply(
        JiboInteractionDecision decision,
        string expectedIntent,
        string? expectedReplySnippet = null)
    {
        AssertImportedScriptedReplyAsync(decision, expectedIntent, expectedReplySnippet).GetAwaiter().GetResult();
    }

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
