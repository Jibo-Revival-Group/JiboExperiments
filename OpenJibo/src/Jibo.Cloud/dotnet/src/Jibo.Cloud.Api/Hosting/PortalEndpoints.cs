using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jibo.Cloud.Api.Hosting;

internal static class PortalEndpoints
{
    internal static void MapPortalEndpoints(this WebApplication app)
    {
        app.MapPost("/api/portal/jibo-verification/confirm", (
            [FromBody] ConfirmJiboVerificationRequest request,
            JiboVerificationService verificationService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code))
                return Results.BadRequest(new { error = "code is required." });

            var result = verificationService.TryConfirmByCode(request.Code);
            if (!result.Ok)
                return Results.BadRequest(new { error = result.Error });

            return Results.Json(new
            {
                jiboVerificationToken = result.Token,
                jiboFriendlyId = result.FriendlyId
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
                token.FriendlyId,
                pendingHa.InstanceId);

            var delivered = await registry.SendPairedAsync(
                pendingHa.InstanceId,
                token.FriendlyId,
                link.LinkId);

            if (!delivered)
                return Results.BadRequest(new { error = "Home Assistant is no longer connected." });

            return Results.Json(new
            {
                linkId = link.LinkId,
                jiboFriendlyId = token.FriendlyId,
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
                    jiboFriendlyId = link.JiboFriendlyName,
                    haInstanceId = link.HaInstanceId,
                    pairedAtUtc = link.PairedAtUtc,
                    lastSeenUtc = link.LastSeenUtc
                });

            return Results.Json(new { links });
        });
    }

    private sealed record ConfirmJiboVerificationRequest(string? Code);

    private sealed record LinkHomeAssistantRequest(string? JiboVerificationToken, string? HaCode);
}
