using System.Net;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Calendar;
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

    internal static void MapPortalEndpoints(this WebApplication app)
    {
        app.MapGet("/api/onboarding/trusted-servers", (
            ICloudStateStore cloudStateStore) =>
        {
            var servers = cloudStateStore.GetTrustedServers();
            return Results.Json(new
            {
                directoryVersion = "1",
                hostedHttpsRequired = true,
                trustedRootHost = "api.openjibo.com",
                allowCustomEntry = true,
                customEntryMode = "self-hosted",
                serverTypes = new
                {
                    managed = new
                    {
                        listed = true,
                        acceptsPublicConnections = true,
                        requiresHttps = true
                    },
                    hybrid = new
                    {
                        listed = true,
                        acceptsPublicConnections = false,
                        requiresHttps = true
                    },
                    selfHosted = new
                    {
                        listed = false,
                        acceptsPublicConnections = false,
                        requiresHttps = false
                    }
                },
                servers = servers.Select(server => new
                {
                    server.ServerId,
                    server.CanonicalHost,
                    server.DisplayName,
                    server.ServerKind,
                    server.IsListed,
                    server.AcceptsPublicConnections,
                    server.ParticipatesInCloudSync,
                    server.RequiresHttps,
                    server.IsTrustRoot,
                    server.IsActive,
                    server.Description,
                    server.RegisteredAtUtc,
                    server.UpdatedAtUtc,
                    server.LastSeenAtUtc
                })
                .Where(server => !server.IsTrustRoot || server.ServerKind is "managed" or "hybrid")
            });
        });

        app.MapPost("/api/portal/trusted-servers", (
            [FromBody] UpsertTrustedServerRequest request,
            HttpRequest httpRequest,
            PortalSessionService portalSessionService,
            ICloudStateStore cloudStateStore) =>
        {
            var session = ResolvePortalSession(httpRequest, request.PortalSessionToken, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.CanonicalHost))
                return Results.BadRequest(new { error = "canonicalHost is required." });

            var serverKind = NormalizeTrustedServerKind(request.ServerKind);
            if (serverKind == "self-hosted")
                return Results.BadRequest(new { error = "self-hosted entries are separate from the trusted registry." });

            var server = cloudStateStore.UpsertTrustedServer(new TrustedServerRecord
            {
                CanonicalHost = request.CanonicalHost.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                    ? request.CanonicalHost.Trim()
                    : request.DisplayName.Trim(),
                ServerKind = serverKind,
                IsListed = request.IsListed ?? true,
                AcceptsPublicConnections = request.AcceptsPublicConnections ?? serverKind != "hybrid",
                ParticipatesInCloudSync = request.ParticipatesInCloudSync ?? true,
                RequiresHttps = request.RequiresHttps ?? true,
                IsActive = request.IsActive ?? true,
                Description = request.Description?.Trim() ?? string.Empty
            });

            var admission = cloudStateStore.RecordTrustedServerAdmission(
                server,
                "admit",
                session.DeviceId,
                session.FriendlyId,
                request.Reason);

            return Results.Json(new
            {
                trustedServer = server,
                admissionRecord = admission
            });
        });

        app.MapPost("/api/portal/trusted-servers/lifecycle", (
            [FromBody] TrustedServerLifecycleRequest request,
            HttpRequest httpRequest,
            PortalSessionService portalSessionService,
            ICloudStateStore cloudStateStore) =>
        {
            var session = ResolvePortalSession(httpRequest, request.PortalSessionToken, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.CanonicalHost))
                return Results.BadRequest(new { error = "canonicalHost is required." });

            if (string.IsNullOrWhiteSpace(request.Action))
                return Results.BadRequest(new { error = "action is required." });

            var action = request.Action.Trim().ToLowerInvariant();
            var normalizedKind = NormalizeTrustedServerKind(request.ServerKind);
            if (normalizedKind == "self-hosted")
                return Results.BadRequest(new { error = "self-hosted entries are separate from the trusted registry." });

            var host = request.CanonicalHost.Trim();
            var existing = cloudStateStore.FindTrustedServer(host);

            if (action is "revoke" or "reactivate" or "mark-seen" && existing is null)
                return Results.NotFound(new { error = "trusted server not found." });

            if (existing is not null && existing.IsTrustRoot && action == "revoke")
                return Results.BadRequest(new { error = "the trust root cannot be revoked." });

            TrustedServerRecord lifecycleUpdate = action switch
            {
                "admit" => UpsertLifecycleServer(existing, request, normalizedKind, host, isActive: true,
                    isListed: request.IsListed ?? true,
                    acceptsPublicConnections: request.AcceptsPublicConnections ?? normalizedKind != "hybrid",
                    participatesInCloudSync: request.ParticipatesInCloudSync ?? normalizedKind != "self-hosted",
                    requiresHttps: request.RequiresHttps ?? true,
                    lastSeenAtUtc: request.LastSeenAtUtc),
                "revoke" => UpsertLifecycleServer(existing!, request, existing!.ServerKind, host, isActive: false,
                    isListed: false,
                    acceptsPublicConnections: false,
                    participatesInCloudSync: false,
                    requiresHttps: existing!.RequiresHttps,
                    lastSeenAtUtc: existing!.LastSeenAtUtc),
                "reactivate" => UpsertLifecycleServer(existing!, request, existing!.ServerKind, host, isActive: true,
                    isListed: true,
                    acceptsPublicConnections: request.AcceptsPublicConnections ?? existing!.AcceptsPublicConnections,
                    participatesInCloudSync: request.ParticipatesInCloudSync ?? existing!.ParticipatesInCloudSync,
                    requiresHttps: request.RequiresHttps ?? existing!.RequiresHttps,
                    lastSeenAtUtc: request.LastSeenAtUtc ?? existing!.LastSeenAtUtc),
                "mark-seen" => UpsertLifecycleServer(existing!, request, existing!.ServerKind, host, isActive: existing!.IsActive,
                    isListed: existing!.IsListed,
                    acceptsPublicConnections: existing!.AcceptsPublicConnections,
                    participatesInCloudSync: existing!.ParticipatesInCloudSync,
                    requiresHttps: existing!.RequiresHttps,
                    lastSeenAtUtc: request.LastSeenAtUtc ?? DateTimeOffset.UtcNow),
                _ => null!
            };

            if (lifecycleUpdate is null)
                return Results.BadRequest(new { error = "action must be admit, revoke, reactivate, or mark-seen." });

            var updated = cloudStateStore.UpsertTrustedServer(lifecycleUpdate);
            var admission = cloudStateStore.RecordTrustedServerAdmission(
                updated,
                action,
                session.DeviceId,
                session.FriendlyId,
                request.Reason);

            return Results.Json(new
            {
                trustedServer = updated,
                admissionRecord = admission
            });
        });

        app.MapPost("/api/onboarding/self-hosted/validate", (
            [FromBody] ValidateSelfHostedRequest request) =>
        {
            var mode = NormalizeSelfHostedMode(request.ServerMode);
            var hostname = NormalizeOnboardingHost(request.ServerHost ?? request.ServerUrl);
            var isLocal = IsLocalSelfHostedTarget(hostname);
            var requiresHttps = mode == "self-hosted-hybrid";
            var allowsHttp = mode == "self-hosted" && isLocal;

            return Results.Json(new
            {
                serverMode = mode,
                registryBacked = false,
                canonicalHost = hostname,
                isLocalTarget = isLocal,
                requiresHttps,
                allowsHttp,
                acceptsPublicConnections = false,
                participatesInCloudSync = mode == "self-hosted-hybrid",
                trustGuidance = allowsHttp
                    ? "Use HTTP for local self-hosted operation."
                    : "Use HTTPS for self-hosted hybrid operation and keep it private from public connections.",
                notes = allowsHttp
                    ? new[]
                    {
                        "This path stays outside the trusted server registry.",
                        "Use a local hostname or IP address for the robot/app setup."
                    }
                    : new[]
                    {
                        "This path stays outside the trusted server registry.",
                        "Use a trusted HTTPS certificate even though the server is not publicly listed."
                }
            });
        });

        app.MapGet("/api/portal/trusted-servers/admissions/export", (
            HttpRequest request,
            PortalSessionService portalSessionService,
            ICloudStateStore cloudStateStore) =>
        {
            var session = ResolvePortalSession(request, null, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            var admissions = cloudStateStore.GetTrustedServerAdmissions();
            var payload = JsonSerializer.Serialize(new
            {
                exportedAtUtc = DateTimeOffset.UtcNow,
                exportedBy = session.FriendlyId,
                admissions
            }, new JsonSerializerOptions { WriteIndented = true });

            var fileName = $"openjibo-trusted-server-admissions-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json";
            return Results.File(
                Encoding.UTF8.GetBytes(payload),
                "application/json; charset=utf-8",
                fileName);
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
            ICloudStateStore cloudStateStore,
            HomeAssistantConnectionRegistry registry) =>
        {
            var session = ResolvePortalSession(request, null, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            var link = integrationStore.FindLinkForJibo(session.DeviceId, session.FriendlyId);
            return Results.Json(BuildDashboardPayload(session, link, registry, cloudStateStore, integrationStore));
        });

        app.MapGet("/api/portal/calendar-feeds", (
            HttpRequest request,
            PortalSessionService portalSessionService,
            IUserIntegrationStore integrationStore,
            ICloudStateStore cloudStateStore) =>
        {
            var session = ResolvePortalSession(request, null, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            return Results.Json(BuildCalendarFeedsPayload(cloudStateStore, integrationStore));
        });

        app.MapPut("/api/portal/calendar-feeds/{memberId}", (
            string memberId,
            [FromBody] UpsertMemberCalendarFeedRequest request,
            HttpRequest httpRequest,
            PortalSessionService portalSessionService,
            IUserIntegrationStore integrationStore,
            ICloudStateStore cloudStateStore) =>
        {
            var session = ResolvePortalSession(httpRequest, request.PortalSessionToken, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(memberId))
                return Results.BadRequest(new { error = "memberId is required." });

            if (!IcalUrlValidator.TryValidateHttpsPublicUrl(request.IcalUrl, out _, out var validationError))
                return Results.BadRequest(new { error = validationError });

            var loopId = ResolvePortalLoopId(cloudStateStore);
            var member = cloudStateStore.GetLoopMembers(loopId)
                .FirstOrDefault(item => item.Id.Equals(memberId, StringComparison.OrdinalIgnoreCase));
            if (member is null)
                return Results.NotFound(new { error = "Loop member not found." });

            var feed = integrationStore.UpsertMemberCalendarFeed(
                loopId,
                member.Id,
                request.IcalUrl!.Trim(),
                request.IsEnabled ?? true);

            return Results.Json(BuildMemberCalendarFeedStatus(member, feed));
        });

        app.MapDelete("/api/portal/calendar-feeds/{memberId}", (
            string memberId,
            HttpRequest request,
            PortalSessionService portalSessionService,
            IUserIntegrationStore integrationStore,
            ICloudStateStore cloudStateStore) =>
        {
            var session = ResolvePortalSession(request, null, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            var loopId = ResolvePortalLoopId(cloudStateStore);
            var removed = integrationStore.ClearMemberCalendarFeed(loopId, memberId);
            if (removed is null)
                return Results.NotFound(new { error = "No calendar feed is configured for that member." });

            return Results.Json(new { cleared = true, memberId });
        });

        app.MapPost("/api/portal/calendar-feeds/{memberId}/test", async (
            string memberId,
            [FromBody] TestMemberCalendarFeedRequest request,
            HttpRequest httpRequest,
            PortalSessionService portalSessionService,
            IUserIntegrationStore integrationStore,
            ICloudStateStore cloudStateStore,
            IcalCalendarFeedInspector feedInspector) =>
        {
            var session = ResolvePortalSession(httpRequest, request.PortalSessionToken, portalSessionService);
            if (session is null)
                return Results.Unauthorized();

            var loopId = ResolvePortalLoopId(cloudStateStore);
            var member = cloudStateStore.GetLoopMembers(loopId)
                .FirstOrDefault(item => item.Id.Equals(memberId, StringComparison.OrdinalIgnoreCase));
            if (member is null)
                return Results.NotFound(new { error = "Loop member not found." });

            var icalUrl = request.IcalUrl;
            if (string.IsNullOrWhiteSpace(icalUrl))
                icalUrl = integrationStore.FindMemberCalendarFeed(loopId, member.Id)?.IcalUrl;

            if (string.IsNullOrWhiteSpace(icalUrl))
                return Results.BadRequest(new { error = "iCal URL is required." });

            if (!IcalUrlValidator.TryValidateHttpsPublicUrl(icalUrl, out _, out var validationError))
                return Results.BadRequest(new { error = validationError });

            var probe = await feedInspector.ProbeAsync(icalUrl);
            if (!probe.Ok)
            {
                integrationStore.UpdateMemberCalendarFeedSyncStatus(loopId, member.Id, null, probe.Error);
                return Results.Json(new
                {
                    ok = false,
                    error = probe.Error,
                    host = IcalUrlValidator.TryGetSafeHost(icalUrl)
                });
            }

            integrationStore.UpdateMemberCalendarFeedSyncStatus(loopId, member.Id, DateTimeOffset.UtcNow, null);
            return Results.Json(new
            {
                ok = true,
                host = IcalUrlValidator.TryGetSafeHost(icalUrl),
                todayEventCount = probe.TodayEventCount,
                tomorrowEventCount = probe.TomorrowEventCount,
                sampleSummaries = probe.SampleSummaries
            });
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
            var trustedServerAdmissions = cloudStateStore.GetTrustedServerAdmissions();

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
                    identityEvidenceSignals = graph.EvidenceSignals.Count,
                    trustedServerAdmissions = trustedServerAdmissions.Count
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
                    trustedServerAdmissions = trustedServerAdmissions.Take(5).Select(admission => new
                    {
                        admission.CanonicalHost,
                        admission.ServerKind,
                        admission.Action,
                        admission.ActorFriendlyId,
                        admission.CreatedUtc,
                        admission.SignatureKeyId,
                        admission.Signature
                    }),
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
        HomeAssistantConnectionRegistry registry,
        ICloudStateStore cloudStateStore,
        IUserIntegrationStore integrationStore)
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
                : BuildHomeAssistantPayload(link, registry),
            calendarFeeds = BuildCalendarFeedsPayload(cloudStateStore, integrationStore)
        };
    }

    private static object BuildCalendarFeedsPayload(
        ICloudStateStore cloudStateStore,
        IUserIntegrationStore integrationStore)
    {
        var loopId = ResolvePortalLoopId(cloudStateStore);
        var feeds = integrationStore.GetMemberCalendarFeeds(loopId);
        var members = cloudStateStore.GetLoopMembers(loopId)
            .Where(static member =>
                !string.Equals(member.Type, "robot", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(member.Status, "removed", StringComparison.OrdinalIgnoreCase))
            .Select(member =>
            {
                var feed = feeds.FirstOrDefault(item =>
                    item.MemberId.Equals(member.Id, StringComparison.OrdinalIgnoreCase));
                return BuildMemberCalendarFeedStatus(member, feed);
            })
            .ToArray();

        return new
        {
            loopId,
            members
        };
    }

    private static object BuildMemberCalendarFeedStatus(
        LoopMemberRecord member,
        MemberCalendarFeedRecord? feed)
    {
        var displayName = !string.IsNullOrWhiteSpace(member.Nickname)
            ? member.Nickname
            : string.Join(' ', new[] { member.FirstName, member.LastName }
                .Where(static part => !string.IsNullOrWhiteSpace(part)));
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = member.Email ?? member.Id;

        return new
        {
            memberId = member.Id,
            displayName,
            firstName = member.FirstName,
            lastName = member.LastName,
            nickname = member.Nickname,
            configured = feed is not null && !string.IsNullOrWhiteSpace(feed.IcalUrl),
            isEnabled = feed?.IsEnabled ?? false,
            host = feed is null ? null : IcalUrlValidator.TryGetSafeHost(feed.IcalUrl),
            lastSuccessUtc = feed?.LastSuccessUtc,
            lastError = feed?.LastError,
            updatedUtc = feed?.UpdatedUtc
        };
    }

    private static string ResolvePortalLoopId(ICloudStateStore cloudStateStore)
    {
        return cloudStateStore.GetLoops().FirstOrDefault()?.LoopId ?? "openjibo-default-loop";
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

    private sealed record UpsertMemberCalendarFeedRequest(
        string? PortalSessionToken,
        string? IcalUrl,
        bool? IsEnabled);

    private sealed record TestMemberCalendarFeedRequest(
        string? PortalSessionToken,
        string? IcalUrl);

    private sealed record PortalLogoutRequest(string? PortalSessionToken);

    private sealed record RevokeIdentityGraphAnchorRequest(string? Anchor, string? PortalSessionToken);

    private sealed record UpsertTrustedServerRequest(
        string? PortalSessionToken,
        string? CanonicalHost,
        string? DisplayName,
        string? ServerKind,
        bool? IsListed,
        bool? AcceptsPublicConnections,
        bool? ParticipatesInCloudSync,
        bool? RequiresHttps,
        bool? IsActive,
        string? Reason,
        string? Description);

    private sealed record TrustedServerLifecycleRequest(
        string? PortalSessionToken,
        string? CanonicalHost,
        string? Action,
        string? ServerKind,
        string? DisplayName,
        bool? IsListed,
        bool? AcceptsPublicConnections,
        bool? ParticipatesInCloudSync,
        bool? RequiresHttps,
        bool? IsActive,
        string? Reason,
        string? Description,
        DateTimeOffset? LastSeenAtUtc);

    private sealed record ValidateSelfHostedRequest(
        string? ServerMode,
        string? ServerHost,
        string? ServerUrl);

    private sealed record VerifyIdentityGraphEvidenceBundleRequest(
        string? Envelope,
        string? PortalSessionToken,
        string[]? LocalRevokedAnchors);

    private static string NormalizeTrustedServerKind(string? serverKind)
    {
        var normalized = string.IsNullOrWhiteSpace(serverKind) ? "managed" : serverKind.Trim();
        normalized = normalized.Equals("hosted", StringComparison.OrdinalIgnoreCase) ? "managed" : normalized;
        normalized = normalized.Equals("self-hosted-hybrid", StringComparison.OrdinalIgnoreCase) ? "hybrid" : normalized;
        normalized = normalized.ToLowerInvariant();
        return normalized is "managed" or "hybrid" or "self-hosted" ? normalized : "managed";
    }

    private static string NormalizeSelfHostedMode(string? serverMode)
    {
        var normalized = string.IsNullOrWhiteSpace(serverMode) ? "self-hosted" : serverMode.Trim();
        normalized = normalized.Equals("local", StringComparison.OrdinalIgnoreCase) ? "self-hosted" : normalized;
        normalized = normalized.Equals("self-hosted-hybrid", StringComparison.OrdinalIgnoreCase)
            ? "self-hosted-hybrid"
            : normalized;
        normalized = normalized.ToLowerInvariant();
        return normalized is "self-hosted" or "self-hosted-hybrid" ? normalized : "self-hosted";
    }

    private static string NormalizeOnboardingHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return uri.Host;

        return trimmed.TrimEnd('/');
    }

    private static bool IsLocalSelfHostedTarget(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;

        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
               IPAddress.TryParse(host, out _);
    }

    private static TrustedServerRecord UpsertLifecycleServer(
        TrustedServerRecord? current,
        TrustedServerLifecycleRequest request,
        string serverKind,
        string canonicalHost,
        bool isActive,
        bool isListed,
        bool acceptsPublicConnections,
        bool participatesInCloudSync,
        bool requiresHttps,
        DateTimeOffset? lastSeenAtUtc)
    {
        return new TrustedServerRecord
        {
            ServerId = current?.ServerId ?? Guid.NewGuid().ToString("N"),
            CanonicalHost = canonicalHost,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? current?.DisplayName ?? canonicalHost
                : request.DisplayName.Trim(),
            ServerKind = serverKind,
            IsListed = isListed,
            AcceptsPublicConnections = acceptsPublicConnections,
            ParticipatesInCloudSync = participatesInCloudSync,
            RequiresHttps = requiresHttps,
            IsTrustRoot = current?.IsTrustRoot == true,
            IsActive = isActive,
            Description = request.Description?.Trim() ?? current?.Description ?? string.Empty,
            RegisteredAtUtc = current?.RegisteredAtUtc ?? DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            LastSeenAtUtc = lastSeenAtUtc ?? current?.LastSeenAtUtc
        };
    }
}
