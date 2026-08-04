using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public sealed class ProtocolRobotIdentityResolver(ICloudStateStore stateStore)
{
    public ProtocolRobotIdentity Resolve(ProtocolEnvelope envelope)
    {
        var headerIdentity = Normalize(envelope.DeviceId);
        var bearerToken = ReadBearerToken(envelope);
        var aws = ReadAwsSignature(envelope);
        var tokenSession = string.IsNullOrWhiteSpace(bearerToken) ? null : stateStore.FindSessionByToken(bearerToken);
        var tokenIdentity = Normalize(tokenSession?.DeviceId);
        var credentialIdentity = Normalize(aws.AccessKeyFingerprint is null
            ? null
            : stateStore.FindDeviceByAwsCredentialFingerprint(aws.AccessKeyFingerprint)?.DeviceId);

        if (!string.IsNullOrWhiteSpace(headerIdentity) && !string.IsNullOrWhiteSpace(tokenIdentity) &&
            !headerIdentity.Equals(tokenIdentity, StringComparison.OrdinalIgnoreCase))
            return new ProtocolRobotIdentity(null, "conflict", true, true, true, aws);

        if ((!string.IsNullOrWhiteSpace(headerIdentity) && !string.IsNullOrWhiteSpace(credentialIdentity) &&
             !headerIdentity.Equals(credentialIdentity, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(tokenIdentity) && !string.IsNullOrWhiteSpace(credentialIdentity) &&
             !tokenIdentity.Equals(credentialIdentity, StringComparison.OrdinalIgnoreCase)))
            return new ProtocolRobotIdentity(null, "conflict", !string.IsNullOrWhiteSpace(headerIdentity),
                !string.IsNullOrWhiteSpace(bearerToken), tokenSession is not null, aws);

        if (!string.IsNullOrWhiteSpace(headerIdentity))
            return new ProtocolRobotIdentity(headerIdentity, "robot-header", true,
                !string.IsNullOrWhiteSpace(bearerToken), tokenSession is not null, aws);

        if (!string.IsNullOrWhiteSpace(tokenIdentity))
            return new ProtocolRobotIdentity(tokenIdentity, "bearer-token", false, true, true, aws);

        if (!string.IsNullOrWhiteSpace(credentialIdentity))
            return new ProtocolRobotIdentity(credentialIdentity, "aws-credential-binding", false,
                !string.IsNullOrWhiteSpace(bearerToken), tokenSession is not null, aws);

        return new ProtocolRobotIdentity(null, "unresolved", false,
            !string.IsNullOrWhiteSpace(bearerToken), tokenSession is not null, aws);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ReadBearerToken(ProtocolEnvelope envelope)
    {
        if (!envelope.Headers.TryGetValue("Authorization", out var authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorization["Bearer ".Length..].Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static AwsSignatureDetails ReadAwsSignature(ProtocolEnvelope envelope)
    {
        envelope.Headers.TryGetValue("Authorization", out var authorization);
        var authScheme = string.IsNullOrWhiteSpace(authorization)
            ? "none"
            : authorization.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        var isSigV4 = authScheme.Equals("aws4-hmac-sha256", StringComparison.OrdinalIgnoreCase) ||
                      envelope.QueryParameters.TryGetValue("X-Amz-Algorithm", out var algorithm) &&
                      algorithm.Equals("AWS4-HMAC-SHA256", StringComparison.OrdinalIgnoreCase);
        var isSigV3 = authScheme.Equals("aws3", StringComparison.OrdinalIgnoreCase) ||
                      authScheme.Equals("aws3-https", StringComparison.OrdinalIgnoreCase);
        var credential = ExtractCredential(authorization) ??
                         GetQueryValue(envelope, "X-Amz-Credential");
        var accessKeyId = credential?.Split('/', 2)[0] ?? ExtractAws3AccessKeyId(authorization);
        var signedHeaders = ExtractSignedHeaders(authorization);
        return new AwsSignatureDetails(
            authScheme,
            isSigV4,
            isSigV3,
            Fingerprint(accessKeyId),
            HasHeaderOrQuery(envelope, "X-Amz-Security-Token"),
            HasHeaderOrQuery(envelope, "X-Amz-Date"),
            HasHeaderOrQuery(envelope, "X-Amz-Signature") || HasAuthorizationParameter(authorization, "Signature"),
            signedHeaders is not null,
            SignedHeaderPresent(signedHeaders, "x-jibo-robotid"),
            SignedHeaderPresent(signedHeaders, "x-jibo-transid"));
    }

    private static string? ExtractCredential(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization)) return null;
        var match = Regex.Match(authorization, @"(?:^|[,\s])Credential=([^,\s]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractAws3AccessKeyId(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization)) return null;
        var match = Regex.Match(authorization, @"(?:^|[,\s])AWSAccessKeyId=([^,\s]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractSignedHeaders(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization)) return null;
        var match = Regex.Match(authorization, @"(?:^|[,\s])SignedHeaders=([^,\s]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool HasAuthorizationParameter(string? authorization, string parameter) =>
        !string.IsNullOrWhiteSpace(authorization) &&
        Regex.IsMatch(authorization, $@"(?:^|[,\s]){Regex.Escape(parameter)}=", RegexOptions.IgnoreCase);

    private static bool SignedHeaderPresent(string? signedHeaders, string header) =>
        !string.IsNullOrWhiteSpace(signedHeaders) && signedHeaders
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => value.Equals(header, StringComparison.OrdinalIgnoreCase));

    private static string? GetQueryValue(ProtocolEnvelope envelope, string key) =>
        envelope.QueryParameters.TryGetValue(key, out var value) ? value : null;

    private static bool HasHeaderOrQuery(ProtocolEnvelope envelope, string key) =>
        envelope.Headers.ContainsKey(key) || envelope.QueryParameters.ContainsKey(key);

    private static string? Fingerprint(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];
}

public sealed record ProtocolRobotIdentity(string? DeviceId, string Source, bool HeaderPresent,
    bool BearerTokenPresent, bool BearerTokenResolved, AwsSignatureDetails Aws)
{
    public bool IsResolved => !string.IsNullOrWhiteSpace(DeviceId);
}

public sealed record AwsSignatureDetails(string AuthScheme, bool IsSigV4, bool IsSigV3, string? AccessKeyFingerprint,
    bool SecurityTokenPresent, bool DatePresent, bool SignaturePresent, bool SignedHeadersPresent,
    bool SignsRobotHeader, bool SignsTransactionHeader);
