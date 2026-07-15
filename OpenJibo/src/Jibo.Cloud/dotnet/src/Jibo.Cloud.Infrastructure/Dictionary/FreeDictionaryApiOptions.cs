namespace Jibo.Cloud.Infrastructure.Dictionary;

public sealed class FreeDictionaryApiOptions
{
    public string BaseUrl { get; set; } = "https://freedictionaryapi.com";

    public string UserAgent { get; set; } = "OpenJiboCloud/1.0";

    public int FailureCacheTtlSeconds { get; set; } = 45;

    public int SuccessCacheTtlSeconds { get; set; } = 300;
}
