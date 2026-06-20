using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jibo.Cloud.Api.Hosting;

internal static class PortalEndpoints
{
    internal static void MapPortalEndpoints(this WebApplication app)
    {
        app.MapPost("/api/portal/jibo-verification/start", (
            [FromBody] StartJiboVerificationRequest request,
            JiboVerificationService verificationService,
            ICloudStateStore stateStore) =>
        {
            if (string.IsNullOrWhiteSpace(request.FriendlyName))
                return Results.BadRequest(new { error = "friendlyName is required." });

            var result = verificationService.StartVerification(stateStore, request.FriendlyName);
            if (!result.Ok)
                return Results.NotFound(new { error = result.Error });

            var expiresInSeconds = result.ExpiresAtUtc.HasValue
                ? (int)Math.Max(0, (result.ExpiresAtUtc.Value - DateTimeOffset.UtcNow).TotalSeconds)
                : 0;

            return Results.Json(new
            {
                sessionId = result.SessionId,
                expiresInSeconds
            });
        });

        app.MapPost("/api/portal/jibo-verification/confirm", (
            [FromBody] ConfirmJiboVerificationRequest request,
            JiboVerificationService verificationService) =>
        {
            if (string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.Code))
                return Results.BadRequest(new { error = "sessionId and code are required." });

            var result = verificationService.TryConfirm(request.SessionId, request.Code);
            if (!result.Ok)
                return Results.BadRequest(new { error = result.Error });

            return Results.Json(new
            {
                jiboVerificationToken = result.Token,
                jiboFriendlyName = result.FriendlyName
            });
        });

        app.MapPost("/api/portal/home-assistant/link", async (
            [FromBody] LinkHomeAssistantRequest request,
            JiboVerificationService verificationService,
            HomeAssistantConnectionRegistry registry,
            IUserIntegrationStore integrationStore) =>
        {
            if (string.IsNullOrWhiteSpace(request.JiboVerificationToken) ||
                string.IsNullOrWhiteSpace(request.HaCode))
                return Results.BadRequest(new { error = "jiboVerificationToken and haCode are required." });

            var token = verificationService.TryConsumeToken(request.JiboVerificationToken);
            if (token is null)
                return Results.BadRequest(new { error = "Jibo verification token is invalid or has expired." });

            var pendingHa = registry.TryGetPendingByCode(request.HaCode);
            if (pendingHa is null)
                return Results.BadRequest(new { error = "Home Assistant verification code is invalid or has expired." });

            var link = integrationStore.AddHomeAssistantLink(
                token.DeviceId,
                token.FriendlyName,
                pendingHa.InstanceId);

            var delivered = await registry.SendPairedAsync(
                pendingHa.InstanceId,
                token.FriendlyName,
                link.LinkId);

            if (!delivered)
                return Results.BadRequest(new { error = "Home Assistant is no longer connected." });

            return Results.Json(new
            {
                linkId = link.LinkId,
                jiboFriendlyName = token.FriendlyName,
                haInstanceId = pendingHa.InstanceId
            });
        });

        app.MapGet("/api/portal/home-assistant/links", (IUserIntegrationStore integrationStore) =>
        {
            var links = integrationStore.GetHomeAssistantLinks()
                .Select(link => new
                {
                    linkId = link.LinkId,
                    jiboDeviceId = link.JiboDeviceId,
                    jiboFriendlyName = link.JiboFriendlyName,
                    haInstanceId = link.HaInstanceId,
                    pairedAtUtc = link.PairedAtUtc,
                    lastSeenUtc = link.LastSeenUtc
                });

            return Results.Json(new { links });
        });
    }

    private sealed record StartJiboVerificationRequest(string? FriendlyName);

    private sealed record ConfirmJiboVerificationRequest(string? SessionId, string? Code);

    private sealed record LinkHomeAssistantRequest(string? JiboVerificationToken, string? HaCode, string? FriendlyName);
}
