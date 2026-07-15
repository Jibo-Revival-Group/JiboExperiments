namespace Jibo.Cloud.Infrastructure.FunFacts;

public sealed class UselessFactsOptions
{
    public string BaseUrl { get; set; } = "https://uselessfacts.jsph.pl";

    public string UserAgent { get; set; } = "OpenJiboCloud/1.0";

    public int FailureCacheTtlSeconds { get; set; } = 45;
}
