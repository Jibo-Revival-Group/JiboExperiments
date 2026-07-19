using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Shared LoopUpdated push path for portal and protocol loop mutations.
/// </summary>
public sealed class LoopUpdatedPushService(
    ICloudStateStore cloudStateStore,
    RobotNotificationRegistry robotNotificationRegistry,
    ILogger<LoopUpdatedPushService> logger)
{
    public async Task<int> PushForLoopIdAsync(
        string loopId,
        IReadOnlyCollection<string>? additionalRobotKeys = null,
        CancellationToken cancellationToken = default)
    {
        var loop = cloudStateStore.GetLoops()
            .FirstOrDefault(item => item.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase));
        if (loop is null)
        {
            logger.LogWarning("LoopUpdated push skipped: loop {LoopId} not found", loopId);
            return 0;
        }

        var robotKeys = BuildRobotKeys(loop, additionalRobotKeys);
        if (robotKeys.Count == 0)
        {
            logger.LogWarning("LoopUpdated push skipped: no robot keys loopId={LoopId}", loopId);
            return 0;
        }

        var payload = JiboCloudProtocolService.BuildLoopNotificationPayload(
            loop,
            cloudStateStore.GetLoopMembers(loopId));
        var pushed = await robotNotificationRegistry.PushLoopUpdatedAsync(robotKeys, payload, cancellationToken);
        if (pushed == 0)
        {
            logger.LogWarning(
                "LoopUpdated push matched no live api-socket loopId={LoopId} keyCount={KeyCount} keys={Keys}. " +
                "Robot must keep wss notification socket open (Host api-socket.jibo.com or /token-... path).",
                loopId,
                robotKeys.Count,
                string.Join(',', robotKeys.Take(8)));
        }
        else
        {
            logger.LogInformation(
                "LoopUpdated push loopId={LoopId} pushCount={PushCount} keyCount={KeyCount} keys={Keys}",
                loopId,
                pushed,
                robotKeys.Count,
                string.Join(',', robotKeys.Take(8)));
        }

        return pushed;
    }

    private HashSet<string> BuildRobotKeys(LoopRecord loop, IReadOnlyCollection<string>? additionalRobotKeys)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                keys.Add(value.Trim());
        }

        if (additionalRobotKeys is not null)
        {
            foreach (var key in additionalRobotKeys)
                Add(key);
        }

        Add(loop.RobotId);
        Add(loop.RobotFriendlyId);

        foreach (var seed in keys.ToArray())
        {
            var device = cloudStateStore.FindDeviceByFriendlyId(seed);
            if (device is null) continue;
            Add(device.DeviceId);
            Add(device.RobotId);
            Add(device.FriendlyName);
        }

        return keys;
    }
}
