CREATE TABLE IF NOT EXISTS PersonalMemoryScopes
(
    ScopeKey TEXT NOT NULL PRIMARY KEY,
    AccountId TEXT NOT NULL,
    LoopId TEXT NOT NULL,
    DeviceId TEXT NOT NULL,
    PersonId TEXT NULL,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS IX_PersonalMemoryScopes_AccountLoop
    ON PersonalMemoryScopes (AccountId, LoopId);

CREATE TABLE IF NOT EXISTS PersonalMemoryProfiles
(
    ScopeKey TEXT NOT NULL PRIMARY KEY REFERENCES PersonalMemoryScopes (ScopeKey) ON DELETE CASCADE,
    Name TEXT NULL,
    Birthday TEXT NULL,
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS PersonalMemoryPreferences
(
    ScopeKey TEXT NOT NULL REFERENCES PersonalMemoryScopes (ScopeKey) ON DELETE CASCADE,
    Category TEXT NOT NULL,
    Value TEXT NOT NULL,
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (ScopeKey, Category)
);

CREATE TABLE IF NOT EXISTS PersonalMemoryImportantDates
(
    ScopeKey TEXT NOT NULL REFERENCES PersonalMemoryScopes (ScopeKey) ON DELETE CASCADE,
    Label TEXT NOT NULL,
    Value TEXT NOT NULL,
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (ScopeKey, Label)
);

CREATE TABLE IF NOT EXISTS PersonalMemoryAffinities
(
    ScopeKey TEXT NOT NULL REFERENCES PersonalMemoryScopes (ScopeKey) ON DELETE CASCADE,
    Item TEXT NOT NULL,
    Affinity TEXT NOT NULL CHECK (Affinity IN ('Like', 'Love', 'Dislike')),
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (ScopeKey, Item)
);

CREATE TABLE IF NOT EXISTS PersonalMemoryListItems
(
    ItemId BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ScopeKey TEXT NOT NULL REFERENCES PersonalMemoryScopes (ScopeKey) ON DELETE CASCADE,
    ListName TEXT NOT NULL,
    ItemKey TEXT NOT NULL,
    ItemValue TEXT NOT NULL,
    CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (ScopeKey, ListName, ItemKey)
);

CREATE INDEX IF NOT EXISTS IX_PersonalMemoryListItems_ScopeList
    ON PersonalMemoryListItems (ScopeKey, ListName, ItemId);

CREATE TABLE IF NOT EXISTS PersonalMemoryImports
(
    ImportName TEXT NOT NULL PRIMARY KEY,
    ImportedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    TenantCount INTEGER NOT NULL,
    SourceRevision BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS PersonalMemoryState
(
    StateKey TEXT NOT NULL PRIMARY KEY,
    Revision BIGINT NOT NULL DEFAULT 0,
    UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO PersonalMemoryState (StateKey, Revision)
VALUES ('personal-memory', 0)
ON CONFLICT (StateKey) DO NOTHING;
