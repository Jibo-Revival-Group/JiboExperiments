using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class KnowledgeSearchSpokenReplyFormatterTests
{
    [Theory]
    [InlineData(SearchBackendKind.Wolfram, "wolf ram alpha")]
    [InlineData(SearchBackendKind.ChatGPT, "chat gee pee tee")]
    [InlineData(SearchBackendKind.Ollama, "ollama")]
    [InlineData(SearchBackendKind.Wikipedia, "wikipedia dot org")]
    public void FormatReply_PrefixesReplyWithSpokenSource(SearchBackendKind backendKind, string spokenSource)
    {
        var reply = KnowledgeSearchSpokenReplyFormatter.FormatReply(
            "The answer is forty two.",
            backendKind);

        Assert.Equal($"According to {spokenSource}. The answer is forty two.", reply);
    }

    [Fact]
    public void FormatNotFoundReply_UsesCantFindAnything()
    {
        Assert.Equal("I can't find anything.", KnowledgeSearchSpokenReplyFormatter.FormatNotFoundReply());
    }

    [Fact]
    public void FormatUnavailableReply_UsesSourcesAreDown()
    {
        Assert.Contains(
            "info sources are down",
            KnowledgeSearchSpokenReplyFormatter.FormatUnavailableReply(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ChatGPT said AI is useful.", "chat gee pee tee said ae eye is useful.")]
    [InlineData("Wolfram Alpha and Ollama are sources.", "wolf ram alpha and ollama are sources.")]
    [InlineData("OpenAI built GPT-4.", "open ae eye built gee pee tee 4.")]
    [InlineData("LLMs use AI.", "ell ell ems use ae eye.")]
    public void FormatReply_ReplacesSpokenAiTerms(string input, string expectedBody)
    {
        var reply = KnowledgeSearchSpokenReplyFormatter.FormatReply(input, SearchBackendKind.ChatGPT);

        Assert.Equal($"According to chat gee pee tee. {expectedBody}", reply);
    }

    [Theory]
    [InlineData(
        "A graphics processing unit (GPU) accelerates rendering.",
        "A graphics processing unit (jee pee you) accelerates rendering.")]
    [InlineData(
        "The CPU and GPU work together.",
        "The see pee you and jee pee you work together.")]
    [InlineData(
        "NASA launched a probe.",
        "en ae es ae launched a probe.")]
    [InlineData(
        "Multiple GPUs can help.",
        "Multiple jee pee yous can help.")]
    [InlineData(
        "THE answer is clear.",
        "THE answer is clear.")]
    public void FormatReply_SpellsAcronymsLetterByLetter(string input, string expectedBody)
    {
        var reply = KnowledgeSearchSpokenReplyFormatter.FormatReply(input, SearchBackendKind.Wikipedia);

        Assert.Equal($"According to wikipedia dot org. {expectedBody}", reply);
    }

    [Theory]
    [InlineData(
        "The definition of & quot ;our& quot ; is possessive.",
        "The definition of \"our\" is possessive.")]
    [InlineData(
        "The definition of &quot;our&quot; is possessive.",
        "The definition of \"our\" is possessive.")]
    [InlineData(
        "The definition of &amp;quot;our&amp;quot; is possessive.",
        "The definition of \"our\" is possessive.")]
    [InlineData(
        "Rock & amp ; roll uses an ampersand.",
        "Rock & roll uses an ampersand.")]
    public void FormatReply_DecodesValidAndMalformedHtmlEntities(string input, string expectedBody)
    {
        var reply = KnowledgeSearchSpokenReplyFormatter.FormatReply(input, SearchBackendKind.Wolfram);

        Assert.Equal($"According to wolf ram alpha. {expectedBody}", reply);
    }

    [Fact]
    public void FormatReply_ReturnsEmpty_WhenAnswerMissing()
    {
        Assert.Equal(string.Empty, KnowledgeSearchSpokenReplyFormatter.FormatReply("   ", SearchBackendKind.Wolfram));
    }
}
