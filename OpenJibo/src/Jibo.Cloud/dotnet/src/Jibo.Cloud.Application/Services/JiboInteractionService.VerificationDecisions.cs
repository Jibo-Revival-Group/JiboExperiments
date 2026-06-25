using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private JiboInteractionDecision BuildVerifyMeDecision(TurnContext turn)
    {
        if (jiboVerificationService is null)
            return new JiboInteractionDecision(
                "verify_me",
                "Verification is not available on this server right now.");

        var friendlyId = turn.DeviceId;
        var deviceId = turn.DeviceId;

        if (cloudStateStore is not null) (deviceId, friendlyId) = JiboIdentityResolver.Resolve(turn, cloudStateStore);

        if (string.IsNullOrWhiteSpace(friendlyId) && string.IsNullOrWhiteSpace(deviceId))
            return new JiboInteractionDecision(
                "verify_me",
                "I can't determine which Jibo is speaking right now.");

        var code = jiboVerificationService.IssueCodeForDevice(friendlyId, deviceId);
        var spokenCode = SpokenDigitFormatter.Format(code);
        return new JiboInteractionDecision(
            "verify_me",
            $"Your verification code is {spokenCode}.");
    }
}