# LAN Credentials API (Port 8765)

The OpenJibo .NET cloud can expose the robot `credentials.json` SigV4 API on HTTP port **8765**, separate from Hub listen/proactive on **443**.

## What this endpoint is for

On-robot `/var/jibo/credentials.json` authenticates Node `@jibo/jibo-server-client` traffic for:

- `Loop_*` (list loops, invite/update/remove members, `LoopUpdated` push)
- `Update_*` (OTA list/get)
- `Backup_*` (new/list/restore + PUT upload)
- `Log_*` (async upload negotiation)
- `Account_*` / `Notification_*` and related families already dispatched by `JiboCloudProtocolService`

Hub wake-word turns stay on `neohub` / `:443` WebSockets. This port is the LAN stand-in for historical `https://api.jibo.com`.

## Robot credentials shape

Node tools honor an `endpoint` override when loading credentials:

```json
{
  "accessKeyId": "#################",
  "secretAccessKey": "#################",
  "region": "api",
  "endpoint": "http://192.168.7.105:8765"
}
```

Placeholder fixture: [`scripts/bootstrap/fixtures/lan-credentials-api/credentials-with-endpoint.json`](../scripts/bootstrap/fixtures/lan-credentials-api/credentials-with-endpoint.json).

Native Jetstream / `libJiboServerService.so` still resolve hosts from `region` + Jetstream config / DNS, not from `endpoint`. Keep Hub routing via hosts/DNS as in [local-jibo-device-runbook.md](local-jibo-device-runbook.md).

## Start the server

Default Linux live start now binds Hub TLS plus both HTTP API ports:

```bash
CERT_PEM=src/Jibo.Cloud/node/cert.pem \
KEY_PEM=src/Jibo.Cloud/node/key.pem \
./scripts/cloud/start-dotnet-with-node-cert.sh
```

URLs:

- `https://0.0.0.0:443` — Hub + TLS API
- `http://0.0.0.0:24605` — local/dev HTTP API
- `http://0.0.0.0:8765` — LAN credentials API

Launch profile URLs also include `http://localhost:8765`.

Leave `OpenJibo:AcceptedHosts` empty for LAN (empty means all hosts accepted). Upload/download URLs returned to the robot are built from the request scheme and `Host` (including port), so `http://192.168.x.x:8765/...` works without forcing HTTPS.

## Seed dump robots (local secrets)

Copy the example and fill in access keys from the robot dumps (do **not** commit the filled file):

```bash
cp scripts/bootstrap/fixtures/lan-credentials-api/robot-credentials.local.example.json \
  src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/robot-credentials.local.json
```

Or set:

```bash
export OpenJibo__RobotCredentialSeed__Path=/absolute/path/to/robot-credentials.local.json
```

On startup the API registers each robot, creates a loop with an editable `Demo` member, and binds the SHA-256 fingerprint of `accessKeyId` for SigV4 identity resolution.

## Smoke checks

1. Health:

```bash
curl -sS http://192.168.7.105:8765/health
```

2. From the robot (after writing credentials with `endpoint`):

```bash
jibo-list-updates --credentials /var/jibo/credentials.json --subsystem os
```

3. Loop member mutation (portal People UI, or protocol `Loop_*.UpdateMember` / `InviteMember` / `RemoveMember` against `:8765`). Connected api-socket robots should receive `LoopUpdated`.

4. Confirm captures under `captures/http` show `X-Amz-Target` hits on the LAN host.

Portal owners can also browse robot-uploaded photos at `/portal` (Photos panel) once media has been created for the linked Loop.

## LRD log streamer port

The on-robot log SSE helper [`log_streamer.py`](../../log_streamer.py) now defaults to **8766** so it does not collide with the credentials API. Portal LRD defaults match.
