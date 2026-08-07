# Jibo Internals Reference

Derived from a full eMMC dump of firmware 10.0.18 (`jibo_full_dump.bin`, 15 GB).
All findings here come from the read-only image.

## eMMC Partition Layout

| # | Name | Purpose |
|---|------|---------|
| 1 | rootfsA | Primary root filesystem slot |
| 2 | rootfsB | Secondary root filesystem slot |
| 3 | recovery | Recovery image area |
| 4 | services | Binds to `/usr/local` at boot |
| 5 | var | Persists `/var/jibo` state |
| 6 | skills | Binds to `/opt/jibo/Jibo/Skills` |

`rootfsA/B` are the A/B OTA slots. `services` contains the native binaries and
the Node SSM. `var` stores credentials, keys, and identity data. `skills`
contains the JavaScript skills, including `@be/be`.

## Boot Init Sequence

BusyBox SysV init executes every file matching `S*` in `/etc/init.d/` in
lexicographic order.

Important warning:

> Backups named `S*` are executed as scripts. Never leave a file like
> `S78something.orig` in `/etc/init.d/`.

Relevant scripts:

| Script | Purpose |
|--------|---------|
| `S21firewall` | iptables rules |
| `S36sshd` | SSH daemon |
| `S72jibo-apply-update` | OTA update application |
| `S76openjibo-bootstrap` | OpenJibo patch: hosts, CA, keys overlay, TLS |
| `S78jibo-system-manager` | Main SSM |

## SSM Internals

The SSM is a Node.js bundle at `/usr/local/bin/jibo-ssm/lib/skills-service-manager.js`.
Its region template lives at `/usr/local/bin/jibo-ssm/node_modules/@jibo/jibo-server-client/lib/region_config.json`, which is the bundle copy of the placeholder-based region routing config used during onboarding and conversion.

### Boot skill

`@be/be` is the only skill that runs in normal mode. `singleSkill: true` means
no other skills run concurrently from SSM startup.

### Service dependency graph

```text
ErrorService
  -> WifiService
GlobalManagerService
  -> KBService
        -> SchedulerService
SkillsService  (starts @be/be once KBService is ready)
RemoteService
NotificationsService
```

### Loop validation

`Loop#list()` is validated by `_isLoopGood(data)`. See
[loop-syncmanager-contract.md](loop-syncmanager-contract.md) for the dump-locked
contract (services-partition SyncManager sources are not readable on all hosts).

Critical requirements:

1. Exactly one loop in the array (OpenJibo scopes `List`/`ListLoops` to the
   calling robot when multiple dump-seeded loops exist).
2. `loop.members` must be a non-empty array.
3. `loop.members[].accountId` must include `loop.owner`.
4. `loop.members[].accountId` must include `loop.robot` (OpenJibo includes the
   `type=robot` member in List/LoopUpdated; portal ListMembers still hides it).
5. Live people should use status `accepted` (not a custom `active` value).

If the robot id is missing from the loop, SSM raises the same failure pattern
that leads to `Q4-Server_connection_lost`. Introductions does not call cloud
Loop APIs itself — it reads `jibo.kb.loop.loadLoop()` after SyncManager applies
`Loop#list()` / `LoopUpdated`.

Operator check after portal People add:

1. Cloud log: `LoopUpdated push … pushCount>0` (or portal response `syncedToRobot: true`).
2. Robot SSM: `pausing loop syncing` / `resuming loop syncing`.
3. Introductions menu shows the new person (`data.firstName` on the KB UserNode).
4. Diagnostics: `GET /api/portal/loop-sync-status`.

### WiFiManager server check

`WiFiManager._checkJiboServers()` runs about every 10 seconds and uses Node's
built-in `https` module to check `api.jibo.com`.

Important detail:

- Node's built-in CA bundle is used.
- `NODE_TLS_REJECT_UNAUTHORIZED=0` is required in `S78` for the local OpenJibo
  setup described in the companion runbook.

## Error Code Highlights

Relevant codes for the OpenJibo work:

| Code | Meaning | Typical action |
|------|---------|----------------|
| `L7` | Can't access sync service / STS init failed | reboot |
| `L8` | App sign-in required / STS key missing | wipe |
| `L9` | Initial SSM sync failed | reboot |
| `Q4` | Lost connection to Jibo's server | wifi |
| `OTA11` | Backup failed | dismiss |

## Jetstream Service

Region selection comes from `credentials.json` -> `region`.

For the working path:

- `api` routes to `api.jibo.com`
- `api` also maps the hub to `neohub.openjibo.com`
- notification WebSocket host becomes `api-socket.jibo.com`
- the Open Jibo managed path uses `api.openjibo.com`, `neohub.openjibo.com`, and `open-jibo-socket.openjibo.com`
- `libJiboServerService.so` does not use those JSON endpoint templates for `Notification.NewRobotToken`; it constructs both the signed URI and HTTPS destination as `credentials.region + ".jibo.com"`
- the supported conversion patches the two equal-length native `jibo.com` literals to `jibo.pro`, yielding `open-jibo.jibo.pro` when the active region is `open-jibo`
- `open-jibo.jibo.pro` is a direct API compatibility binding, not a redirect; the canonical configured API remains `api.openjibo.com`

Do not switch the credentials region to `openjibo-local` on this build. That
causes derived hosts like `openjibo-local.jibo.com`, which do not exist.

## Cloud API Protocol

Requests are HTTP POSTs to `https://api.jibo.com/` with:

```text
Content-Type: application/json
X-Amz-Target: <ServiceName>_<YYYYMMDD>.<OperationName>
```

The body is a flat JSON object.

### Loop management

`Loop_20160324.ListLoops` is the most important startup call. The loop object
must include the robot id that SSM expects in its local KB.

### Robot registry

Observed robot calls include:

- `Robot_20160225.GetRobot`
- `Robot_20160225.UpdateRobot`
- `Notification_20150505.NewRobotToken`

The robot sends the friendly/device id (`Ghost-Instance-Onion-Silk`) in those
calls, not the local KB robot id.

### STS

`Key_20160201` drives secure transfer setup. The important local file is:

```text
/var/jibo/keys/symmetric-<loopId>.json
```

For the default OpenJibo loop:

```text
/var/jibo/keys/symmetric-openjibo-default-loop.json
```

If `/var/jibo/keys` is not writable, STS fails with `L8-UGC_key_not_found`.

### OOBE

`OOBE_20161026` handles first-time setup:

- `PrepareRobot`
- `GetStatus`
- `SetupRobot`
- `ReconnectRobot`

`SetupRobot` is the call that claims a robot into a loop during first-time
setup.

### Updates

`Update_20160301` covers OTA metadata:

- `ListUpdates`
- `ListUpdatesFrom`
- `GetUpdateFrom`
- `CreateUpdate`
- `RemoveUpdate`

## Robot Files And Paths

Relevant on-device paths:

- `/var/jibo/credentials.json`
- `/var/jibo/keys/`
- `/var/jibo/identity/`
- `/var/jibo/asr/`
- `/usr/local/etc/jibo-jetstream-service.json`
- `/usr/local/etc/jibo-ssm/jibo-ssm-normal.json`
- `/opt/jibo/Jibo/Skills/@be/be/`
- `/opt/jibo/openjibo-bootstrap.sh`
- `/etc/init.d/S76openjibo-bootstrap`

## Skills Partition

`@be/be` is the main Jibo app and includes the idle, conversation, greetings,
games, settings, IFTTT, clock, and related flows.

Notable sub-skills:

- `@be/idle`
- `@be/first-contact`
- `@be/surprises`
- `@be/restore`
- `@be/clock`
- `@be/settings`
- `@be/radio`
- `@be/nimbus`
- `@be/tutorial`

## ASR Notes

The stock firmware uses a mix of cloud and on-device speech paths. The relevant
high-level configuration includes:

- Nuance NCS fallback
- Google credential-based speech path
- on-device hotphrase detection for `hey_jibo`

## Hardware Notes

Observed body-service axes:

- Pelvis: `/dev/ttyTHS1`
- Torso: `/dev/ttyTHS1`
- Neck/Head: `/dev/ttyTHS0`

Identity service backends observed:

- `eigenfaces`
- `deepid`
- `resnetfaceid`

## Why This Matters

This reference anchors the boot, loop, STS, and update contracts that OpenJibo
needs to satisfy for parity. It is especially useful when a cloud change causes
the robot to drift into `Q4`, `L7`, or `L8`.
