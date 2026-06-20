using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private JiboInteractionDecision BuildVerifyMeDecision(TurnContext turn)
    {
        if (jiboVerificationService is null)
        {
            return new JiboInteractionDecision(
                "verify_me",
                "Verification is not available on this server right now.");
        }

        var deviceId = turn.DeviceId;
        if (string.IsNullOrWhiteSpace(deviceId) && cloudStateStore is not null)
            deviceId = cloudStateStore.GetRobot().DeviceId;

        var code = jiboVerificationService.GetPendingCodeForDevice(deviceId);
        if (string.IsNullOrWhiteSpace(code))
        {
            return new JiboInteractionDecision(
                "verify_me",
                "I don't have a verification request for you right now. Start one from the OpenJibo portal.");
        }

        var spokenCode = string.Join(", ", code.ToCharArray());
        return new JiboInteractionDecision(
            "verify_me",
            $"Your verification code is {spokenCode}.");
    }
}
