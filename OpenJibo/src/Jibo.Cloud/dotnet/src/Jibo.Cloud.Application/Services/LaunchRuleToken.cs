namespace Jibo.Cloud.Application.Services;

public sealed class LaunchRuleToken
{
    public required string Text { get; init; }
    public bool IsOptional { get; init; }
}
