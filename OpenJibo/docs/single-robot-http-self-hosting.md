# Single-Robot HTTP Self-Hosting

OpenJibo normally requires an issued credential on every robot WebSocket:

- notification/API sockets use a `robot` token from `Notification.NewRobotToken`
- NeoHub listen and proactive sockets use a `hub` token from `Account.CreateHubToken`

Unknown, expired, and wrong-kind tokens are rejected. The text `v1/listen` is a
route, not a credential.

## Diagnosing an incomplete robot override

The entrypoint and Hub settings are independent. For one process listening on
`10.0.0.80:24605`, use all five values:

```json
"override": {
  "entrypoint_hostname": "10.0.0.80",
  "entrypoint_port": 24605,
  "hub_hostname": "10.0.0.80",
  "hub_port": 24605,
  "hub_secure": false
}
```

An override that omits `entrypoint_port` can leave token issuance pointed at the
stock endpoint. Omitting `hub_secure: false` can make Jetstream attempt TLS
against the local endpoint. Typical evidence is a combination of:

- server: `tokenFingerprint=none`, `missing token`, and HTTP 401
- robot: `WebSocket Exception: Not authorized`
- server: TLS `unknown ca` when the robot tries HTTPS
- robot: `Host not found: api.jibo.com` when local DNS is incomplete

The local DNS override must also resolve `api.jibo.com` to the self-host server;
Jetstream configuration alone does not redirect every robot subsystem.

## Explicit tokenless compatibility mode

Some stock robots do not obtain a Hub token from a local HTTP entrypoint. A
single-robot LAN installation can opt into a bounded compatibility mode:

```dotenv
OpenJibo__Deployment__Mode=self-hosted-isolated
OPENJIBO_ALLOW_TOKENLESS_SINGLE_ROBOT_HUB=true
```

The opt-in maps to `OpenJibo:SelfHosted:AllowTokenlessSingleRobotHub`. It is
disabled by default and is ignored unless the deployment mode is explicitly
`self-hosted-isolated`.

When enabled, a tokenless socket is accepted only when all of these are true:

- the route is exactly `/listen`, `/v1/listen`, `/proactive`, or `/v1/proactive`
- the request is plain HTTP/WebSocket, not HTTPS/WSS
- both the Host and client address are private, loopback, link-local, or `.local`
- no different client IP currently holds a compatibility lease
- no `Forwarded` or `X-Forwarded-*` proxy headers are present
- the client address holds at most two sockets, for listen and proactive

Each lease lasts only for its WebSocket connection. At most two sockets from the
same robot address can coexist so listen and proactive can both operate. A third
socket or a second client address is rejected until the relevant existing socket
closes.

This is an operational one-client boundary, not cryptographic robot identity.
Do not publish this mode through port forwarding, a public reverse proxy, or a
shared untrusted LAN. Prefer issued Hub tokens whenever the robot can obtain
them.

## Hybrid and managed deployments

Self-host hybrid and managed deployments must use HTTPS/WSS and issued tokens.
The tokenless compatibility flag does not bypass HTTPS authentication, and a
public numeric IP is not classified as a local HTTP target by the portal.
