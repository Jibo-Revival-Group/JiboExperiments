# Jibo Internals Reference

Derived from a full eMMC dump of firmware 10.0.18 (`jibo_full_dump.bin`, 15 GB).
All findings are from the read-only image — nothing is inferred or guessed.

---

## eMMC Partition Layout

| # | Name | Start LBA | End LBA | Size | Filesystem |
|---|------|-----------|---------|------|------------|
| 1 | rootfsA | 34 | 2 048 033 | 1 000 MB | ext4 |
| 2 | rootfsB | 2 048 034 | 4 096 033 | 1 000 MB | ext4 |
| 3 | recovery | 4 096 034 | 4 198 433 | 50 MB | (empty/raw) |
| 4 | services | 4 198 434 | 8 294 433 | 2 000 MB | ext4 |
| 5 | var | 8 294 434 | 9 318 433 | 500 MB | ext4 |
| 6 | skills | 9 318 434 | 30 777 310 | 10 478 MB | ext4 |

**rootfsA/B** are the two root filesystem slots for A/B OTA updates.
**services** is bind-mounted to `/usr/local` at boot — contains all native binaries and Node SSM.
**var** is `/var/jibo` state (credentials, keys, identity, ASR data).
**skills** is `/opt/jibo/Jibo/Skills` — all JavaScript skills including `@be/be`.

---

## Boot Init Sequence (`/etc/init.d/` in rootfsA)

BusyBox SysV init executes every file matching `S*` in `/etc/init.d/` in
lexicographic order.

> ⚠️ **Backups named `S*` are executed as scripts.** Never leave a file like
> `S78something.orig` or `S78something.bak` in `/etc/init.d/`. It will launch
> a second copy of that service at boot.

| Script | Purpose |
|--------|---------|
| `S00fix-os` | Early OS fixups |
| `S01logging` | Syslog daemon |
| `S06coredumps` | Coredump config |
| `S09wifi-enable` | WiFi hardware enable |
| `S12dns-prime` | DNS priming |
| `S15crond` | Cron daemon |
| `S18udev` | udev rules |
| `S21firewall` | iptables rules (SSH port 22 must be allowed here for dev access) |
| `S24cpufreq` | CPU frequency governor |
| `S30urandom` | /dev/urandom seed |
| `S33dbus` | D-Bus daemon |
| `S36sshd` | SSH daemon |
| `S39audio-enable` | Audio hardware enable |
| `S42avahi-setup.sh` | mDNS/Avahi setup |
| `S45network` | Network configuration |
| `S48avahi-daemon` | Avahi daemon |
| `S51upload-logs` | Log upload |
| `S54modules` | Kernel module loading |
| `S57alsa-volume` | ALSA volume init |
| `S60alsaloopback` | ALSA loopback |
| `S63body-board-power` | Body board power |
| `S66ntp` | NTP time sync |
| `S69start-X11` | X11 display server |
| `S72jibo-apply-update` | OTA update application |
| `S75jibo-service-registry` | Service registry daemon |
| `S76openjibo-bootstrap` | **OpenJibo patch** — hosts, CA, keys overlay, TLS |
| `S78jibo-system-manager` | **Main SSM** — launches all Jibo services and skills |
| `S81named` | DNS resolver |
| `S84identity-syslog` | Identity service syslog |

---

## `/etc/init.d/S78jibo-system-manager` (stock)

```sh
#!/bin/sh
set -e
# NOTE: stock image does NOT have NODE_TLS_REJECT_UNAUTHORIZED.
# OpenJibo adds: export NODE_TLS_REJECT_UNAUTHORIZED=0
# without it, WiFiManager._checkJiboServers() fails every 10s → Q4

PROCESS=jibo-system-manager
BIN_DIR=/usr/local/bin
CFG_DIR=/usr/local/etc
```

Modes accepted by `jibo-getmode`: `oobe`, `int-developer`, `developer`,
`normal`, `certification`, `service`. Any other mode → SSM exits immediately.

---

## Services Partition Layout (`/usr/local` → services partition)

### Native Binaries (`/usr/local/bin`)

| Binary | Purpose |
|--------|---------|
| `jibo-system-manager` | Top-level service supervisor |
| `jibo-service-registry` | Service discovery (port 8181) |
| `jibo-ssm/` | Node.js Skills Service Manager (JavaScript, bundled) |
| `jibo-jetstream-service` | WebSocket/hub relay (port 8090) |
| `jibo-server-service` | Cloud notification client (port 8888) |
| `jibo-body-service` | Motors, IMU, LEDs, touch (port 8282) |
| `jibo-audio-service` | ALSA audio I/O, hotphrase detection (port 8383) |
| `jibo-asr-service` | Speech recognition (Nuance + Google, port 8088) |
| `jibo-tts-service` | Text-to-speech |
| `jibo-nlu-service` | Natural language understanding (port ~8089) |
| `jibo-identity-service` | Face recognition / DeepID (port 8489) |
| `jibo-lps-service` | Local perception (camera, face tracking) |
| `jibo-media-service` | Media management |
| `jibo-sts` | Secure Transfer Service (symmetric key exchange) |
| `jibo-system-backup` | Backup utility |
| `jibo-system-restore` | Restore utility |
| `jibo-system-monitoring-service` | System health monitoring |
| `jibo-certification-service` | Factory certification |
| `jibo-service-center-service` | Service center diagnostics |
| `jibo-index-body` | Motor index/homing routine |
| `head-on` / `head-off` | Head power control |
| `torso-on` / `torso-off` | Torso power control |
| `jibo-getmode` | Returns current robot mode string |
| `stm32flash` | STM32 microcontroller firmware updater |

### Service Config Files (`/usr/local/etc`)

All configs are JSON. Default paths shown.

---

## Service Port Map

| Port | Service |
|------|---------|
| 8001 | NotificationsService (SSM internal) |
| 8088 | jibo-asr-service HTTP |
| 8090 | jibo-jetstream-service (Jetstream/hub WebSocket) |
| 8181 | jibo-service-registry |
| 8282 | jibo-body-service |
| 8338 | GlobalManagerService (SSM internal) |
| 8383 | jibo-audio-service |
| 8489 | jibo-identity-service |
| 8585 | jibo-system-manager WebCore |
| 8668 | WifiService (SSM internal) |
| 8778 | KBService (SSM internal) |
| 8779 | SkillsService (SSM internal) |
| 8888 | jibo-server-service |
| 10004 | ErrorService (SSM internal) |
| 10005 | SchedulerService (SSM internal) |
| 10321 | RemoteService (SSM internal) |

---

## SSM (Skills Service Manager) Internals

The SSM is a Node.js bundle at `/usr/local/bin/jibo-ssm/lib/skills-service-manager.js`
(~740 KB, CommonJS bundle, not minified beyond name mangling).

### Boot skill

```json
"SkillsService": {
  "startSkill": "@be/be",
  "singleSkill": true,
  "port": 8779
}
```

`@be/be` is the only skill that runs in normal mode. `singleSkill: true` means
no other skills run concurrently from SSM startup.

### Service dependency graph (startup order)

```
ErrorService
  └─ WifiService
GlobalManagerService
  └─ KBService
        └─ SchedulerService
SkillsService  (starts @be/be once KBService is ready)
RemoteService
NotificationsService
```

### `_isLoopGood(data)` — exact source

Called after every `Loop#list()` cloud sync. `data` is the array returned by
`Loop_20160324.ListLoops`.

```javascript
_isLoopGood(data) {
    if (!data || !Number.isInteger(data.length) || data.length === 0) {
        this._errorOnce('JSC Loop#list() account ' + this.robotAccountId
            + ' does not have a loop');
        return false;
    }
    if (data.length !== 1) {
        this._errorOnce('JSC Loop#list() account ' + this.robotAccountId
            + ' is returning multiple loops');
        return false;
    }
    let loop = data[0];
    let members = loop.members;
    if (!members || !Number.isInteger(members.length) || members.length === 0) {
        this._errorOnce('JSC server call Loop#list() loop has no members');
        return false;
    }
    let loopAccountIds = loop.members.map(element => element.accountId);
    if (!loopAccountIds.includes(loop.owner)) {
        this._errorOnce('JSC server call Loop#list() owner not in loop for robot '
            + this.robotAccountId);
    }
    if (!loopAccountIds.includes(loop.robot)) {
        this._errorOnce('JSC server call Loop#list() robot '
            + this.robotAccountId + ' not in loop');
    }
    return true;
}
```

**Critical requirements for `ListLoops` response:**
1. Exactly one loop in the array (`data.length === 1`).
2. `loop.members` is a non-empty array.
3. `loop.members[].accountId` must include `loop.owner`.
4. `loop.members[].accountId` must include `loop.robot`.

Both owner and robot checks log errors but only members-empty returns `false`.
`loop.robot` must equal the numeric robot id SSM has in its local KB
(`5a0b6398faa0f0001c5d0df1` for Ghost-Instance-Onion-Silk), not the friendly id.

### `WiFiManager._checkJiboServers()` — exact source

Called every ~10 seconds via `verifyConnection()`.

```javascript
_checkJiboServers() {
    return new Promise((resolve, reject) => {
        let options = {
            host: this._jiboServerUrl,   // "api.jibo.com"
            path: '/'
        };
        let req = https.get(options, (res) => { ... resolve() ... });
        req.on('error', (e) => {
            // on error: tries time sync, then rejects → Q4
            reject(e);
        });
        req.on('socket', function(socket) {
            socket.setTimeout(15000);
            socket.on('timeout', function() { req.abort(); });
        });
    });
}
```

Uses Node's built-in `https` module which carries its **own compiled-in CA bundle**
(Node v6). The system CA bundle is irrelevant for this call. This is why
`NODE_TLS_REJECT_UNAUTHORIZED=0` is required in S78: no amount of CA bundle
patching will make this call trust a self-signed cert.

`_jiboServerUrl` is set from `WifiService` config:

```json
"WifiService": { "port": 8668, "region": "api" }
```

The `region` selects `api → entrypoint_hostname: api.jibo.com` from
`jibo-jetstream-service.json`, which is what `_jiboServerUrl` resolves to.

### Error Code Table (complete)

Extracted from `ErrorCodes.json` embedded in the SSM bundle.

| Code | Id | Title | Tap action | Priority | Repeat (ms) |
|------|----|-------|-----------|---------|-------------|
| B1 | `B1-Head_overtemp` | Head overtemperature | reboot | 1 | 900 000 |
| B2 | `B2-Torso_overtemp` | Torso overtemperature | reboot | 1 | 900 000 |
| B3 | `B3-Pelvis_overtemp` | Pelvis overtemperature | reboot | 1 | 900 000 |
| C1 | `C1-Head_undertemp` | Head undertemperature | reboot | 1 | — |
| C2 | `C2-Torso_undertemp` | Torso undertemperature | reboot | 1 | — |
| C3 | `C3-Pelvis_undertemp` | Pelvis undertemperature | reboot | 1 | — |
| D1 | `D1-Processor_overtemp` | Processor overtemperature | reboot | 1 | 900 000 |
| E1 | `E1-Head_encoder` | Head encoder fault | dismiss | 3 | — |
| E2 | `E2-Torso_encoder` | Torso encoder fault | dismiss | 3 | — |
| E3 | `E3-Pelvis_encoder` | Pelvis encoder fault | dismiss | 3 | — |
| F1 | `F1-Head_index_flag` | Head index flag fault | dismiss | 3 | — |
| F2 | `F2-Torso_index_flag` | Torso index flag fault | dismiss | 3 | — |
| F3 | `F3-Pelvis_index_flag` | Pelvis index flag fault | dismiss | 3 | — |
| F4 | `F4-Index_timeout` | Motor index timeout | dismiss | 3 | — |
| H1 | `H1-Head_BB_crash` | Head body-board crash | reboot | 2 | — |
| H2 | `H2-Torso_BB_crash` | Torso body-board crash | reboot | 2 | — |
| H3 | `H3-Pelvis_BB_crash` | Pelvis body-board crash | reboot | 2 | — |
| J1 | `J1-Skill_crash` | Skill crashed | dismiss | 5 | — |
| K1 | `K1-Battery_undertemp` | Battery undertemperature | none | 2 | — |
| K2 | `K2-Battery_overtemp` | Battery overtemperature | none | 2 | — |
| K3 | `K3-Battery_not_installed` | Battery not installed | none | 1 | — |
| K4 | `K4-Low_battery` | Low battery | none | 4 | — |
| L1 | `L1-Cannot_connect_to_speech_server` | Can't access speech service | reboot | 9 | 1 800 000 |
| L2 | `L2-Cannot_connect_to_server` | Can't access server | reboot | 9 | 1 800 000 |
| L3 | `L3-Cannot_connect_to_Bing_server` | Can't access Bing | dismiss | 11 | — |
| L4 | `L4-Cannot_connect_to_Music_server` | Can't access music service | dismiss | 11 | — |
| L5 | `L5-Cannot_connect_to_3rd_party_server` | Can't access 3rd party | dismiss | 11 | — |
| L6 | `L6-Cannot_connect_to_Wolfram_server` | Can't access Wolfram | dismiss | 11 | — |
| L7 | `L7-Cannot_connect_to_auth_server` | Can't access sync service — **STS init failed** | reboot | 9 | 1 800 000 |
| L8 | `L8-UGC_key_not_found` | App sign-in required — **STS EROFS / key missing** | wipe | 10 | 1 800 000 |
| L9 | `L9-Cannot_connect_to_sync_server` | Reboot needed — **initial SSM sync failed** | reboot | 8 | 1 800 000 |
| M1 | `M1-Camera_failure` | Camera failure | dismiss | 3 | — |
| N1 | `N1-Service_crash_asr` | ASR service crashed | reboot | 3 | — |
| N2 | `N2-Service_crash_tts` | TTS service crashed | reboot | 3 | — |
| N3 | `N3-Service_crash_nlu` | NLU service crashed | reboot | 3 | — |
| N5 | `N5-Service_crash_ssm` | SSM crashed | reboot | 3 | — |
| N6 | `N6-Service_crash_body` | Body service crashed | reboot | 3 | — |
| N7 | `N7-Service_crash_lps` | LPS crashed | reboot | 3 | — |
| N8 | `N8-Service_crash_audio` | Audio service crashed | reboot | 3 | — |
| N9 | `N9-Service_crash_identity` | Identity service crashed | reboot | 3 | — |
| O1 | `O1-Microphone_failure` | Microphone failure | dismiss | 3 | — |
| OTA11 | `OTA11-Backup_failed` | Backup failed | dismiss | 12 | 900 000 |
| P1 | `P1-Low_Robot_Storage` | Low robot storage | dismiss | 11 | 1 800 000 |
| Q1 | `Q1-Lost_Wi-Fi_connection` | Wi-Fi connection lost | wifi | 6 | 2 000 |
| Q4 | `Q4-Server_connection_lost` | Lost connection to Jibo's server | wifi | 6 | 1 800 000 |
| R1 | `R1-Restore_failed` | Restore failed | reboot | 1 | 900 000 |
| S1 | `S1-Maintenance_mode` | Server upgrades in progress | none | 7 | 900 000 |
| T1 | `T1-Geolocation_failed` | Can't access location service | dismiss | 10 | 1 800 000 |

**Priority** — lower number = more critical (priority 1 blocks everything).  
**Tap action** — what happens when user taps error screen: `wifi` opens WiFi settings,
`reboot` reboots, `wipe` factory wipes, `dismiss` clears.  
**Repeat time** — how often the error is re-raised if still present.

---

## Jetstream Service (`jibo-jetstream-service.json`)

The Jetstream service proxies audio and intent data between the robot and the
cloud hub via WebSocket.

### Region routing

Selected by `credentials.json` → `region` field:

```json
"region-settings": {
  "api":                { "hub_port": 443, "hub_hostname": "neo-hub.jibo.com",          "entrypoint_hostname": "api.jibo.com" },
  "dev-entrypoint":     { "hub_port": 443, "hub_hostname": "dev-hub.jibo.com",          "entrypoint_hostname": "dev-entrypoint.jibo.com" },
  "alpha-entrypoint":   { "hub_port": 443, "hub_hostname": "alpha-hub.jibo.com",        "entrypoint_hostname": "alpha-entrypoint.jibo.com" },
  "stg-entrypoint":     { "hub_port": 443, "hub_hostname": "stg-hub.jibo.com",          "entrypoint_hostname": "stg-entrypoint.jibo.com" },
  "preprod-entrypoint": { "hub_port": 443, "hub_hostname": "preprod-hub.jibo.com",      "entrypoint_hostname": "preprod-entrypoint.jibo.com" }
}
```

`HubClient.override` in the stock image routes to `api.5x1.com:80`.
Remove `override` to use the `region-settings` → `api` path for OpenJibo.

> ⚠️ Changing `region` to a value not in `region-settings` (e.g. `openjibo-local`)
> causes STS and server-service to derive `openjibo-local.jibo.com` — which does
> not exist. Always keep `region: api`.

### Audio encoding

```json
"encoding_type": "OGG_OPUS",
"encoding-settings": {
  "OGG_OPUS": { "streaming_rate": 1.2, "channels": 1, "sample_rate": 16000, "bitrate": 64000, "vbr": true },
  "FLAC":     { "streaming_rate": 3.0, "channels": 1, "sample_rate": 16000, "bps": 16 }
}
```

Turns are OGG_OPUS, 16 kHz mono, 64 kbps VBR by default.

### Hub URLs used by Jetstream

```
POST https://api.jibo.com/v1/proactive   (proactive_url)
POST https://api.jibo.com/v1/listen      (listen_url)
WSS  wss://neo-hub.jibo.com/             (hub WebSocket)
```

---

## Server Service (`jibo-server-service.json`)

Manages the cloud notification WebSocket.

```json
"NotificationSubsystem": {
  "registryPort": 8181,
  "refreshInterval": 15000,
  "serverURLSuffix": "-socket.jibo.com"
}
```

The notification WebSocket host is built as:
`<region><serverURLSuffix>` → for region `api`:
```
api-socket.jibo.com
```

This is why `/etc/hosts` must map `api-socket.jibo.com` to the Mac along with
`api.jibo.com` and `neo-hub.jibo.com`.

---

## Cloud API Protocol (`jibo-server-client`)

All requests are HTTP POST to `https://api.jibo.com/` with:

```
Content-Type: application/json
X-Amz-Target: <ServiceName>_<YYYYMMDD>.<OperationName>
```

Body is a flat JSON object (no envelope wrapper).

### `Loop_20160324` — Loop management

#### `ListLoops` ← most important for boot

**Target:** `Loop_20160324.ListLoops`  
**Input:** `{ loopId?: string }`  
**Output:** array of Loop objects

Loop object shape:
```json
{
  "id": "string",
  "name": "string",
  "owner": "string",         ← accountId of loop owner (required)
  "robot": "string",         ← numeric robot id (5a0b6398...)
  "robotFriendlyId": "string",
  "isSuspended": false,
  "created": 0,
  "updated": 0,
  "members": [
    {
      "id": "string",        ← member record id (required)
      "loopId": "string",    ← (required)
      "accountId": "string", ← must equal loop.owner OR loop.robot
      "status": "active",    ← (required)
      "type": "owner|robot", ← (required)
      "account": {
        "email": "string",
        "firstName": "string",
        "lastName": "string",
        "gender": "string",
        "birthday": 0,
        "photoUrl": "string",
        "isChild": false,
        "messagingAllowed": false
      },
      "enrolled": { "face": false, "voice": false },
      "nickname": null,
      "phoneticName": null,
      "legalGuardianId": null,
      "created": 0
    }
  ]
}
```

#### Other Loop operations

| Target | Key input fields | Notes |
|--------|-----------------|-------|
| `InviteMember` | `loopId`, `email`, `firstName`, `lastName`, `gender` | Returns full loop |
| `RemoveMember` | `loopId`, `id` | `id` = member record id |
| `ListMembers` | `statusList`, `typeList` | Filters |
| `UpdateMember` | `loopId`, `id`, name/gender fields | |
| `SetEnrollment` | `loopId`, `id`, `face`, `voice` | |
| `SuspendLoop` | `loopId` | Returns `{result}` |
| `ClearRobot` | `robotId` | Detaches robot from loop |
| `GetRobot` | `loopId` | Returns `{accessKeyId, secretAccessKey, friendlyId}` |

---

### `Robot_20160225` — Robot registry

| Target | Input | Output |
|--------|-------|--------|
| `GetRobot` | `id` (friendlyId), `serialNumber?` | `{id, payload, calibrationPayload, updated, created}` |
| `UpdateRobot` | `id` (friendlyId) *(required)*, `payload` (object) *(required)* | `{result}` |
| `GetCalibrationData` | `id`, `serialNumber?` | `{id, calibrationPayload}` |
| `GetFriendlyIds` | `count` | array of strings |
| `RemoveRobot` | `id`, `serialNumber?` | `{result}` |

Observed call from Ghost-Instance-Onion-Silk:

```json
{
  "id": "Ghost-Instance-Onion-Silk",
  "payload": {
    "SSID": "TaylorSwift-Fi_Plus",
    "connectedAt": 1781280301142,
    "platform": "12.10.0",
    "serialNumber": "BOJW-1000-0017-0820-0020"
  }
}
```

---

### `Notification_20150505` — Push / robot token

| Target | Input | Output |
|--------|-------|--------|
| `NewRobotToken` | `deviceId` (friendlyId) | `{token}` |
| `GetStatus` | `accountId` *(required)* | `{connected: boolean}` |

`NewRobotToken` is called early in boot with the friendly id. The returned
`token` is used to authenticate the robot's WebSocket session with `neo-hub`.

---

### `Key_20160201` — Secure Transfer Service (STS)

| Target | Input | Output |
|--------|-------|--------|
| `ShouldCreate` | `loopId` *(required)* | `{shouldCreate: boolean}` |
| `CreateRequest` | `loopId`, `publicKey` | `{id, accountId, loopId, publicKey, encryptedKey}` |
| `GetRequest` | `id` | same as above |
| `Share` | `id`, `encryptedKey`, `keyHash?` | same |
| `Backup` | `loopId`, `encryptedKey`, `passwordHash?` | `{loopId, accountId, encryptedKey}` |
| `Restore` | `loopId`, `passwordHash?` | same |
| `ListIncomingRequests` | `loopId` | array of key request objects |
| `ListBinaryRequests` | `loopId` | array |
| `ShareBinary` | `body` (blob), `id` | `{id, accountId, loopId, encryptedUrl, decryptedUrl}` |

STS writes the symmetric key to:
```
/var/jibo/keys/symmetric-<loopId>.json
```

For the default OpenJibo loop:
```
/var/jibo/keys/symmetric-openjibo-default-loop.json
```

This path requires a **writable `/var/jibo/keys`** (see bootstrap writable overlay).
If the write fails with EROFS → `L8-UGC_key_not_found`.

---

### `Update_20160301` — OTA updates

| Target | Input | Output |
|--------|-------|--------|
| `ListUpdates` | `subsystem?`, `filter?` | array of update records |
| `ListUpdatesFrom` | `fromVersion` *(required)*, `subsystem?`, `filter?` | array |
| `GetUpdateFrom` | `fromVersion` *(required)*, `subsystem?`, `filter?` | single update |
| `CreateUpdate` | `fromVersion`, `toVersion`, `changes`, `body` (binary), `subsystem`, `filter`, `dependencies` | update record |
| `RemoveUpdate` | `id` | update record |

Update record shape:
```json
{
  "_id": "string",
  "fromVersion": "string",
  "toVersion": "string",
  "changes": "string",
  "url": "string",
  "shaHash": "string",
  "length": 0,
  "subsystem": "robot",
  "filter": "string",
  "dependencies": {}
}
```

---

### `Media_20160725` — Photo/video storage

| Target | Input | Output |
|--------|-------|--------|
| `Create` | `body` (blob), `loopId`, `path`, `type`, `reference`, `isEncrypted`, `meta` | media record |
| `List` | `loopIds` *(required)*, `after?`, `before?` | — |
| `Get` | `paths` *(required)* | — |
| `Remove` | `paths` *(required)* | — |

---

### `Account_20151111` — User accounts

Full CRUD for user accounts. Key operations:

| Target | Notes |
|--------|-------|
| `Login` | Returns `{id, accessKeyId, secretAccessKey, ...}` |
| `Create` | New account + returns credentials |
| `ResetKeys` | Rotates `accessKeyId`/`secretAccessKey` |
| `CreateHubToken` | Short-lived JWT for hub auth |
| `GetAccountByAccessToken` | Validate a hub token |

---

### `OOBE_20161026` — Out-of-box experience

| Target | Input | Output |
|--------|-------|--------|
| `PrepareRobot` | `loopId?` | `{token, expires}` |
| `GetStatus` | `{token, expires}` | `{complete: boolean}` |
| `SetupRobot` | `{token, id (friendlyId)}` | `{accessKeyId, secretAccessKey, serviceMode}` |
| `ReconnectRobot` | `{token, id?}` | `{result}` |

`SetupRobot` is the call that claims a robot into a loop during first-time setup.

---

### `Settings_20171219` — Skill settings

| Target | Input | Notes |
|--------|-------|-------|
| `GetSettings` | `loopId`, `transId`, `skills?`, `getView?` | Retrieves skill setting values |
| `UpdateSettings` | `loopId`, `transId`, `data` (JSON string) | |
| `DeleteSettings` | `loopId`, `transId`, `data` (map) | |

---

### `Person_20160801` — Loop/person properties

| Target | Input | Notes |
|--------|-------|-------|
| `SetLoopProperty` | `loopId`, `key`, `value` | Arbitrary KV store per loop |
| `GetLoopProperties` | `loopId`, `keys` | |
| `SetAccountProperty` | `key`, `value` | Per-account KV store |
| `Answer` | `key`, `answer` | Personal memory answers |
| `EnableHolidays` / `DisableHolidays` | `loopId`, `ids` | Holiday feature flags |

---

### `GQA_20160930` — General Question Answering

| Target | Input | Output |
|--------|-------|--------|
| `Question` | `Input` (text), `Intent?`, `Latitude?`, `Longitude?`, `Country?`, `HasKid?`, `Timezone?` | `{success, source, answer, message, type, response}` |

Used by Be for "Hey Jibo, what is X?" queries.

---

### `ROM_20171011` — Robot certificate / mTLS setup

| Target | Input | Output |
|--------|-------|--------|
| `SetupServer` | `ipAddress`, `ipAddresses?` | `{cert, public, private, fingerprint}` |
| `SetupClient` | `friendlyId` | `{cert, public, private, p12, fingerprint, payload}` |
| `Create` | `friendlyId`, `aco?` | `{created}` |

ROM is the certificate provisioning service for robot-local mTLS connections.

---

## ASR Service Config Highlights

```json
"cloud_url": "https://jibo-ncs-engusa-http.nuancemobility.net/NmspServlet/",
"cloud_appid": "HTTP_NMDPPRODUCTION_Jibo_Jibo_Robot_20151231124503",
"nuance_uId": "b8fb02f2c5794963aaafb8c716ef384c",
"google_credential": "/usr/local/share/asr/google_asr/credentials-key.json",
"resident_task": "... hotphrase: hey_jibo ...",
"dictation_type": "dictation"
```

- Primary cloud ASR: Google (credential file on `/usr/local/share`).
- Hotphrase detection: Sensory on-device model at `/usr/local/share/asr/hey_jibo`.
- Speaker ID: Sensory TD model at `/usr/local/share/asr/sensory_spkr_id_td`.
- Fallback: Nuance NCS.

---

## Body Service Hardware Details

From `jibo-body-service.json`:

| Axis | Device | Offset (rad) | Flipped |
|------|--------|-------------|---------|
| Pelvis | `/dev/ttyTHS1` | 2.742 | yes |
| Torso | `/dev/ttyTHS1` | 0.052 | yes |
| Neck/Head | `/dev/ttyTHS0` | 0.061 | yes |

Temperature thresholds:
- CPU high: 90°C / low: 85°C
- Battery: 0°C min — 47°C max
- Low battery capacity: < 42% high threshold, < 35% low threshold

---

## Identity Service — Face Recognition

Three identifier backends (configured in `jibo-identity-service.json`):

| Backend | Type | Model path |
|---------|------|-----------|
| `eigenfaces` | PCA eigenfaces | `/var/jibo/identity/eigenfaces.data` |
| `deepid` | DeepID CNN (Caffe) | `/usr/local/share/lps/deepid/CASIA_iter_666000.caffemodel` |
| `resnetfaceid` | ResNet (dlib) | `/usr/local/share/identity/resnet/dlib_face_recognition_resnet_model_v1.dat` |

Active identifier: `deepid`. Data stored at `/var/jibo/identity/`.

---

## Skills Partition (`/opt/jibo` → skills partition)

Mount point: `/opt/jibo`

### Skill directory layout

```
/opt/jibo/
  jibo/
    Jibo/
      Skills/
        @be/
          be/                 ← main Jibo app (version 10.0.18)
        oobe-config/          ← out-of-box experience UI
        jibo-diagnostics/
        jibo-rhino/           ← Rhino game
        jibo-trivia/          ← Trivia game
        jibo-tbd/
        fin-goods-test/
    Knowledge/
    Photos/
    Recordings/
    mfg-test/
  ota/                        ← OTA staging area
  tmp/
  coredumps/
```

### `@be/be` skill (version 10.0.18)

`@be/be` is the entire Jibo application — idle, conversation, greetings, games,
settings, IFTTT, clock, etc. It is an Electron/browser app bundled as `index.js`
with an `index.html` entry point.

**Sub-skills loaded by `@be/be`:**

| Skill | Purpose |
|-------|---------|
| `@be/idle` | **Default** — idle face, animations when not spoken to |
| `@be/first-contact` | **First skill on new robot** — OOBE intro |
| `@be/surprises` | **EOS skill** — "surprise me" and end-of-session |
| `@be/restore` | **Restore skill** — post-backup restore flow |
| `@be/clock` | Clock / time display |
| `@be/circuit-saver` | Screen saver |
| `@be/main-menu` | Settings screen menu |
| `@be/settings` | Settings management |
| `@be/create` | Content creation |
| `@be/exercise` | Fitness skill |
| `@be/friendly-tips` | Tips and hints |
| `@be/gallery` | Photo gallery |
| `@be/greetings` | Greeting people by face/name |
| `@be/hue-control` | Philips Hue integration |
| `@be/ifttt` | IFTTT triggers |
| `@be/introductions` | Name learning / introductions |
| `@be/remote` | Remote control |
| `@be/nimbus` | Weather skill |
| `@be/radio` | Music / radio |
| `@be/surprises-date` | Date-based surprises |
| `@be/surprises-ota` | Post-update surprises |
| `@be/tutorial` | Tutorial for new users |
| `@be/who-am-i` | Identity / face enrollment flow |
| `@be/word-of-the-day` | Word of the day |

**Key dependencies:**
- `jibo` ^14.0.0 — robot SDK
- `jibo-client-framework` ^4.0.0 — SSM/service communication
- `@jibo/chitchat-mims` ^3.0.0 — conversation MIMs
- `jibo-anim-db-animations` 19.0.2 — animation database

**Config files** (`config/`):
- `be-normal.json` — logging only (syslog info to 127.0.0.1:514)
- `be-developer.json` / `be-int-developer.json` — developer overrides
- `be-oobe.json` — OOBE mode config

---

## Key File Paths on Robot

| Path | Contents |
|------|----------|
| `/var/jibo/credentials.json` | `{accessKeyId, secretAccessKey, region}` |
| `/var/jibo/keys/symmetric-<loopId>.json` | STS symmetric key (written by STS) |
| `/var/jibo/identity/` | Face recognition data |
| `/var/jibo/asr/` | Speaker ID and name learning data |
| `/var/jibo/imu/imu-cal.json` | IMU calibration |
| `/var/jibo/lps/` | Camera calibration |
| `/usr/local/etc/jibo-jetstream-service.json` | Jetstream config (region, override) |
| `/usr/local/etc/jibo-ssm/jibo-ssm-normal.json` | SSM config for normal mode |
| `/opt/jibo/Jibo/Skills/@be/be/` | Be skill (main Jibo app) |
| `/etc/init.d/S78jibo-system-manager` | SSM init script |
| `/etc/init.d/S76openjibo-bootstrap` | OpenJibo bootstrap |
| `/opt/jibo/openjibo-bootstrap.sh` | Bootstrap script |
| `/opt/jibo/openjibo-ca.crt` | OpenJibo CA cert |
