# Local OpenJibo Cloud Quickstart

## Purpose

This guide is for people who want to run OpenJibo locally.

There are three different local paths:

- `.NET cloud`: the current OpenJibo cloud implementation and the path we are actively shipping.
- `Node cloud`: the legacy protocol oracle and reverse-engineering server. It is still useful and fun to run, but it is not the production direction.
- `Playground`: a direct local Jibo ASR/TTS demo. It talks to Jibo on local ports and does not replace the cloud.

For a physical Jibo, local cloud testing still assumes a controlled network, DNS/host routing, and certificate setup. See [device-bootstrap.md](device-bootstrap.md) for the device side.

## Prerequisites

Install:

- .NET SDK for the repo target framework
- Node.js and npm, for the Node oracle only
- PowerShell, for the Windows helper scripts
- `openssl`, for Linux live testing on port `443` with PEM certificate material

Optional for real audio experiments:

- `ffmpeg`
- `whisper.cpp`

## Run The .NET Cloud

From the repo root:

```powershell
.\scripts\cloud\Start-OpenJiboDotNet.ps1
```

By default, this starts:

- HTTPS: `https://localhost:24604`
- HTTP: `http://localhost:24605`
- health check: `http://localhost:24605/health`
- websocket captures: `captures/websocket`
- HTTP captures: `captures/http`

Smoke check:

```powershell
.\scripts\cloud\Invoke-CloudSmoke.ps1 -BaseUrl http://localhost:24605
```

Run with the Azure Blob sample launch profile:

```powershell
.\scripts\cloud\Start-OpenJiboDotNet.ps1 -UseAzureBlobProfile
```

Run directly without a launch profile, useful when you want to supply all URLs and certificate settings by environment:

```powershell
$env:ASPNETCORE_URLS = "http://0.0.0.0:24605"
.\scripts\cloud\Start-OpenJiboDotNet.ps1 -NoLaunchProfile
```

For a Linux live-device run on port `443`, reuse the existing PEM certificate material:

```bash
CERT_PEM=/path/to/cert.pem \
KEY_PEM=/path/to/key.pem \
ASPNETCORE_URLS="https://0.0.0.0:443;http://0.0.0.0:24605" \
./scripts/cloud/start-dotnet-with-node-cert.sh
```

Then run:

```bash
./scripts/cloud/invoke-live-jibo-prep.sh
```

## Run The Node Cloud

The Node cloud lives at `src/Jibo.Cloud/node`.

From the repo root:

```powershell
.\scripts\cloud\Start-OpenJiboNode.ps1 -Install
```

After dependencies are installed once, you can usually run:

```powershell
.\scripts\cloud\Start-OpenJiboNode.ps1
```

Important details:

- The Node server binds HTTPS on port `443`.
- It expects `cert.pem` and `key.pem` in `src/Jibo.Cloud/node`.
- Use the same certificate material that your controlled Jibo routing already trusts.
- On Windows or Linux, binding port `443` may require an elevated shell.
- Stop the .NET cloud first if it is also using port `443`.

Manual equivalent:

```powershell
cd src\Jibo.Cloud\node
npm install
node .\open-jibo-link.js
```

The Node server writes discovery logs under `src/Jibo.Cloud/node/logs`.

## Run Playground

Playground is not a cloud server. It connects straight to a Jibo on your LAN:

- ASR HTTP: `http://JIBO_IP:8088/asr_simple_interface`
- ASR websocket: `ws://JIBO_IP:8088/simple_port`
- TTS HTTP: `http://JIBO_IP:8089/tts_speak`

From the repo root:

```powershell
.\scripts\cloud\Start-OpenJiboPlayground.ps1
```

When prompted, enter the Jibo IP address.

Use Playground when you want to test the local ASR/TTS client behavior directly. Use the `.NET` or Node cloud when you want Jibo to boot and talk through the cloud-shaped protocol path.

## Which One Should I Use?

Use `.NET cloud` if you want the current OpenJibo behavior, release testing, captures, or anything close to the hosted future.

Use `Node cloud` if you want the original prototype/oracle, protocol discovery, or a quick comparison against older behavior.

Use `Playground` if you already know the robot IP and just want a local microphone-to-ASR-to-TTS loop through Jibo's local client interfaces.

## Common Issues

If `/health` fails, confirm the .NET cloud is running and use `http://localhost:24605/health` for local checks.

If the Node server fails with a certificate error, add `cert.pem` and `key.pem` to `src/Jibo.Cloud/node`.

If port `443` is busy, stop the other cloud server first or run the .NET cloud on the local dev ports.

If a physical Jibo does not connect, confirm DNS/host routing for:

- `api.jibo.com`
- `api-socket.jibo.com`
- `neo-hub.jibo.com`

Then compare with the live runbook in [live-jibo-test-runbook.md](live-jibo-test-runbook.md).
