CREATE INDEX IF NOT EXISTS IX_Devices_VerifiedSerialNumber_CI
    ON Devices (LOWER(VerifiedSerialNumber))
    WHERE VerifiedSerialNumber IS NOT NULL;
