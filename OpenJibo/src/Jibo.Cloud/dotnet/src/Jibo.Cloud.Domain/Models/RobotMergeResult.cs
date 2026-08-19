namespace Jibo.Cloud.Domain.Models;

public sealed record RobotMergeResult(string SourceDeviceId, string TargetDeviceId, int MigratedSessions,
    int MigratedCredentialBindings, DateTimeOffset MergedUtc);

public sealed record RobotIdentityCleanupPreview(
    int MergeRelationshipCount,
    int ExplicitSessionBindingCount,
    int AuthenticationSessionCount,
    int CredentialBindingCount,
    IReadOnlyList<RobotMergeRelationship> MergeRelationships);

public sealed record RobotIdentityCleanupResult(
    int RestoredRobotRecords,
    int ClearedSessionBindings,
    int RevokedAuthenticationSessions,
    int PreservedCredentialBindings,
    DateTimeOffset ResetUtc);

public sealed record RobotMergeRelationship(string SourceDeviceId, string TargetDeviceId);
