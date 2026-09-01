namespace Jibo.Cloud.Domain.Models;

public sealed record UserDeviceLink(
    string UserId,
    string DeviceId,
    string ClaimSource,
    DateTimeOffset LinkedUtc);
