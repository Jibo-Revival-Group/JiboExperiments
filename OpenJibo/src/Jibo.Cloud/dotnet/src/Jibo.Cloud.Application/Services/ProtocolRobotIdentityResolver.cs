using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

public sealed class ProtocolRobotIdentityResolver(ICloudStateStore stateStore)
{
    public ProtocolRobotIdentity Resolve(ProtocolEnvelope envelope)
    {
        var headerIdentity = Normalize(envelope.DeviceId);
        var bearerToken = ReadBearerToken(envelope);
        var tokenSession = string.IsNullOrWhiteSpace(bearerToken) ? null : stateStore.FindSessionByToken(bearerToken);
        var tokenIdentity = Normalize(tokenSession?.DeviceId);

        if (!string.IsNullOrWhiteSpace(headerIdentity) && !string.IsNullOrWhiteSpace(tokenIdentity) &&
            !headerIdentity.Equals(tokenIdentity, StringComparison.OrdinalIgnoreCase))
            return new ProtocolRobotIdentity(null, "conflict", true, true, true);

        if (!string.IsNullOrWhiteSpace(headerIdentity))
            return new ProtocolRobotIdentity(headerIdentity, "robot-header", true,
                !string.IsNullOrWhiteSpace(bearerToken), tokenSession is not null);

        if (!string.IsNullOrWhiteSpace(tokenIdentity))
            return new ProtocolRobotIdentity(tokenIdentity, "bearer-token", false, true, true);

        return new ProtocolRobotIdentity(null, "unresolved", false,
            !string.IsNullOrWhiteSpace(bearerToken), tokenSession is not null);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ReadBearerToken(ProtocolEnvelope envelope)
    {
        if (!envelope.Headers.TryGetValue("Authorization", out var authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorization["Bearer ".Length..].Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}

public sealed record ProtocolRobotIdentity(string? DeviceId, string Source, bool HeaderPresent,
    bool BearerTokenPresent, bool BearerTokenResolved)
{
    public bool IsResolved => !string.IsNullOrWhiteSpace(DeviceId);
}
