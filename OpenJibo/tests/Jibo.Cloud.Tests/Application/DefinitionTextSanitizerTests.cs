using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class DefinitionTextSanitizerTests
{
    [Fact]
    public void Sanitize_StripsSingleParentheticalPrefix()
    {
        var sanitized = DefinitionTextSanitizer.Sanitize(
            "(uncountable) A dessert made from frozen sweetened cream or a similar substance, usually flavoured.");

        Assert.Equal(
            "A dessert made from frozen sweetened cream or a similar substance, usually flavoured.",
            sanitized);
    }

    [Fact]
    public void Sanitize_StripsRegionalParentheticalPrefix()
    {
        var sanitized = DefinitionTextSanitizer.Sanitize(
            "(chiefly UK, Australia) A period of one or more days taken off work for leisure and often travel; often plural.");

        Assert.Equal(
            "A period of one or more days taken off work for leisure and often travel; often plural.",
            sanitized);
    }

    [Fact]
    public void Sanitize_LeavesDefinitionWithoutParenthesesUnchanged()
    {
        const string definition =
            "A day on which a religious event or secular celebration is traditionally observed.";

        Assert.Equal(definition, DefinitionTextSanitizer.Sanitize(definition));
    }
}
