-- A hub token records the hardware identity observed on the wire. That identity is not
-- necessarily a registered inventory device yet, so it must not be constrained to Devices.
ALTER TABLE CloudAuthTokens
    DROP CONSTRAINT IF EXISTS cloudauthtokens_deviceid_fkey;
