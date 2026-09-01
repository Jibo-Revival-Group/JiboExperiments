# Local Jibo Device Runbook

This runbook records the verified local-device setup used to connect a physical
Jibo to the OpenJibo cloud during development.

It is intentionally practical. The goal is to preserve the exact shape that
worked on the robot we tested, including the failure modes that mattered.

## Current Working Shape

The working device path is:

```text
Mac runs OpenJibo .NET cloud on 443
Jibo resolves api.jibo.com, api-socket.jibo.com, open-jibo-socket.openjibo.com,
and neohub.openjibo.com to the Mac
Jibo keeps /var/jibo/credentials.json region as open-jibo
Jetstream uses api region settings for api.jibo.com and neohub.openjibo.com
Jibo boot script reapplies local hosts, CA, writable key overlay, and TLS patch
.NET cloud is configured with the robot id Jibo expects in its local KB
```

Both the Mac and Jibo get DHCP addresses that can change between sessions. Use
mDNS to find Jibo reliably. The bootstrap script on the robot keeps the Mac IP
as a default; update it when the Mac changes.

Last observed values on the tested robot:

```text
Mac LAN IP: 192.168.1.41
Jibo LAN IP: 192.168.1.40
Jibo SSH: root@<jibo-ip>, password jibo
Jibo device/friendly id: Ghost-Instance-Onion-Silk
Jibo serial number: BOJW-1000-0017-0820-0020
Jibo expected robot id in SSM/KB: 5a0b6398faa0f0001c5d0df1
Observed related local ids:
  symmetric key loop id: 5a0b6398faa0f0001c5d0df2
  face/person identity id: 5a0b6398faa0f0001c5d0df4
```

The most important lesson:

```text
Do not set /var/jibo/credentials.json region to openjibo-local on this build.
```

Some Jibo services use the credentials `region` field as an AWS/JSC endpoint
prefix. Setting it to `openjibo-local` made STS and server-service try to reach
`openjibo-local.jibo.com`.

The working value is:

```json
{"region":"open-jibo"}
```

Local routing is handled by hosts/DNS and Jetstream host settings, not by
changing the credentials region to a new region label.

## Mac Server

The recommended server for current OpenJibo testing is the .NET cloud. From the
repo root:

```bash
cd ~/JiboExperiments/OpenJibo

CERT_PEM=src/Jibo.Cloud/node/cert.pem \
KEY_PEM=src/Jibo.Cloud/node/key.pem \
OpenJibo__Robot__RobotId=5a0b6398faa0f0001c5d0df1 \
ASPNETCORE_URLS="https://0.0.0.0:443;http://0.0.0.0:24605" \
sudo env "PATH=$PATH" ./scripts/cloud/start-dotnet-with-node-cert.sh
```

Notes:

- Port `443` requires sudo on macOS.
- The script needs `dotnet` available through the sudo environment.
- Stop any Node server or other process already bound to `443`.
- `OpenJibo__Robot__RobotId` is required for this robot because some robot
  calls only send the friendly id (`Ghost-Instance-Onion-Silk`), while SSM
  checks loop membership against the real local KB robot id.

The local health check on the Mac is:

```bash
curl -k https://localhost/health
curl http://localhost:24605/health
```

The robot-facing health check from Jibo is:

```sh
curl -k https://api.jibo.com/health
```

Expected response:

```json
{"ok":true,"service":"OpenJibo Cloud Api","version":"1.0.19"}
```

## Certificate Material

The local .NET cloud reuses the Node certificate material:

```text
src/Jibo.Cloud/node/cert.pem
src/Jibo.Cloud/node/key.pem
src/Jibo.Cloud/node/tls/openjibo-ca.crt
```

The CA certificate is copied to Jibo at:

```text
/opt/jibo/openjibo-ca.crt
```

At boot, the persistent bootstrap copies that to `/tmp/openjibo-ca.crt` and
appends it to a temporary CA bundle bind-mounted over
`/etc/ssl/certs/ca-certificates.crt`.

## Device Paths Observed

On the tested robot, the real Jetstream config path was:

```text
/usr/local/etc/jibo-jetstream-service.json
```

There was no active:

```text
/etc/jibo-jetstream-service.json
```

Credentials live at:

```text
/var/jibo/credentials.json
```

But `/var` may boot read-only, and writes under `/var/jibo` can fail. Avoid
using a persistent bind mount over `credentials.json`; keep the real credentials
file with `region: api`.

## Jetstream Region Settings

The working Jetstream region config keeps `api` as the active region:

```json
"region-settings": {
  "api": {
    "hub_port": 443,
    "hub_hostname": "neohub.openjibo.com",
    "entrypoint_hostname": "api.jibo.com"
  },
  "openjibo-local": {
    "hub_port": 443,
    "hub_hostname": "neohub.openjibo.com",
    "entrypoint_hostname": "api.jibo.com"
  }
}
```

`openjibo-local` can remain documented in this file, but the credentials region
must not be switched to it on this build.

The notification subsystem config at `/usr/local/etc/jibo-server-service.json`
should stage `NotificationSubsystem.serverURLSuffix` as `-socket.openjibo.com`
so the converted robot resolves `open-jibo-socket.openjibo.com` without a robot
code change.

Also remove `HubClient.override` unless deliberately testing override behavior.

Verify on Jibo:

```sh
cat /var/jibo/credentials.json
grep -n -A5 -B2 openjibo-local /usr/local/etc/jibo-jetstream-service.json
grep -n override /usr/local/etc/jibo-jetstream-service.json || echo "no override"
```

Expected for the hosts/DNS path:

```text
"region":"api"
no override
```

### Credentials region vs HubClient.override vs LoopUpdated

Three separate seams:

1. **`/var/jibo/credentials.json` → `region`** selects which `HubClient.region-settings`
   entry Jetstream uses (stock dump comment: switch selected by credentials region).
2. **`HubClient.override`** (when not prefixed `xxx_`) overrides
   `entrypoint_hostname` / `hub_hostname` / ports for **Jetstream hub listen and
   proactive only**. Pointing override at OpenJibo `:24605` or `:443` can make
   voice turns work on an unmodded robot without rewriting hosts.
3. **Portal Loop edits** also need a live notification socket classified as
   `api-socket` (`wss://api-socket.jibo.com/...` on TLS `:443`, or the token path
   on the OpenJibo TLS listener). On `LoopUpdated`, stock SSM re-fetches
   `Loop.List` / `ListLoops` and applies members — including the `type=robot`
   member whose `accountId` must equal `loop.robot`.

So: override alone is not enough for portal Loop editing. If hub listen works
but portal edits never appear on-robot, check portal dashboard `loopSync`
(`apiSocketMatchedForThisRobot`) and cloud logs for
`LoopUpdated push matched no live api-socket`.

## Persistent Bootstrap

The persistent bootstrap lives in:

```text
/opt/jibo/openjibo-bootstrap.sh
/etc/init.d/S76openjibo-bootstrap
```

It runs before:

```text
/etc/init.d/S78jibo-system-manager
```

The bootstrap recreates volatile boot-time state:

- `/etc/hosts` entries for the Mac.
- `/tmp/openjibo-ca.crt`.
- Temporary CA bundle bind mount.
- Writable `/var/jibo/keys` overlay for STS key writes.
- Global `@jibo/jibo-server-client` TLS patch for STS/JSC.

It intentionally does not mount over `/var/jibo/credentials.json`.

### Bootstrap Script

The installed script used this shape. This documented version is slightly more
idempotent than the first live draft.

```sh
#!/bin/sh
set -u

MAC_IP="${OPENJIBO_MAC_IP:-192.168.1.41}"  # update this when Mac IP changes
CA_SRC="/opt/jibo/openjibo-ca.crt"
CA_TMP="/tmp/openjibo-ca.crt"
HOSTS_TMP="/tmp/hosts.openjibo"
CA_BUNDLE="/etc/ssl/certs/ca-certificates.crt"
CA_BUNDLE_TMP="/tmp/ca-bundle.openjibo"
KEYS_TMP="/tmp/openjibo-var-jibo-keys"
GLOBAL_JSC="/usr/lib/node_modules/@jibo/jibo-server-client/lib/http/node.js"
GLOBAL_JSC_PATCH="/tmp/jibo-server-client-global-http-node.openjibo.js"
GLOBAL_JSC_ORIGINAL="/tmp/jibo-server-client-global-http-node.original.js"

is_mounted() {
  mount | grep -q " on $1 "
}

log() {
  echo "openjibo-bootstrap: $*"
}

if [ ! -f "$CA_SRC" ]; then
  log "missing $CA_SRC"
  exit 1
fi

cp "$CA_SRC" "$CA_TMP"
chmod 644 "$CA_TMP"

cat > "$HOSTS_TMP" <<EOF
127.0.0.1	localhost
127.0.1.1	Ghost-Instance-Onion-Silk
$MAC_IP	api.jibo.com
$MAC_IP	api-socket.jibo.com
$MAC_IP	neohub.openjibo.com
EOF
chmod 644 "$HOSTS_TMP"

if ! is_mounted /var/etc/hosts && ! is_mounted /etc/hosts; then
  mount -o bind "$HOSTS_TMP" /var/etc/hosts 2>/dev/null ||
    mount -o bind "$HOSTS_TMP" /etc/hosts 2>/dev/null ||
    true
fi
chmod 644 /var/etc/hosts /etc/hosts 2>/dev/null || true

if [ -f "$CA_BUNDLE" ] && ! is_mounted "$CA_BUNDLE"; then
  cp "$CA_BUNDLE" "$CA_BUNDLE_TMP"
  cat "$CA_TMP" >> "$CA_BUNDLE_TMP"
  chmod 644 "$CA_BUNDLE_TMP"
  mount -o bind "$CA_BUNDLE_TMP" "$CA_BUNDLE" 2>/dev/null || true
fi

if ! is_mounted /var/jibo/keys; then
  rm -rf "$KEYS_TMP"
  mkdir -p "$KEYS_TMP"
  cp -a /var/jibo/keys/. "$KEYS_TMP"/ 2>/dev/null || true
  mount -o bind "$KEYS_TMP" /var/jibo/keys 2>/dev/null || true
fi

for f in \
  /opt/jibo/Jibo/Skills/@be/be/node_modules/@jibo/jibo-server-client/lib/http/node.js \
  /opt/jibo/Jibo/Skills/oobe-config/node_modules/@jibo/jibo-server-client/lib/http/node.js \
  /usr/local/bin/jibo-ssm/node_modules/@jibo/jibo-server-client/lib/http/node.js \
  /usr/lib/node_modules/@jibo/jibo-server-client/lib/http/node.js
do
  if [ -f "$f" ]; then
    sed -i 's/rejectUnauthorized: true/rejectUnauthorized: false/g' "$f" 2>/dev/null || true
  fi
done

if [ -f "$GLOBAL_JSC" ] && ! is_mounted "$GLOBAL_JSC"; then
  cp "$GLOBAL_JSC" "$GLOBAL_JSC_ORIGINAL"
  cp "$GLOBAL_JSC_ORIGINAL" "$GLOBAL_JSC_PATCH"
  sed -i 's/rejectUnauthorized: true/rejectUnauthorized: false/g' "$GLOBAL_JSC_PATCH" 2>/dev/null || true
  mount -o bind "$GLOBAL_JSC_PATCH" "$GLOBAL_JSC" 2>/dev/null || true
fi

log "applied for $MAC_IP"
```

The init wrapper:

```sh
#!/bin/sh
case "$1" in
  start)
    /opt/jibo/openjibo-bootstrap.sh start
    ;;
  stop)
    ;;
  *)
    echo "Usage: $0 {start|stop}"
    exit 1
    ;;
esac
exit 0
```

Install locations and permissions:

```sh
mount -o remount,rw /
mount -o remount,rw /usr/local

mkdir -p /opt/jibo
cp /tmp/openjibo-ca.crt /opt/jibo/openjibo-ca.crt
cp /tmp/openjibo-bootstrap.sh /opt/jibo/openjibo-bootstrap.sh
cp /tmp/S76openjibo-bootstrap /etc/init.d/S76openjibo-bootstrap

chmod 644 /opt/jibo/openjibo-ca.crt
chmod 755 /opt/jibo/openjibo-bootstrap.sh
chmod 755 /etc/init.d/S76openjibo-bootstrap
```

## Why `/var/jibo/keys` Is Overlaid

STS needs to create a symmetric key file similar to:

```text
/var/jibo/keys/symmetric-openjibo-default-loop.json
```

On the tested robot, `/var` could be read-only and STS failed with `EROFS`.
Bind-mounting a writable temporary directory over `/var/jibo/keys` allowed STS
to finish initialization without replacing broader `/var/jibo` state.

Expected success log:

```text
P.secure-transfer-service.Service: Successfully completed STS initialization!
```

### L8 Means The Keys Overlay Is Missing

Observed on the robot: the error code
`L8-UGC_key_not_found` matched a missing keys overlay.

The matching STS log signature is an init-restart loop:

```text
P.secure-transfer-service.Service: Still initializing: UGC Key is not cached
P.secure-transfer-service.Exchange: Key not present: ENOENT ... symmetric-openjibo-default-loop.json
P.secure-transfer-service.Service: Restarting STS init: EROFS: read-only file system, open '/var/jibo/keys/symmetric-openjibo-default-loop.json'
```

Root cause that time: the installed bootstrap script only handled hosts and the
CA bundle. It had no `/var/jibo/keys` overlay step.

Fix: install the full bootstrap script documented above, which includes the
keys overlay. Once the overlay is mounted, STS completes on its next retry.

Diagnosis one-liners on the robot:

```sh
mount | grep /var/jibo/keys || echo "KEYS OVERLAY MISSING"
touch /var/jibo/keys/.writetest && rm /var/jibo/keys/.writetest || echo "KEYS NOT WRITABLE"
```

## Why `/etc/hosts` Must Be Readable

At one point `/var/etc/hosts` was mode `600`, which caused Electron/Be to fail
lookups like:

```text
getaddrinfo ENOTFOUND localhost localhost:8090
```

The bootstrap sets:

```sh
chmod 644 /var/etc/hosts /etc/hosts
```

This is required because not every Jibo process reads hosts as root.

## Verification

After boot, verify on Jibo:

```sh
cat /var/jibo/credentials.json
cat /etc/hosts
curl -k https://api.jibo.com/health
curl -k https://open-jibo-socket.openjibo.com/
curl -k https://neohub.openjibo.com/v1/proactive
grep -n 'rejectUnauthorized' /usr/lib/node_modules/@jibo/jibo-server-client/lib/http/node.js
mount | grep -E 'hosts|ca-certificates|jibo-server-client|/var/jibo/keys'
```

Expected (IP will vary):

```text
"region":"api"
<mac-ip> api.jibo.com
<mac-ip> api-socket.jibo.com
<mac-ip> open-jibo-socket.openjibo.com
<mac-ip> neohub.openjibo.com
{"ok":true,"service":"OpenJibo Cloud Api","version":"1.0.19"}
rejectUnauthorized: false
```

Then verify service logs:

```sh
tail -n 260 /var/log/messages | grep -iE \
  'NotificationSubsystem::connect established|Successfully completed STS|HubClient settings|Host not found|UnknownEndpoint'
```

Expected success lines:

```text
NotificationSubsystem::connect established connection to server
HubClient settings: hub_hostname=neohub.openjibo.com, hub_port=443, entrypoint_hostname=api.jibo.com
P.secure-transfer-service.Service: Successfully completed STS initialization!
```

Failure lines to avoid:

```text
Host not found: openjibo-local.jibo.com
UnknownEndpoint: Inaccessible host: `openjibo-local.jibo.com'
Host not found: api.jibo.com
SSL certificate problem
LoopID is not cached
```

## What Jibo Expects From The Cloud

This is the current observed contract for getting past boot, STS, loop sync,
and the Be `Q4-Server_connection_lost` screen.

### Name Resolution

Jibo must resolve these public production names to the local Mac:

```text
api.jibo.com
api-socket.jibo.com
open-jibo-socket.openjibo.com
neohub.openjibo.com
```

The hosts patch has to be boot-persistent because Jibo recreates or remounts
parts of `/etc` and `/var` during startup.

### TLS

Jibo must trust the local OpenJibo certificate chain for:

```text
https://api.jibo.com/
https://neohub.openjibo.com/
```

Stock server-service opens `wss://api-socket.jibo.com/{token}` on TLS port 443.
If that handshake fails (`unknown ca`), Portal `LoopUpdated` push stays at
`openConnections=0` even while `/v1/listen` on `:24605` works.

For physical robots, do not rely on Node/JSC `wsendpoint` rewrites. The C++
NotificationSubsystem uses HTTPS/WSS on `:443` directly, so the fix is:

- OpenJibo listening on `https://<host>:443`
- robot trust store includes the OpenJibo CA (with OpenSSL hash links)

The working setup installs the OpenJibo CA and also creates OpenSSL hash
symlinks under `/etc/ssl/certs`. Appending the CA to
`/etc/ssl/certs/ca-certificates.crt` alone was not enough on this robot.

Verify from Jibo:

```sh
curl https://api.jibo.com/health
```

Expected, without `-k`:

```json
{"ok":true,"service":"OpenJibo Cloud Api","version":"1.0.19"}
```

If this fails with:

```text
curl: (60) SSL certificate problem: unable to get local issuer certificate
```

then the CA bundle/hash installation is incomplete or the bootstrap did not run.

### Region And Credentials

`/var/jibo/credentials.json` should keep:

```json
{"region":"api"}
```

On this build, changing region to `openjibo-local` made Jibo derive
`openjibo-local.jibo.com` internally. That is wrong for the current local path.

The observed credentials shape:

```json
{"accessKeyId":"...","secretAccessKey":"...","region":"api"}
```

Do not paste real credentials into docs or logs unless needed for a local
debugging session.

### Early HTTP Calls

The robot reaches the server through AWS-style JSON targets. The backend logs
show requests like:

```text
POST https://api.jibo.com/
X-Amz-Target: Loop_20160324.ListLoops
X-Amz-Target: Key_20160201.ShouldCreate
X-Amz-Target: Notification_20150505.NewRobotToken
X-Amz-Target: Robot_20160225.GetRobot
X-Amz-Target: Robot_20160225.UpdateRobot
```

Observed bodies from this robot:

```json
{"deviceId":"Ghost-Instance-Onion-Silk"}
```

for `Notification_20150505.NewRobotToken`, and:

```json
{"id":"Ghost-Instance-Onion-Silk"}
```

for `Robot_20160225.GetRobot`.

`Robot_20160225.UpdateRobot` included useful identity payload:

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

Important finding: these calls did not send the SSM/KB robot id
`5a0b6398faa0f0001c5d0df1`. They sent the friendly/device id
`Ghost-Instance-Onion-Silk`.

### Loop Shape

`Loop_20160324.ListLoops` must return a non-empty loop and its `robot` field
must match the robot id that SSM has in its local KB.

Two different Q4 causes were observed:

```text
T.SSM.Svc.KB.SyncManager: JSC server call Loop#list() loop has no members
T.SSM.Svc.KB.SyncManager: JSC server call Loop#list() robot 5a0b6398faa0f0001c5d0df1 not in loop
```

The first means the server returned `members: []`.

The second means the server returned a loop, but the loop `robot` did not match
Jibo's local robot id.

The .NET backend now supports:

```text
OpenJibo__Robot__RobotId=5a0b6398faa0f0001c5d0df1
```

When set, the backend promotes the robot profile and loop membership to that id,
even if Jibo only sends `Ghost-Instance-Onion-Silk` in protocol requests.

## Empty Loop and Q4 Recovery

After a reflash or fresh local cloud state, the robot can connect to the Mac
server but still show the Be settings error:

```text
Q4-Server_connection_lost
```

In the observed failure, network and TLS were already working, but SSM logged:

```text
T.SSM.Svc.KB.SyncManager: JSC server call Loop#list() loop has no members
T.SSM.Svc.Error.Logger: Added Q4-Server_connection_lost
```

The cause was that `Loop_20160324.ListLoops` returned a loop with empty
`members`.

The .NET cloud now seeds an active `owner` member into the default loop on
first boot, even with no persisted snapshot.

### Clear the Stale Q4 Error

If `@be/be` is already showing the Q4 error, the SSM error service may keep
that error active even after the server-side loop is fixed. Mark the Q4 as
processed:

```sh
curl -sS -X POST \
  -H 'Content-Type: application/json' \
  --data '{"errorCode":"Q4-Server_connection_lost"}' \
  http://127.0.0.1:10004/processedError
```

Then verify:

```sh
curl -sS -X POST \
  -H 'Content-Type: application/json' \
  --data '{}' \
  http://127.0.0.1:10004/getCurrentErrorId
```

Expected:

```json
{"status":"OK","currentErrorId":null}
```

At this point the robot can be connected to the Mac cloud while `@be/be` is
running, without needing to change the robot region away from `api`.

## Restarting Services

This device does not use `systemctl`. The init scripts are BusyBox/SysV-style.

System manager supports only `start` and `stop`:

```sh
/etc/init.d/S78jibo-system-manager stop
/etc/init.d/S76openjibo-bootstrap start
/etc/init.d/S78jibo-system-manager start
```

## SSH And Device Access

### Finding Jibo on the LAN

Jibo uses mDNS and broadcasts its hostname. The most reliable way to find it
after a DHCP change:

```bash
ping -c 1 Ghost-Instance-Onion-Silk.local
dns-sd -G v4 Ghost-Instance-Onion-Silk.local
```

### Connecting via SSH

```bash
ssh root@<jibo-ip>
scp /path/to/file root@<jibo-ip>:/tmp/file
```

For scripted / non-interactive SSH, install `sshpass`:

```bash
brew install sshpass
SSHPASS='jibo' sshpass -e ssh root@<jibo-ip> '<command>'
```

Do not include `http://` in `scp` targets.

## NODE_TLS_REJECT_UNAUTHORIZED In S78

Jibo's `WiFiManager._checkJiboServers()` runs every 10 seconds on a periodic
timer and calls Node's built-in `https.get({host:'api.jibo.com', path:'/'})`.
Node v6 uses its own compiled-in CA bundle, not the system bundle. Because of
this, the call fails with:

```text
UNABLE_TO_VERIFY_LEAF_SIGNATURE
```

even when the CA bundle bind mount and OpenSSL hash symlinks are correctly
installed. Each failure triggers:

```text
T.SSM.Svc.Error.Logger: Added Q4-Server_connection_lost
```

Fix: add `export NODE_TLS_REJECT_UNAUTHORIZED=0` to the init script before
jibo-system-manager starts. Because S78 is the parent process, all Node children
inherit the env var.

## BusyBox Init: Never Leave S* Backups in /etc/init.d

BusyBox SysV init executes every file whose name begins with `S` in
`/etc/init.d/`. It does not skip files with extensions like `.orig` or `.bak`.

Always name backup files with a prefix that does not start with `S`.

If a bad backup already exists, remove it.

## Updating the Mac IP Without a Reboot

When the Mac gets a new DHCP address, the hosts bind mount on the robot still
points to the old IP. Fix without rebooting Jibo by updating the bootstrap
default and re-running the bootstrap.

## Known Bad Attempts

### Experimental: Cloudflare Tunnel

We tested publishing the local server through Cloudflare with a public domain,
but the physical Jibo failed TLS verification. Because the robot has an old
TLS/CA stack and the local LAN path is under our control, the current working
path is direct LAN resolution of production hostnames to the Mac, not Cloudflare.

### Bad: credentials region `openjibo-local`

This looked aligned with the region docs, but on this robot it broke STS and
server-service.

Fix:

```json
{"region":"api"}
```

### Bad: relying on temporary bind mounts only

Manual bind mounts made the robot work until reboot, but these were lost. The
persistent init script recreates those at boot.

### Bad: assuming `/var` is writable

The tested robot mounted `/var` read-only after filesystem errors. Prefer narrow
overlays for volatile writable paths, such as `/var/jibo/keys`, instead of
replacing broad Jibo directories.

## Multiple Jibos On One OpenJibo Server

When more than one physical Jibo points at the same OpenJibo cloud instance, each
robot must keep isolated websocket turn state. If both wake on "Hey Jibo" at the
same time while sharing one cloud session, they can both enter listen mode and
remain stuck in the blue ring until reboot.

### Authenticated Hub configuration

NeoHub connections use a per-robot token from `Account.CreateHubToken`:

```text
Authorization: Bearer hub-<account>-<guid>
```

The exact route `/v1/listen` is not a token. Older self-hosted behavior mistakenly
treated the route text `v1/listen` as a connection token, which allowed direct-IP
clients to connect without authentication. Do not restore that fallback.
An explicit, disabled-by-default single-robot LAN compatibility mode is documented
in [Single-Robot HTTP Self-Hosting](single-robot-http-self-hosting.md). It does not
restore implicit route-token behavior.


A direct-IP override must configure both the API entrypoint that issues the token
and the Hub listener that consumes it. The ports may differ:

```json
"override": {
  "entrypoint_hostname": "192.168.1.133",
  "entrypoint_port": 8080,
  "hub_hostname": "192.168.1.133",
  "hub_port": 9000,
  "hub_secure": false
}
```

The process on the entrypoint port must implement `Account.CreateHubToken`. Pointing
only `hub_hostname` and `hub_port` at a local Hub shim leaves Jetstream without a
credential, causing it to send an empty Bearer header. If one process serves both
HTTP protocol calls and WebSockets, use that process's port for both settings.

Minimum setup:

1. Point each Jibo at the same OpenJibo host (`api.jibo.com`, `neohub.openjibo.com`,
   and related hosts rewritten to the server).
2. Complete `SetupRobot` separately for each robot with a unique friendly id and
   device id. Example ids: `BOJW-KITCHEN-0001`, `BOJW-OFFICE-0002`.
3. Confirm each robot requests its own hub token via `CreateHubToken`. The cloud
   binds that token to the robot `deviceId` when the request includes one.
4. Say "Hey Jibo" once while both robots are in range.

Expected behavior with isolated robot sessions:

- both Jibos orient and handle their own turn
- neither robot's listen state, transID, or buffered audio overwrites the other
- neither robot should require reset

Live proof checklist:

```text
capture robot logs from both devices
capture websocket turn telemetry for both connections
confirm both robots receive their own greeting/skill response
confirm hub-token logs show distinct hub-* tokens when Bearer auth is in use
```

If both robots still hang, verify they are not sharing one session id in cloud
logs and that each `SetupRobot` wrote a different device id into cloud state.

## Voice Recognition Notes

After connectivity worked, speech behavior was still imperfect. Captures showed
short OGG_OPUS turns and transcripts such as `hello` or empty transcripts.

That suggests two follow-up areas:

- Tune websocket turn finalization so hotphrase audio is not cut too early.
- Filter `CLIENT_ASR` and `CLIENT_NLU` so local skill/menu state does not get
  treated as general conversational input.

For live testing, prefer:

```text
Hey Jibo, how are you?
Hey Jibo, tell me a joke.
Hey Jibo, what time is it?
```

and inspect the websocket capture before assuming the STT model itself is the
only cause.
