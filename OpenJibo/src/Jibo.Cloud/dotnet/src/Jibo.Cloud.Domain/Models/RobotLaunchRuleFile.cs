namespace Jibo.Cloud.Domain.Models;

public sealed class RobotLaunchRuleFile
{
    public required string RobotFriendlyName { get; init; }
    public required string FileName { get; init; }
    public required string Content { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset UploadedUtc { get; init; }
}
