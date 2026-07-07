using System.Text;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Jibo.Cloud.Api.Hosting;

internal static class PortalEndpoints
{
    private static readonly string[] RequiredLegacyHostMappings =
    [
        "api.jibo.com",
        "api-socket.jibo.com",
        "neo-hub.jibo.com"
    ];

    private static readonly TrustedServerDirectoryEntry[] TrustedServerDirectory =
    [
        new("api.openjibo.com", "Managed Open Jibo API", "Hosted", true, "Primary robot-facing hosted API."),
        new("openjibo.com", "Open Jibo owner site", "Hosted", true, "Owner entry surface and onboarding handoff."),
        new("api.jibo.com", "Legacy Jibo API", "Trusted legacy", true, "Historical trusted root preserved for conversion evidence."),
        new("api-socket.jibo.com", "Legacy Jibo socket API", "Trusted legacy", true, "Historical socket endpoint preserved for conversion evidence."),
        new("neo-hub.jibo.com", "Legacy Jibo hub API", "Trusted legacy", true, "Historical listen/proactive endpoint preserved for conversion evidence."),
        new("self-hosted", "Custom self-hosted server", "Self-hosted", false, "Use a typed hostname or IP for local or self-hosted deployment.")
    ];

    internal static void MapPortalEndpoints(this WebApplication app)
    {
        app.MapGet("/api/onboarding/trusted-servers", () =>
        {
            return Results.Json(new
            {
                directoryVersion = "1",
                allowCustomEntry = true,
                hostedHttpsRequired = true,
                selfHostedHttpAllowed = true,
                trustedRootHost = "openjibo.com",
                servers = TrustedServerDirectory.Select(server => new
                {
                    server.Id,
                    server.DisplayName,
                    server.Category,
                    server.RequiresHttps,
                    server.AllowsHttp,
                    server.Description
                })
            });
        });

        app.MapPost("/api/portal/jibo-verification/confirm", (
            [FromBody] ConfirmJiboVerificationRequest request,
            JiboVerificationService verificationService,
            PortalSessionService portalSessionService,
            ICloudStateStore cloudStateStore) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code))
                return Results.BadRequest(new { error = "code is required." });

            var result = verificationService.TryConfirmByCode(request.Code);
            if (!result.Ok)
                return Results.BadRequest(new { error = result.Error });

            var session = portalSessionService.CreateSession(result.DeviceId!, result.FriendlyId!);
            RegisterVerifiedRobotIdentity(cloudStateStore, result.DeviceId!, result.FriendlyId!);

            return Results.Json(new
            {
                portalSessionToken = session.Token,
                jiboFriendlyId = result.FriendlyId,
                jiboDeviceId = result.DeviceId,
                expiresAtUtc = session.ExpiresAtUtc
            });
        });

        app.MapGet("/api/portal/dashboard", (
            HttpRequest request,
            PortalSessionService portalSessionService,
            IUserIntegrationStore integrationStore,
            HomeAssistantConnectionRegistry registry) =>
        {
            var session = ResolvePortalSession(request, null, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            var link = integrationStore.FindLinkForJibo(session.DeviceId, session.FriendlyId);
            return Results.Json(BuildDashboardPayload(session, link, registry));
        });

        app.MapGet("/api/portal/identity-graph", (
            HttpRequest request,
            PortalSessionService portalSessionService,
            ICloudStateStore cloudStateStore) =>
        {
            var session = ResolvePortalSession(request, null, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            var graph = cloudStateStore.GetIdentityGraph();
            return Results.Json(new
            {
                graph.AccountId,
                graph.LoopId,
                graph.RobotId,
                graph.DeviceId,
                graph.SnapshotVersion,
                graph.ContentHash,
                graph.SignatureAlgorithm,
                graph.SignatureKeyId,
                graph.SignaturePayload,
                graph.Signature,
                graph.AdmissionAssessment,
                graph.EvidenceBundle,
                graph.People,
                graph.Members,
                graph.Relationships,
                graph.EvidenceSignals
            });
        });


        app.MapPost("/api/portal/identity-graph/revocations", (
            [FromBody] RevokeIdentityGraphAnchorRequest request,
            HttpRequest httpRequest,
            PortalSessionService portalSessionService,
            ICloudStateStore cloudStateStore) =>
        {
            var session = ResolvePortalSession(httpRequest, request.PortalSessionToken, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Anchor))
                return Results.BadRequest(new { error = "anchor is required." });

            cloudStateStore.RevokeIdentityGraphAnchor(request.Anchor);
            var graph = cloudStateStore.GetIdentityGraph();

            return Results.Json(new
            {
                revoked = true,
                anchor = request.Anchor.Trim(),
                graph.AdmissionAssessment,
                graph.EvidenceBundle
            });
        });

        app.MapGet("/api/portal/identity-graph/evidence-bundle", (
            HttpRequest request,
            PortalSessionService portalSessionService,
            ICloudStateStore cloudStateStore) =>
        {
            var session = ResolvePortalSession(request, null, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            var graph = cloudStateStore.GetIdentityGraph();
            var fileName = $"openjibo-identity-evidence-{graph.DeviceId}-{graph.EvidenceBundle.BundleHash}.txt";
            return Results.File(
                Encoding.UTF8.GetBytes(graph.EvidenceBundle.Envelope),
                "text/plain; charset=utf-8",
                fileName);
        });


        app.MapPost("/api/portal/identity-graph/evidence-bundle/verify", (
            [FromBody] VerifyIdentityGraphEvidenceBundleRequest request,
            HttpRequest httpRequest,
            PortalSessionService portalSessionService) =>
        {
            var session = ResolvePortalSession(httpRequest, request.PortalSessionToken, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Envelope))
                return Results.BadRequest(new { error = "envelope is required." });

            var verification = IdentityGraphEvidenceBundleVerifier.Verify(
                request.Envelope,
                request.LocalRevokedAnchors ?? []);

            return Results.Json(new
            {
                verification.IsValid,
                verification.IsLocallyAdmissible,
                verification.EffectiveAdmissionRecommendation,
                verification.AdmissionRecommendation,
                verification.AdmissionPolicyVersion,
                verification.AdmissionReasons,
                verification.RequiredEvidence,
                verification.SatisfiedEvidence,
                verification.BlockingEvidence,
                verification.RecommendedActions,
                verification.RevocationChecks,
                verification.RevocationAnchors,
                verification.RevocationListHash,
                verification.LocalRevocationMatches,
                verification.TrustPurpose,
                verification.PeerTransportStatus,
                verification.ReplicationReadiness,
                verification.SyncDirection,
                verification.PeerAdmissionMode,
                verification.RetentionPolicy,
                verification.AdmissionReviewStatus,
                verification.ExportedByCloudVersion,
                verification.ExportedByService,
                verification.DirectPeerTransportAllowed,
                verification.AccountId,
                verification.LoopId,
                verification.RobotId,
                verification.DeviceId,
                verification.PeopleCount,
                verification.MemberCount,
                verification.RelationshipCount,
                verification.EvidenceSignalCount,
                verification.RelationshipKinds,
                verification.EvidenceSignalKinds,
                verification.BundleHash,
                verification.ComputedBundleHash,
                verification.AdmissionDecisionSignatureValid,
                verification.SnapshotSignatureValid,
                verification.Errors
            });
        });

        app.MapPost("/api/portal/home-assistant/link", async (
            [FromBody] LinkHomeAssistantRequest request,
            HttpRequest httpRequest,
            PortalSessionService portalSessionService,
            HomeAssistantConnectionRegistry registry,
            IUserIntegrationStore integrationStore) =>
        {
            var session = ResolvePortalSession(httpRequest, request.PortalSessionToken, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.HaCode))
                return Results.BadRequest(new { error = "haCode is required." });

            var pendingHa = registry.TryGetPendingByCode(request.HaCode);
            if (pendingHa is null)
                return Results.BadRequest(new
                    { error = "Home Assistant verification code is invalid or has expired." });

            var link = integrationStore.AddHomeAssistantLink(
                session.DeviceId,
                session.FriendlyId,
                pendingHa.InstanceId);

            var delivered = await registry.SendPairedAsync(
                pendingHa.InstanceId,
                session.FriendlyId,
                link.LinkId);

            if (!delivered)
                return Results.BadRequest(new { error = "Home Assistant is not connected to this server right now." });

            return Results.Json(new
            {
                linkId = link.LinkId,
                jiboFriendlyId = session.FriendlyId,
                haInstanceId = pendingHa.InstanceId,
                homeAssistant = BuildHomeAssistantPayload(link, registry)
            });
        });

        app.MapDelete("/api/portal/home-assistant/link", async (
            HttpRequest request,
            PortalSessionService portalSessionService,
            IUserIntegrationStore integrationStore,
            HomeAssistantConnectionRegistry registry) =>
        {
            var session = ResolvePortalSession(request, null, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            var link = integrationStore.FindLinkForJibo(session.DeviceId, session.FriendlyId);
            if (link is null)
                return Results.NotFound(new { error = "No Home Assistant link exists for this Jibo." });

            integrationStore.RemoveHomeAssistantLink(link.LinkId);
            await registry.SendUnpairedAsync(link.HaInstanceId, link.LinkId);

            return Results.Json(new
            {
                unlinked = true,
                jiboFriendlyId = session.FriendlyId
            });
        });


        app.MapGet("/api/portal/admin/summary", (
            HttpRequest request,
            PortalSessionService portalSessionService,
            ICloudStateStore cloudStateStore,
            IUserIntegrationStore integrationStore,
            HomeAssistantConnectionRegistry registry) =>
        {
            var session = ResolvePortalSession(request, null, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            var robot = cloudStateStore.GetRobot();
            var persistence = cloudStateStore.GetPersistenceStateInfo();
            var graph = cloudStateStore.GetIdentityGraph();
            var backups = cloudStateStore.GetBackups();
            var updates = cloudStateStore.ListUpdates();
            var media = cloudStateStore.ListMedia();
            var loops = cloudStateStore.GetLoops();
            var people = cloudStateStore.GetPeople();
            var haLinks = integrationStore.GetHomeAssistantLinks();

            return Results.Json(new
            {
                cloudVersion = OpenJiboCloudBuildInfo.Version,
                persistence,
                robot = new
                {
                    robot.DeviceId,
                    robot.RobotId,
                    robot.FriendlyName,
                    robot.FirmwareVersion,
                    robot.ApplicationVersion,
                    robot.IsActive,
                    robot.HostMappings
                },
                counts = new
                {
                    loops = loops.Count,
                    people = people.Count,
                    updates = updates.Count,
                    backups = backups.Count,
                    media = media.Count,
                    homeAssistantLinks = haLinks.Count,
                    homeAssistantConnected = haLinks.Count(link => registry.IsInstanceConnected(link.HaInstanceId)),
                    identityRelationships = graph.Relationships.Count,
                    identityEvidenceSignals = graph.EvidenceSignals.Count
                },
                conversion = new
                {
                    targetMode = robot.HostMappings.TryGetValue("api.jibo.com", out var apiHost) &&
                                 apiHost.Contains("openjibo", StringComparison.OrdinalIgnoreCase)
                        ? "open-jibo"
                        : "unconfirmed",
                    hostMappings = robot.HostMappings,
                    requiredHostMappings = RequiredLegacyHostMappings,
                    missingHostMappings = GetMissingRequiredHostMappings(robot),
                    blockers = BuildAdminConversionBlockers(robot, graph),
                    operatorQuestions = new[]
                    {
                        "Which physical robot variant should be the first conversion target?",
                        "Has the latest non-destructive backup/rollback snapshot been filmed and approved?",
                        "Which safe awakening assets are approved for first-boot reuse?",
                        "Do live websocket captures expose stable face/person identifiers, or should the demo stay smoke-seeded?"
                    }
                },
                harness = new
                {
                    url = "/harness",
                    suggestedOperations = new[]
                    {
                        "OOBE_20161026.AuditConversion",
                        "OOBE_20161026.PrepareRobot",
                        "OOBE_20161026.GetStatus",
                        "OOBE_20161026.VerifyConnection",
                        "Robot_20160225.GetRobot",
                        "Update_20160225.ListUpdates"
                    }
                }
            });
        });

        app.MapPost("/api/portal/logout", (
            [FromBody] PortalLogoutRequest request,
            HttpRequest httpRequest,
            PortalSessionService portalSessionService) =>
        {
            var token = ResolvePortalSessionToken(httpRequest, request.PortalSessionToken);
            portalSessionService.RevokeSession(token);
            return Results.Json(new { ok = true });
        });

        app.MapGet("/api/portal/home-assistant/links", (
            PortalSessionService portalSessionService,
            IUserIntegrationStore integrationStore,
            HomeAssistantConnectionRegistry registry,
            HttpRequest request) =>
        {
            var session = ResolvePortalSession(request, null, portalSessionService);
            if (session is not null)
            {
                var link = integrationStore.FindLinkForJibo(session.DeviceId, session.FriendlyId);
                return Results.Json(new
                {
                    links = link is null
                        ? Array.Empty<object>()
                        : new[] { BuildHomeAssistantPayload(link, registry) }
                });
            }

            var links = integrationStore.GetHomeAssistantLinks()
                .Select(link => BuildHomeAssistantPayload(link, registry));
            return Results.Json(new { links });
        });
    }


    private static IReadOnlyList<string> BuildAdminConversionBlockers(
        DeviceRegistration robot,
        IdentityGraphSnapshot graph)
    {
        var blockers = new List<string>();

        if (!robot.IsActive)
            blockers.Add("robot-not-active");

        if (robot.HostMappings.Count == 0)
            blockers.Add("missing-host-mappings");

        if (graph.AdmissionAssessment.BlockingEvidence.Count > 0)
            blockers.AddRange(graph.AdmissionAssessment.BlockingEvidence.Select(evidence => $"identity-{evidence}"));

        foreach (var missingHost in GetMissingRequiredHostMappings(robot))
            blockers.Add($"missing-host-mapping:{missingHost}");

        return blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> GetMissingRequiredHostMappings(DeviceRegistration robot)
    {
        return RequiredLegacyHostMappings
            .Where(host => !robot.HostMappings.TryGetValue(host, out var mappedHost) ||
                           string.IsNullOrWhiteSpace(mappedHost))
            .ToArray();
    }

    private static PortalSessionService.PortalSession? ResolvePortalSession(
        HttpRequest request,
        string? portalSessionToken,
        PortalSessionService portalSessionService)
    {
        return portalSessionService.TryGetSession(ResolvePortalSessionToken(request, portalSessionToken));
    }

    private static string? ResolvePortalSessionToken(
        HttpRequest request,
        string? portalSessionToken)
    {
        var token = request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(token) &&
            token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token["Bearer ".Length..].Trim();

        token ??= request.Query["portalSessionToken"].FirstOrDefault();
        token ??= portalSessionToken;
        return token;
    }

    private static void RegisterVerifiedRobotIdentity(ICloudStateStore cloudStateStore, string deviceId,
        string friendlyId)
    {
        var currentRobot = cloudStateStore.GetRobot();
        if (string.Equals(currentRobot.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(currentRobot.RobotId, friendlyId, StringComparison.OrdinalIgnoreCase))
            return;

        cloudStateStore.UpdateRobot(new DeviceRegistration
        {
            DeviceId = deviceId,
            RobotId = friendlyId,
            FriendlyName = currentRobot.FriendlyName,
            FirmwareVersion = currentRobot.FirmwareVersion,
            ApplicationVersion = currentRobot.ApplicationVersion,
            IsActive = currentRobot.IsActive,
            HostMappings = new Dictionary<string, string>(currentRobot.HostMappings, StringComparer.OrdinalIgnoreCase)
        });
    }

    private static object BuildDashboardPayload(
        PortalSessionService.PortalSession session,
        HomeAssistantLinkRecord? link,
        HomeAssistantConnectionRegistry registry)
    {
        return new
        {
            jiboFriendlyId = session.FriendlyId,
            jiboDeviceId = session.DeviceId,
            sessionExpiresAtUtc = session.ExpiresAtUtc,
            homeAssistant = link is null
                ? new
                {
                    linked = false,
                    connected = false
                }
                : BuildHomeAssistantPayload(link, registry)
        };
    }

    private static object BuildHomeAssistantPayload(
        HomeAssistantLinkRecord link,
        HomeAssistantConnectionRegistry registry)
    {
        return new
        {
            linked = true,
            connected = registry.IsInstanceConnected(link.HaInstanceId),
            linkId = link.LinkId,
            jiboFriendlyId = link.JiboFriendlyName,
            jiboDeviceId = link.JiboDeviceId,
            haInstanceId = link.HaInstanceId,
            pairedAtUtc = link.PairedAtUtc,
            lastSeenUtc = link.LastSeenUtc
        };
    }

    private sealed record ConfirmJiboVerificationRequest(string? Code);

    private sealed record LinkHomeAssistantRequest(string? PortalSessionToken, string? HaCode);

    private sealed record PortalLogoutRequest(string? PortalSessionToken);

    private sealed record RevokeIdentityGraphAnchorRequest(string? Anchor, string? PortalSessionToken);

    private sealed record VerifyIdentityGraphEvidenceBundleRequest(
        string? Envelope,
        string? PortalSessionToken,
        string[]? LocalRevokedAnchors);

    private sealed record TrustedServerDirectoryEntry(
        string Id,
        string DisplayName,
        string Category,
        bool RequiresHttps,
        string Description)
    {
        public bool AllowsHttp => !RequiresHttps;
    }
}
