# JetStream Compatibility Probe

The JetStream compatibility probe exercises OpenJibo from a workstation without
converting a physical robot. Unlike the browser release smoke, it can set the
stock-style `Authorization: Bearer <hub-token>` WebSocket header.

It diagnoses four independent boundaries:

- API entrypoint reachability and `CreateHubToken` issuance
- notification token issuance and the `/{robot-token}` API socket
- authenticated `/v1/listen` and `/v1/proactive` Hub sockets
- the bounded tokenless compatibility path for a private, isolated HTTP server

The probe never prints tokens, token-bearing URLs, or Authorization headers.
Results contain only the same 12-character SHA-256 fingerprints used by server
logs.

## Install

From the `OpenJibo` directory:

```powershell
npm install --prefix src/Jibo.Cloud/node
```

## Authenticated local check

Start a local OpenJibo server in `self-hosted-isolated` mode, then run:

```powershell
node src/Jibo.Cloud/node/invoke-jetstream-compatibility-probe.mjs `
  --entrypoint-url http://127.0.0.1:8080 `
  --hub-url ws://127.0.0.1:8080 `
  --notification-url ws://127.0.0.1:8080 `
  --mode authenticated `
  --robots 2 `
  --skip-turn
```

`--skip-turn` isolates token and WebSocket connectivity. Omit it when the local
server also has its interaction dependencies configured. The active check proves
both the `CLIENT_ASR` reply sequence and the proactive `TRIGGER` then `CONTEXT`
transaction. The deterministic `SURPRISE` transaction must return one final,
empty `PROACTIVE` reply.

To test that proactive contract without invoking the configured interaction
provider, replace `--skip-turn` with `--skip-listen-turn`. Conversely,
`--skip-proactive-transaction` tests only the `CLIENT_ASR` path.

Expected authenticated evidence:

- `NewRobotToken` and `CreateHubToken` return HTTP 200
- notification, listen, and proactive sockets return HTTP 101
- every robot has a distinct non-`none` Hub-token fingerprint
- Hub tokens are sent in the Authorization header, not placed in the URL
- active checks return `LISTEN`, `EOS`, `SKILL_ACTION` on the listen socket and
  final `PROACTIVE` on the proactive socket

## Tokenless compatibility checks

With compatibility disabled:

```powershell
node src/Jibo.Cloud/node/invoke-jetstream-compatibility-probe.mjs `
  --entrypoint-url http://127.0.0.1:8080 `
  --hub-url ws://127.0.0.1:8080 `
  --mode tokenless `
  --expect-tokenless rejected `
  --skip-turn
```

With both `OpenJibo__Deployment__Mode=self-hosted-isolated` and
`OpenJibo__SelfHosted__AllowTokenlessSingleRobotHub=true`:

```powershell
node src/Jibo.Cloud/node/invoke-jetstream-compatibility-probe.mjs `
  --entrypoint-url http://127.0.0.1:8080 `
  --hub-url ws://127.0.0.1:8080 `
  --mode tokenless `
  --expect-tokenless accepted `
  --skip-listen-turn
```

The accepted check above holds listen and proactive sockets open together, sends
`TRIGGER` then `CONTEXT`, requires final `PROACTIVE`, and verifies that a third
tokenless socket receives HTTP 401. Use `--skip-turn` instead when only the
handshakes and connection limit should be tested. To exercise the second-client
address boundary, assign two local addresses or use separate network namespaces,
then add `--local-address` and `--secondary-local-address`.

## TLS and non-local targets

Use `--ca-file path/to/ca.pem` to add a private certificate authority for both
HTTPS token calls and WSS sockets. The probe does not provide a TLS-verification
bypass.

Non-private hosts require `--allow-public-target` because token calls create
diagnostic device records. Known production OpenJibo hosts require the additional
`--dangerously-allow-production` switch. Do not use either switch without explicit
authorization.

The entrypoint and Hub URLs are independent so omitted ports, incorrect schemes,
DNS failures, and certificate failures remain visible rather than being hidden by
a single base URL.

## Interpreting failures

| Last successful phase | Likely boundary |
| --- | --- |
| No token request reached the server | entrypoint hostname, port, DNS, or TLS |
| `CreateHubToken` succeeded but Hub handshake has `tokenFingerprint=none` | client did not attach the issued token |
| Hub handshake returns 401 with a fingerprint | token was unknown, expired, or the wrong kind |
| Hub handshake returns 426 | HTTP was used outside isolated self-hosting |
| Listen connects but proactive fails | proactive route or second Hub connection handling |
| Proactive connects but returns no final `PROACTIVE` | proactive transaction routing or `TRIGGER`/`CONTEXT` correlation |
| Both sockets connect but a listen turn times out | interaction/provider path, not authentication |

The server log path `/v1/listen/{token}` is always redacted and does not prove a
token was present. Use `tokenFingerprint` as the authoritative signal.
