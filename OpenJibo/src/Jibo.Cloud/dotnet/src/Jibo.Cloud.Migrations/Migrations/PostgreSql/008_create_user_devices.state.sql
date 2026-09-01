-- Portal users own robots independently from the singleton cloud account.
-- A device has one current portal-user owner; pairing a new account transfers it.

CREATE TABLE IF NOT EXISTS UserDevices
(
    UserId TEXT NOT NULL REFERENCES Users (UserId) ON DELETE CASCADE,
    DeviceId TEXT NOT NULL REFERENCES Devices (DeviceId) ON DELETE CASCADE,
    ClaimSource TEXT NOT NULL DEFAULT 'portal-pairing',
    LinkedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (UserId, DeviceId)
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_UserDevices_Device
    ON UserDevices (DeviceId);

CREATE INDEX IF NOT EXISTS IX_UserDevices_User
    ON UserDevices (UserId, LinkedUtc DESC);
