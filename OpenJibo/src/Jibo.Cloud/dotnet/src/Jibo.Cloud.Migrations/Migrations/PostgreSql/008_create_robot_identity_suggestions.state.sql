CREATE TABLE IF NOT EXISTS RobotIdentitySuggestions
(
    ObservedDeviceId TEXT NOT NULL REFERENCES Devices (DeviceId) ON DELETE CASCADE,
    ProposedRobotId TEXT NOT NULL,
    ObservationCount INTEGER NOT NULL DEFAULT 1 CHECK (ObservationCount > 0),
    FirstObservedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    LastObservedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    Evidence JSONB NOT NULL DEFAULT '[]'::JSONB,
    DismissedUtc TIMESTAMPTZ NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_RobotIdentitySuggestions_Identity_CI
    ON RobotIdentitySuggestions (LOWER(ObservedDeviceId), LOWER(ProposedRobotId));

CREATE INDEX IF NOT EXISTS IX_RobotIdentitySuggestions_Pending
    ON RobotIdentitySuggestions (LOWER(ObservedDeviceId), ObservationCount DESC, LastObservedUtc DESC)
    WHERE DismissedUtc IS NULL;
