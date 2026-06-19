using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class KnowledgeSearchSpokenReplyFormatterTests
{
    [Theory]
    [InlineData(SearchBackendKind.Wolfram, "wolf ram alpha")]
    [InlineData(SearchBackendKind.ChatGPT, "chat gee pee tee")]
    [InlineData(SearchBackendKind.Ollama, "ollama")]
    public void FormatReply_PrefixesReplyWithSpokenSource(SearchBackendKind backendKind, string spokenSource)
    {
        var reply = KnowledgeSearchSpokenReplyFormatter.FormatReply(
            "The answer is forty two.",
            backendKind);

        Assert.Equal($"According to {spokenSource}. The answer is forty two.", reply);
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

    [Fact]
    public void FormatReply_ReturnsEmpty_WhenAnswerMissing()
    {
        Assert.Equal(string.Empty, KnowledgeSearchSpokenReplyFormatter.FormatReply("   ", SearchBackendKind.Wolfram));
    }
}
