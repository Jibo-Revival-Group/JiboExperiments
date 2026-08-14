using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Abstractions;

public interface ICloudStateStore
{
    PersistenceStateInfo GetPersistenceStateInfo();
    void LoadPersistedState();
    void SavePersistedState();
    AccountProfile GetAccount();
    DeviceRegistration GetRobot();
    IReadOnlyList<DeviceRegistration> GetDevices();
    IReadOnlyList<CloudSession> GetSessions();
    RobotProfile GetRobotProfile();
    DeviceRegistration GetOrCreateDevice(string deviceId, string? firmwareVersion, string? applicationVersion,
        string? registrationSource = null);
    DeviceRegistration UpsertDevice(DeviceRegistration registration);
    DeviceRegistration RenameDevice(string deviceId, string robotId);
    DeviceRegistration? FindDeviceByFriendlyId(string friendlyId);
    DeviceRegistration? FindDeviceByAwsCredentialFingerprint(string accessKeyFingerprint);
    IReadOnlyList<RobotCredentialBinding> GetRobotCredentialBindings();
    RobotCredentialBinding BindAwsCredentialFingerprint(string deviceId, string accessKeyFingerprint,
        string claimSource);
    IReadOnlyList<RobotCredentialBinding> SwapAwsCredentialFingerprintBindings(string firstAccessKeyFingerprint,
        string secondAccessKeyFingerprint, string claimSource);
    RobotMergeResult MergeRobotRecords(string sourceDeviceId, string targetDeviceId);
    UserRecord? CreateUser(string email, string password, string? firstName, string? lastName);
    UserRecord? AuthenticateUser(string email, string password);
    UserRecord? GetUserById(string id);
    UserRecord? GetUserByEmail(string email);
    UserRecord UpdateUser(string id, string? firstName, string? lastName, string? gender, long? birthday);
    string IssueHubToken(string? deviceId = null, bool useDefaultRobot = true);
    string IssueRobotToken(string deviceId);
    CloudSession OpenSession(string kind, string? deviceId, string? token, string? hostName, string? path);
    CloudSession? FindSessionByToken(string token);
    bool BindSessionToDevice(string sessionId, string deviceId);
    bool ClearSessionDeviceBinding(string sessionId);
    /// <summary>
    /// Copies dialog-continuation metadata from other sessions that share this session's DeviceId
    /// (same robot reconnecting on a new path-token websocket).
    /// </summary>
    void ReinheritDialogMetadata(CloudSession session);
    IReadOnlyList<LoopRecord> GetLoops();
    LoopRecord AddLoop(string? name, string? ownerAccountId, string? robotId, string? robotFriendlyId,
        string? preferredLoopId = null);

    /// <summary>
    /// Aligns (or creates) the household loop so <c>Loop#list()</c> matches the robot's
    /// existing KB identity: <paramref name="robotId"/> (required), optional stock
    /// <paramref name="preferredLoopId"/> / <paramref name="ownerAccountId"/>.
    /// Rematerializes the loop id across members/people when the preferred id differs.
    /// </summary>
    LoopRecord AlignHouseholdIdentity(string robotId, string? robotFriendlyId = null,
        string? preferredLoopId = null, string? ownerAccountId = null, string? loopName = null);
    IReadOnlyList<PersonRecord> GetPeople();
    PersonRecord UpsertPerson(PersonRecord person);
    /// <summary>
    /// Upserts people (and matching non-robot loop members) from the robot's
    /// <c>runtime.loop.users</c> roster — the same source Pegasus personal report uses.
    /// People are scoped to <paramref name="loopId"/> + <paramref name="robotId"/> so
    /// multiple Jibos on one cloud do not merge households.
    /// </summary>
    int SyncPeopleFromLoopUsers(string loopId, string? robotId, IReadOnlyList<LoopUserSnapshot> loopUsers);
    IReadOnlyList<LoopMemberRecord> GetLoopMembers(string loopId);
    IReadOnlyList<TrustedServerRecord> GetTrustedServers();
    IReadOnlyList<TrustedServerAdmissionRecord> GetTrustedServerAdmissions(string? canonicalHost = null);
    TrustedServerRecord UpsertTrustedServer(TrustedServerRecord trustedServer);
    TrustedServerAdmissionRecord RecordTrustedServerAdmission(TrustedServerRecord trustedServer, string action,
        string? actorDeviceId, string? actorFriendlyId, string? reason = null);
    TrustedServerRecord? FindTrustedServer(string canonicalHost);
    IdentityGraphSnapshot GetIdentityGraph(string? loopId = null);
    void RevokeIdentityGraphAnchor(string anchor);

    LoopMemberRecord AddLoopMember(string loopId, string? accountId, string? email, string? firstName,
        string? lastName, string? gender, long? birthday, bool isChild, string type, string? legalGuardianId = null,
        bool markPortalEdited = false);

    LoopMemberRecord UpdateLoopMember(string loopId, string memberId, string? firstName, string? lastName,
        string? gender, long? birthday, bool isChild, string? nickname, string? phoneticName,
        bool markPortalEdited = false);

    /// <summary>
    /// Rewrites the still-untouched seeded owner ("Jibo Owner") into a real household
    /// person, keeping its member id and accountId so the robot updates the existing KB
    /// UserNode instead of gaining a second one. Returns <c>null</c> when the owner has
    /// already been claimed, in which case the caller should add a normal member.
    /// </summary>
    LoopMemberRecord? ClaimSeededOwner(string loopId, string firstName, string? lastName, string? gender,
        long? birthday, bool isChild);

    /// <summary>
    /// Moves loop ownership onto an existing member. The member takes over
    /// <see cref="LoopRecord.OwnerAccountId"/> so SyncManager can still resolve
    /// <c>loop.owner</c> to a member id, and the previous owner becomes a plain member
    /// (or is dropped entirely if it was still the untouched seed).
    /// </summary>
    LoopMemberRecord PromoteLoopMemberToOwner(string loopId, string memberId);

    bool RemoveLoopMember(string loopId, string memberId);
    LoopMemberRecord SetMemberEnrollment(string loopId, string memberId, bool? face, bool? voice);

    RecognitionObservationRecord RecordRecognitionObservation(string loopId, string memberId, string modality,
        string outcome, double? confidence = null, string? source = null);

    IReadOnlyList<RecognitionObservationRecord> GetRecognitionObservations(string loopId);
    IReadOnlyList<UpdateManifest> ListUpdates(string? subsystem = null, string? filter = null);
    UpdateManifest? GetUpdateFrom(string? subsystem, string? fromVersion, string? filter);

    UpdateManifest CreateUpdate(string? fromVersion, string? toVersion, string? changes, string? shaHash, long? length,
        string? subsystem, string? filter, IDictionary<string, object?>? dependencies);

    UpdateManifest RemoveUpdate(string? updateId);

    IReadOnlyList<MediaRecord> ListMedia(IReadOnlyList<string>? loopIds = null, long? after = null,
        long? before = null);

    IReadOnlyList<MediaRecord> GetMedia(IReadOnlyList<string> paths);
    IReadOnlyList<MediaRecord> RemoveMedia(IReadOnlyList<string> paths);

    MediaRecord CreateMedia(string loopId, string path, string type, string reference, bool isEncrypted,
        IDictionary<string, object?>? meta);

    IReadOnlyList<BackupRecord> GetBackups();
    BackupRecord CreateBackup(string loopId, string name);
    BackupRecord? RestoreBackup(string? backupId = null);
    bool ShouldCreateSymmetricKey(string loopId);
    string GetOrCreateSymmetricKey(string loopId);
    KeyRequestRecord CreateKeyRequest(string loopId, string publicKey);
    KeyRequestRecord GetKeyRequest(string loopId, string? requestId, string? publicKey);
    IReadOnlyList<KeyRequestRecord> GetIncomingKeyRequests();
    IReadOnlyList<KeyRequestRecord> GetBinaryRequests();
    IReadOnlyList<HolidayRecord> GetHolidays(string? loopId = null);
    HolidayRecord UpsertHoliday(HolidayRecord holiday);
    IReadOnlyList<CommuteProfileRecord> GetCommuteProfiles(string? loopId = null);
    CommuteProfileRecord UpsertCommuteProfile(CommuteProfileRecord commuteProfile);
    IReadOnlyList<CalendarEventRecord> GetCalendarEvents(string? loopId = null);
    CalendarEventRecord UpsertCalendarEvent(CalendarEventRecord calendarEvent);
    IReadOnlyList<GreetingPresenceRecord> GetGreetingPresences(string? loopId = null);
    GreetingPresenceRecord UpsertGreetingPresence(GreetingPresenceRecord greetingPresence);
    void UpdateRobot(DeviceRegistration registration);
}
