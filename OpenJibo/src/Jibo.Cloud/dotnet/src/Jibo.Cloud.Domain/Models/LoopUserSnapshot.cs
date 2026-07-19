namespace Jibo.Cloud.Domain.Models;

/// <summary>
/// One entry from the robot's <c>runtime.loop.users</c> roster (Pegasus loop identity).
/// </summary>
public sealed record LoopUserSnapshot(
    string Id,
    string? FirstName = null,
    string? LastName = null,
    string? AccountId = null,
    string? Nickname = null,
    string? Type = null);
