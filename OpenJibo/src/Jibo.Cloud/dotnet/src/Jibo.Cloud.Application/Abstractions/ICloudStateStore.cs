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
    DeviceRegistration GetOrCreateDevice(string deviceId, string? firmwareVersion, string? applicationVersion);
    DeviceRegistration UpsertDevice(DeviceRegistration registration);
    DeviceRegistration? FindDeviceByFriendlyId(string friendlyId);
    UserRecord? CreateUser(string email, string password, string? firstName, string? lastName);
    UserRecord? AuthenticateUser(string email, string password);
    UserRecord? GetUserById(string id);
    UserRecord? GetUserByEmail(string email);
    UserRecord UpdateUser(string id, string? firstName, string? lastName, string? gender, long? birthday);
    string IssueHubToken(string? deviceId = null);
    string IssueRobotToken(string deviceId);
    CloudSession OpenSession(string kind, string? deviceId, string? token, string? hostName, string? path);
    CloudSession? FindSessionByToken(string token);
    /// <summary>
    /// Copies dialog-continuation metadata from other sessions that share this session's DeviceId
    /// (same robot reconnecting on a new path-token websocket).
    /// </summary>
    void ReinheritDialogMetadata(CloudSession session);
    IReadOnlyList<LoopRecord> GetLoops();
    LoopRecord AddLoop(string? name, string? ownerAccountId, string? robotId, string? robotFriendlyId);
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
