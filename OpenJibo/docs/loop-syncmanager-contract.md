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
| OpenJibo CA bind + OpenSSL hash links | Native WSS on `:443` trusts local/OpenJibo CA — `install-openjibo-ca.sh` |
| `S78` `NODE_TLS_REJECT_UNAUTHORIZED=0` | WiFiManager Node HTTPS — `install-openjibo-ca.sh` |
| Writable `/var/jibo/keys/` | STS `symmetric-<loopId>.json` — `install-openjibo-ca.sh` |
| JSC `rejectUnauthorized=false` patches | Node JSC HTTPS to local cert — `install-openjibo-ca.sh` |
| `/opt/jibo/Knowledge/.../nodes` | SyncManager write; introductions `loadLoop()` read |

Native NotificationSubsystem uses `wss://api-socket.jibo.com/{token}` on **:443**,
not Node `wsendpoint`.

### `point-at-server.sh` (BEam, domain-first)

`~/BEam/point-at-server.sh` covers jetstream hub
override, server-service socket suffix, credentials `region`+`endpoint`, and
**hosts rewrite only for IPv4**. Domains are the normal path (DNS must resolve
legacy names). Hub port and credentials/JSC API port are prompted separately
(examples: hub `443`, credentials `8765` or `24605`). It does **not** install CA,
S78, keys overlay, or JSC patches — run `~/BEam/install-openjibo-ca.sh` next for
those (below).

### CA install for live `LoopUpdated` push (`install-openjibo-ca.sh`)

The CA is **one shared file, not per-robot**: every robot that needs live
push gets byte-for-byte the same `openjibo-ca.crt`. Generate it once on the
server:

```sh
OpenJibo/scripts/cloud/generate-openjibo-ca.sh "IP:192.168.7.142"
```

This writes `src/Jibo.Cloud/node/{cert.pem,key.pem}` (server cert, signed by
the CA) and `src/Jibo.Cloud/node/tls/openjibo-ca.{crt,key}` (the CA itself),
prints the standard `ASPNETCORE_Kestrel__Certificates__Default__Path`/
`Password` env vars for whatever process manager runs the API (works
identically under `dotnet run`, systemd, or Docker — it's plain ASP.NET
Core Kestrel config, not launcher-specific), and needs no code change if
already using `scripts/cloud/start-dotnet-with-node-cert.sh`. The server
then serves the CA at an anonymous `GET /openjibo-ca.crt`
([`Program.cs`](../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/Program.cs)).

On the robot, `~/BEam/install-openjibo-ca.sh` fetches that endpoint (or
takes a local path) and makes the trust change boot-persistent: CA bundle
bind-mount + OpenSSL hash symlink under `/etc/ssl/certs` (the bundle append
alone was not enough for the native WSS handshake), writable
`/var/jibo/keys` overlay, JSC `rejectUnauthorized` patches, and a
`NODE_TLS_REJECT_UNAUTHORIZED=0` patch to `S78jibo-system-manager` — backed
up outside `/etc/init.d` since BusyBox init runs every `S*`-prefixed file
there, including backups. It restarts `jibo-server-service` immediately so
`NotificationSubsystem` reconnects without waiting for a reboot.

Skipping this script doesn't lose `LoopUpdated` — `SyncManager`'s periodic
`Loop#list()` resync (~7200s) still picks up changes, and restarting
`jibo-server-service` by hand forces that resync immediately.

### Cloud env

Set `OpenJibo__Robot__RobotId=<local KB hex>` so `loop.robot` matches SyncManager
after portal mutations. Portal People and `Loop#list` share one loop via
`LoopRosterResolver` (never more than one List item).

## Verification

1. Run `point-at-server.sh` with domain (or LAN IP) + hub + credentials ports.
2. Run `install-openjibo-ca.sh` on the robot for live push (see above), or
   skip it and rely on the ~7200s periodic resync / manual
   `jibo-server-service` restart.
3. `GET /api/portal/loop-sync-status` → `liveApiSocketOverlaps > 0` /
   `openApiSocketConnections > 0`.
4. After portal People add: response `syncedToRobot: true`, `pushCount > 0`.
5. SSM: no `robot … not in loop`; after `LoopUpdated`, pause/resume loop syncing;
   introductions menu shows the new person.

## Root cause: the bootstrap-loop trap

`Loop#list()` from SSM carries **no** `X-Jibo-RobotId` header, **no** bearer
token, and an empty body — the only identity signal is the SigV4
access-key fingerprint in `Authorization`. Every `InMemoryCloudStateStore`
starts with a synthetic loop (`openjibo-default-loop`) owned by a
`openjibo-bootstrap-<guid>` device
([`InMemoryCloudStateStore.cs`](../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Infrastructure/Persistence/InMemoryCloudStateStore.cs)
constructor). If that SigV4 fingerprint has never been bound to a real
device, the old resolver fell back to `openjibo-default-loop`, whose
`RobotId` never matches the physical robot's local KB hex — SyncManager's
`_isLoopGood` rejects it as `robot <kb hex> not in loop`, the KB is never
written, and the introductions menu never updates even though the portal
roster is correct.

Two fixes close this trap:

- [`LoopRosterResolver.IsBootstrapLoop`](../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Application/Services/LoopRosterResolver.cs)
  excludes any loop whose `RobotId`/`RobotFriendlyId` starts with
  `openjibo-bootstrap-` from every fallback and tie-break path. An
  unidentified caller now gets the real household loop whenever exactly one
  non-bootstrap loop exists (or the one with a live `type=robot` member, or
  the most recently updated one, if several do).
- [`JiboCloudProtocolService.EnsureFirstUseCredentialBinding`](../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Application/Services/JiboCloudProtocolService.cs)
  binds an unbound SigV4 fingerprint to the single unambiguous candidate
  device (configured `OpenJibo:Robot:RobotId`, else the robot behind the one
  non-bootstrap household loop, else the one physical device) the first time
  it calls `List`/`ListLoops`/`ListMembers`, with `claimSource:
  "protocol-first-use"`. If more than one candidate exists it leaves the
  fingerprint unbound rather than guessing. The same request re-resolves
  immediately with the new binding, so the very first `Loop#list()` call
  already returns the right loop.

## Observability: loop-sync diagnostics

[`LoopSyncDiagnostics`](../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Application/Services/LoopSyncDiagnostics.cs)
is a 25-entry ring buffer, registered as a singleton and fed from every
`List` / `ListLoops` / `ListMembers` / `ListLoopMembers` call in
`JiboCloudProtocolService.HandleLoop`. Each entry records the call time,
host/path, resolved identity source and device id, short access-key
fingerprint, whether that call created a first-use binding, the returned
loop id, member counts, and whether the bootstrap loop was returned.

`GET /api/portal/loop-sync-status` now also reports:

| Field | Meaning |
|---|---|
| `robotListCallsSeen` | Total `List`/`ListLoops` calls ever seen (not just the last 25) |
| `credentialBindingCount` | Total first-use SigV4 bindings created via protocol calls |
| `lastListLoops` | The most recent `List`/`ListLoops` call record |
| `recentLoopCalls` | Last 10 calls (List/ListLoops/ListMembers), newest first |
| `warnings` | `no-robot-list-calls-seen`, `bootstrap-loop-returned`, `portal-loop-differs-from-listed-loop` |

Use this before assuming a code bug: if `robotListCallsSeen` is `0`, the
robot has never reached this server (check DNS/hosts, hub vs. credentials
port, and CA trust) — the loop code itself is not the problem yet.

## `ContactsView` caches the roster until reopened

Even after SyncManager writes a fresh KB roster, the **open** introductions
menu will not show it. `ContactsView` snapshots the loop into `_loopMembers`
once when the menu is opened (`loadData`) and only clears it in `destroy()`;
`removeInvalidLoopers()` filters that cached snapshot on every render, it
never re-fetches
([`jibo.js`](../../../BEam/@be/be/node_modules/jibo/lib/jibo.js), `ContactsView`).
So the correct verification sequence is: add the person in the portal, wait
for `syncedToRobot: true`/`pushCount > 0`, then **close and reopen** the
introductions menu on the robot — a sync can succeed while the already-open
menu still looks stale.
