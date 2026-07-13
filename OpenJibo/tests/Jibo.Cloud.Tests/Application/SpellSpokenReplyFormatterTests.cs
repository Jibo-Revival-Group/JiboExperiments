using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class SpellSpokenReplyFormatterTests
{
    [Fact]
    public void Format_Attacking_ReturnsPhoneticSpelling()
    {
        var reply = SpellSpokenReplyFormatter.Format("attacking");

        Assert.Equal(
            "attacking is spelt with. ae, tea, tea, ae, see, kay, eye, en, jee.",
            reply);
    }

    [Fact]
    public void Format_MixedCaseWord_PronouncesLettersCaseInsensitively()
    {
        var reply = SpellSpokenReplyFormatter.Format("Cat");

        Assert.Equal("Cat is spelt with. see, ae, tea.", reply);
    }

    [Fact]
    public void Format_WordWithApostropheAndHyphen_SpellsOnlyLetters()
    {
        var reply = SpellSpokenReplyFormatter.Format("o'brien");

        Assert.Equal("o'brien is spelt with. hoh, b, are, eye, e, en.", reply);
    }

    [Fact]
    public void Format_NullOrEmptyWord_ReturnsClarification()
    {
        Assert.Equal(
            "I didn't catch what word you wanted me to spell. Can you ask me again with a hey jibo?",
            SpellSpokenReplyFormatter.Format(null));
        Assert.Equal(
            "I didn't catch what word you wanted me to spell. Can you ask me again with a hey jibo?",
            SpellSpokenReplyFormatter.Format("   "));
    }

    [Fact]
    public void Format_WordWithoutSpellableLetters_ReturnsClarification()
    {
        var reply = SpellSpokenReplyFormatter.Format("123");

        Assert.Equal(
            "I didn't catch what word you wanted me to spell. Can you ask me again with a hey jibo?",
            reply);
    }
}
