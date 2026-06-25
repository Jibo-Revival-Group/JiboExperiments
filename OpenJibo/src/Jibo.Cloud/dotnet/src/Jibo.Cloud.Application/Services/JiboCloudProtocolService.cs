using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Microsoft.Extensions.Configuration;

namespace Jibo.Cloud.Application.Services;

public sealed class JiboCloudProtocolService(
    ICloudStateStore stateStore,
    IMediaContentStore? mediaContentStore = null,
    IConfiguration? configuration = null,
    ICloudAuthProtocolHandler? authHandler = null)
{
    private const int SchedulerBackupDelayMs = 250;
    private const int SchedulerDownloadTickMs = 100;
    private const int SchedulerDownloadFinishDelayMs = 150;

    private readonly HashSet<string> _acceptedHosts = BuildAcceptedHosts(configuration);

    private readonly ICloudAuthProtocolHandler _authHandler =
        authHandler ?? new CloudAuthProtocolHandler(stateStore);

    private readonly string? _configuredRobotId = ReadConfiguredRobotId(configuration);

    private readonly IMediaContentStore _mediaContentStore = mediaContentStore ?? new NullMediaContentStore();
    private readonly ConcurrentDictionary<string, OobeTokenState> _oobeTokens = new(StringComparer.Ordinal);
    private readonly Lock _schedulerLock = new();
    private readonly SchedulerRuntimeState _schedulerState = new();

    private static HashSet<string> BuildAcceptedHosts(IConfiguration? configuration)
    {
        var hosts = configuration?
            .GetSection("OpenJibo:AcceptedHosts")
            .GetChildren()
            .Select(child => child.Value)
            .OfType<string>()
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .ToArray() ?? [];

        return new HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ReadConfiguredRobotId(IConfiguration? configuration)
    {
        var robotId = configuration?["OpenJibo:Robot:RobotId"];
        return string.IsNullOrWhiteSpace(robotId) ? null : robotId.Trim();
    }

    public Task<ProtocolDispatchResult> DispatchAsync(ProtocolEnvelope envelope)
    {
        if (envelope.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
            envelope.Path == "/" &&
            string.IsNullOrWhiteSpace(envelope.ServicePrefix))
            return Task.FromResult(ProtocolDispatchResult.NoContent());

        if (envelope.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
            envelope.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(ProtocolDispatchResult.Ok(new { ok = true, host = envelope.HostName }));

        if (envelope.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
            envelope.Path.StartsWith("/media/", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HandleMediaContent(envelope));

        if (TryHandleLocalSchedulerRequest(envelope, out var schedulerResult))
            return Task.FromResult(schedulerResult);

        if (envelope.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase) &&
            (envelope.Path.Equals("/upload/asr-binary", StringComparison.OrdinalIgnoreCase) ||
             envelope.Path.Equals("/upload/log-events", StringComparison.OrdinalIgnoreCase) ||
             envelope.Path.Equals("/upload/log-binary", StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(ProtocolDispatchResult.Raw(200, string.Empty));

        if ((envelope.ServicePrefix ?? string.Empty).StartsWith("OOBE_", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HandleOobe(envelope.Operation ?? string.Empty, envelope));

        if (_acceptedHosts.Count > 0 && !_acceptedHosts.Contains(envelope.HostName))
            return Task.FromResult(ProtocolDispatchResult.Ok(new
            {
                ok = true,
                accepted = false,
                host = envelope.HostName
            }));

        if (TryHandleLegacyRestRequest(envelope, out var legacyResult))
            return Task.FromResult(legacyResult);

        var servicePrefix = envelope.ServicePrefix ?? string.Empty;
        var operation = envelope.Operation ?? string.Empty;

        if (servicePrefix.StartsWith("Log_", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HandleLog(operation));

        if (servicePrefix.StartsWith("Backup_", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HandleBackup(operation, envelope));

        if (servicePrefix.StartsWith("Account_", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HandleAccount(operation, envelope));

        if (servicePrefix.StartsWith("Notification_", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HandleNotification(operation, envelope));

        if (servicePrefix.StartsWith("Loop_", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HandleLoop(operation, envelope));

        if (servicePrefix.Equals("Media_20160725", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HandleMedia(operation, envelope));

        if (servicePrefix.StartsWith("Key_", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HandleKey(operation, envelope));

        if (servicePrefix.StartsWith("Person_", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HandlePerson(operation, envelope));

        if (servicePrefix.StartsWith("Robot_", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HandleRobot(operation, envelope));

        if (servicePrefix.StartsWith("Update_", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HandleUpdate(operation, envelope));

        return Task.FromResult(ProtocolDispatchResult.Ok(new
        {
            ok = true,
            host = envelope.HostName,
            target = $"{servicePrefix}.{operation}".Trim('.'),
            operation,
            note = "unknown target default response"
        }));
    }

    private static bool TryHandleLegacyRestRequest(ProtocolEnvelope envelope,
        out ProtocolDispatchResult result)
    {
        if (string.IsNullOrWhiteSpace(envelope.ServicePrefix) &&
            envelope.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            envelope.Path.Equals("/v1/loop/suspend", StringComparison.OrdinalIgnoreCase))
        {
            result = ProtocolDispatchResult.Ok(new { ok = true });
            return true;
        }

        result = ProtocolDispatchResult.Ok(new { });
        return false;
    }

    private ProtocolDispatchResult HandleAccount(string operation, ProtocolEnvelope envelope)
    {
        return _authHandler.HandleAccount(operation, envelope);
    }

    private ProtocolDispatchResult HandleNotification(string operation, ProtocolEnvelope envelope)
    {
        return _authHandler.HandleNotification(operation, envelope);
    }

    private ProtocolDispatchResult HandleOobe(string operation, ProtocolEnvelope envelope)
    {
        var body = envelope.TryParseBody();
        var token = ReadString(body, "token");

        if (operation.Equals("PrepareRobot", StringComparison.OrdinalIgnoreCase))
        {
            var expiresUtc = DateTimeOffset.UtcNow.AddHours(1);
            var issuedToken = CreateOobeToken();
            _oobeTokens[issuedToken] = new OobeTokenState
            {
                DeviceId = ReadString(body, "deviceId") ?? envelope.DeviceId,
                LoopId = ReadString(body, "loopId"),
                ExpiresUtc = expiresUtc
            };

            return ProtocolDispatchResult.Ok(new
            {
                token = issuedToken,
                expires = expiresUtc.ToUnixTimeMilliseconds()
            });
        }

        if (operation.Equals("GetStatus", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(new
            {
                complete = token is not null &&
                           _oobeTokens.TryGetValue(token, out var current) &&
                           current.Complete
            });

        if (!operation.Equals("SetupRobot", StringComparison.OrdinalIgnoreCase) &&
            !operation.Equals("ReconnectRobot", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(new { ok = true, operation });

        var robotId = ReadString(body, "id") ??
                      ReadString(body, "robotId") ??
                      (string.IsNullOrWhiteSpace(envelope.DeviceId) ? "unknown-robot" : envelope.DeviceId!);

        var state = _oobeTokens.GetOrAdd(token ?? "oobe-implicit", _ => new OobeTokenState
        {
            DeviceId = robotId,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        state.Complete = true;
        state.DeviceId = robotId;

        stateStore.GetOrCreateDevice(robotId, envelope.FirmwareVersion, envelope.ApplicationVersion);

        if (operation.Equals("ReconnectRobot", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(new { result = "ok" });

        var account = stateStore.GetAccount();
        return ProtocolDispatchResult.Ok(new
        {
            accessKeyId = account.AccessKeyId,
            secretAccessKey = account.SecretAccessKey,
            serviceMode = false
        });

    }

    private ProtocolDispatchResult HandleLoop(string operation, ProtocolEnvelope envelope)
    {
        if (operation is "ListMembers" or "ListLoopMembers")
        {
            var listBody = envelope.TryParseBody();
            var loopId = ReadString(listBody, "loopId") ??
                         ReadString(listBody, "id") ??
                         stateStore.GetLoops().FirstOrDefault()?.LoopId;

            var members = stateStore.GetLoopMembers(loopId ?? string.Empty)
                .Where(static member => !string.Equals(member.Type, "robot", StringComparison.OrdinalIgnoreCase))
                .Select(MapLoopMember)
                .ToArray();

            return ProtocolDispatchResult.Ok(members);
        }

        var body = envelope.TryParseBody();
        var loopIdForMutation = ReadString(body, "loopId") ??
                                ReadString(body, "id") ??
                                stateStore.GetLoops().FirstOrDefault()?.LoopId ??
                                "openjibo-default-loop";

        switch (operation)
        {
            case "InviteMember" or "InviteLoopMember":
            {
                stateStore.AddLoopMember(
                    loopIdForMutation,
                    null,
                    ReadString(body, "email"),
                    ReadString(body, "firstName"),
                    ReadString(body, "lastName"),
                    ReadString(body, "gender"),
                    ReadLong(body, "birthday"),
                    ReadBool(body, "isChild"),
                    "member",
                    ReadString(body, "legalGuardianId"));

                var loop = stateStore.GetLoops().FirstOrDefault(l =>
                    l.LoopId.Equals(loopIdForMutation, StringComparison.OrdinalIgnoreCase));
                return ProtocolDispatchResult.Ok(loop is null
                    ? new { result = "ok" }
                    : MapLoopRecord(loop, stateStore.GetLoopMembers(loopIdForMutation)));
            }
            case "UpdateMember" or "UpdateLoopMember":
            {
                var memberId = ReadString(body, "id") ?? string.Empty;
                try
                {
                    stateStore.UpdateLoopMember(loopIdForMutation, memberId,
                        ReadString(body, "firstName"), ReadString(body, "lastName"),
                        ReadString(body, "gender"), ReadLong(body, "birthday"),
                        ReadBool(body, "isChild"), ReadString(body, "nickname"), ReadString(body, "phoneticName"));
                }
                catch (InvalidOperationException)
                {
                    // Member not found - keep protocol flow moving.
                }

                var loop = stateStore.GetLoops().FirstOrDefault(l =>
                    l.LoopId.Equals(loopIdForMutation, StringComparison.OrdinalIgnoreCase));
                return ProtocolDispatchResult.Ok(loop is null
                    ? new { result = "ok" }
                    : MapLoopRecord(loop, stateStore.GetLoopMembers(loopIdForMutation)));
            }
            case "RemoveMember" or "RemoveLoopMember":
            {
                stateStore.RemoveLoopMember(loopIdForMutation, ReadString(body, "id") ?? string.Empty);
                var loop = stateStore.GetLoops().FirstOrDefault(l =>
                    l.LoopId.Equals(loopIdForMutation, StringComparison.OrdinalIgnoreCase));
                return ProtocolDispatchResult.Ok(loop is null
                    ? new { result = "ok" }
                    : MapLoopRecord(loop, stateStore.GetLoopMembers(loopIdForMutation)));
            }
            case "AcceptInvitation" or "AcceptLoopInvitation" or
                "DeclineInvitation" or "DeclineLoopInvitation":
            {
                var loop = stateStore.GetLoops().FirstOrDefault(l =>
                    l.LoopId.Equals(loopIdForMutation, StringComparison.OrdinalIgnoreCase));
                return ProtocolDispatchResult.Ok(loop is null
                    ? new { result = "ok" }
                    : MapLoopRecord(loop, stateStore.GetLoopMembers(loopIdForMutation)));
            }
            case "SetEnrollment":
            {
                var memberId = ReadString(body, "id") ?? string.Empty;
                bool? face = body?.TryGetProperty("face", out var faceEl) == true ? faceEl.GetBoolean() : null;
                bool? voice = body?.TryGetProperty("voice", out var voiceEl) == true ? voiceEl.GetBoolean() : null;
                try
                {
                    stateStore.SetMemberEnrollment(loopIdForMutation, memberId, face, voice);
                }
                catch (InvalidOperationException)
                {
                    // Member not found - keep protocol flow moving.
                }

                var loop = stateStore.GetLoops().FirstOrDefault(l =>
                    l.LoopId.Equals(loopIdForMutation, StringComparison.OrdinalIgnoreCase));
                return ProtocolDispatchResult.Ok(loop is null
                    ? new { result = "ok" }
                    : MapLoopRecord(loop, stateStore.GetLoopMembers(loopIdForMutation)));
            }
            case "UpdateNickname" or "UpdatePhoneticName":
            {
                var memberId = ReadString(body, "id") ?? string.Empty;
                var nickname = operation is "UpdateNickname" ? ReadString(body, "nickname") : null;
                var phoneticName = operation is "UpdatePhoneticName" ? ReadString(body, "phoneticName") : null;
                try
                {
                    stateStore.UpdateLoopMember(loopIdForMutation, memberId,
                        null, null, null, null, false, nickname, phoneticName);
                }
                catch (InvalidOperationException)
                {
                    // Member not found - keep protocol flow moving.
                }

                return ProtocolDispatchResult.Ok(new { result = "ok" });
            }
            case "SuspendLoop" or "Remove" or "RemoveLoop" or
                "SetLegalGuardian" or "UpdateAgreementStatus" or "Update" or "UpdateLoop":
                return ProtocolDispatchResult.Ok(new { result = "ok" });
        }

        if (operation is not ("List" or "ListLoops")) return ProtocolDispatchResult.Ok(Array.Empty<object>());

        return ProtocolDispatchResult.Ok(stateStore.GetLoops()
            .Select(loop => MapLoopRecord(loop, stateStore.GetLoopMembers(loop.LoopId)))
            .ToArray());
    }

    private static object MapLoopMember(LoopMemberRecord member)
    {
        return new
        {
            id = member.Id,
            loopId = member.LoopId,
            accountId = member.AccountId,
            account = new
            {
                email = member.Email,
                firstName = member.FirstName,
                lastName = member.LastName,
                gender = member.Gender,
                birthday = member.Birthday,
                isChild = member.IsChild,
                phoneNumber = member.PhoneNumber
            },
            enrolled = new { face = member.FaceEnrolled, voice = member.VoiceEnrolled },
            status = member.Status,
            type = member.Type,
            nickname = member.Nickname,
            phoneticName = member.PhoneticName,
            legalGuardianId = member.LegalGuardianId,
            agreementId = member.AgreementId,
            created = member.CreatedUtc.ToUnixTimeMilliseconds()
        };
    }

    private static object MapLoopRecord(LoopRecord loop, IEnumerable<LoopMemberRecord> members)
    {
        return new
        {
            id = loop.LoopId,
            name = loop.Name,
            owner = loop.OwnerAccountId,
            robot = loop.RobotId,
            robotFriendlyId = loop.RobotFriendlyId,
            members = members
                .Where(static m => !string.Equals(m.Type, "robot", StringComparison.OrdinalIgnoreCase))
                .Select(MapLoopMember)
                .ToArray(),
            isSuspended = loop.IsSuspended,
            created = loop.CreatedUtc.ToUnixTimeMilliseconds(),
            updated = loop.UpdatedUtc.ToUnixTimeMilliseconds()
        };
    }

    private static ProtocolDispatchResult HandleLog(string operation)
    {
        return operation switch
        {
            "PutEventsAsync" => ProtocolDispatchResult.Ok(new
            {
                contentEncoding = "gzip",
                uploadUrl = "https://api.jibo.com/upload/log-events"
            }),
            "PutEvents" => ProtocolDispatchResult.Ok(new { }),
            "PutBinaryAsync" => ProtocolDispatchResult.Ok(new
            {
                url = "https://api.jibo.com/log/binary/fake-id",
                uploadUrl = "https://api.jibo.com/upload/log-binary"
            }),
            "PutAsrBinary" => ProtocolDispatchResult.Ok(new
            {
                bucketName = "openjibo-test",
                key = "asr/fake-key",
                uploadUrl = "https://api.jibo.com/upload/asr-binary"
            }),
            "NewKinesisCredentials" => ProtocolDispatchResult.Ok(new
            {
                credentials = new
                {
                    AccessKeyId = "fake-access-key",
                    Expiration = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
                    SecretAccessKey = "fake-secret",
                    SessionToken = "fake-session"
                },
                region = "us-east-1",
                streamName = "openjibo-log-stream"
            }),
            _ => ProtocolDispatchResult.Ok(new { })
        };
    }

    private ProtocolDispatchResult HandleMedia(string operation, ProtocolEnvelope envelope)
    {
        var body = envelope.TryParseBody();

        if (operation.Equals("List", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(stateStore.ListMedia(
                ReadStringArray(body, "loopIds"),
                ReadLong(body, "after"),
                ReadLong(body, "before")).Select(MapMedia).ToArray());

        if (operation.Equals("Get", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(stateStore.GetMedia(ReadStringArray(body, "paths")).Select(MapMedia)
                .ToArray());

        if (operation.Equals("Remove", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(stateStore.RemoveMedia(ReadStringArray(body, "paths")).Select(MapMedia)
                .ToArray());

        if (!operation.Equals("Create", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(Array.Empty<object>());

        var loopId = ReadHeader(envelope, "x-loop-id") ?? ReadString(body, "loopId") ?? stateStore.GetLoops()[0].LoopId;
        var path = ReadHeader(envelope, "x-path") ??
                   ReadString(body, "path") ?? $"/media/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var type = ReadHeader(envelope, "x-type") ?? ReadString(body, "type") ?? "unknown";
        var reference = ReadHeader(envelope, "x-reference") ?? ReadString(body, "reference") ?? string.Empty;
        var isEncrypted = ReadBooleanHeader(envelope, "x-encrypted") || ReadBool(body, "isEncrypted");
        var meta = ReadObject(body, "meta") ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var contentType = ReadHeader(envelope, "Content-Type") ?? "application/octet-stream";
        meta["contentType"] = contentType;
        var bodyBytes = string.IsNullOrWhiteSpace(envelope.BodyText)
            ? []
            : Encoding.UTF8.GetBytes(envelope.BodyText);
        meta["contentLength"] = bodyBytes.Length;
        meta["contentSha256"] = Convert.ToHexString(SHA256.HashData(bodyBytes)).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(envelope.BodyText)) meta["bodyText"] = envelope.BodyText;

        _mediaContentStore.StoreAsync(path, contentType,
            bodyBytes,
            meta as IReadOnlyDictionary<string, object?>, CancellationToken.None).GetAwaiter().GetResult();

        return ProtocolDispatchResult.Ok(
            MapMedia(stateStore.CreateMedia(loopId, path, type, reference, isEncrypted, meta)));
    }

    private ProtocolDispatchResult HandlePerson(string operation, ProtocolEnvelope envelope)
    {
        var body = envelope.TryParseBody();

        if (operation.Equals("ListHolidays", StringComparison.OrdinalIgnoreCase))
        {
            var loopId = ReadString(body, "loopId");
            return ProtocolDispatchResult.Ok(stateStore.GetHolidays(loopId).Select(MapHoliday));
        }

        if (operation.Equals("ListCommute", StringComparison.OrdinalIgnoreCase))
        {
            var loopId = ReadString(body, "loopId");
            return ProtocolDispatchResult.Ok(stateStore.GetCommuteProfiles(loopId).Select(MapCommute));
        }

        if (!operation.Equals("UpsertCommute", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(Array.Empty<object>());

        var hasIsEnabled = body is { } enabledBody && enabledBody.TryGetProperty("isEnabled", out _);
        var hasIsComplete = body is { } completeBody && completeBody.TryGetProperty("isComplete", out _);
        var workHour = ReadLong(body, "workHour");
        var workMinute = ReadLong(body, "workMinute");
        var typicalDurationMinutes = ReadLong(body, "typicalDurationMinutes");
        var commute = new CommuteProfileRecord
        {
            Id = ReadString(body, "id") ?? string.Empty,
            LoopId = ReadString(body, "loopId") ?? string.Empty,
            MemberId = ReadString(body, "memberId"),
            IsEnabled = !hasIsEnabled || ReadBool(body, "isEnabled"),
            IsComplete = !hasIsComplete || ReadBool(body, "isComplete"),
            Mode = ReadString(body, "mode") ?? "driving",
            WorkHour = workHour is > 0 and < 24 ? (int)workHour.Value : 8,
            WorkMinute = workMinute is >= 0 and < 60 ? (int)workMinute.Value : 30,
            OriginName = ReadString(body, "originName"),
            DestinationName = ReadString(body, "destinationName"),
            TypicalDurationMinutes = typicalDurationMinutes is > 0
                ? (int)typicalDurationMinutes.Value
                : 25
        };
        return ProtocolDispatchResult.Ok(MapCommute(stateStore.UpsertCommuteProfile(commute)));
    }

    private ProtocolDispatchResult HandleBackup(string operation, ProtocolEnvelope envelope)
    {
        if (operation.Equals("List", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(stateStore.GetBackups().Select(MapBackup).ToArray());

        if (operation.Equals("New", StringComparison.OrdinalIgnoreCase))
        {
            var body = envelope.TryParseBody();
            var loopId = ReadString(body, "loopId") ?? stateStore.GetLoops()[0].LoopId;
            var backupName = ReadString(body, "name") ?? ReadString(body, "backupName")
                ?? $"backup-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            var backup = stateStore.CreateBackup(loopId, backupName);
            return ProtocolDispatchResult.Ok(new
            {
                uploadUrl = $"https://api.jibo.com/upload/backup/{backup.BackupId}"
            });
        }

        if (operation.Equals("Restore", StringComparison.OrdinalIgnoreCase))
        {
            var body = envelope.TryParseBody();
            var backupId = ReadString(body, "backupId") ?? ReadString(body, "id") ??
                ReadString(body, "etag") ?? ReadString(body, "location");
            var restored = stateStore.RestoreBackup(backupId);
            return restored is null
                ? ProtocolDispatchResult.Raw(404, "{\"error\":\"backup not found\"}", "application/json")
                : ProtocolDispatchResult.Ok(new
                {
                    result = "ok",
                    rebootRequired = true,
                    backupId = restored.BackupId
                });
        }

        if (!operation.Equals("Create", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(Array.Empty<object>());

        var createBody = envelope.TryParseBody();
        var requestedName = ReadString(createBody, "name") ?? ReadString(createBody, "backupName");
        var createLoopId = ReadString(createBody, "loopId") ?? stateStore.GetLoops()[0].LoopId;
        return ProtocolDispatchResult.Ok(
            MapBackup(stateStore.CreateBackup(createLoopId,
                requestedName ?? $"backup-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}")));
    }

    private ProtocolDispatchResult HandleKey(string operation, ProtocolEnvelope envelope)
    {
        var body = envelope.TryParseBody();
        var loopId = ReadString(body, "loopId") ?? ReadString(body, "id") ?? stateStore.GetLoops()[0].LoopId;

        if (operation.Equals("ShouldCreate", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(new
            {
                shouldCreate = stateStore.ShouldCreateSymmetricKey(loopId)
            });

        string? symmetricKey;
        if (operation.Equals("CreateSymmetricKey", StringComparison.OrdinalIgnoreCase))
        {
            symmetricKey = stateStore.GetOrCreateSymmetricKey(loopId);
            return ProtocolDispatchResult.Ok(new
            {
                loopId,
                key = symmetricKey,
                symmetricKey,
                created = true
            });
        }

        if (operation is "CreateRequest" or "RequestSymmetricKey")
        {
            var record = stateStore.CreateKeyRequest(loopId, ReadString(body, "publicKey") ?? string.Empty);
            return ProtocolDispatchResult.Ok(new
            {
                id = record.RequestId,
                loopId = record.LoopId
            });
        }

        if (operation.Equals("GetRequest", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(stateStore.GetKeyRequest(loopId, ReadString(body, "id"),
                ReadString(body, "publicKey")));

        if (operation.Equals("ListIncomingRequests", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(stateStore.GetIncomingKeyRequests());

        if (operation.Equals("ListBinaryRequests", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(stateStore.GetBinaryRequests());

        if (operation is "Share" or "ShareSymmetricKey" or "ShareBinary")
            return ProtocolDispatchResult.Ok(new { ok = true });

        if (!operation.Equals("LoadSymmetricKey", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(new { ok = true, operation });

        symmetricKey = stateStore.GetOrCreateSymmetricKey(loopId);
        return ProtocolDispatchResult.Ok(new
        {
            loopId,
            key = symmetricKey,
            symmetricKey
        });
    }

    private ProtocolDispatchResult HandleRobot(string operation, ProtocolEnvelope envelope)
    {
        var robot = stateStore.GetRobot();
        var effectiveRobotId = _configuredRobotId ?? robot.RobotId;

        if (operation.Equals("UpdateRobot", StringComparison.OrdinalIgnoreCase))
        {
            var updated = new DeviceRegistration
            {
                DeviceId = robot.DeviceId,
                RobotId = effectiveRobotId,
                FriendlyName = robot.FriendlyName,
                FirmwareVersion = envelope.FirmwareVersion ?? robot.FirmwareVersion,
                ApplicationVersion = envelope.ApplicationVersion ?? robot.ApplicationVersion,
                CertificateThumbprint = robot.CertificateThumbprint,
                IssuedIdentityId = robot.IssuedIdentityId,
                BuildHash = robot.BuildHash,
                ConfigHash = robot.ConfigHash,
                HostMappings = robot.HostMappings
            };

            stateStore.UpdateRobot(updated);
            return ProtocolDispatchResult.Ok(new
            {
                result = "ok"
            });
        }

        if (!operation.Equals("GetRobot", StringComparison.OrdinalIgnoreCase))
            return ProtocolDispatchResult.Ok(new
            {
                result = "ok"
            });

        var profile = stateStore.GetRobotProfile();
        var requestedRobotId = _configuredRobotId ?? ReadString(envelope.TryParseBody(), "id");
        if (!string.IsNullOrWhiteSpace(requestedRobotId) &&
            !requestedRobotId.Equals(robot.RobotId, StringComparison.OrdinalIgnoreCase))
            stateStore.UpdateRobot(new DeviceRegistration
            {
                DeviceId = robot.DeviceId,
                RobotId = requestedRobotId,
                FriendlyName = robot.FriendlyName,
                FirmwareVersion = envelope.FirmwareVersion ?? robot.FirmwareVersion,
                ApplicationVersion = envelope.ApplicationVersion ?? robot.ApplicationVersion,
                CertificateThumbprint = robot.CertificateThumbprint,
                IssuedIdentityId = robot.IssuedIdentityId,
                BuildHash = robot.BuildHash,
                ConfigHash = robot.ConfigHash,
                HostMappings = robot.HostMappings
            });

        return ProtocolDispatchResult.Ok(new
        {
            id = requestedRobotId ?? profile.RobotId,
            payload = profile.Payload,
            calibrationPayload = profile.CalibrationPayload,
            updated = profile.UpdatedUtc.ToUnixTimeMilliseconds(),
            created = profile.CreatedUtc.ToUnixTimeMilliseconds()
        });
    }

    private ProtocolDispatchResult HandleUpdate(string operation, ProtocolEnvelope envelope)
    {
        var body = envelope.TryParseBody();
        var subsystem = ReadString(body, "subsystem");
        var filter = ReadString(body, "filter");
        var fromVersion = ReadString(body, "fromVersion");

        return operation switch
        {
            "ListUpdates" => ProtocolDispatchResult.Ok(stateStore.ListUpdates(subsystem, filter).Select(MapUpdate)
                .ToArray()),
            "ListUpdatesFrom" => ProtocolDispatchResult.Ok(stateStore.ListUpdates(subsystem, filter)
                .Where(update =>
                    fromVersion is null ||
                    update.FromVersion.Equals(fromVersion, StringComparison.OrdinalIgnoreCase))
                .Select(MapUpdate)
                .ToArray()),
            "GetUpdateFrom" => HandleGetUpdateFrom(subsystem, fromVersion, filter),
            "CreateUpdate" => ProtocolDispatchResult.Ok(MapUpdate(stateStore.CreateUpdate(
                fromVersion,
                ReadString(body, "toVersion"),
                ReadString(body, "changes"),
                ReadString(body, "shaHash"),
                ReadLong(body, "length"),
                subsystem,
                filter,
                ReadObject(body, "dependencies")))),
            "RemoveUpdate" => ProtocolDispatchResult.Ok(MapUpdate(stateStore.RemoveUpdate(ReadString(body, "id")))),
            _ => ProtocolDispatchResult.Ok(Array.Empty<object>())
        };
    }

    private ProtocolDispatchResult HandleMediaContent(ProtocolEnvelope envelope)
    {
        var path = Uri.UnescapeDataString(envelope.Path["/media/".Length..]);
        var candidatePaths = new[] { path, $"/{path}" };
        var media = stateStore.GetMedia(candidatePaths).FirstOrDefault();
        if (media is null || media.IsDeleted) return ProtocolDispatchResult.Raw(404, string.Empty);

        var storedContent = _mediaContentStore.LoadAsync(media.Path, CancellationToken.None).GetAwaiter().GetResult();
        var contentType = storedContent?.ContentType ?? TryReadMetaString(media.Meta, "contentType") ??
            "application/octet-stream";
        var bodyText = storedContent is not null
            ? Encoding.UTF8.GetString(storedContent.Content)
            : TryReadMetaString(media.Meta, "bodyText") ?? string.Empty;
        return ProtocolDispatchResult.Raw(200, bodyText, contentType);
    }

    private ProtocolDispatchResult HandleGetUpdateFrom(string? subsystem, string? fromVersion, string? filter)
    {
        var update = stateStore.GetUpdateFrom(subsystem, fromVersion, filter);
        return update is null
            ? ProtocolDispatchResult.NoContent()
            : ProtocolDispatchResult.Ok(MapUpdate(update));
    }

    private static string? ReadSchedulerFilterFromPath(string path)
    {
        const string prefix = "/update/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || path.Length <= prefix.Length)
            return null;

        return Uri.UnescapeDataString(path[prefix.Length..]);
    }

    private bool TryHandleLocalSchedulerRequest(ProtocolEnvelope envelope, out ProtocolDispatchResult result)
    {
        result = ProtocolDispatchResult.Ok(new { });

        if (string.IsNullOrWhiteSpace(envelope.Path) || !string.IsNullOrWhiteSpace(envelope.ServicePrefix))
            return false;

        if (envelope.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
            envelope.Path.StartsWith("/update", StringComparison.OrdinalIgnoreCase))
        {
            var filter = ReadSchedulerFilterFromPath(envelope.Path);
            result = ProtocolDispatchResult.Ok(new
            {
                updates = ListSchedulerUpdates(filter)
            });
            return true;
        }

        if (!envelope.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return false;

        if (envelope.Path.Equals("/backup-status", StringComparison.OrdinalIgnoreCase))
        {
            result = ProtocolDispatchResult.Ok(new
            {
                status = "OK",
                data = GetSchedulerBackupStatus()
            });
            return true;
        }

        if (envelope.Path.Equals("/download-status", StringComparison.OrdinalIgnoreCase))
        {
            result = ProtocolDispatchResult.Ok(new
            {
                status = "OK",
                data = GetSchedulerDownloadStatus()
            });
            return true;
        }

        if (envelope.Path.Equals("/apply-update", StringComparison.OrdinalIgnoreCase))
        {
            result = ApplySchedulerUpdate(envelope);
            return true;
        }

        if (envelope.Path.Equals("/check-updates", StringComparison.OrdinalIgnoreCase))
        {
            var body = envelope.TryParseBody();
            var filter = ReadString(body, "filter");
            result = ProtocolDispatchResult.Ok(new
            {
                status = "OK",
                data = ListSchedulerUpdates(filter)
            });
            return true;
        }

        if (!envelope.Path.Equals("/backup-robot", StringComparison.OrdinalIgnoreCase) &&
            !envelope.Path.Equals("/ota-update", StringComparison.OrdinalIgnoreCase)) return false;

        if (envelope.Path.Equals("/backup-robot", StringComparison.OrdinalIgnoreCase))
        {
            StartSchedulerBackupCycle();
        }
        else
        {
            StartSchedulerUpdateCycle(ReadSchedulerFilter(envelope));
        }

        result = ProtocolDispatchResult.Ok(new
        {
            status = "OK"
        });
        return true;

    }

    private ProtocolDispatchResult ApplySchedulerUpdate(ProtocolEnvelope envelope)
    {
        var body = envelope.TryParseBody();
        var updateId = ReadString(body, "id") ?? ReadString(body, "updateId");
        var subsystem = ReadString(body, "subsystem") ?? "robot";
        var update = stateStore.ListUpdates()
            .FirstOrDefault(candidate => updateId is not null
                ? candidate.UpdateId.Equals(updateId, StringComparison.OrdinalIgnoreCase)
                : candidate.Subsystem.Equals(subsystem, StringComparison.OrdinalIgnoreCase));

        if (update is null)
            return ProtocolDispatchResult.Raw(404, JsonSerializer.Serialize(new
            {
                status = "NOT_FOUND",
                updateId
            }), "application/json");

        var robot = stateStore.GetRobot();
        stateStore.UpdateRobot(new DeviceRegistration
        {
            DeviceId = robot.DeviceId,
            RobotId = robot.RobotId,
            FriendlyName = robot.FriendlyName,
            FirmwareVersion = update.ToVersion,
            ApplicationVersion = robot.ApplicationVersion,
            CertificateThumbprint = robot.CertificateThumbprint,
            IssuedIdentityId = robot.IssuedIdentityId,
            BuildHash = robot.BuildHash,
            ConfigHash = robot.ConfigHash,
            HostMappings = robot.HostMappings
        });

        lock (_schedulerLock)
        {
            _schedulerState.DownloadedUpdateIds.Remove(update.UpdateId);
            if (_schedulerState.DownloadStatus?.Updates.Any(item =>
                    item.Id.Equals(update.UpdateId, StringComparison.OrdinalIgnoreCase)) == true)
                _schedulerState.DownloadStatus = null;
        }

        return ProtocolDispatchResult.Ok(new
        {
            status = "OK",
            updateId = update.UpdateId,
            fromVersion = update.FromVersion,
            toVersion = update.ToVersion,
            firmwareVersion = update.ToVersion,
            rebootRequired = true
        });
    }

    private static string? ReadSchedulerFilter(ProtocolEnvelope envelope)
    {
        var pathFilter = ReadSchedulerFilterFromPath(envelope.Path);
        if (!string.IsNullOrWhiteSpace(pathFilter)) return pathFilter;

        var body = envelope.TryParseBody();
        return ReadString(body, "filter") ?? ReadString(body, "subsystem");
    }

    private object[] ListSchedulerUpdates(string? filter)
    {
        var robotVersion = stateStore.GetRobot().FirmwareVersion ?? "12.10.0";

        return stateStore.ListUpdates()
            .Where(update => IsUpdateNewerThanRequest(update.ToVersion, robotVersion))
            .Where(update =>
                string.IsNullOrWhiteSpace(filter) ||
                update.Subsystem.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                (update.Filter is not null &&
                 update.Filter.Equals(filter, StringComparison.OrdinalIgnoreCase)))
            .Select(update => MapSchedulerUpdate(update, IsSchedulerUpdateDownloaded(update.UpdateId)))
            .ToArray();
    }

    private bool GetSchedulerBackupStatus()
    {
        lock (_schedulerLock)
        {
            return _schedulerState.BackingUp || _schedulerState.BackingBeforeUpdate;
        }
    }

    private object? GetSchedulerDownloadStatus()
    {
        lock (_schedulerLock)
        {
            if (_schedulerState.DownloadStatus is null) return null;

            return new
            {
                updates = _schedulerState.DownloadStatus.Updates
                    .Select(update =>
                        MapSchedulerUpdate(update, _schedulerState.DownloadedUpdateIds.Contains(update.Id)))
                    .ToArray(),
                status = _schedulerState.DownloadStatus.Status is null
                    ? null
                    : new
                    {
                        id = _schedulerState.DownloadStatus.Status.Id,
                        length = _schedulerState.DownloadStatus.Status.Length,
                        received = _schedulerState.DownloadStatus.Status.Received,
                        status = _schedulerState.DownloadStatus.Status.Status,
                        reason = _schedulerState.DownloadStatus.Status.Reason,
                        error = _schedulerState.DownloadStatus.Status.Error
                    }
            };
        }
    }

    private void StartSchedulerUpdateCycle(string? filter)
    {
        lock (_schedulerLock)
        {
            if (_schedulerState.BackingUp || _schedulerState.BackingBeforeUpdate ||
                _schedulerState.DownloadStatus is not null)
                return;

            _schedulerState.BackingUp = true;
            _schedulerState.BackingBeforeUpdate = true;
            _schedulerState.ActiveFilter = filter;
            _schedulerState.DownloadedUpdateIds.Clear();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SchedulerBackupDelayMs).ConfigureAwait(false);

                var robotVersion = stateStore.GetRobot().FirmwareVersion ?? "12.10.0";
                var pendingUpdates = stateStore.ListUpdates()
                    .Where(update => IsUpdateNewerThanRequest(update.ToVersion, robotVersion))
                    .Where(update =>
                        string.IsNullOrWhiteSpace(filter) ||
                        update.Subsystem.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                        (update.Filter is not null &&
                         update.Filter.Equals(filter, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                lock (_schedulerLock)
                {
                    _schedulerState.BackingUp = false;
                    _schedulerState.BackingBeforeUpdate = false;

                    _schedulerState.DownloadStatus = pendingUpdates.Length == 0
                        ? null
                        : new SchedulerDownloadState
                        {
                            Updates = pendingUpdates.Select(update => new SchedulerUpdateState
                            {
                                Id = update.UpdateId,
                                Subsystem = update.Subsystem,
                                Changes = update.Changes,
                                Length = update.Length,
                                ToVersion = update.ToVersion,
                                Dependencies = new Dictionary<string, object?>(),
                                Downloaded = false
                            }).ToArray(),
                            Status = new SchedulerDownloadProgress
                            {
                                Id = pendingUpdates[0].UpdateId,
                                Length = pendingUpdates[0].Length,
                                Received = 0,
                                Status = "downloading"
                            }
                        };
                }

                if (pendingUpdates.Length == 0) return;

                for (var i = 0; i < pendingUpdates.Length; i++)
                {
                    var update = pendingUpdates[i];
                    var updateLength = update.Length > 0 ? update.Length : 1000;
                    var received = 0L;

                    while (received < updateLength)
                    {
                        await Task.Delay(SchedulerDownloadTickMs).ConfigureAwait(false);
                        received = Math.Min(updateLength, received + Math.Max(100L, updateLength / 4));
                        lock (_schedulerLock)
                        {
                            _schedulerState.DownloadStatus?.Status = new SchedulerDownloadProgress
                            {
                                Id = update.UpdateId,
                                Length = updateLength,
                                Received = received,
                                Status = "downloading"
                            };
                        }
                    }

                    lock (_schedulerLock)
                    {
                        _schedulerState.DownloadedUpdateIds.Add(update.UpdateId);

                        if (_schedulerState.DownloadStatus is null) continue;

                        _schedulerState.DownloadStatus.Updates[i] =
                            _schedulerState.DownloadStatus.Updates[i] with { Downloaded = true };
                        _schedulerState.DownloadStatus.Status = new SchedulerDownloadProgress
                        {
                            Id = update.UpdateId,
                            Length = updateLength,
                            Received = updateLength,
                            Status = "finished"
                        };
                    }
                }

                await Task.Delay(SchedulerDownloadFinishDelayMs).ConfigureAwait(false);
                lock (_schedulerLock)
                {
                    _schedulerState.DownloadStatus = null;
                }
            }
            catch
            {
                lock (_schedulerLock)
                {
                    _schedulerState.BackingUp = false;
                    _schedulerState.BackingBeforeUpdate = false;
                    _schedulerState.DownloadStatus = null;
                }
            }
        });
    }

    private void StartSchedulerBackupCycle()
    {
        lock (_schedulerLock)
        {
            if (_schedulerState.BackingUp || _schedulerState.BackingBeforeUpdate ||
                _schedulerState.DownloadStatus is not null)
                return;

            _schedulerState.BackingUp = true;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SchedulerBackupDelayMs).ConfigureAwait(false);
            }
            finally
            {
                lock (_schedulerLock)
                {
                    _schedulerState.BackingUp = false;
                }
            }
        });
    }

    private bool IsSchedulerUpdateDownloaded(string updateId)
    {
        lock (_schedulerLock)
        {
            return _schedulerState.DownloadedUpdateIds.Contains(updateId);
        }
    }

    private static object MapSchedulerUpdate(UpdateManifest update, bool downloaded)
    {
        return new
        {
            id = update.UpdateId,
            subsystem = update.Subsystem,
            changes = update.Changes,
            length = update.Length,
            toVersion = update.ToVersion,
            dependencies = new Dictionary<string, object?>(),
            downloaded
        };
    }

    private static object MapSchedulerUpdate(SchedulerUpdateState update, bool downloaded)
    {
        return new
        {
            id = update.Id,
            subsystem = update.Subsystem,
            changes = update.Changes,
            length = update.Length,
            toVersion = update.ToVersion,
            dependencies = update.Dependencies ?? new Dictionary<string, object?>(),
            downloaded
        };
    }

    private static object MapUpdate(UpdateManifest update)
    {
        return new
        {
            _id = update.UpdateId,
            created = update.CreatedUtc.ToUnixTimeMilliseconds(),
            accountId = "usr_openjibo_owner",
            fromVersion = update.FromVersion,
            toVersion = update.ToVersion,
            changes = update.Changes,
            url = update.Url,
            shaHash = update.ShaHash,
            length = update.Length,
            subsystem = update.Subsystem,
            filter = update.Filter,
            dependencies = new Dictionary<string, object?>()
        };
    }

    private static bool IsUpdateNewerThanRequest(string candidateVersion, string? fromVersion)
    {
        if (string.IsNullOrWhiteSpace(fromVersion)) return true;

        if (string.IsNullOrWhiteSpace(candidateVersion)) return false;

        if (Version.TryParse(candidateVersion, out var candidate) &&
            Version.TryParse(fromVersion, out var requested))
            return candidate > requested;

        return string.Compare(candidateVersion, fromVersion, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static object MapBackup(BackupRecord backup)
    {
        return new
        {
            modified = backup.CreatedUtc.ToString("O"),
            etag = backup.BackupId,
            size = "0",
            location = new
            {
                expires = backup.CreatedUtc.AddHours(1).ToString("O"),
                url = $"https://api.jibo.com/backup/{backup.BackupId}"
            }
        };
    }

    private static object MapHoliday(HolidayRecord holiday)
    {
        return new
        {
            id = holiday.Id,
            eventId = holiday.EventId,
            name = holiday.Name,
            category = holiday.Category,
            subcategory = holiday.Subcategory,
            loopId = holiday.LoopId,
            memberId = holiday.MemberId,
            isEnabled = holiday.IsEnabled,
            date = holiday.Date,
            endDate = holiday.EndDate,
            source = holiday.Source,
            countryCode = holiday.CountryCode,
            created = holiday.Created
        };
    }

    private static object MapCommute(CommuteProfileRecord commute)
    {
        return new
        {
            id = commute.Id,
            loopId = commute.LoopId,
            memberId = commute.MemberId,
            isEnabled = commute.IsEnabled,
            isComplete = commute.IsComplete,
            mode = commute.Mode,
            workHour = commute.WorkHour,
            workMinute = commute.WorkMinute,
            originName = commute.OriginName,
            destinationName = commute.DestinationName,
            typicalDurationMinutes = commute.TypicalDurationMinutes,
            created = commute.Created,
            updated = commute.Updated
        };
    }

    private static object MapMedia(MediaRecord item)
    {
        return new
        {
            path = item.Path,
            created = item.CreatedUtc.ToUnixTimeMilliseconds(),
            type = item.MediaType,
            reference = item.Reference,
            accountId = item.AccountId,
            loopId = item.LoopId,
            url = item.Url,
            thumbnailUrl = item.Url,
            originalUrl = item.Url,
            isEncrypted = item.IsEncrypted,
            isDeleted = item.IsDeleted,
            meta = item.Meta
        };
    }

    private static string? TryReadMetaString(IDictionary<string, object?> meta, string key)
    {
        return meta.TryGetValue(key, out var value)
            ? value?.ToString()
            : null;
    }

    private static string? ReadString(JsonElement? element, string propertyName)
    {
        if (element is null || !element.Value.TryGetProperty(propertyName, out var property)) return null;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static long? ReadLong(JsonElement? element, string propertyName)
    {
        if (element is null || !element.Value.TryGetProperty(propertyName, out var property)) return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number)) return number;

        return long.TryParse(property.ToString(), out var parsed) ? parsed : null;
    }

    private static bool ReadBool(JsonElement? element, string propertyName)
    {
        if (element is null || !element.Value.TryGetProperty(propertyName, out var property)) return false;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => bool.TryParse(property.ToString(), out var parsed) && parsed
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement? element, string propertyName)
    {
        if (element is null || !element.Value.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array) return [];

        return
        [
            .. property.EnumerateArray()
                .Select(item =>
                    item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
        ];
    }

    private static IDictionary<string, object?>? ReadObject(JsonElement? element, string propertyName)
    {
        if (element is null || !element.Value.TryGetProperty(propertyName, out var property)) return null;

        if (property.ValueKind != JsonValueKind.Object) return null;

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in property.EnumerateObject())
            result[child.Name] = child.Value.ValueKind switch
            {
                JsonValueKind.String => child.Value.GetString(),
                JsonValueKind.Number when child.Value.TryGetInt64(out var longValue) => longValue,
                JsonValueKind.Number when child.Value.TryGetDouble(out var doubleValue) => doubleValue,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => child.Value.ToString()
            };

        return result;
    }

    private static string? ReadHeader(ProtocolEnvelope envelope, string headerName)
    {
        return envelope.Headers.TryGetValue(headerName, out var value) ? value : null;
    }

    private static string CreateOobeToken()
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return $"oobe-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static bool ReadBooleanHeader(ProtocolEnvelope envelope, string headerName)
    {
        return envelope.Headers.TryGetValue(headerName, out var value) &&
               bool.TryParse(value, out var parsed) &&
               parsed;
    }

    private sealed class SchedulerRuntimeState
    {
        public bool BackingUp { get; set; }
        public bool BackingBeforeUpdate { get; set; }
        public string? ActiveFilter { get; set; }
        public SchedulerDownloadState? DownloadStatus { get; set; }
        public HashSet<string> DownloadedUpdateIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SchedulerDownloadState
    {
        public SchedulerUpdateState[] Updates { get; set; } = [];
        public SchedulerDownloadProgress? Status { get; set; }
    }

    private sealed record SchedulerUpdateState
    {
        public string Id { get; init; } = string.Empty;
        public string Subsystem { get; init; } = "robot";
        public string Changes { get; init; } = string.Empty;
        public long Length { get; init; }
        public string ToVersion { get; init; } = string.Empty;
        public IDictionary<string, object?> Dependencies { get; init; } = new Dictionary<string, object?>();
        public bool Downloaded { get; init; }
    }

    private sealed class SchedulerDownloadProgress
    {
        public string Id { get; set; } = string.Empty;
        public long Length { get; set; }
        public long Received { get; set; }
        public string Status { get; set; } = "downloading";
        public string? Reason { get; set; }
        public string? Error { get; set; }
    }

    private sealed class OobeTokenState
    {
        public string? DeviceId { get; set; }
        public string? LoopId { get; init; }
        public bool Complete { get; set; }
        public DateTimeOffset ExpiresUtc { get; init; }
    }

    private sealed class NullMediaContentStore : IMediaContentStore
    {
        public Task StoreAsync(string path, string contentType, byte[] content,
            IReadOnlyDictionary<string, object?>? meta, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<MediaContentSnapshot?> LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<MediaContentSnapshot?>(null);
        }
    }
}