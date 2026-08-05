namespace Jibo.Cloud.Domain.Models;

public sealed record RobotMergeResult(string SourceDeviceId, string TargetDeviceId, int MigratedSessions,
    int MigratedCredentialBindings, DateTimeOffset MergedUtc);
