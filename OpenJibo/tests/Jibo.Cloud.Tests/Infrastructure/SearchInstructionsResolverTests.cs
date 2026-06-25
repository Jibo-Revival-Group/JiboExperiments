using Jibo.Cloud.Infrastructure.Search;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class SearchInstructionsResolverTests
{
    [Fact]
    public void Resolve_PrefersInlineValue_OverFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"search-instructions-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "File instructions.");

        try
        {
            var resolved = SearchInstructionsResolver.Resolve("Inline instructions.", tempFile);
            Assert.Equal("Inline instructions.", resolved);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Resolve_ReadsInstructionsFile_WhenInlineMissing()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"search-instructions-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "Loaded from file.");

        try
        {
            var resolved = SearchInstructionsResolver.Resolve(null, tempFile);
            Assert.Equal("Loaded from file.", resolved);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Normalize_ExpandsEscapedNewlines()
    {
        var normalized = SearchInstructionsResolver.Normalize("Line one.\\nLine two.");
        Assert.Equal("Line one.\nLine two.", normalized);
    }
}