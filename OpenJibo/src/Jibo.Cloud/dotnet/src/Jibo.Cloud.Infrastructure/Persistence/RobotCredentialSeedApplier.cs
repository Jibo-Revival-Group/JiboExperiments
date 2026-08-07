using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Infrastructure.Persistence;

/// <summary>
/// Seeds physical dump robots and AWS credential fingerprint bindings from a local override file.
/// Secrets stay out of git; only fingerprints are bound into cloud state.
/// </summary>
public static class RobotCredentialSeedApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static int Apply(ICloudStateStore stateStore, IConfiguration configuration, ILogger logger)
    {
        var configuredPath = configuration["OpenJibo:RobotCredentialSeed:Path"];
        var path = ResolveSeedPath(configuredPath);
        if (path is null)
        {
            logger.LogDebug("Robot credential seed skipped: no local override file found");
            return 0;
        }

        if (!File.Exists(path))
        {
            logger.LogWarning("Robot credential seed path configured but missing: {Path}", path);
            return 0;
        }

        RobotCredentialSeedFile? seed;
        try
        {
            seed = JsonSerializer.Deserialize<RobotCredentialSeedFile>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse robot credential seed file {Path}", path);
            return 0;
        }

        if (seed?.Robots is null || seed.Robots.Count == 0)
        {
            logger.LogWarning("Robot credential seed file {Path} contained no robots", path);
            return 0;
        }

        var applied = 0;
        foreach (var robot in seed.Robots)
        {
            if (string.IsNullOrWhiteSpace(robot.RobotId) ||
                string.IsNullOrWhiteSpace(robot.DeviceId) ||
                string.IsNullOrWhiteSpace(robot.AccessKeyId))
            {
                logger.LogWarning(
                    "Skipping incomplete robot credential seed entry robotId={RobotId} deviceId={DeviceId}",
                    robot.RobotId, robot.DeviceId);
                continue;
            }

            try
            {
                ApplyRobot(stateStore, robot);
                applied++;
                logger.LogInformation(
                    "Seeded dump robot {RobotId} deviceId={DeviceId} from {Path}",
                    robot.RobotId, robot.DeviceId, path);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to seed robot {RobotId} from {Path}", robot.RobotId, path);
            }
        }

        if (applied > 0)
            stateStore.SavePersistedState();

        return applied;
    }

    public static void ApplyRobot(ICloudStateStore stateStore, RobotCredentialSeedEntry robot)
    {
        var deviceId = robot.DeviceId.Trim();
        var robotId = robot.RobotId.Trim();
        var serial = string.IsNullOrWhiteSpace(robot.SerialNumber) ? deviceId : robot.SerialNumber.Trim();

        var device = stateStore.GetOrCreateDevice(deviceId, null, null, RobotRegistrationSources.Physical);
        stateStore.UpsertDevice(new DeviceRegistration
        {
            DeviceId = device.DeviceId,
            RobotId = robotId,
            FriendlyName = robotId,
            FirmwareVersion = device.FirmwareVersion,
            ApplicationVersion = device.ApplicationVersion,
            IsActive = true,
            CertificateThumbprint = device.CertificateThumbprint,
            IssuedIdentityId = device.IssuedIdentityId,
            BuildHash = device.BuildHash,
            ConfigHash = device.ConfigHash,
            VerifiedSerialNumber = serial,
            SerialEvidenceSource = "dump-seed",
            SerialEvidenceVerifiedUtc = DateTimeOffset.UtcNow,
            RegistrationSource = RobotRegistrationSources.Physical,
            IsHidden = false,
            ArchivedUtc = null,
            HostMappings = device.HostMappings
        });

        var loop = stateStore.AddLoop($"{robotId} Loop", stateStore.GetAccount().AccountId, robotId, deviceId);
        var members = stateStore.GetLoopMembers(loop.LoopId);
        var hasEditableMember = members.Any(member =>
            member.Type.Equals("member", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(member.FirstName, "OpenJibo", StringComparison.OrdinalIgnoreCase));
        if (!hasEditableMember)
        {
            stateStore.AddLoopMember(
                loop.LoopId,
                null,
                null,
                "Demo",
                "Member",
                "unknown",
                null,
                false,
                "member");
        }

        var fingerprint = FingerprintAccessKeyId(robot.AccessKeyId);
        stateStore.BindAwsCredentialFingerprint(deviceId, fingerprint, "local-override");
    }

    public static string FingerprintAccessKeyId(string accessKeyId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessKeyId.Trim())))
            .ToLowerInvariant()[..16];

    private static string? ResolveSeedPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configured = configuredPath.Trim();
            return Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configured));
        }

        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "robot-credentials.local.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "robot-credentials.local.json"),
            Path.Combine(AppContext.BaseDirectory, "robot-credentials.local.json"),
            Path.Combine(AppContext.BaseDirectory, "App_Data", "robot-credentials.local.json")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private sealed class RobotCredentialSeedFile
    {
        public List<RobotCredentialSeedEntry> Robots { get; set; } = [];
    }
}

public sealed class RobotCredentialSeedEntry
{
    public string RobotId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public string AccessKeyId { get; set; } = string.Empty;
    public string? SecretAccessKey { get; set; }
}
