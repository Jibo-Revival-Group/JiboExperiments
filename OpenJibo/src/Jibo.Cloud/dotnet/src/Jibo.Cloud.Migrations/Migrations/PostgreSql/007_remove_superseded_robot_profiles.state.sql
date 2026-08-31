-- GetRobot historically changed the default device RobotId and wrote a replacement
-- profile without removing the previous profile linked to that same device. Remove
-- only superseded rows for which a correct same-device replacement already exists.
DELETE FROM RobotProfiles stale
USING Devices device
WHERE stale.DeviceId = device.DeviceId
  AND LOWER(stale.RobotId) <> LOWER(device.RobotId)
  AND EXISTS
  (
      SELECT 1
      FROM RobotProfiles current
      WHERE current.DeviceId = stale.DeviceId
        AND LOWER(current.RobotId) = LOWER(device.RobotId)
  );
