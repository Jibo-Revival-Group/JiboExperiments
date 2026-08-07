# Loop SyncManager contract (dump-verified)

Services-partition SyncManager sources under `/run/media/zane/891f7d4a-…/bin`
are not readable on this host (mode `750` root:group). The contract below is
locked from:

- Artifact logs (`artifact-output/jibo-test-31`, `jibo-test-32`)
- Stock API shapes `S6`/`S9` in `loop-2016-03-24.min.json`
- On-robot `jibo-kb` + `@be/introductions` on the skills mount
- Prior OpenJibo runbook observations of `_isLoopGood` failure strings

## SyncManager behavior

1. Calls JSC **`Loop#list()`** / `ListLoops` (not ListMembers) for roster sync.
2. Validates with `_isLoopGood(data)` before writing KB UserNodes.
3. Registers for **`LoopUpdated`** notifications; on receive, pauses/resumes
   loop syncing (re-runs List). Observed log pair:
   `pausing loop syncing` → `resuming loop syncing`.
4. Periodic loop sync every **7200 seconds**.
5. Observed failure strings:
   - `JSC server call Loop#list() loop has no members`
   - `JSC server call Loop#list() robot <localKbRobotId> not in loop`

## `_isLoopGood` requirements

1. Exactly **one** loop in the List array.
2. `loop.members` is a non-empty array.
3. Some `members[].accountId` equals `loop.owner`.
4. Some `members[].accountId` equals `loop.robot` (OpenJibo: `type=robot`).
5. Live people use stock status **`accepted`** (`invited|accepted|declined|removed`).

## Stock List item shape (`S6`)

Required: `owner`. Members: `id`, `name`, `owner`, `robot`, `robotFriendlyId`,
`members` (`S9` list), `isSuspended`, `created`, `updated`.

## Stock Member shape (`S9` element)

Required: `id`, `loopId`, `status`, `type`.  
Names under nested `account.firstName` / `account.lastName` (SyncManager
flattens to UserNode `data.firstName` for introductions).

## Introductions menu gate

After KB write, `@be/introductions` calls `jibo.kb.loop.loadLoop()` and shows
nodes where `data.firstName` is truthy (`!isJibo`) and status is not
`declined`/`removed`. It does **not** call cloud Loop APIs.

## Robot file checklist (LoopUpdated → List → introductions)

Beyond credentials + jetstream, physical robots need:

| Path | Role |
|------|------|
| `/etc/hosts` and/or `/var/etc/hosts` (mode **644**) | Only when pointing at a raw LAN IP: map `api.jibo.com`, **`api-socket.jibo.com`**, `open-jibo-socket.openjibo.com`, `neohub.openjibo.com` |
| `/usr/local/etc/jibo-server-service.json` | `NotificationSubsystem.serverURLSuffix` = `-socket.openjibo.com` |
| OpenJibo CA bind + OpenSSL hash links | Native WSS on `:443` trusts local/OpenJibo CA |
| `S78` `NODE_TLS_REJECT_UNAUTHORIZED=0` | WiFiManager Node HTTPS |
| Writable `/var/jibo/keys/` | STS `symmetric-<loopId>.json` |
| JSC `rejectUnauthorized=false` patches | Node JSC HTTPS to local cert |
| `/opt/jibo/Knowledge/.../nodes` | SyncManager write; introductions `loadLoop()` read |

Native NotificationSubsystem uses `wss://api-socket.jibo.com/{token}` on **:443**,
not Node `wsendpoint`.

### `point-at-server.sh` (BEam, domain-first)

`~/BEam/point-at-server.sh` covers jetstream hub
override, server-service socket suffix, credentials `region`+`endpoint`, and
**hosts rewrite only for IPv4**. Domains are the normal path (DNS must resolve
legacy names). Hub port and credentials/JSC API port are prompted separately
(examples: hub `443`, credentials `8765` or `24605`). It does **not** install CA,
S78, keys overlay, or JSC patches (OpenJibo bootstrap).

### Cloud env

Set `OpenJibo__Robot__RobotId=<local KB hex>` so `loop.robot` matches SyncManager
after portal mutations. Portal People and `Loop#list` share one loop via
`LoopRosterResolver` (never more than one List item).

## Verification

1. Run `point-at-server.sh` with domain (or LAN IP) + hub + credentials ports.
2. Confirm CA/bootstrap if using local TLS OpenJibo.
3. `GET /api/portal/loop-sync-status` → `liveApiSocketOverlaps > 0` /
   `openApiSocketConnections > 0`.
4. After portal People add: response `syncedToRobot: true`, `pushCount > 0`.
5. SSM: no `robot … not in loop`; after `LoopUpdated`, pause/resume loop syncing;
   introductions menu shows the new person.
