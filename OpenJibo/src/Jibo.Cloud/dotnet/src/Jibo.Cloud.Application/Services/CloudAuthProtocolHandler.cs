using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

public sealed class CloudAuthProtocolHandler(ICloudStateStore stateStore) : ICloudAuthProtocolHandler
{
    public ProtocolDispatchResult HandleAccount(string operation, ProtocolEnvelope envelope)
    {
        var account = stateStore.GetAccount();
        var body = envelope.TryParseBody();

        if (operation.Equals("CreateHubToken", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(new
            {
                token = stateStore.IssueHubToken(),
                expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });

        if (operation.Equals("CreateAccessToken", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(new
            {
                token = $"access-{account.AccountId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });

        if (operation.Equals("CheckEmail", StringComparison.OrdinalIgnoreCase))
        {
            var email = ReadString(body, "email") ?? string.Empty;
            var emailExists = stateStore.GetUserByEmail(email) is not null ||
                              email.Equals(account.Email, StringComparison.OrdinalIgnoreCase);
            return ProtocolDispatchResult.Ok(new { exists = emailExists });
        }

        if (operation.Equals("Create", StringComparison.OrdinalIgnoreCase))
        {
            var email = ReadString(body, "email") ?? string.Empty;
            var password = ReadString(body, "password") ?? string.Empty;
            var firstName = ReadString(body, "firstName") ?? string.Empty;
            var lastName = ReadString(body, "lastName") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return ProtocolDispatchResult.Raw(400, "{\"message\":\"Email and password are required\"}",
                    "application/json");

            var created = stateStore.CreateUser(email, password, firstName, lastName);
            if (created is null)
                return ProtocolDispatchResult.Raw(409,
                    "{\"message\":\"An account with this email already exists\"}",
                    "application/json");

            return ProtocolDispatchResult.Ok(BuildAccountResponse(created));
        }

        if (operation.Equals("Login", StringComparison.OrdinalIgnoreCase))
        {
            var email = ReadString(body, "email") ?? string.Empty;
            var password = ReadString(body, "password") ?? string.Empty;

            var authenticated = stateStore.AuthenticateUser(email, password);
            if (authenticated is null)
                return ProtocolDispatchResult.Raw(401,
                    "{\"message\":\"Invalid email or password\"}",
                    "application/json");

            return ProtocolDispatchResult.Ok(BuildAccountResponse(authenticated));
        }

        if (operation.Equals("Get", StringComparison.OrdinalIgnoreCase))
        {
            var ids = ReadStringArray(body, "ids");
            if (ids.Count == 0)
                return ProtocolDispatchResult.Ok(new[] { BuildAccountResponse(account) });

            var results = ids
                .Select(id =>
                {
                    var user = stateStore.GetUserById(id);
                    if (user is not null) return (object)BuildAccountResponse(user);
                    if (id.Equals(account.AccountId, StringComparison.OrdinalIgnoreCase))
                        return (object)BuildAccountResponse(account);
                    return null;
                })
                .Where(result => result is not null)
                .ToArray();

            return ProtocolDispatchResult.Ok(results);
        }

        switch (operation)
        {
            case "Update" or "ResetKeys" or "Remove" or "ActivateByCode" or "ResendActivationCode" or
                "ChangePassword" or "SendPasswordReset" or "PasswordResetByCode" or "UpdatePhoto" or "RemovePhoto" or
                "VerifyPhoneByCode" or "AcceptTerms" or "FacebookConnect" or "FacebookMobileConnect":
                return ProtocolDispatchResult.Ok(new
                {
                    id = account.AccountId,
                    email = account.Email,
                    firstName = account.FirstName,
                    lastName = account.LastName,
                    accessKeyId = account.AccessKeyId,
                    secretAccessKey = account.SecretAccessKey
                });
            case "ChangeEmail" or "SendPhoneVerificationCode":
                return ProtocolDispatchResult.Ok(new
                {
                    id = account.AccountId
                });
        }

        if (operation.Equals("GetAccountByAccessToken", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(new
            {
                id = account.AccountId,
                accessKeyId = account.AccessKeyId,
                secretAccessKey = account.SecretAccessKey,
                email = account.Email,
                friendlyId = stateStore.GetRobot().RobotId,
                payload = ReadObject(body, "payload")
            });

        if (operation.Equals("Search", StringComparison.OrdinalIgnoreCase))
        {
            var query = (ReadString(body, "query") ?? string.Empty).ToLowerInvariant();
            var haystack = $"{account.Email} {account.FirstName} {account.LastName} {account.AccountId}"
                .ToLowerInvariant();

            return ProtocolDispatchResult.Ok(query.Length > 0 && haystack.Contains(query)
                ?
                [
                    BuildAccountResponse(account)
                ]
                : Array.Empty<object>());
        }

        if (operation.Equals("FacebookPrepareLogin", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(new
            {
                url = "https://example.com/facebook-login",
                client_id = "fake-client-id",
                scope = "email",
                response_type = "token",
                state = $"fb-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                redirect_uri = "https://api.jibo.com/facebook/callback"
            });

        if (operation.Equals("ConfirmEmailReset", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(new { });

        return ProtocolDispatchResult.Ok(new
        {
            id = account.AccountId,
            email = account.Email,
            firstName = account.FirstName,
            lastName = account.LastName
        });
    }

    public ProtocolDispatchResult HandleNotification(string operation, ProtocolEnvelope envelope)
    {
        if (!operation.Equals("NewRobotToken", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(new { ok = true, operation });

        var body = envelope.TryParseBody();
        var deviceId = !string.IsNullOrWhiteSpace(envelope.DeviceId)
            ? envelope.DeviceId!
            : ReadString(body, "deviceId")
              ?? ReadString(body, "serial_number")
              ?? ReadString(body, "serialNumber")
              ?? ReadString(body, "cpuid")
              ?? ReadString(body, "cpuId")
              ?? ReadString(body, "robotId")
              ?? "unknown-device";

        stateStore.GetOrCreateDevice(deviceId, envelope.FirmwareVersion, envelope.ApplicationVersion);

        return ProtocolDispatchResult.Ok(new
        {
            token = stateStore.IssueRobotToken(deviceId)
        });
    }

    private static string? ReadString(JsonElement? body, string propertyName)
    {
        return body is { } element &&
               element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement? body, string propertyName)
    {
        if (body is not { } element ||
            element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
            return [];

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static JsonElement? ReadObject(JsonElement? body, string propertyName)
    {
        return body is { } element &&
               element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Object
            ? property
            : null;
    }

    private static object BuildAccountResponse(AccountProfile account)
    {
        return new
        {
            id = account.AccountId,
            email = account.Email,
            firstName = account.FirstName,
            lastName = account.LastName,
            gender = "unknown",
            birthday = 631152000000L,
            phoneNumber = "+10000000000",
            photoUrl = string.Empty,
            isActive = true,
            messagingAllowed = true,
            accessKeyId = account.AccessKeyId,
            secretAccessKey = account.SecretAccessKey,
            roles = Array.Empty<object>(),
            facebookConnected = false,
            termsAccepted = true
        };
    }

    private static object BuildAccountResponse(UserRecord user)
    {
        return new
        {
            id = user.Id,
            email = user.Email,
            firstName = user.FirstName,
            lastName = user.LastName,
            gender = user.Gender ?? "unknown",
            birthday = user.Birthday ?? 631152000000L,
            phoneNumber = "+10000000000",
            photoUrl = string.Empty,
            isActive = user.IsActive,
            messagingAllowed = true,
            accessKeyId = user.AccessKeyId,
            secretAccessKey = user.SecretAccessKey,
            roles = Array.Empty<object>(),
            facebookConnected = false,
            termsAccepted = true
        };
    }
}