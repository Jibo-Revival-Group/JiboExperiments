using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Holidays;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class InMemoryCloudStateStore : ICloudStateStore
{
    private const string CurrentSchemaVersion = "1";
    private const int IdentityGraphSnapshotVersion = 1;
    private const string IdentityGraphSignatureAlgorithm = "HMAC-SHA256";
    private const string IdentityGraphSignatureKeyId = "open-jibo-local-snapshot-v1";
    private const string IdentityGraphSigningKey = "open-jibo-local-identity-graph-development-key";
    private const string IdentityGraphAdmissionSignatureKeyId = "open-jibo-local-admission-v1";
    private const string IdentityGraphEvidenceBundleSignatureKeyId = "open-jibo-local-evidence-bundle-v1";
    private static long _nextUpdateIdSeed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static readonly JsonSerializerOptions PersistenceJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly List<BackupRecord> _backups = [];
    private readonly List<CalendarEventRecord> _calendarEvents = [];
    private readonly List<CommuteProfileRecord> _commuteProfiles = [];
    private readonly ConcurrentDictionary<string, DeviceRegistration> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GreetingPresenceRecord> _greetingPresences = [];

    private readonly IHolidayCalendarProvider _holidayCalendarProvider;
    private readonly List<HolidayRecord> _holidayOverrides = [];

    private readonly ConcurrentDictionary<string, KeyRequestRecord>
        _keyRequests = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<LoopMemberRecord> _loopMembers;

    private readonly List<LoopRecord> _loops;
    private readonly List<MediaRecord> _media = [];
    private readonly string? _ownerFirstName;
    private readonly string? _ownerLastName;
    private readonly List<PersonRecord> _people;

    private readonly ConcurrentDictionary<string, CloudSession>
        _sessionsByToken = new(StringComparer.OrdinalIgnoreCase);

    private readonly ISnapshotStore _snapshotStore;
    private readonly ConcurrentDictionary<string, string> _symmetricKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _syncRoot = new();
    private readonly List<UpdateManifest> _updates;
    private readonly List<UserRecord> _users;

    private AccountProfile _account = new();
    private DateTimeOffset? _lastLoadedUtc;
    private DateTimeOffset? _lastSavedUtc;
    private long _revision;
    private DeviceRegistration _robot;
    private RobotProfile _robotProfile;

    public InMemoryCloudStateStore(string? persistencePath = null)
        : this(new JsonFileSnapshotStore(persistencePath, PersistenceJsonOptions))
    {
    }

    public InMemoryCloudStateStore(ISnapshotStore snapshotStore)
        : this(snapshotStore, new NagerDateHolidayCalendarProvider())
    {
    }

    public InMemoryCloudStateStore(ISnapshotStore snapshotStore, IHolidayCalendarProvider holidayCalendarProvider,
        string? ownerFirstName = null, string? ownerLastName = null)
    {
        _snapshotStore = snapshotStore;
        _holidayCalendarProvider = holidayCalendarProvider;
        _ownerFirstName = ownerFirstName;
        _ownerLastName = ownerLastName;
        var bootstrapDeviceId = CreateBootstrapDeviceId();
        _robot = new DeviceRegistration
        {
            DeviceId = bootstrapDeviceId,
            RobotId = bootstrapDeviceId,
            FriendlyName = "OpenJibo Dev Robot",
            HostMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["api.jibo.com"] = "openjibo.com",
                ["api-socket.jibo.com"] = "openjibo.com",
                ["neo-hub.jibo.com"] = "openjibo.com"
            }
        };

        _devices[_robot.DeviceId] = _robot;
        _robotProfile = new RobotProfile
        {
            RobotId = _robot.RobotId,
            Payload = new Dictionary<string, object?>
            {
                ["SSID"] = "my-ssid",
                ["connectedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["platform"] = "12.10.0",
                ["serialNumber"] = _robot.DeviceId
            }
        };
        _loops =
        [
            new LoopRecord
            {
                OwnerAccountId = _account.AccountId,
                RobotId = _robot.RobotId,
                RobotFriendlyId = _robot.DeviceId
            }
        ];
        _loopMembers = [];
        _users = [];
        _people =
        [
            new PersonRecord
            {
                PersonId = "person-openjibo-owner",
                AccountId = _account.AccountId,
                LoopId = _loops[0].LoopId,
                RobotId = _robot.RobotId,
                DisplayName = $"{_account.FirstName} {_account.LastName}",
                Alias = _account.FirstName,
                IsPrimary = true
            },
            new PersonRecord
            {
                PersonId = "person-openjibo-household-member",
                AccountId = _account.AccountId,
                LoopId = _loops[0].LoopId,
                RobotId = _robot.RobotId,
                DisplayName = "OpenJibo Household Member",
                Alias = "Household Member",
                IsPrimary = false
            }
        ];

        _updates = [];
        ApplyConfiguredOwnerName();
        EnsureDefaultTopology();
        LoadPersistedState();
    }

    public PersistenceStateInfo GetPersistenceStateInfo()
    {
        return new PersistenceStateInfo(
            CurrentSchemaVersion,
            Interlocked.Read(ref _revision),
            _lastLoadedUtc,
            _lastSavedUtc);
    }

    public void LoadPersistedState()
    {
        var snapshot = _snapshotStore.Load<PersistentStateSnapshot>();
        if (snapshot is null) return;
        ApplySnapshot(snapshot);
    }

    public void SavePersistedState()
    {
        lock (_syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            var snapshot = CaptureSnapshot(now);
            _snapshotStore.Save(snapshot);
            _lastSavedUtc = now;
        }
    }

    public AccountProfile GetAccount()
    {
        return _account;
    }

    public DeviceRegistration GetRobot()
    {
        return _robot;
    }

    public RobotProfile GetRobotProfile()
    {
        return _robotProfile;
    }

    public DeviceRegistration GetOrCreateDevice(string deviceId, string? firmwareVersion, string? applicationVersion)
    {
        var device = _devices.AddOrUpdate(
            deviceId,
            _ => new DeviceRegistration
            {
                DeviceId = deviceId,
                RobotId = $"robot-{deviceId}",
                FriendlyName = "OpenJibo Registered Robot",
                FirmwareVersion = firmwareVersion,
                ApplicationVersion = applicationVersion
            },
            (_, current) => new DeviceRegistration
            {
                DeviceId = current.DeviceId,
                RobotId = current.RobotId,
                FriendlyName = current.FriendlyName,
                FirmwareVersion = firmwareVersion ?? current.FirmwareVersion,
                ApplicationVersion = applicationVersion ?? current.ApplicationVersion,
                CertificateThumbprint = current.CertificateThumbprint,
                IssuedIdentityId = current.IssuedIdentityId,
                BuildHash = current.BuildHash,
                ConfigHash = current.ConfigHash,
                HostMappings = new Dictionary<string, string>(current.HostMappings, StringComparer.OrdinalIgnoreCase)
            });

        TouchState();
        return device;
    }

    public DeviceRegistration? FindDeviceByFriendlyId(string friendlyId)
    {
        if (string.IsNullOrWhiteSpace(friendlyId)) return null;

        var trimmed = friendlyId.Trim();
        return _devices.Values.FirstOrDefault(device =>
            device.RobotId.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
            device.DeviceId.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public UserRecord? CreateUser(string email, string password, string? firstName, string? lastName)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return null;

        lock (_syncRoot)
        {
            if (_users.Any(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
                return null;

            var salt = GenerateSalt();
            var user = new UserRecord
            {
                Email = email.Trim().ToLowerInvariant(),
                PasswordHash = HashPassword(password, salt),
                Salt = salt,
                FirstName = firstName?.Trim() ?? string.Empty,
                LastName = lastName?.Trim() ?? string.Empty
            };
            _users.Add(user);
        }

        TouchState();
        return _users.Last(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
    }

    public UserRecord? AuthenticateUser(string email, string password)
    {
        var user = _users.FirstOrDefault(u => u.Email.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user is null) return null;

        var hash = HashPassword(password, user.Salt);
        return hash == user.PasswordHash ? user : null;
    }

    public UserRecord? GetUserById(string id)
    {
        return _users.FirstOrDefault(u => u.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public UserRecord? GetUserByEmail(string email)
    {
        return _users.FirstOrDefault(u => u.Email.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public UserRecord UpdateUser(string id, string? firstName, string? lastName, string? gender, long? birthday)
    {
        lock (_syncRoot)
        {
            var index = _users.FindIndex(u => u.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidOperationException($"User '{id}' not found.");

            var existing = _users[index];
            _users[index] = new UserRecord
            {
                Id = existing.Id,
                Email = existing.Email,
                PasswordHash = existing.PasswordHash,
                Salt = existing.Salt,
                FirstName = firstName?.Trim() ?? existing.FirstName,
                LastName = lastName?.Trim() ?? existing.LastName,
                Gender = gender ?? existing.Gender,
                Birthday = birthday ?? existing.Birthday,
                AccessKeyId = existing.AccessKeyId,
                SecretAccessKey = existing.SecretAccessKey,
                IsActive = existing.IsActive,
                CreatedUtc = existing.CreatedUtc
            };
        }

        TouchState();
        return _users.First(u => u.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public string IssueHubToken()
    {
        var token = $"hub-{_account.AccountId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        _sessionsByToken[token] = new CloudSession
        {
            Kind = "hub",
            AccountId = _account.AccountId,
            Token = token,
            DeviceId = _robot.DeviceId,
            Metadata = BuildSessionMetadata(_account.AccountId, _robot.DeviceId, ResolveDefaultLoopId())
        };

        TouchState();
        return token;
    }

    public string IssueRobotToken(string deviceId)
    {
        var token = $"token-{deviceId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        _sessionsByToken[token] = new CloudSession
        {
            Kind = "robot",
            AccountId = _account.AccountId,
            Token = token,
            DeviceId = deviceId,
            Metadata = BuildSessionMetadata(_account.AccountId, deviceId, ResolveDefaultLoopId())
        };

        TouchState();
        return token;
    }

    public CloudSession OpenSession(string kind, string? deviceId, string? token, string? hostName, string? path)
    {
        var resolvedDeviceId = deviceId ?? _robot.DeviceId;
        var resolvedLoopId = ResolveDefaultLoopId();
        var session = new CloudSession
        {
            Kind = kind,
            AccountId = _account.AccountId,
            DeviceId = resolvedDeviceId,
            Token = token,
            HostName = hostName,
            Path = path,
            Metadata = BuildSessionMetadata(_account.AccountId, resolvedDeviceId, resolvedLoopId)
        };

        if (string.IsNullOrWhiteSpace(token)) return session;

        _sessionsByToken[token] = session;
        TouchState();

        return session;
    }

    public CloudSession? FindSessionByToken(string token)
    {
        return _sessionsByToken.GetValueOrDefault(token);
    }

    public IReadOnlyList<LoopRecord> GetLoops()
    {
        return _loops.ToArray();
    }

    public IReadOnlyList<PersonRecord> GetPeople()
    {
        return _people.ToArray();
    }

    public IReadOnlyList<LoopMemberRecord> GetLoopMembers(string loopId)
    {
        return _loopMembers
            .Where(m => m.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
                        !m.Status.Equals("removed", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public IdentityGraphSnapshot GetIdentityGraph(string? loopId = null)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(loopId) ? ResolveDefaultLoopId() : loopId.Trim();
        var members = GetLoopMembers(resolvedLoopId);
        var people = _people
            .Where(person => person.LoopId.Equals(resolvedLoopId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var relationships = new List<IdentityGraphRelationship>();

        AddIdentityRelationship(relationships, _account.AccountId, "account", "owns", resolvedLoopId, "loop", resolvedLoopId);
        AddIdentityRelationship(relationships, resolvedLoopId, "loop", "served-by", _robot.RobotId, "robot", resolvedLoopId);
        AddIdentityRelationship(relationships, _robot.RobotId, "robot", "runs-on", _robot.DeviceId, "device", resolvedLoopId);

        foreach (var person in people)
        {
            AddIdentityRelationship(relationships, person.PersonId, "person",
                person.IsPrimary ? "primary-member-of" : "member-of", resolvedLoopId, "loop", resolvedLoopId);

            if (!string.IsNullOrWhiteSpace(person.AccountId))
                AddIdentityRelationship(relationships, person.PersonId, "person", "backed-by", person.AccountId, "account",
                    resolvedLoopId);

            if (!string.IsNullOrWhiteSpace(person.RobotId))
                AddIdentityRelationship(relationships, person.PersonId, "person",
                    person.IsPrimary ? "primary-user-of" : "known-by", person.RobotId, "robot", resolvedLoopId);
        }

        foreach (var member in members)
        {
            var subjectId = string.IsNullOrWhiteSpace(member.AccountId) ? member.Id : member.AccountId;
            AddIdentityRelationship(relationships, subjectId, member.Type, "member-of", resolvedLoopId, "loop",
                resolvedLoopId);

            if (string.Equals(member.Type, "robot", StringComparison.OrdinalIgnoreCase))
            {
                AddIdentityRelationship(relationships, subjectId, "robot", "runs-on", _robot.DeviceId, "device",
                    resolvedLoopId);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(member.AccountId))
                    AddIdentityRelationship(relationships, member.Id, "loop-member", "represented-by", member.AccountId,
                        "account", resolvedLoopId);

                if (member.FaceEnrolled)
                    AddIdentityRelationship(relationships, member.Id, "loop-member", "face-enrolled-with", _robot.RobotId,
                        "robot", resolvedLoopId);

                if (member.VoiceEnrolled)
                    AddIdentityRelationship(relationships, member.Id, "loop-member", "voice-enrolled-with",
                        _robot.RobotId, "robot", resolvedLoopId);

                if (member.IsChild && !string.IsNullOrWhiteSpace(member.LegalGuardianId))
                {
                    AddIdentityRelationship(relationships, member.Id, "loop-member", "dependent-of",
                        member.LegalGuardianId, "loop-member", resolvedLoopId);
                    AddIdentityRelationship(relationships, member.LegalGuardianId, "loop-member", "guardian-of",
                        member.Id, "loop-member", resolvedLoopId);
                }
            }
        }

        var evidenceSignals = BuildIdentityGraphEvidenceSignals(resolvedLoopId, _robot);
        var contentHash = ComputeIdentityGraphContentHash(_account.AccountId, resolvedLoopId, _robot, people, members,
            relationships, evidenceSignals);

        var signaturePayload = BuildIdentityGraphSignaturePayload(_account.AccountId, resolvedLoopId, contentHash);
        var signature = SignIdentityGraphPayload(signaturePayload);
        var admissionAssessment = BuildSignedIdentityGraphAdmissionAssessment(_account.AccountId, resolvedLoopId, contentHash, evidenceSignals);
        var evidenceBundle = BuildSignedIdentityGraphEvidenceBundle(_account.AccountId, resolvedLoopId, _robot,
            contentHash, signature, admissionAssessment, people.Length, members.Count, relationships.Count,
            evidenceSignals.Count, SummarizeIdentityGraphRelationshipKinds(relationships),
            SummarizeIdentityGraphEvidenceSignalKinds(evidenceSignals));

        return new IdentityGraphSnapshot
        {
            AccountId = _account.AccountId,
            LoopId = resolvedLoopId,
            RobotId = _robot.RobotId,
            DeviceId = _robot.DeviceId,
            SnapshotVersion = IdentityGraphSnapshotVersion,
            ContentHash = contentHash,
            SignatureAlgorithm = IdentityGraphSignatureAlgorithm,
            SignatureKeyId = IdentityGraphSignatureKeyId,
            SignaturePayload = signaturePayload,
            Signature = signature,
            AdmissionAssessment = admissionAssessment,
            EvidenceBundle = evidenceBundle,
            People = people,
            Members = members,
            Relationships = relationships,
            EvidenceSignals = evidenceSignals
        };
    }

    private static string ComputeIdentityGraphContentHash(string accountId, string loopId, DeviceRegistration robot,
        IReadOnlyCollection<PersonRecord> people, IReadOnlyCollection<LoopMemberRecord> members,
        IReadOnlyCollection<IdentityGraphRelationship> relationships,
        IReadOnlyCollection<IdentityGraphEvidenceSignal> evidenceSignals)
    {
        var lines = new List<string>
        {
            $"snapshot-version|{IdentityGraphSnapshotVersion}",
            $"account|{accountId}",
            $"loop|{loopId}",
            $"robot|{robot.RobotId}|device|{robot.DeviceId}|cert|{robot.CertificateThumbprint}|issued|{robot.IssuedIdentityId}|build|{robot.BuildHash}|config|{robot.ConfigHash}"
        };

        lines.AddRange(people
            .OrderBy(person => person.PersonId, StringComparer.OrdinalIgnoreCase)
            .Select(person =>
                $"person|{person.PersonId}|account|{person.AccountId}|robot|{person.RobotId}|primary|{person.IsPrimary}"));

        lines.AddRange(members
            .OrderBy(member => member.Id, StringComparer.OrdinalIgnoreCase)
            .Select(member =>
                $"member|{member.Id}|account|{member.AccountId}|type|{member.Type}|status|{member.Status}|child|{member.IsChild}|face|{member.FaceEnrolled}|voice|{member.VoiceEnrolled}|guardian|{member.LegalGuardianId}"));

        lines.AddRange(relationships
            .OrderBy(relationship => relationship.SubjectId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => relationship.SubjectKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => relationship.Relationship, StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => relationship.ObjectId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => relationship.ObjectKind, StringComparer.OrdinalIgnoreCase)
            .Select(relationship =>
                $"relationship|{relationship.SubjectId}|{relationship.SubjectKind}|{relationship.Relationship}|{relationship.ObjectId}|{relationship.ObjectKind}|{relationship.LoopId}"));

        lines.AddRange(evidenceSignals
            .OrderBy(signal => signal.SignalKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(signal => signal.SignalId, StringComparer.OrdinalIgnoreCase)
            .Select(signal =>
                $"evidence|{signal.SignalKind}|{signal.SignalId}|{signal.Value}|{signal.Role}|{signal.LoopId}"));

        var payload = string.Join('\n', lines);
        return ComputeSha256Hex(payload);
    }

    private static IReadOnlyList<string> SummarizeIdentityGraphRelationshipKinds(
        IEnumerable<IdentityGraphRelationship> relationships) =>
        relationships
            .GroupBy(relationship => relationship.Relationship, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}:{group.Count()}")
            .ToArray();

    private static IReadOnlyList<string> SummarizeIdentityGraphEvidenceSignalKinds(
        IEnumerable<IdentityGraphEvidenceSignal> evidenceSignals) =>
        evidenceSignals
            .GroupBy(signal => signal.SignalKind, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}:{group.Count()}")
            .ToArray();


    private static IReadOnlyList<IdentityGraphEvidenceSignal> BuildIdentityGraphEvidenceSignals(string loopId,
        DeviceRegistration robot)
    {
        var signals = new List<IdentityGraphEvidenceSignal>();

        AddIdentityEvidenceSignal(signals, "device-id", robot.DeviceId, robot.DeviceId, loopId);
        AddIdentityEvidenceSignal(signals, "robot-id", robot.RobotId, robot.RobotId, loopId);
        AddIdentityEvidenceSignal(signals, "firmware-version", robot.RobotId, robot.FirmwareVersion, loopId);
        AddIdentityEvidenceSignal(signals, "application-version", robot.RobotId, robot.ApplicationVersion, loopId);
        AddIdentityEvidenceSignal(signals, "certificate-thumbprint", robot.RobotId, robot.CertificateThumbprint, loopId);
        AddIdentityEvidenceSignal(signals, "issued-identity", robot.RobotId, robot.IssuedIdentityId, loopId);
        AddIdentityEvidenceSignal(signals, "build-hash", robot.RobotId, robot.BuildHash, loopId);
        AddIdentityEvidenceSignal(signals, "config-hash", robot.RobotId, robot.ConfigHash, loopId);

        foreach (var mapping in robot.HostMappings.OrderBy(mapping => mapping.Key, StringComparer.OrdinalIgnoreCase))
            AddIdentityEvidenceSignal(signals, "host-mapping", mapping.Key, mapping.Value, loopId);

        return signals;
    }

    private static void AddIdentityEvidenceSignal(ICollection<IdentityGraphEvidenceSignal> signals, string signalKind,
        string? signalId, string? value, string loopId)
    {
        if (string.IsNullOrWhiteSpace(signalId) || string.IsNullOrWhiteSpace(value)) return;

        signals.Add(new IdentityGraphEvidenceSignal
        {
            SignalKind = signalKind,
            SignalId = signalId.Trim(),
            Value = value.Trim(),
            LoopId = loopId
        });
    }


    private static IdentityGraphAdmissionAssessment BuildSignedIdentityGraphAdmissionAssessment(string accountId, string loopId,
        string contentHash, IReadOnlyCollection<IdentityGraphEvidenceSignal> evidenceSignals)
    {
        var assessment = BuildIdentityGraphAdmissionAssessment(evidenceSignals);
        var decisionPayload = BuildIdentityGraphAdmissionDecisionPayload(accountId, loopId, contentHash, assessment);
        var decisionHash = ComputeSha256Hex(decisionPayload);

        return new IdentityGraphAdmissionAssessment
        {
            PolicyVersion = assessment.PolicyVersion,
            Recommendation = assessment.Recommendation,
            Reasons = assessment.Reasons,
            RequiredEvidence = assessment.RequiredEvidence,
            SatisfiedEvidence = assessment.SatisfiedEvidence,
            BlockingEvidence = assessment.BlockingEvidence,
            RecommendedActions = assessment.RecommendedActions,
            DecisionPayload = decisionPayload,
            DecisionHash = decisionHash,
            SignatureAlgorithm = IdentityGraphSignatureAlgorithm,
            SignatureKeyId = IdentityGraphAdmissionSignatureKeyId,
            Signature = SignIdentityGraphPayload(decisionPayload)
        };
    }

    private static IdentityGraphAdmissionAssessment BuildIdentityGraphAdmissionAssessment(
        IReadOnlyCollection<IdentityGraphEvidenceSignal> evidenceSignals)
    {
        string[] requiredEvidence =
        [
            "device-id",
            "robot-id",
            "application-version",
            "host-mapping"
        ];

        var presentEvidence = evidenceSignals
            .Select(signal => signal.SignalKind)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingEvidence = requiredEvidence
            .Where(required => !presentEvidence.Contains(required))
            .ToArray();

        var satisfiedEvidence = requiredEvidence
            .Where(required => presentEvidence.Contains(required))
            .ToArray();
        var reasons = missingEvidence
            .Select(missing => $"missing-{missing}")
            .ToList();
        var blockingEvidence = missingEvidence
            .Select(missing => $"required:{missing}")
            .ToList();

        var untrustedHostMappings = evidenceSignals
            .Where(signal => signal.SignalKind.Equals("host-mapping", StringComparison.OrdinalIgnoreCase))
            .Where(signal => !IsTrustedOpenJiboHostMappingTarget(signal.Value))
            .Select(signal => $"host-mapping:{signal.SignalId}->{signal.Value}")
            .ToArray();

        if (untrustedHostMappings.Length > 0)
        {
            reasons.Add("untrusted-host-mapping-target");
            blockingEvidence.AddRange(untrustedHostMappings);
        }

        if (reasons.Count == 0)
        {
            return new IdentityGraphAdmissionAssessment
            {
                Recommendation = "admit",
                Reasons = ["required-corroborating-evidence-present"],
                RequiredEvidence = requiredEvidence,
                SatisfiedEvidence = satisfiedEvidence,
                RecommendedActions = ["record-signed-snapshot-for-peer-admission"]
            };
        }

        return new IdentityGraphAdmissionAssessment
        {
            Recommendation = "quarantine",
            Reasons = reasons,
            RequiredEvidence = requiredEvidence,
            SatisfiedEvidence = satisfiedEvidence,
            BlockingEvidence = blockingEvidence,
            RecommendedActions = BuildIdentityGraphRecommendedActions(missingEvidence, untrustedHostMappings)
        };
    }


    private static IReadOnlyList<string> BuildIdentityGraphRecommendedActions(
        IReadOnlyCollection<string> missingEvidence,
        IReadOnlyCollection<string> untrustedHostMappings)
    {
        var actions = new List<string>();

        if (missingEvidence.Contains("device-id", StringComparer.OrdinalIgnoreCase) ||
            missingEvidence.Contains("robot-id", StringComparer.OrdinalIgnoreCase))
            actions.Add("verify-robot-identity-before-admission");

        if (missingEvidence.Contains("application-version", StringComparer.OrdinalIgnoreCase))
            actions.Add("capture-current-open-jibo-application-version");

        if (missingEvidence.Contains("host-mapping", StringComparer.OrdinalIgnoreCase))
            actions.Add("record-open-jibo-host-mapping");

        if (untrustedHostMappings.Count > 0)
            actions.Add("redirect-legacy-host-mapping-to-open-jibo-target");

        if (actions.Count == 0)
            actions.Add("manual-review-required");

        return actions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsTrustedOpenJiboHostMappingTarget(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var target = value.Trim();
        return target.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               target.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
               target.Contains("openjibo", StringComparison.OrdinalIgnoreCase);
    }


    private static string BuildIdentityGraphAdmissionDecisionPayload(string accountId, string loopId, string contentHash,
        IdentityGraphAdmissionAssessment assessment)
    {
        var lines = new[]
        {
            $"policy-version|{assessment.PolicyVersion}",
            $"account|{accountId}",
            $"loop|{loopId}",
            $"content-hash|{contentHash}",
            $"recommendation|{assessment.Recommendation}",
            $"reasons|{string.Join(',', assessment.Reasons.Order(StringComparer.Ordinal))}",
            $"required-evidence|{string.Join(',', assessment.RequiredEvidence.Order(StringComparer.Ordinal))}",
            $"satisfied-evidence|{string.Join(',', assessment.SatisfiedEvidence.Order(StringComparer.Ordinal))}",
            $"blocking-evidence|{string.Join(',', assessment.BlockingEvidence.Order(StringComparer.Ordinal))}",
            $"recommended-actions|{string.Join(',', assessment.RecommendedActions.Order(StringComparer.Ordinal))}"
        };

        return string.Join('\n', lines);
    }


    private static IdentityGraphEvidenceBundle BuildSignedIdentityGraphEvidenceBundle(string accountId, string loopId,
        DeviceRegistration robot, string contentHash, string snapshotSignature,
        IdentityGraphAdmissionAssessment admissionAssessment, int peopleCount, int memberCount, int relationshipCount,
        int evidenceSignalCount, IReadOnlyList<string> relationshipKinds, IReadOnlyList<string> evidenceSignalKinds)
    {
        var payload = BuildIdentityGraphEvidenceBundlePayload(accountId, loopId, robot, contentHash, snapshotSignature,
            admissionAssessment, peopleCount, memberCount, relationshipCount, evidenceSignalCount, relationshipKinds,
            evidenceSignalKinds);
        var bundleHash = ComputeSha256Hex(payload);

        var signature = SignIdentityGraphPayload(payload);
        var envelope = BuildIdentityGraphEvidenceBundleEnvelope(payload, bundleHash, signature);

        return new IdentityGraphEvidenceBundle
        {
            AccountId = accountId,
            LoopId = loopId,
            RobotId = robot.RobotId,
            DeviceId = robot.DeviceId,
            SnapshotContentHash = contentHash,
            SnapshotSignature = snapshotSignature,
            AdmissionDecisionHash = admissionAssessment.DecisionHash,
            AdmissionSignature = admissionAssessment.Signature,
            AdmissionPolicyVersion = admissionAssessment.PolicyVersion,
            AdmissionRecommendation = admissionAssessment.Recommendation,
            AdmissionReasons = admissionAssessment.Reasons,
            RequiredEvidence = admissionAssessment.RequiredEvidence,
            SatisfiedEvidence = admissionAssessment.SatisfiedEvidence,
            RecommendedActions = admissionAssessment.RecommendedActions,
            PeopleCount = peopleCount,
            MemberCount = memberCount,
            RelationshipCount = relationshipCount,
            EvidenceSignalCount = evidenceSignalCount,
            RelationshipKinds = relationshipKinds,
            EvidenceSignalKinds = evidenceSignalKinds,
            BlockingEvidence = admissionAssessment.BlockingEvidence,
            Payload = payload,
            Envelope = envelope,
            BundleHash = bundleHash,
            SignatureAlgorithm = IdentityGraphSignatureAlgorithm,
            SignatureKeyId = IdentityGraphEvidenceBundleSignatureKeyId,
            Signature = signature
        };
    }


    private static string BuildIdentityGraphEvidenceBundleEnvelope(string payload, string bundleHash, string signature)
    {
        var lines = new[]
        {
            "envelope-version|identity-graph-evidence-envelope-v1",
            $"bundle-hash|{bundleHash}",
            $"bundle-signature-algorithm|{IdentityGraphSignatureAlgorithm}",
            $"bundle-signature-key-id|{IdentityGraphEvidenceBundleSignatureKeyId}",
            $"bundle-signature|{signature}",
            "payload-begin",
            payload,
            "payload-end"
        };

        return string.Join('\n', lines);
    }

    private static string BuildIdentityGraphEvidenceBundlePayload(string accountId, string loopId, DeviceRegistration robot,
        string contentHash, string snapshotSignature, IdentityGraphAdmissionAssessment admissionAssessment,
        int peopleCount, int memberCount, int relationshipCount, int evidenceSignalCount,
        IReadOnlyList<string> relationshipKinds, IReadOnlyList<string> evidenceSignalKinds)
    {
        var lines = new[]
        {
            "bundle-version|identity-graph-evidence-bundle-v1",
            $"account|{accountId}",
            $"loop|{loopId}",
            $"robot|{robot.RobotId}",
            $"device|{robot.DeviceId}",
            $"people-count|{peopleCount}",
            $"member-count|{memberCount}",
            $"relationship-count|{relationshipCount}",
            $"evidence-signal-count|{evidenceSignalCount}",
            $"relationship-kinds|{string.Join(',', relationshipKinds)}",
            $"evidence-signal-kinds|{string.Join(',', evidenceSignalKinds)}",
            $"snapshot-version|{IdentityGraphSnapshotVersion}",
            $"snapshot-content-hash|{contentHash}",
            $"snapshot-signature-key-id|{IdentityGraphSignatureKeyId}",
            $"snapshot-signature|{snapshotSignature}",
            $"admission-policy-version|{admissionAssessment.PolicyVersion}",
            $"admission-recommendation|{admissionAssessment.Recommendation}",
            $"admission-reasons|{string.Join(',', admissionAssessment.Reasons.Order(StringComparer.Ordinal))}",
            $"admission-required-evidence|{string.Join(',', admissionAssessment.RequiredEvidence.Order(StringComparer.Ordinal))}",
            $"admission-satisfied-evidence|{string.Join(',', admissionAssessment.SatisfiedEvidence.Order(StringComparer.Ordinal))}",
            $"admission-blocking-evidence|{string.Join(',', admissionAssessment.BlockingEvidence.Order(StringComparer.Ordinal))}",
            $"admission-recommended-actions|{string.Join(',', admissionAssessment.RecommendedActions.Order(StringComparer.Ordinal))}",
            $"admission-decision-hash|{admissionAssessment.DecisionHash}",
            $"admission-signature-key-id|{admissionAssessment.SignatureKeyId}",
            $"admission-signature|{admissionAssessment.Signature}"
        };

        return string.Join('\n', lines);
    }

    private static string BuildIdentityGraphSignaturePayload(string accountId, string loopId, string contentHash)
    {
        return $"{IdentityGraphSnapshotVersion}|{accountId}|{loopId}|{contentHash}";
    }

    private static string SignIdentityGraphPayload(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(IdentityGraphSigningKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static void AddIdentityRelationship(ICollection<IdentityGraphRelationship> relationships, string? subjectId,
        string subjectKind, string relationship, string? objectId, string objectKind, string loopId)
    {
        if (string.IsNullOrWhiteSpace(subjectId) || string.IsNullOrWhiteSpace(objectId)) return;

        var trimmedSubjectId = subjectId.Trim();
        var trimmedObjectId = objectId.Trim();
        if (relationships.Any(existing =>
                existing.SubjectId.Equals(trimmedSubjectId, StringComparison.OrdinalIgnoreCase) &&
                existing.SubjectKind.Equals(subjectKind, StringComparison.OrdinalIgnoreCase) &&
                existing.Relationship.Equals(relationship, StringComparison.OrdinalIgnoreCase) &&
                existing.ObjectId.Equals(trimmedObjectId, StringComparison.OrdinalIgnoreCase) &&
                existing.ObjectKind.Equals(objectKind, StringComparison.OrdinalIgnoreCase) &&
                existing.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase)))
            return;

        relationships.Add(new IdentityGraphRelationship
        {
            SubjectId = trimmedSubjectId,
            SubjectKind = subjectKind,
            Relationship = relationship,
            ObjectId = trimmedObjectId,
            ObjectKind = objectKind,
            LoopId = loopId
        });
    }

    public LoopMemberRecord AddLoopMember(string loopId, string? accountId, string? email, string? firstName,
        string? lastName, string? gender, long? birthday, bool isChild, string type, string? legalGuardianId = null)
    {
        var member = new LoopMemberRecord
        {
            LoopId = loopId,
            AccountId = accountId,
            Email = email?.Trim(),
            FirstName = firstName?.Trim(),
            LastName = lastName?.Trim(),
            Gender = gender,
            Birthday = birthday,
            IsChild = isChild,
            Type = type,
            Status = "active",
            LegalGuardianId = legalGuardianId?.Trim()
        };
        lock (_syncRoot)
        {
            _loopMembers.Add(member);
        }

        TouchState();
        return member;
    }

    public LoopMemberRecord UpdateLoopMember(string loopId, string memberId, string? firstName, string? lastName,
        string? gender, long? birthday, bool isChild, string? nickname, string? phoneticName)
    {
        lock (_syncRoot)
        {
            var index = _loopMembers.FindIndex(m =>
                m.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
                m.Id.Equals(memberId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidOperationException($"Member '{memberId}' not found in loop '{loopId}'.");

            var existing = _loopMembers[index];
            _loopMembers[index] = new LoopMemberRecord
            {
                Id = existing.Id,
                LoopId = existing.LoopId,
                AccountId = existing.AccountId,
                Email = existing.Email,
                FirstName = firstName?.Trim() ?? existing.FirstName,
                LastName = lastName?.Trim() ?? existing.LastName,
                Gender = gender ?? existing.Gender,
                Birthday = birthday ?? existing.Birthday,
                IsChild = isChild,
                PhoneNumber = existing.PhoneNumber,
                Status = existing.Status,
                Type = existing.Type,
                Nickname = nickname ?? existing.Nickname,
                PhoneticName = phoneticName ?? existing.PhoneticName,
                FaceEnrolled = existing.FaceEnrolled,
                VoiceEnrolled = existing.VoiceEnrolled,
                LegalGuardianId = existing.LegalGuardianId,
                AgreementId = existing.AgreementId,
                CreatedUtc = existing.CreatedUtc
            };
        }

        TouchState();
        return GetLoopMember(loopId, memberId);
    }

    public bool RemoveLoopMember(string loopId, string memberId)
    {
        lock (_syncRoot)
        {
            var index = _loopMembers.FindIndex(m =>
                m.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
                m.Id.Equals(memberId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;

            var existing = _loopMembers[index];
            _loopMembers[index] = new LoopMemberRecord
            {
                Id = existing.Id,
                LoopId = existing.LoopId,
                AccountId = existing.AccountId,
                Email = existing.Email,
                FirstName = existing.FirstName,
                LastName = existing.LastName,
                Gender = existing.Gender,
                Birthday = existing.Birthday,
                IsChild = existing.IsChild,
                PhoneNumber = existing.PhoneNumber,
                Status = "removed",
                Type = existing.Type,
                Nickname = existing.Nickname,
                PhoneticName = existing.PhoneticName,
                FaceEnrolled = existing.FaceEnrolled,
                VoiceEnrolled = existing.VoiceEnrolled,
                LegalGuardianId = existing.LegalGuardianId,
                AgreementId = existing.AgreementId,
                CreatedUtc = existing.CreatedUtc
            };
        }

        TouchState();
        return true;
    }

    public LoopMemberRecord SetMemberEnrollment(string loopId, string memberId, bool? face, bool? voice)
    {
        lock (_syncRoot)
        {
            var index = _loopMembers.FindIndex(m =>
                m.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
                m.Id.Equals(memberId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidOperationException($"Member '{memberId}' not found in loop '{loopId}'.");

            var existing = _loopMembers[index];
            _loopMembers[index] = new LoopMemberRecord
            {
                Id = existing.Id,
                LoopId = existing.LoopId,
                AccountId = existing.AccountId,
                Email = existing.Email,
                FirstName = existing.FirstName,
                LastName = existing.LastName,
                Gender = existing.Gender,
                Birthday = existing.Birthday,
                IsChild = existing.IsChild,
                PhoneNumber = existing.PhoneNumber,
                Status = existing.Status,
                Type = existing.Type,
                Nickname = existing.Nickname,
                PhoneticName = existing.PhoneticName,
                FaceEnrolled = face ?? existing.FaceEnrolled,
                VoiceEnrolled = voice ?? existing.VoiceEnrolled,
                LegalGuardianId = existing.LegalGuardianId,
                AgreementId = existing.AgreementId,
                CreatedUtc = existing.CreatedUtc
            };
        }

        TouchState();
        return GetLoopMember(loopId, memberId);
    }

    public IReadOnlyList<UpdateManifest> ListUpdates(string? subsystem = null, string? filter = null)
    {
        return _updates
            .Where(update =>
                subsystem is null || update.Subsystem.Equals(subsystem, StringComparison.OrdinalIgnoreCase))
            .Where(update => filter is null || string.Equals(update.Filter, filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public UpdateManifest? GetUpdateFrom(string? subsystem, string? fromVersion, string? filter)
    {
        return ListUpdates(subsystem, filter)
            .FirstOrDefault(update =>
                fromVersion is null || update.FromVersion.Equals(fromVersion, StringComparison.OrdinalIgnoreCase));
    }

    public UpdateManifest CreateUpdate(string? fromVersion, string? toVersion, string? changes, string? shaHash,
        long? length, string? subsystem, string? filter, IDictionary<string, object?>? dependencies)
    {
        var updateId = $"upd-{Interlocked.Increment(ref _nextUpdateIdSeed)}";
        var update = new UpdateManifest
        {
            UpdateId = updateId,
            FromVersion = fromVersion ?? "unknown",
            ToVersion = toVersion ?? fromVersion ?? "unknown",
            Changes = changes ?? string.Empty,
            Url = $"https://api.jibo.com/update/{updateId}",
            ShaHash = shaHash ?? "fake-sha-hash",
            Length = length ?? 0,
            Subsystem = subsystem ?? "unknown",
            Filter = filter
        };

        _updates.Add(update);
        TouchState();
        return update;
    }

    public UpdateManifest RemoveUpdate(string? updateId)
    {
        var existing = _updates.FirstOrDefault(update => update.UpdateId == updateId);
        if (existing is null)
            return new UpdateManifest
            {
                UpdateId = updateId ?? "unknown-update",
                Changes = "Update not found",
                Url = "https://api.jibo.com/update/missing",
                ShaHash = "missing",
                Subsystem = "unknown"
            };

        _updates.Remove(existing);
        TouchState();
        return existing;
    }

    public IReadOnlyList<MediaRecord> ListMedia(IReadOnlyList<string>? loopIds = null, long? after = null,
        long? before = null)
    {
        return _media
            .Where(item => !item.IsDeleted)
            .Where(item => loopIds is null || loopIds.Count == 0 || loopIds.Contains(item.LoopId))
            .Where(item => after is null || item.CreatedUtc.ToUnixTimeMilliseconds() > after)
            .Where(item => before is null || item.CreatedUtc.ToUnixTimeMilliseconds() < before)
            .ToArray();
    }

    public IReadOnlyList<MediaRecord> GetMedia(IReadOnlyList<string> paths)
    {
        return _media.Where(item => paths.Contains(item.Path)).ToArray();
    }

    public IReadOnlyList<MediaRecord> RemoveMedia(IReadOnlyList<string> paths)
    {
        var replacements = new List<MediaRecord>();
        for (var i = 0; i < _media.Count; i++)
        {
            if (!paths.Contains(_media[i].Path)) continue;

            var updated = new MediaRecord
            {
                Path = _media[i].Path,
                CreatedUtc = _media[i].CreatedUtc,
                MediaType = _media[i].MediaType,
                Reference = _media[i].Reference,
                AccountId = _media[i].AccountId,
                LoopId = _media[i].LoopId,
                Url = _media[i].Url,
                IsEncrypted = _media[i].IsEncrypted,
                IsDeleted = true,
                Meta = _media[i].Meta
            };

            _media[i] = updated;
            replacements.Add(updated);
        }

        if (replacements.Count > 0) TouchState();

        return replacements;
    }

    public MediaRecord CreateMedia(string loopId, string path, string type, string reference, bool isEncrypted,
        IDictionary<string, object?>? meta)
    {
        var item = new MediaRecord
        {
            Path = path,
            MediaType = type,
            Reference = reference,
            AccountId = _account.AccountId,
            LoopId = loopId,
            Url = $"https://api.jibo.com/media/{Uri.EscapeDataString(path)}",
            IsEncrypted = isEncrypted,
            Meta = meta ?? new Dictionary<string, object?>()
        };

        var existingIndex =
            _media.FindIndex(existing => existing.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            _media[existingIndex] = item;
        else
            _media.Add(item);

        TouchState();
        return item;
    }

    public IReadOnlyList<BackupRecord> GetBackups()
    {
        return _backups.ToArray();
    }

    public BackupRecord CreateBackup(string loopId, string name)
    {
        var snapshotJson = JsonSerializer.Serialize(CaptureSnapshot(DateTimeOffset.UtcNow), PersistenceJsonOptions);
        var backup = new BackupRecord
        {
            LoopId = string.IsNullOrWhiteSpace(loopId) ? null : loopId.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? "backup" : name.Trim(),
            SnapshotJson = snapshotJson
        };

        _backups.Add(backup);
        TouchState();
        return backup;
    }

    public BackupRecord? RestoreBackup(string? backupId = null)
    {
        BackupRecord? backup;
        lock (_syncRoot)
        {
            backup = string.IsNullOrWhiteSpace(backupId)
                ? _backups.OrderByDescending(item => item.CreatedUtc).FirstOrDefault()
                : _backups.FirstOrDefault(item => item.BackupId.Equals(backupId, StringComparison.OrdinalIgnoreCase));

            if (backup is null || string.IsNullOrWhiteSpace(backup.SnapshotJson))
                return null;

            PersistentStateSnapshot? snapshot;
            try
            {
                snapshot = JsonSerializer.Deserialize<PersistentStateSnapshot>(backup.SnapshotJson,
                    PersistenceJsonOptions);
            }
            catch
            {
                snapshot = null;
            }

            if (snapshot is null)
                return null;

            ApplySnapshot(snapshot);
            Interlocked.Increment(ref _revision);
        }

        SavePersistedState();
        return backup;
    }

    public IReadOnlyList<CalendarEventRecord> GetCalendarEvents(string? loopId = null)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(loopId) ? ResolveDefaultLoopId() : loopId.Trim();
        return _calendarEvents
            .Where(calendarEvent => calendarEvent.IsEnabled)
            .Where(calendarEvent =>
                string.Equals(calendarEvent.LoopId, resolvedLoopId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(calendarEvent => calendarEvent.Date)
            .ThenBy(calendarEvent => calendarEvent.TimeLabel ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(calendarEvent => calendarEvent.Summary, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public CalendarEventRecord UpsertCalendarEvent(CalendarEventRecord calendarEvent)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(calendarEvent.LoopId)
            ? ResolveDefaultLoopId()
            : calendarEvent.LoopId.Trim();
        var normalizedId = string.IsNullOrWhiteSpace(calendarEvent.Id)
            ? $"calendar-{resolvedLoopId}-{Slugify(calendarEvent.Summary)}"
            : calendarEvent.Id.Trim();

        var resolvedCalendarEvent = new CalendarEventRecord
        {
            Id = normalizedId,
            LoopId = resolvedLoopId,
            Summary =
                string.IsNullOrWhiteSpace(calendarEvent.Summary) ? "Calendar event" : calendarEvent.Summary.Trim(),
            TimeLabel = string.IsNullOrWhiteSpace(calendarEvent.TimeLabel) ? null : calendarEvent.TimeLabel.Trim(),
            Date = calendarEvent.Date,
            EndDate = calendarEvent.EndDate,
            IsAllDay = calendarEvent.IsAllDay,
            IsEnabled = calendarEvent.IsEnabled,
            Source = string.IsNullOrWhiteSpace(calendarEvent.Source) ? "manual" : calendarEvent.Source.Trim(),
            MemberId = calendarEvent.MemberId,
            Created = calendarEvent.Created
        };

        var existingIndex = _calendarEvents.FindIndex(existing =>
            string.Equals(existing.Id, normalizedId, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
            _calendarEvents[existingIndex] = resolvedCalendarEvent;
        else
            _calendarEvents.Add(resolvedCalendarEvent);

        TouchState();
        return resolvedCalendarEvent;
    }

    public IReadOnlyList<GreetingPresenceRecord> GetGreetingPresences(string? loopId = null)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(loopId) ? ResolveDefaultLoopId() : loopId.Trim();
        return _greetingPresences
            .Where(greeting =>
                string.Equals(greeting.LoopId, resolvedLoopId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static greeting => greeting.LastGreetedUtc ?? greeting.LastSeenUtc)
            .ThenByDescending(static greeting => greeting.UpdatedUtc)
            .ToArray();
    }

    public GreetingPresenceRecord UpsertGreetingPresence(GreetingPresenceRecord greetingPresence)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(greetingPresence.LoopId)
            ? ResolveDefaultLoopId()
            : greetingPresence.LoopId.Trim();
        var resolvedPersonId = string.IsNullOrWhiteSpace(greetingPresence.PersonId)
            ? greetingPresence.SpeakerId?.Trim() ?? "unknown-person"
            : greetingPresence.PersonId.Trim();
        var normalizedId = string.IsNullOrWhiteSpace(greetingPresence.Id)
            ? $"greeting-presence-{resolvedLoopId}-{Slugify(resolvedPersonId)}"
            : greetingPresence.Id.Trim();
        var now = DateTimeOffset.UtcNow;
        var resolvedPresence = new GreetingPresenceRecord
        {
            Id = normalizedId,
            AccountId = string.IsNullOrWhiteSpace(greetingPresence.AccountId)
                ? _account.AccountId
                : greetingPresence.AccountId.Trim(),
            LoopId = resolvedLoopId,
            PersonId = resolvedPersonId,
            SpeakerId =
                string.IsNullOrWhiteSpace(greetingPresence.SpeakerId) ? null : greetingPresence.SpeakerId.Trim(),
            PreferredName = string.IsNullOrWhiteSpace(greetingPresence.PreferredName)
                ? null
                : greetingPresence.PreferredName.Trim(),
            LastSeenUtc = greetingPresence.LastSeenUtc == default ? now : greetingPresence.LastSeenUtc,
            LastGreetedUtc = greetingPresence.LastGreetedUtc,
            LastGreetingRoute = string.IsNullOrWhiteSpace(greetingPresence.LastGreetingRoute)
                ? null
                : greetingPresence.LastGreetingRoute.Trim(),
            LastGreetingIntent = string.IsNullOrWhiteSpace(greetingPresence.LastGreetingIntent)
                ? null
                : greetingPresence.LastGreetingIntent.Trim(),
            CreatedUtc = greetingPresence.CreatedUtc == default ? now : greetingPresence.CreatedUtc,
            UpdatedUtc = now
        };

        var existingIndex = _greetingPresences.FindIndex(existing =>
            string.Equals(existing.LoopId, resolvedLoopId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.PersonId, resolvedPersonId, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
            _greetingPresences[existingIndex] = resolvedPresence;
        else
            _greetingPresences.Add(resolvedPresence);

        TouchState();
        return resolvedPresence;
    }

    public bool ShouldCreateSymmetricKey(string loopId)
    {
        return !_symmetricKeys.ContainsKey(loopId);
    }

    public string GetOrCreateSymmetricKey(string loopId)
    {
        if (_symmetricKeys.TryGetValue(loopId, out var existing)) return existing;

        var key = Convert.ToBase64String(Encoding.UTF8.GetBytes($"open-jibo-symmetric-key:{loopId}"));
        if (!_symmetricKeys.TryAdd(loopId, key)) return _symmetricKeys[loopId];

        TouchState();
        return key;
    }

    public KeyRequestRecord CreateKeyRequest(string loopId, string publicKey)
    {
        var record = new KeyRequestRecord
        {
            RequestId = $"req-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            LoopId = loopId,
            PublicKey = publicKey
        };

        _keyRequests[record.RequestId] = record;
        TouchState();
        return record;
    }

    public KeyRequestRecord GetKeyRequest(string loopId, string? requestId, string? publicKey)
    {
        if (!string.IsNullOrWhiteSpace(requestId) && _keyRequests.TryGetValue(requestId, out var record)) return record;

        return new KeyRequestRecord
        {
            RequestId = requestId ?? "unknown-request",
            LoopId = loopId,
            PublicKey = publicKey ?? string.Empty
        };
    }

    public IReadOnlyList<KeyRequestRecord> GetIncomingKeyRequests()
    {
        return [];
    }

    public IReadOnlyList<KeyRequestRecord> GetBinaryRequests()
    {
        return [];
    }

    public IReadOnlyList<HolidayRecord> GetHolidays(string? loopId = null)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(loopId) ? ResolveDefaultLoopId() : loopId.Trim();
        var years = new[] { DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Year + 1 };

        var systemHolidays = years
            .SelectMany(year => _holidayCalendarProvider.GetPublicHolidays(null, year))
            .Where(holiday => holiday.IsEnabled)
            .Select(holiday => new HolidayRecord
            {
                Id = holiday.Id,
                EventId = holiday.EventId,
                Name = holiday.Name,
                Category = holiday.Category,
                Subcategory = holiday.Subcategory,
                LoopId = resolvedLoopId,
                MemberId = holiday.MemberId,
                IsEnabled = true,
                Date = holiday.Date,
                EndDate = holiday.EndDate,
                Source = holiday.Source,
                CountryCode = holiday.CountryCode,
                Created = holiday.Created
            })
            .ToList();

        var overrides = _holidayOverrides
            .Where(holiday => string.Equals(holiday.LoopId, resolvedLoopId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var overrideHoliday in overrides.Where(overrideHoliday =>
                     !string.IsNullOrWhiteSpace(overrideHoliday.EventId)))
            systemHolidays.RemoveAll(systemHoliday =>
                string.Equals(systemHoliday.EventId, overrideHoliday.EventId, StringComparison.OrdinalIgnoreCase));

        return systemHolidays
            .Concat(overrides.Where(holiday => holiday.IsEnabled))
            .OrderBy(holiday => holiday.Date)
            .ThenBy(holiday => holiday.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<CommuteProfileRecord> GetCommuteProfiles(string? loopId = null)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(loopId) ? ResolveDefaultLoopId() : loopId.Trim();
        return _commuteProfiles
            .Where(commute => commute.IsEnabled)
            .Where(commute => string.Equals(commute.LoopId, resolvedLoopId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static commute => commute.IsComplete)
            .ThenByDescending(static commute => commute.Updated)
            .ToArray();
    }

    public CommuteProfileRecord UpsertCommuteProfile(CommuteProfileRecord commuteProfile)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(commuteProfile.LoopId)
            ? ResolveDefaultLoopId()
            : commuteProfile.LoopId.Trim();
        var normalizedId = string.IsNullOrWhiteSpace(commuteProfile.Id)
            ? $"commute-{resolvedLoopId}"
            : commuteProfile.Id.Trim();

        var resolvedProfile = new CommuteProfileRecord
        {
            Id = normalizedId,
            LoopId = resolvedLoopId,
            MemberId = string.IsNullOrWhiteSpace(commuteProfile.MemberId) ? null : commuteProfile.MemberId.Trim(),
            IsEnabled = commuteProfile.IsEnabled,
            IsComplete = commuteProfile.IsComplete,
            Mode = string.IsNullOrWhiteSpace(commuteProfile.Mode) ? "driving" : commuteProfile.Mode.Trim(),
            WorkHour = commuteProfile.WorkHour,
            WorkMinute = commuteProfile.WorkMinute,
            OriginName = string.IsNullOrWhiteSpace(commuteProfile.OriginName) ? null : commuteProfile.OriginName.Trim(),
            DestinationName = string.IsNullOrWhiteSpace(commuteProfile.DestinationName)
                ? null
                : commuteProfile.DestinationName.Trim(),
            TypicalDurationMinutes = commuteProfile.TypicalDurationMinutes > 0
                ? commuteProfile.TypicalDurationMinutes
                : 25,
            Created = commuteProfile.Created == default ? DateTimeOffset.UtcNow : commuteProfile.Created,
            Updated = DateTimeOffset.UtcNow
        };

        var existingIndex = _commuteProfiles.FindIndex(existing =>
            string.Equals(existing.Id, normalizedId, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
            _commuteProfiles[existingIndex] = resolvedProfile;
        else
            _commuteProfiles.Add(resolvedProfile);

        TouchState();
        return resolvedProfile;
    }

    public HolidayRecord UpsertHoliday(HolidayRecord holiday)
    {
        var resolvedLoopId = string.IsNullOrWhiteSpace(holiday.LoopId) ? ResolveDefaultLoopId() : holiday.LoopId.Trim();
        var normalizedEventId = string.IsNullOrWhiteSpace(holiday.EventId)
            ? $"holiday-{resolvedLoopId}-{Slugify(holiday.Name)}"
            : holiday.EventId.Trim();
        var normalizedId = string.IsNullOrWhiteSpace(holiday.Id) ? normalizedEventId : holiday.Id.Trim();
        var resolvedHoliday = new HolidayRecord
        {
            Id = normalizedId,
            EventId = normalizedEventId,
            Name = string.IsNullOrWhiteSpace(holiday.Name) ? "Holiday" : holiday.Name.Trim(),
            Category = string.IsNullOrWhiteSpace(holiday.Category) ? "holiday" : holiday.Category.Trim(),
            Subcategory = holiday.Subcategory,
            LoopId = resolvedLoopId,
            MemberId = holiday.MemberId,
            IsEnabled = holiday.IsEnabled,
            Date = holiday.Date,
            EndDate = holiday.EndDate,
            Source = string.IsNullOrWhiteSpace(holiday.Source) ? "manual" : holiday.Source.Trim(),
            CountryCode = string.IsNullOrWhiteSpace(holiday.CountryCode) ? "US" : holiday.CountryCode.Trim(),
            Created = holiday.Created
        };

        var existingIndex = _holidayOverrides.FindIndex(existing =>
            string.Equals(existing.LoopId, resolvedLoopId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.EventId, normalizedEventId, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
            _holidayOverrides[existingIndex] = resolvedHoliday;
        else
            _holidayOverrides.Add(resolvedHoliday);

        TouchState();
        return resolvedHoliday;
    }

    public void UpdateRobot(DeviceRegistration registration)
    {
        var oldRobotId = _robot.RobotId;
        _robot = registration;
        _devices[registration.DeviceId] = registration;
        _robotProfile = new RobotProfile
        {
            RobotId = registration.RobotId,
            Payload = new Dictionary<string, object?>
            {
                ["SSID"] = "my-ssid",
                ["connectedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["platform"] = registration.FirmwareVersion ?? "12.10.0",
                ["serialNumber"] = registration.DeviceId
            },
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        for (var i = 0; i < _people.Count; i++)
        {
            var person = _people[i];
            if (!string.Equals(person.RobotId, oldRobotId, StringComparison.OrdinalIgnoreCase)) continue;

            _people[i] = new PersonRecord
            {
                PersonId = person.PersonId,
                AccountId = person.AccountId,
                LoopId = person.LoopId,
                RobotId = registration.RobotId,
                DisplayName = person.DisplayName,
                Alias = person.Alias,
                IsPrimary = person.IsPrimary,
                CreatedUtc = person.CreatedUtc,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
        }

        for (var i = 0; i < _loopMembers.Count; i++)
        {
            var member = _loopMembers[i];
            if (string.Equals(member.Type, "robot", StringComparison.OrdinalIgnoreCase) ||
                (member.AccountId != null && member.AccountId.Equals(oldRobotId, StringComparison.OrdinalIgnoreCase)))
                _loopMembers[i] = new LoopMemberRecord
                {
                    Id = member.Id,
                    LoopId = member.LoopId,
                    AccountId = string.Equals(member.AccountId, oldRobotId, StringComparison.OrdinalIgnoreCase)
                        ? registration.RobotId
                        : member.AccountId,
                    Email = member.Email,
                    FirstName = member.FirstName,
                    LastName = member.LastName,
                    Gender = member.Gender,
                    Birthday = member.Birthday,
                    IsChild = member.IsChild,
                    PhoneNumber = member.PhoneNumber,
                    Status = member.Status,
                    Type = member.Type,
                    Nickname = member.Nickname,
                    PhoneticName = member.PhoneticName,
                    FaceEnrolled = member.FaceEnrolled,
                    VoiceEnrolled = member.VoiceEnrolled,
                    LegalGuardianId = member.LegalGuardianId,
                    AgreementId = member.AgreementId,
                    CreatedUtc = member.CreatedUtc
                };
        }

        TouchState();
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

    private static string GenerateSalt()
    {
        Span<byte> saltBytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(saltBytes);
        return Convert.ToHexString(saltBytes).ToLowerInvariant();
    }

    private static string HashPassword(string password, string salt)
    {
        return ComputeSha256Hex($"{salt}:{password}");
    }

    private static string ComputeSha256Hex(string value)
    {
        return ComputeSha256Hex(Encoding.UTF8.GetBytes(value));
    }

    private static string ComputeSha256Hex(byte[] value)
    {
        return Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    }

    private void TouchState()
    {
        Interlocked.Increment(ref _revision);
        SavePersistedState();
    }

    private void ApplyConfiguredOwnerName()
    {
        if (string.IsNullOrWhiteSpace(_ownerFirstName) && string.IsNullOrWhiteSpace(_ownerLastName))
            return;

        _account = new AccountProfile
        {
            AccountId = _account.AccountId,
            Email = _account.Email,
            FirstName = !string.IsNullOrWhiteSpace(_ownerFirstName) ? _ownerFirstName : _account.FirstName,
            LastName = !string.IsNullOrWhiteSpace(_ownerLastName) ? _ownerLastName : _account.LastName,
            AccessKeyId = _account.AccessKeyId,
            SecretAccessKey = _account.SecretAccessKey
        };

        for (var i = 0; i < _people.Count; i++)
        {
            var person = _people[i];
            if (!person.AccountId.Equals(_account.AccountId, StringComparison.OrdinalIgnoreCase)) continue;

            _people[i] = new PersonRecord
            {
                PersonId = person.PersonId,
                AccountId = person.AccountId,
                LoopId = person.LoopId,
                RobotId = person.RobotId,
                DisplayName = $"{_account.FirstName} {_account.LastName}".Trim(),
                Alias = _account.FirstName,
                IsPrimary = person.IsPrimary,
                CreatedUtc = person.CreatedUtc,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
        }

        for (var i = 0; i < _loopMembers.Count; i++)
        {
            var member = _loopMembers[i];
            if (member.AccountId == null ||
                !member.AccountId.Equals(_account.AccountId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member.Type, "robot", StringComparison.OrdinalIgnoreCase))
                continue;

            _loopMembers[i] = new LoopMemberRecord
            {
                Id = member.Id,
                LoopId = member.LoopId,
                AccountId = member.AccountId,
                Email = member.Email,
                FirstName = _account.FirstName,
                LastName = _account.LastName,
                Gender = member.Gender,
                Birthday = member.Birthday,
                IsChild = member.IsChild,
                PhoneNumber = member.PhoneNumber,
                Status = member.Status,
                Type = member.Type,
                Nickname = member.Nickname,
                PhoneticName = member.PhoneticName,
                FaceEnrolled = member.FaceEnrolled,
                VoiceEnrolled = member.VoiceEnrolled,
                LegalGuardianId = member.LegalGuardianId,
                AgreementId = member.AgreementId,
                CreatedUtc = member.CreatedUtc
            };
        }
    }

    private void EnsureDefaultTopology()
    {
        if (_loops.Count == 0)
            _loops.Add(new LoopRecord
            {
                OwnerAccountId = _account.AccountId,
                RobotId = _robot.RobotId,
                RobotFriendlyId = _robot.DeviceId
            });

        EnsureOwnerLoopMember(_loops[0].LoopId);
        EnsureRobotLoopMember(_loops[0].LoopId, _robot.RobotId);

        if (_people.Count != 0)
        {
            EnsureDefaultCommuteProfile();
            return;
        }

        var loopId = _loops[0].LoopId;
        _people.Add(new PersonRecord
        {
            PersonId = "person-openjibo-owner",
            AccountId = _account.AccountId,
            LoopId = loopId,
            RobotId = _robot.RobotId,
            DisplayName = $"{_account.FirstName} {_account.LastName}",
            Alias = _account.FirstName,
            IsPrimary = true
        });
        _people.Add(new PersonRecord
        {
            PersonId = "person-openjibo-household-member",
            AccountId = _account.AccountId,
            LoopId = loopId,
            RobotId = _robot.RobotId,
            DisplayName = "OpenJibo Household Member",
            Alias = "Household Member",
            IsPrimary = false
        });

        EnsureDefaultCommuteProfile();
    }

    private static string CreateBootstrapDeviceId()
    {
        return $"openjibo-bootstrap-{Guid.NewGuid():N}";
    }

    private void EnsureOwnerLoopMember(string loopId)
    {
        if (_loopMembers.Any(member =>
                member.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
                member.AccountId != null &&
                member.AccountId.Equals(_account.AccountId, StringComparison.OrdinalIgnoreCase) &&
                !member.Status.Equals("removed", StringComparison.OrdinalIgnoreCase)))
            return;

        _loopMembers.Add(new LoopMemberRecord
        {
            LoopId = loopId,
            AccountId = _account.AccountId,
            Email = _account.Email,
            FirstName = _account.FirstName,
            LastName = _account.LastName,
            Gender = "unknown",
            Birthday = 631152000000,
            Type = "owner",
            Status = "active"
        });
    }

    private void EnsureRobotLoopMember(string loopId, string robotId)
    {
        if (string.IsNullOrWhiteSpace(robotId)) return;

        if (_loopMembers.Any(member =>
                member.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
                member.AccountId != null &&
                member.AccountId.Equals(robotId, StringComparison.OrdinalIgnoreCase) &&
                !member.Status.Equals("removed", StringComparison.OrdinalIgnoreCase)))
            return;

        _loopMembers.Add(new LoopMemberRecord
        {
            LoopId = loopId,
            AccountId = robotId,
            FirstName = "Jibo",
            LastName = "Robot",
            Gender = "unknown",
            Type = "robot",
            Status = "active"
        });
    }

    private void EnsureDefaultCommuteProfile()
    {
        if (_commuteProfiles.Any(commute =>
                string.Equals(commute.LoopId, ResolveDefaultLoopId(), StringComparison.OrdinalIgnoreCase)))
            return;

        _commuteProfiles.Add(new CommuteProfileRecord
        {
            Id = $"commute-{ResolveDefaultLoopId()}",
            LoopId = ResolveDefaultLoopId(),
            IsEnabled = true,
            IsComplete = true,
            Mode = "driving",
            WorkHour = 8,
            WorkMinute = 30,
            OriginName = "home",
            DestinationName = "work",
            TypicalDurationMinutes = 25
        });
    }

    private LoopMemberRecord GetLoopMember(string loopId, string memberId)
    {
        return _loopMembers.First(m =>
            m.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
            m.Id.Equals(memberId, StringComparison.OrdinalIgnoreCase));
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasDash = false;
                continue;
            }

            if (lastWasDash) continue;

            builder.Append('-');
            lastWasDash = true;
        }

        return builder.ToString().Trim('-');
    }

    private static string ResolveDefaultLoopId(IReadOnlyList<LoopRecord> loops, AccountProfile account)
    {
        return loops.FirstOrDefault(loop =>
                   string.Equals(loop.OwnerAccountId, account.AccountId, StringComparison.OrdinalIgnoreCase))?.LoopId
               ?? loops.FirstOrDefault()?.LoopId
               ?? "openjibo-default-loop";
    }

    private string ResolveDefaultLoopId()
    {
        return ResolveDefaultLoopId(_loops, _account);
    }

    private static IDictionary<string, object?> BuildSessionMetadata(string accountId, string? deviceId, string loopId)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["accountId"] = accountId,
            ["loopId"] = loopId,
            ["deviceId"] = deviceId
        };
    }

    private static CloudSessionSnapshot MapSessionSnapshot(CloudSession session)
    {
        return new CloudSessionSnapshot
        {
            SessionId = session.SessionId,
            Kind = session.Kind,
            AccountId = session.AccountId,
            DeviceId = session.DeviceId,
            Token = session.Token,
            HostName = session.HostName,
            Path = session.Path,
            CreatedUtc = session.CreatedUtc,
            LastSeenUtc = session.LastSeenUtc,
            FollowUpExpiresUtc = session.FollowUpExpiresUtc,
            LastMessageType = session.LastMessageType,
            LastListenType = session.LastListenType,
            LastIntent = session.LastIntent,
            LastTranscript = session.LastTranscript,
            LastTransId = session.LastTransId,
            Metadata = session.Metadata
        };
    }

    private PersistentStateSnapshot CaptureSnapshot(DateTimeOffset now)
    {
        return new PersistentStateSnapshot
        {
            SchemaVersion = CurrentSchemaVersion,
            Revision = Interlocked.Read(ref _revision),
            LastLoadedUtc = _lastLoadedUtc,
            LastSavedUtc = now,
            Account = _account,
            Robot = _robot,
            RobotProfile = _robotProfile,
            Devices = _devices.Values.ToArray(),
            Sessions = _sessionsByToken.Values.Select(MapSessionSnapshot).ToArray(),
            SymmetricKeys = _symmetricKeys.ToDictionary(entry => entry.Key, entry => entry.Value,
                StringComparer.OrdinalIgnoreCase),
            KeyRequests = _keyRequests.Values.ToArray(),
            Updates = _updates.ToArray(),
            Media = _media.ToArray(),
            Backups = _backups.ToArray(),
            CommuteProfiles = _commuteProfiles.ToArray(),
            CalendarEvents = _calendarEvents.ToArray(),
            GreetingPresences = _greetingPresences.ToArray(),
            Loops = _loops.ToArray(),
            Holidays = _holidayOverrides.ToArray(),
            LoopMembers = _loopMembers.ToArray(),
            People = _people.ToArray(),
            Users = _users.ToArray()
        };
    }

    private void ApplySnapshot(PersistentStateSnapshot snapshot)
    {
        _account = snapshot.Account ?? _account;
        _robot = snapshot.Robot ?? _robot;
        _robotProfile = snapshot.RobotProfile ?? _robotProfile;

        _devices.Clear();
        foreach (var device in snapshot.Devices ?? []) _devices[device.DeviceId] = device;

        if (_devices.IsEmpty || !_devices.ContainsKey(_robot.DeviceId)) _devices[_robot.DeviceId] = _robot;

        _sessionsByToken.Clear();
        foreach (var session in snapshot.Sessions ?? [])
            if (!string.IsNullOrWhiteSpace(session.Token))
                _sessionsByToken[session.Token] = session.ToRecord();

        _symmetricKeys.Clear();
        foreach (var pair in snapshot.SymmetricKeys ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            _symmetricKeys[pair.Key] = pair.Value;

        _keyRequests.Clear();
        foreach (var keyRequest in snapshot.KeyRequests ?? []) _keyRequests[keyRequest.RequestId] = keyRequest;

        _updates.Clear();
        _updates.AddRange(snapshot.Updates ?? []);

        _media.Clear();
        _media.AddRange(snapshot.Media ?? []);

        _backups.Clear();
        _backups.AddRange(snapshot.Backups ?? []);

        _commuteProfiles.Clear();
        _commuteProfiles.AddRange(snapshot.CommuteProfiles ?? []);

        _calendarEvents.Clear();
        _calendarEvents.AddRange(snapshot.CalendarEvents ?? []);

        _greetingPresences.Clear();
        _greetingPresences.AddRange(snapshot.GreetingPresences ?? []);

        _loops.Clear();
        _loops.AddRange(snapshot.Loops ?? []);

        _holidayOverrides.Clear();
        _holidayOverrides.AddRange(snapshot.Holidays ?? []);

        _loopMembers.Clear();
        _loopMembers.AddRange(snapshot.LoopMembers ?? []);

        _people.Clear();
        _people.AddRange(snapshot.People ?? []);

        _users.Clear();
        _users.AddRange(snapshot.Users ?? []);

        ApplyConfiguredOwnerName();
        EnsureDefaultTopology();

        if (_robotProfile is null ||
            !string.Equals(_robotProfile.RobotId, _robot.RobotId, StringComparison.OrdinalIgnoreCase))
            _robotProfile = new RobotProfile
            {
                RobotId = _robot.RobotId,
                Payload = new Dictionary<string, object?>
                {
                    ["SSID"] = "my-ssid",
                    ["connectedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ["platform"] = _robot.FirmwareVersion ?? "12.10.0",
                    ["serialNumber"] = _robot.DeviceId
                },
                UpdatedUtc = DateTimeOffset.UtcNow
            };

        Interlocked.Exchange(ref _revision, snapshot.Revision);
        _lastLoadedUtc = snapshot.LastLoadedUtc ?? DateTimeOffset.UtcNow;
        _lastSavedUtc = snapshot.LastSavedUtc;
    }

    private sealed class PersistentStateSnapshot
    {
        public string SchemaVersion { get; init; } = CurrentSchemaVersion;
        public long Revision { get; init; }
        public DateTimeOffset? LastLoadedUtc { get; init; }
        public DateTimeOffset? LastSavedUtc { get; init; }
        public AccountProfile? Account { get; init; }
        public DeviceRegistration? Robot { get; init; }
        public RobotProfile? RobotProfile { get; init; }
        public DeviceRegistration[]? Devices { get; init; }
        public CloudSessionSnapshot[]? Sessions { get; init; }
        public Dictionary<string, string>? SymmetricKeys { get; init; }
        public KeyRequestRecord[]? KeyRequests { get; init; }
        public UpdateManifest[]? Updates { get; init; }
        public MediaRecord[]? Media { get; init; }
        public BackupRecord[]? Backups { get; init; }
        public CommuteProfileRecord[]? CommuteProfiles { get; init; }
        public CalendarEventRecord[]? CalendarEvents { get; init; }
        public GreetingPresenceRecord[]? GreetingPresences { get; init; }
        public LoopRecord[]? Loops { get; init; }
        public HolidayRecord[]? Holidays { get; init; }
        public LoopMemberRecord[]? LoopMembers { get; init; }
        public PersonRecord[]? People { get; init; }
        public UserRecord[]? Users { get; init; }
    }

    private sealed class CloudSessionSnapshot
    {
        public string SessionId { get; init; } = Guid.NewGuid().ToString("N");
        public string Kind { get; init; } = "http";
        public string? AccountId { get; init; }
        public string? DeviceId { get; init; }
        public string? Token { get; init; }
        public string? HostName { get; init; }
        public string? Path { get; init; }
        public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastSeenUtc { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? FollowUpExpiresUtc { get; init; }
        public string? LastMessageType { get; init; }
        public string? LastListenType { get; init; }
        public string? LastIntent { get; init; }
        public string? LastTranscript { get; init; }
        public string? LastTransId { get; init; }
        public IDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();

        public CloudSession ToRecord()
        {
            return new CloudSession
            {
                SessionId = SessionId,
                Kind = Kind,
                AccountId = AccountId,
                DeviceId = DeviceId,
                Token = Token,
                HostName = HostName,
                Path = Path,
                CreatedUtc = CreatedUtc,
                LastSeenUtc = LastSeenUtc,
                FollowUpExpiresUtc = FollowUpExpiresUtc,
                LastMessageType = LastMessageType,
                LastListenType = LastListenType,
                LastIntent = LastIntent,
                LastTranscript = LastTranscript,
                LastTransId = LastTransId,
                Metadata = new Dictionary<string, object?>(Metadata, StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
