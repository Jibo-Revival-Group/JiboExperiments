-- Durable cloud state only. Live websocket/turn objects remain in the bounded
-- process-local session registry; issued credentials and explicit robot links
-- are durable below.

CREATE TABLE IF NOT EXISTS CloudStateMetadata
(
    StateKey TEXT NOT NULL PRIMARY KEY,
    SchemaVersion INTEGER NOT NULL DEFAULT 1,
    Revision BIGINT NOT NULL DEFAULT 0,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO CloudStateMetadata (StateKey, SchemaVersion, Revision)
VALUES ('cloud-state', 1, 0)
ON CONFLICT (StateKey) DO NOTHING;

CREATE TABLE IF NOT EXISTS CloudStateImports
(
    ImportName TEXT NOT NULL PRIMARY KEY,
    SourceSnapshotName TEXT NOT NULL,
    SourceSchemaVersion TEXT NULL,
    SourceRevision BIGINT NOT NULL DEFAULT 0,
    SourceUpdatedUtc TIMESTAMPTZ NULL,
    SourceSha256 CHAR(64) NOT NULL,
    ImportedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ImportedCounts JSONB NOT NULL DEFAULT '{}'::JSONB,
    CHECK (SourceSha256 ~ '^[0-9a-fA-F]{64}$')
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_CloudStateImports_Source
    ON CloudStateImports (SourceSnapshotName, SourceSha256);

CREATE TABLE IF NOT EXISTS Accounts
(
    AccountId TEXT NOT NULL PRIMARY KEY,
    Email TEXT NOT NULL,
    FirstName TEXT NOT NULL DEFAULT '',
    LastName TEXT NOT NULL DEFAULT '',
    AccessKeyId TEXT NOT NULL,
    SecretAccessKeyCiphertext BYTEA NULL,
    SecretWrappingKeyId TEXT NULL,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    IsDefault BOOLEAN NOT NULL DEFAULT FALSE,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_Accounts_Email_CI ON Accounts (LOWER(Email));
CREATE UNIQUE INDEX IF NOT EXISTS UX_Accounts_AccessKeyId_CI ON Accounts (LOWER(AccessKeyId));
CREATE UNIQUE INDEX IF NOT EXISTS UX_Accounts_Default ON Accounts (IsDefault) WHERE IsDefault;

CREATE TABLE IF NOT EXISTS Users
(
    UserId TEXT NOT NULL PRIMARY KEY,
    Email TEXT NOT NULL,
    PasswordHash TEXT NOT NULL,
    PasswordSalt TEXT NOT NULL,
    FirstName TEXT NOT NULL DEFAULT '',
    LastName TEXT NOT NULL DEFAULT '',
    Gender TEXT NULL,
    Birthday BIGINT NULL,
    AccessKeyId TEXT NOT NULL,
    SecretAccessKeyCiphertext BYTEA NULL,
    SecretWrappingKeyId TEXT NULL,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_Email_CI ON Users (LOWER(Email));
CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_AccessKeyId_CI ON Users (LOWER(AccessKeyId));

CREATE TABLE IF NOT EXISTS Devices
(
    DeviceId TEXT NOT NULL PRIMARY KEY,
    RobotId TEXT NOT NULL,
    FriendlyName TEXT NOT NULL,
    FirmwareVersion TEXT NULL,
    ApplicationVersion TEXT NULL,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CertificateThumbprint TEXT NULL,
    IssuedIdentityId TEXT NULL,
    BuildHash TEXT NULL,
    ConfigHash TEXT NULL,
    VerifiedSerialNumber TEXT NULL,
    SerialEvidenceSource TEXT NULL,
    SerialEvidenceVerifiedUtc TIMESTAMPTZ NULL,
    RegistrationSource TEXT NOT NULL DEFAULT 'unknown',
    IsHidden BOOLEAN NOT NULL DEFAULT FALSE,
    IsDefault BOOLEAN NOT NULL DEFAULT FALSE,
    ArchivedUtc TIMESTAMPTZ NULL,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_Devices_DeviceId_CI ON Devices (LOWER(DeviceId));
-- Historical merge sources can legitimately retain the same RobotId while archived.
-- Identity cleanup must complete before any future uniqueness constraint is considered.
CREATE INDEX IF NOT EXISTS IX_Devices_RobotId_CI ON Devices (LOWER(RobotId));
CREATE INDEX IF NOT EXISTS IX_Devices_FriendlyName_CI ON Devices (LOWER(FriendlyName));
CREATE INDEX IF NOT EXISTS IX_Devices_Visible ON Devices (IsHidden, ArchivedUtc) WHERE IsActive;
CREATE UNIQUE INDEX IF NOT EXISTS UX_Devices_Default ON Devices (IsDefault) WHERE IsDefault;

CREATE TABLE IF NOT EXISTS AccountDevices
(
    AccountId TEXT NOT NULL REFERENCES Accounts (AccountId) ON DELETE CASCADE,
    DeviceId TEXT NOT NULL REFERENCES Devices (DeviceId) ON DELETE CASCADE,
    Relationship TEXT NOT NULL DEFAULT 'owner',
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (AccountId, DeviceId)
);

CREATE INDEX IF NOT EXISTS IX_AccountDevices_Device ON AccountDevices (DeviceId, AccountId);

CREATE TABLE IF NOT EXISTS DeviceHostMappings
(
    DeviceId TEXT NOT NULL REFERENCES Devices (DeviceId) ON DELETE CASCADE,
    MappingKey TEXT NOT NULL,
    MappingValue TEXT NOT NULL,
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (DeviceId, MappingKey)
);

CREATE INDEX IF NOT EXISTS IX_DeviceHostMappings_Value_CI
    ON DeviceHostMappings (LOWER(MappingValue));

CREATE TABLE IF NOT EXISTS RobotProfiles
(
    RobotId TEXT NOT NULL PRIMARY KEY,
    DeviceId TEXT NULL REFERENCES Devices (DeviceId) ON DELETE SET NULL,
    Payload JSONB NOT NULL DEFAULT '{}'::JSONB,
    CalibrationPayload JSONB NOT NULL DEFAULT '{}'::JSONB,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_RobotProfiles_RobotId_CI ON RobotProfiles (LOWER(RobotId));

CREATE TABLE IF NOT EXISTS RobotCredentialBindings
(
    AccessKeyFingerprint TEXT NOT NULL PRIMARY KEY,
    DeviceId TEXT NOT NULL REFERENCES Devices (DeviceId) ON DELETE CASCADE,
    ClaimedUtc TIMESTAMPTZ NOT NULL,
    ClaimSource TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_RobotCredentialBindings_Device
    ON RobotCredentialBindings (DeviceId, ClaimedUtc DESC);

CREATE TABLE IF NOT EXISTS CloudAuthTokens
(
    TokenHash CHAR(64) NOT NULL PRIMARY KEY,
    TokenKind TEXT NOT NULL CHECK (TokenKind IN ('hub', 'robot', 'access')),
    TokenHint TEXT NULL,
    AccountId TEXT NULL REFERENCES Accounts (AccountId) ON DELETE CASCADE,
    DeviceId TEXT NULL REFERENCES Devices (DeviceId) ON DELETE CASCADE,
    IssuedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ExpiresUtc TIMESTAMPTZ NOT NULL,
    RevokedUtc TIMESTAMPTZ NULL,
    Metadata JSONB NOT NULL DEFAULT '{}'::JSONB,
    CHECK (TokenHash ~ '^[0-9a-fA-F]{64}$')
);

CREATE INDEX IF NOT EXISTS IX_CloudAuthTokens_Account ON CloudAuthTokens (AccountId, ExpiresUtc DESC);
CREATE INDEX IF NOT EXISTS IX_CloudAuthTokens_Device ON CloudAuthTokens (DeviceId, ExpiresUtc DESC);
CREATE INDEX IF NOT EXISTS IX_CloudAuthTokens_Active
    ON CloudAuthTokens (ExpiresUtc) WHERE RevokedUtc IS NULL;

CREATE TABLE IF NOT EXISTS RobotIdentityLinks
(
    ObservedDeviceId TEXT NOT NULL PRIMARY KEY,
    InventoryDeviceId TEXT NOT NULL REFERENCES Devices (DeviceId) ON DELETE CASCADE,
    ClaimSource TEXT NOT NULL,
    ClaimedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    RevokedUtc TIMESTAMPTZ NULL,
    Audit JSONB NOT NULL DEFAULT '[]'::JSONB
);

CREATE INDEX IF NOT EXISTS IX_RobotIdentityLinks_Inventory
    ON RobotIdentityLinks (InventoryDeviceId) WHERE RevokedUtc IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS UX_RobotIdentityLinks_ObservedDeviceId_CI
    ON RobotIdentityLinks (LOWER(ObservedDeviceId));

CREATE TABLE IF NOT EXISTS Loops
(
    LoopId TEXT NOT NULL PRIMARY KEY,
    Name TEXT NOT NULL,
    OwnerAccountId TEXT NOT NULL REFERENCES Accounts (AccountId),
    PrimaryRobotId TEXT NULL,
    PrimaryRobotFriendlyId TEXT NULL,
    IsSuspended BOOLEAN NOT NULL DEFAULT FALSE,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS IX_Loops_Owner ON Loops (OwnerAccountId, IsSuspended, UpdatedUtc DESC);

CREATE TABLE IF NOT EXISTS LoopDevices
(
    LoopId TEXT NOT NULL REFERENCES Loops (LoopId) ON DELETE CASCADE,
    DeviceId TEXT NOT NULL REFERENCES Devices (DeviceId) ON DELETE CASCADE,
    IsPrimary BOOLEAN NOT NULL DEFAULT FALSE,
    AddedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (LoopId, DeviceId)
);

CREATE INDEX IF NOT EXISTS IX_LoopDevices_Device ON LoopDevices (DeviceId, LoopId);
CREATE UNIQUE INDEX IF NOT EXISTS UX_LoopDevices_Primary
    ON LoopDevices (LoopId) WHERE IsPrimary;

CREATE TABLE IF NOT EXISTS LoopMembers
(
    MemberId TEXT NOT NULL PRIMARY KEY,
    LoopId TEXT NOT NULL REFERENCES Loops (LoopId) ON DELETE CASCADE,
    AccountId TEXT NULL,
    Email TEXT NULL,
    FirstName TEXT NULL,
    LastName TEXT NULL,
    Gender TEXT NULL,
    Birthday BIGINT NULL,
    IsChild BOOLEAN NOT NULL DEFAULT FALSE,
    PhoneNumber TEXT NULL,
    Status TEXT NOT NULL DEFAULT 'active',
    MemberType TEXT NOT NULL DEFAULT 'owner',
    Nickname TEXT NULL,
    PhoneticName TEXT NULL,
    FaceEnrolled BOOLEAN NOT NULL DEFAULT FALSE,
    VoiceEnrolled BOOLEAN NOT NULL DEFAULT FALSE,
    LegalGuardianId TEXT NULL,
    AgreementId TEXT NULL,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PortalEditedUtc TIMESTAMPTZ NULL,
    CONSTRAINT FK_LoopMembers_LegalGuardian
        FOREIGN KEY (LegalGuardianId) REFERENCES LoopMembers (MemberId)
        ON DELETE SET NULL DEFERRABLE INITIALLY DEFERRED
);

CREATE INDEX IF NOT EXISTS IX_LoopMembers_Loop ON LoopMembers (LoopId, Status, MemberType);
CREATE INDEX IF NOT EXISTS IX_LoopMembers_Account ON LoopMembers (AccountId) WHERE AccountId IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_LoopMembers_Email_CI ON LoopMembers (LOWER(Email)) WHERE Email IS NOT NULL;

CREATE TABLE IF NOT EXISTS People
(
    PersonId TEXT NOT NULL,
    AccountId TEXT NOT NULL REFERENCES Accounts (AccountId),
    LoopId TEXT NOT NULL REFERENCES Loops (LoopId) ON DELETE CASCADE,
    RobotId TEXT NOT NULL,
    DisplayName TEXT NOT NULL,
    Alias TEXT NULL,
    IsPrimary BOOLEAN NOT NULL DEFAULT FALSE,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (AccountId, LoopId, PersonId)
);

CREATE INDEX IF NOT EXISTS IX_People_LoopRobot ON People (LoopId, RobotId, IsPrimary);
CREATE INDEX IF NOT EXISTS IX_People_Account ON People (AccountId, LoopId);

CREATE TABLE IF NOT EXISTS RecognitionObservations
(
    ObservationId TEXT NOT NULL PRIMARY KEY,
    LoopId TEXT NOT NULL REFERENCES Loops (LoopId) ON DELETE CASCADE,
    MemberId TEXT NOT NULL REFERENCES LoopMembers (MemberId) ON DELETE CASCADE,
    RobotId TEXT NOT NULL,
    Modality TEXT NOT NULL,
    Outcome TEXT NOT NULL,
    Confidence DOUBLE PRECISION NULL CHECK (Confidence IS NULL OR (Confidence >= 0 AND Confidence <= 1)),
    Source TEXT NULL,
    ObservedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS IX_RecognitionObservations_LoopTime
    ON RecognitionObservations (LoopId, ObservedUtc DESC);
CREATE INDEX IF NOT EXISTS IX_RecognitionObservations_MemberTime
    ON RecognitionObservations (MemberId, ObservedUtc DESC);

CREATE TABLE IF NOT EXISTS TrustedServers
(
    ServerId TEXT NOT NULL PRIMARY KEY,
    CanonicalHost TEXT NOT NULL,
    DisplayName TEXT NOT NULL,
    ServerKind TEXT NOT NULL,
    IsListed BOOLEAN NOT NULL DEFAULT TRUE,
    AcceptsPublicConnections BOOLEAN NOT NULL DEFAULT TRUE,
    ParticipatesInCloudSync BOOLEAN NOT NULL DEFAULT TRUE,
    RequiresHttps BOOLEAN NOT NULL DEFAULT TRUE,
    IsTrustRoot BOOLEAN NOT NULL DEFAULT FALSE,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    Description TEXT NOT NULL DEFAULT '',
    RegisteredAtUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedAtUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    LastSeenAtUtc TIMESTAMPTZ NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_TrustedServers_CanonicalHost_CI
    ON TrustedServers (LOWER(CanonicalHost));

CREATE TABLE IF NOT EXISTS TrustedServerAdmissions
(
    AdmissionId TEXT NOT NULL PRIMARY KEY,
    ServerId TEXT NOT NULL REFERENCES TrustedServers (ServerId) ON DELETE CASCADE,
    CanonicalHost TEXT NOT NULL,
    ServerKind TEXT NOT NULL,
    Action TEXT NOT NULL,
    ActorDeviceId TEXT NULL,
    ActorFriendlyId TEXT NULL,
    Reason TEXT NULL,
    SignatureAlgorithm TEXT NOT NULL,
    SignatureKeyId TEXT NOT NULL,
    Payload TEXT NOT NULL,
    Signature TEXT NOT NULL,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS IX_TrustedServerAdmissions_HostTime
    ON TrustedServerAdmissions (LOWER(CanonicalHost), CreatedUtc DESC);

CREATE TABLE IF NOT EXISTS RevokedIdentityGraphAnchors
(
    Anchor TEXT NOT NULL PRIMARY KEY,
    RevokedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    Reason TEXT NULL
);

CREATE TABLE IF NOT EXISTS UpdateManifests
(
    UpdateId TEXT NOT NULL PRIMARY KEY,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    FromVersion TEXT NOT NULL,
    ToVersion TEXT NOT NULL,
    Changes TEXT NOT NULL,
    Url TEXT NOT NULL,
    ShaHash TEXT NOT NULL,
    ContentLength BIGINT NOT NULL DEFAULT 0 CHECK (ContentLength >= 0),
    Subsystem TEXT NOT NULL,
    Filter TEXT NULL,
    Dependencies JSONB NOT NULL DEFAULT '{}'::JSONB
);

CREATE INDEX IF NOT EXISTS IX_UpdateManifests_Lookup
    ON UpdateManifests (Subsystem, Filter, CreatedUtc DESC);

CREATE TABLE IF NOT EXISTS MediaRecords
(
    MediaPath TEXT NOT NULL PRIMARY KEY,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    MediaType TEXT NOT NULL,
    Reference TEXT NOT NULL,
    AccountId TEXT NOT NULL REFERENCES Accounts (AccountId),
    LoopId TEXT NOT NULL REFERENCES Loops (LoopId) ON DELETE CASCADE,
    BlobUri TEXT NOT NULL,
    ContentSha256 CHAR(64) NULL,
    ContentLength BIGINT NULL CHECK (ContentLength IS NULL OR ContentLength >= 0),
    IsEncrypted BOOLEAN NOT NULL DEFAULT FALSE,
    EncryptionKeyId TEXT NULL,
    IsDeleted BOOLEAN NOT NULL DEFAULT FALSE,
    DeletedUtc TIMESTAMPTZ NULL,
    Meta JSONB NOT NULL DEFAULT '{}'::JSONB,
    CHECK (ContentSha256 IS NULL OR ContentSha256 ~ '^[0-9a-fA-F]{64}$')
);

CREATE INDEX IF NOT EXISTS IX_MediaRecords_LoopCreated
    ON MediaRecords (LoopId, CreatedUtc DESC) WHERE NOT IsDeleted;
CREATE INDEX IF NOT EXISTS IX_MediaRecords_AccountCreated
    ON MediaRecords (AccountId, CreatedUtc DESC) WHERE NOT IsDeleted;

CREATE TABLE IF NOT EXISTS BackupManifests
(
    BackupId TEXT NOT NULL PRIMARY KEY,
    AccountId TEXT NULL REFERENCES Accounts (AccountId),
    LoopId TEXT NULL REFERENCES Loops (LoopId) ON DELETE SET NULL,
    Name TEXT NOT NULL,
    BlobUri TEXT NOT NULL,
    ContentSha256 CHAR(64) NOT NULL,
    ContentLength BIGINT NOT NULL CHECK (ContentLength >= 0),
    BackupSchemaVersion INTEGER NOT NULL,
    Status TEXT NOT NULL DEFAULT 'available',
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ExpiresUtc TIMESTAMPTZ NULL,
    RestoredUtc TIMESTAMPTZ NULL,
    CHECK (ContentSha256 ~ '^[0-9a-fA-F]{64}$')
);

CREATE INDEX IF NOT EXISTS IX_BackupManifests_LoopCreated
    ON BackupManifests (LoopId, CreatedUtc DESC);
CREATE INDEX IF NOT EXISTS IX_BackupManifests_StatusExpiry
    ON BackupManifests (Status, ExpiresUtc);

CREATE TABLE IF NOT EXISTS LoopSymmetricKeys
(
    LoopId TEXT NOT NULL PRIMARY KEY REFERENCES Loops (LoopId) ON DELETE CASCADE,
    EncryptedKey BYTEA NOT NULL,
    WrappingKeyId TEXT NOT NULL,
    Algorithm TEXT NOT NULL,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    RotatedUtc TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS KeyRequests
(
    RequestId TEXT NOT NULL PRIMARY KEY,
    LoopId TEXT NOT NULL REFERENCES Loops (LoopId) ON DELETE CASCADE,
    PublicKey TEXT NOT NULL,
    EncryptedKey TEXT NOT NULL DEFAULT '',
    RequestKind TEXT NOT NULL DEFAULT 'incoming',
    Status TEXT NOT NULL DEFAULT 'pending',
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CompletedUtc TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS IX_KeyRequests_LoopStatus
    ON KeyRequests (LoopId, Status, CreatedUtc DESC);

CREATE TABLE IF NOT EXISTS HolidayOverrides
(
    HolidayId TEXT NOT NULL PRIMARY KEY,
    EventId TEXT NOT NULL,
    Name TEXT NOT NULL,
    Category TEXT NOT NULL,
    Subcategory TEXT NULL,
    LoopId TEXT NOT NULL REFERENCES Loops (LoopId) ON DELETE CASCADE,
    MemberId TEXT NULL,
    IsEnabled BOOLEAN NOT NULL DEFAULT TRUE,
    EventDate DATE NOT NULL,
    EndDate DATE NULL,
    Source TEXT NOT NULL,
    CountryCode TEXT NOT NULL,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS IX_HolidayOverrides_LoopDate
    ON HolidayOverrides (LoopId, EventDate, IsEnabled);

CREATE TABLE IF NOT EXISTS CommuteProfiles
(
    CommuteProfileId TEXT NOT NULL PRIMARY KEY,
    LoopId TEXT NOT NULL REFERENCES Loops (LoopId) ON DELETE CASCADE,
    MemberId TEXT NULL,
    IsEnabled BOOLEAN NOT NULL DEFAULT TRUE,
    IsComplete BOOLEAN NOT NULL DEFAULT TRUE,
    Mode TEXT NOT NULL,
    WorkHour INTEGER NOT NULL CHECK (WorkHour BETWEEN 0 AND 23),
    WorkMinute INTEGER NOT NULL CHECK (WorkMinute BETWEEN 0 AND 59),
    OriginName TEXT NULL,
    DestinationName TEXT NULL,
    TypicalDurationMinutes INTEGER NOT NULL CHECK (TypicalDurationMinutes >= 0),
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS IX_CommuteProfiles_LoopMember
    ON CommuteProfiles (LoopId, MemberId, IsEnabled);

CREATE TABLE IF NOT EXISTS CalendarEvents
(
    CalendarEventId TEXT NOT NULL PRIMARY KEY,
    LoopId TEXT NOT NULL REFERENCES Loops (LoopId) ON DELETE CASCADE,
    Summary TEXT NOT NULL,
    TimeLabel TEXT NULL,
    EventDate DATE NOT NULL,
    EndDate DATE NULL,
    IsAllDay BOOLEAN NOT NULL DEFAULT FALSE,
    IsEnabled BOOLEAN NOT NULL DEFAULT TRUE,
    Source TEXT NOT NULL,
    MemberId TEXT NULL,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS IX_CalendarEvents_LoopDate
    ON CalendarEvents (LoopId, EventDate, IsEnabled);

CREATE TABLE IF NOT EXISTS GreetingPresences
(
    GreetingPresenceId TEXT NOT NULL PRIMARY KEY,
    AccountId TEXT NOT NULL REFERENCES Accounts (AccountId),
    LoopId TEXT NOT NULL REFERENCES Loops (LoopId) ON DELETE CASCADE,
    PersonId TEXT NOT NULL,
    SpeakerId TEXT NULL,
    PreferredName TEXT NULL,
    LastSeenUtc TIMESTAMPTZ NOT NULL,
    LastGreetedUtc TIMESTAMPTZ NULL,
    LastGreetingRoute TEXT NULL,
    LastGreetingIntent TEXT NULL,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (LoopId, PersonId)
);

CREATE INDEX IF NOT EXISTS IX_GreetingPresences_LoopRecent
    ON GreetingPresences (LoopId, LastSeenUtc DESC);
