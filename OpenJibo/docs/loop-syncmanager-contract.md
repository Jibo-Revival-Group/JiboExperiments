# Loop SyncManager contract (source-verified)

The services partition is mode `750` root:group, so a loop-mount of a robot
dump still hides `bin/` from a normal user — which is why this document
previously had to be inferred from logs. That limitation is gone.
`scripts/dump/extract-robot-partition.sh` reads the partition straight out of
the raw `.bin` with `debugfs` (ext4 metadata walk, so Unix permissions never
apply; no root, no mount, no dd carve), and the stock services ship their
browserify source maps with `sourcesContent` intact, so
`scripts/dump/unpack-sourcemap.py` recovers the **original TypeScript**:

```sh
DUMP=~/Documents/Jibos/Air-Degree-Lunch-Canvas.bin
scripts/dump/extract-robot-partition.sh rget "$DUMP" services /bin/jibo-ssm
scripts/dump/unpack-sourcemap.py \
  artifact-output/dumps/<slug>/services/bin/jibo-ssm/lib/skills-service-manager.js.map
```

Everything below is quoted from that recovered source (OS 1.9,
`Air-Degree-Lunch-Canvas`), from `libJiboServerService.so` disassembly, or from
`/etc/jibo-server-service.json` on the same dump:

| Fact | Source |
|---|---|
| SyncManager / LoopManager behavior | `jibo-ssm/lib/…src/src/services/kb/{SyncManager,LoopManager}.ts` |
| Notification frame shape | `jibo-client-framework/lib/…src/src/NotificationsDispatcher.ts` |
| Socket inactivity timeout | `libJiboServerService.so`, `jibo::server::ServerPort::{onTimer,onReadable}` |
| Notification subsystem config | `services:/etc/jibo-server-service.json` |

## SyncManager behavior

1. Calls JSC **`Loop#list()`** / `ListLoops` (not ListMembers) for roster sync.
2. Validates with `_isLoopGood(data)` before writing KB UserNodes.
3. Registers for **`AccountUpdated`** and **`LoopUpdated`** notifications, but
   only from inside the callback of the first `_syncWithCloud()` during
   `LoopManager.init()` — so a robot that has never completed one List attempt
   is not yet listening. Log line: `registering for "LoopUpdated" notifications`.
4. Notification handling is **debounced 5 s**, with a 60 s maximum span
   (`NOTIFICATIONS_DEBOUNCE_PERIOD` / `_MAX_SPAN` in `SyncManager.ts`), so even a
   perfect push takes ~5 s to show up. On fire it logs
   `got a notification named "LoopUpdated"`, then `pausing loop syncing` →
   `resuming loop syncing`.
5. Periodic loop sync every **7200 seconds** — `PERIODIC_SECONDS = 60 * 60 * 2`
   in `LoopManager.ts`, passed to the `SyncManager` constructor as
   `syncingPeriod`. This is the fallback that makes portal edits appear "about
   two hours later" whenever push does not land.

## `_isLoopGood` only enforces three things

The name oversells it. Reading the recovered `LoopManager._isLoopGood`, only
these are hard failures:

1. `data` is a non-empty array.
2. `data.length === 1` (exactly one loop).
3. `loop.members` is a non-empty array.

The owner and robot checks are **warnings that still return `true`**:

```ts
// warn if the owner is not in the loop for some reason (but let it pass)
if (!loopAccountIds.includes(loop.owner)) {
    this._errorOnce('JSC server call Loop#list() owner not in loop for robot '+this.robotAccountId);
}
```

## …but `_applyLoopChanges` hard-crashes without them

Passing `_isLoopGood` is not enough. The very next function does:

```ts
let ownerMemberId = memberIdsByAccountId[cloudLoop.owner];
rootNode.addEdges(ownerMemberId, 'owner');   // and the same for cloudLoop.robot
```

If no member carries that `accountId`, `ownerMemberId` is `undefined`, and
`jibo-kb`'s `Node._resolveIdAndLayer` takes the non-string branch and
dereferences `node._id` — a `TypeError` thrown mid-sync, after the KB has
already been partially updated.

**So the real contract is stricter than the old checklist claimed, for a
different reason: `loop.owner` and `loop.robot` must each equal the
`accountId` of some member in `loop.members`, or the sync throws.** Deleting
the owner member outright is therefore not an option; the supported way to get
rid of the placeholder "Jibo Owner" person is to make a real household member
*be* the owner (portal "Make owner", or the first person you add, which claims
the seeded owner record in place).

`_filterOutInvitedChildren` runs before the merge and reads
`member.account.isChild` unconditionally, so every member must carry an
`account` object — `{}` is fine, `null`/missing is not.

Live people use stock status **`accepted`** (`invited|accepted|declined|removed`).

## Removed members are pruned from the loop, but their nodes survive

`_applyLoopChanges` diffs by **member `id`** (`cloudLoopEntry.id === loopNode._id`),
then for anyone no longer in the List payload:

```ts
removeLoop.forEach( (removeNode) => {
    rootNode.removeEdges(removeNode, 'user');
    // we remove them from the list of loop members, but
    // we don't delete the removed member's node on purpose
```

So dropping someone from the cloud roster *does* remove them from the loop the
robot enumerates — no manual KB cleanup needed — while the orphaned UserNode
lingers harmlessly. Two consequences worth keeping in mind:

- Renaming a member **in place** (same member `id`) updates the existing
  UserNode. Replacing it with a new `id` drops the old node from the loop and
  creates a second one, so in-place rewrites are always preferable.
- `rootNode.data` fields (`id`, `name`, `owner`, `robot`, `robotFriendlyId`,
  `created`, `updated`) and the member/account field lists are synced strictly:
  a field absent from the cloud payload is **deleted** from the node.

## Stock List item shape (`S6`)

Required: `owner`. Members: `id`, `name`, `owner`, `robot`, `robotFriendlyId`,
`members` (`S9` list), `isSuspended`, `created`, `updated`.

## Stock Member shape (`S9` element)

Required: `id`, `loopId`, `status`, `type`.  
Wire `type` values from stock cloud are **`incoming`** (owner) and **`outgoing`**
(robot + household). OpenJibo stores `owner`/`member`/`robot` internally and
translates at the `MapLoopMember` boundary.

Names live under nested `account.firstName` / `account.lastName` (SyncManager
flattens those onto UserNode `data.firstName` for introductions). Stock List
payloads also flatten the same fields onto the member object; OpenJibo emits
both. The robot member uses `account: {}` with no flattened names so
`UserNode.isJibo` (`!data.firstName`) stays true.

Unauthenticated LAN check: `GET /api/diagnostics/loop-sync` (same counters as
`/api/portal/loop-sync-status`, no portal session required).

## Notification transport: frame shape and the 120 s silence timeout

The cloud → robot path has two hops. Native `jibo-server-service` holds
`wss://api-socket.jibo.com/{token}` on **:443** and republishes whatever it
receives to local node services over `ws://<server>/server/notifications`;
`NotificationsDispatcher` in `jibo-client-framework` then fans it out as an
EventEmitter event.

### Frame shape (exact)

```ts
interface NotificationMessage {
    _id:string;
    skillId:string;
    payload:{ name:string; payload:any };
    created:string;
}
```

Dispatch is forgiving — `_processNotification` requires only
`message.payload.name` to be truthy, emits `payload.name` as the event with
`payload.payload` as the argument, and otherwise logs `notification message
being dropped here!`. `created` is declared a string but never read, so the
numeric epoch OpenJibo sends is harmless.

`RobotNotificationRegistry.CreateStockNotificationRecord` already matches this
exactly. **The frame shape was never the problem.**

One caveat worth respecting: the dispatcher emits the name verbatim, so a name
of `error` would hit EventEmitter's throw-on-unhandled-`error` rule. Any other
unknown name is a silent no-op on the robot.

### The robot hangs up after 120 s of cloud silence

`ServerPort::onTimer` compares `steady_clock::now()` against the last-contact
timestamp and disconnects past a threshold the constructor hard-codes as
`movw r3, #0xd4c0; movt r3, #0x1` — **0x1D4C0 = 120000 ms**:

```
1d686: ldr   r3, [r4, #0x170]     ; threshold = 120000
1d68c: cmp   r0, r3               ; elapsed ms since last contact
1d68e: bls   <keep going>
       ; else log "Haven't had contact from the server in <n>" + " disconnecting."
```

`onReadable` refreshes that timestamp (`strd` to `[r4, #376]`) on **all three**
of the paths that matter, dispatched through a `tbh` jump table on the frame
opcode:

| Opcode | Path | Refreshes timer |
|---|---|---|
| 1/2 (text/binary, >0 bytes) | application message | yes |
| 9 (ping) | replies with a pong | yes |
| 10 (pong) | unsolicited pong accepted | yes |

So a WebSocket keepalive is both sufficient and safe here — the native Poco
client handles ping/pong properly. This is **not** the Neo Hub client, which
is a Node `ws` client that logged `Hubclient: received zero bytes` when it saw
control frames and therefore keeps `KeepAliveInterval = InfiniteTimeSpan` in
`WebSocketRequestCoordinator`. The two sockets need opposite treatment.

**This is the root cause of "loop members only sync every two hours."**
ASP.NET Core's default `KeepAliveInterval` is 2 minutes, exactly equal to the
robot's 120 s tolerance, so whether the keepalive or the disconnect timer wins
is a coin flip on jitter. `docs/feature-backlog.md` records the loss case: a
robot that logged `ServerPort::onTimer Haven't had contact from the server`
every three seconds and sat on a dead TLS connection for ~60 hours with a
stale-contact counter around 216,000,000 ms, never recovering on its own.
A push into that socket is silently lost and the roster only catches up on the
7200 s periodic List.

The fix is to keep api-socket connections comfortably inside the window
(OpenJibo uses 30 s) rather than relying on the framework default.

## Fallback: shortening the 7200 s periodic resync

`scripts/robot/set-loop-sync-period.sh` rewrites `PERIODIC_SECONDS` in
`/usr/local/bin/jibo-ssm/lib/skills-service-manager.js` (one anchored match;
the Holiday/MediaList/Robot managers use `SYNC_PERIODIC_SECONDS` and are left
alone), keeps a `.openjibo-orig` backup, and supports `--restore`.

This is a last resort, not part of the fix. It does not repair push — it only
bounds how long a lost `LoopUpdated` stays invisible. Reach for it only when
`/api/diagnostics/loop-sync` reports pushes being *delivered* and the robot
still logs no `got a notification named "LoopUpdated"`. Every shortened period
is a full `Loop#list()` round trip, so stay well above the 5 s notification
debounce; ~60 s is the intended setting.

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
the CA) and `src/Jibo.Cloud/node/tls/openjibo-ca.{crt,key}` (the CA itself).
Nothing else to configure: `ConfigureDefaultKestrelEndpoints` in
[`Program.cs`](../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/Program.cs) checks
for that `cert.pem`/`key.pem` pair on **every** startup and, if present,
auto-binds `https://0.0.0.0:443` with it (plus `24605`/`8765` HTTP) via
`IConfiguration`, before any explicit `Kestrel:Endpoints`/`ASPNETCORE_URLS`
config is applied — no env vars, no PFX, no launcher-specific wrapper script.
This is genuinely launcher-agnostic: `dotnet run`, a published binary,
systemd, or Docker all pick it up identically because it's the same
`IConfiguration` Kestrel always reads, just seeded with defaults in code
instead of `appsettings.json` (so a repo checkout with no cert yet still
starts fine on just the two HTTP ports). Restarting the process after running
`generate-openjibo-ca.sh` is the only step. The server also serves the CA at
an anonymous `GET /openjibo-ca.crt`.

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

The stock List member **shape** (incoming/outgoing, flattened names, empty robot
`account`) is the same for every robot. Identity values are **per robot**.

Set `OpenJibo__Robot__RobotId` to **this** unit's KB hex
(`Knowledge/jibo/loop` root `data.robot`) so `loop.robot` matches SyncManager
after portal mutations. Do **not** copy ids from another robot's dump.

Portal People and `Loop#list` share one loop via `LoopRosterResolver` (never
more than one List item). Contract tests use the synthetic fixture
`tests/Jibo.Cloud.Tests/Fixtures/stock-loop-list-contract.json` — never commit
a real household roster.

To also reuse **this** robot's existing KB loop/owner ObjectIds:

```bash
OpenJibo__Loop__SeedIdentity=true
OpenJibo__Robot__RobotId=<this-robot-data.robot>
OpenJibo__Robot__FriendlyId=<this-robot-data.robotFriendlyId>
OpenJibo__Loop__LoopId=<this-robot-data.id>                 # optional
OpenJibo__Loop__OwnerAccountId=<this-robot-data.owner>      # optional
OpenJibo__Loop__Name=<this-robot-data.name>                 # optional
```

`SeedIdentity` rematerializes the household loop (and moves existing portal
members onto the preferred loop id). Without it, `RobotId` alone still feeds
`LoopRosterResolver` / first-use credential binding but will not rewrite a
persisted `openjibo-default-loop` id.

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
