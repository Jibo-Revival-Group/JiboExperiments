using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Infrastructure.Persistence;

/// <summary>
/// Aligns the cloud household loop with the physical robot's existing KB identity
/// when <c>OpenJibo:Loop:SeedIdentity</c> is true (or <c>OpenJibo__Loop__SeedIdentity=1</c>).
/// Set <c>OpenJibo:Robot:RobotId</c> to the robot's KB account id
/// (<c>Knowledge/jibo/loop</c> root <c>data.robot</c>) so Loop#list() matches SyncManager.
/// Optional <c>OpenJibo:Loop:LoopId</c> / <c>OwnerAccountId</c> reuse the stock ObjectIds
/// already on the robot so SyncManager merges into the existing KB loop rather than
/// replacing the root id alone.
/// </summary>
public static class LoopIdentitySeedApplier
{
    public static bool Apply(ICloudStateStore stateStore, IConfiguration configuration, ILogger logger)
    {
        var seedEnabled = bool.TryParse(configuration["OpenJibo:Loop:SeedIdentity"], out var enabled) && enabled;
        if (!seedEnabled)
        {
            if (!string.IsNullOrWhiteSpace(configuration["OpenJibo:Robot:RobotId"]))
            {
                logger.LogDebug(
                    "OpenJibo:Robot:RobotId is set but OpenJibo:Loop:SeedIdentity is not true; " +
                    "skipping automatic household loop seed (RobotId still applies via LoopRosterResolver)");
            }

            return false;
        }

        var robotId = NullIfWhiteSpace(configuration["OpenJibo:Robot:RobotId"]);
        if (robotId is null)
        {
            logger.LogWarning(
                "OpenJibo:Loop:SeedIdentity=true but OpenJibo:Robot:RobotId is missing; " +
                "set it to the robot's KB account id (Knowledge/jibo/loop root data.robot)");
            return false;
        }

        var friendlyId = NullIfWhiteSpace(configuration["OpenJibo:Robot:FriendlyId"]) ??
                         NullIfWhiteSpace(configuration["OpenJibo:Robot:RobotFriendlyId"]);
        var preferredLoopId = NullIfWhiteSpace(configuration["OpenJibo:Loop:LoopId"]);
        var ownerAccountId = NullIfWhiteSpace(configuration["OpenJibo:Loop:OwnerAccountId"]);
        var loopName = NullIfWhiteSpace(configuration["OpenJibo:Loop:Name"]);

        var deviceId = friendlyId ?? robotId;
        stateStore.UpdateRobot(new DeviceRegistration
        {
            DeviceId = deviceId,
            RobotId = robotId,
            FriendlyName = friendlyId ?? deviceId,
            RegistrationSource = RobotRegistrationSources.Physical
        });

        var loop = stateStore.AlignHouseholdIdentity(
            robotId,
            friendlyId ?? deviceId,
            preferredLoopId,
            ownerAccountId,
            loopName);

        if (preferredLoopId is not null &&
            !loop.LoopId.Equals(preferredLoopId, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Configured OpenJibo:Loop:LoopId={PreferredLoopId} but household loop is {ActualLoopId}. " +
                "SyncManager will rewrite the on-robot root loop id on the next successful List; " +
                "existing KB member ids will only merge if they match cloud member ids.",
                preferredLoopId, loop.LoopId);
        }

        logger.LogInformation(
            "Loop identity seeded loopId={LoopId} robotId={RobotId} robotFriendlyId={FriendlyId} owner={Owner}",
            loop.LoopId, loop.RobotId, loop.RobotFriendlyId, loop.OwnerAccountId);
        return true;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
