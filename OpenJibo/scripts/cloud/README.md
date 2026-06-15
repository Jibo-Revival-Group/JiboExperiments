# Cloud Scripts

These scripts help exercise the new .NET hosted cloud locally.

- `Start-OpenJiboDotNet.ps1`
  Starts the current `.NET` cloud with local capture directories configured.
- `Start-OpenJiboNode.ps1`
  Starts the legacy Node protocol oracle from `src/Jibo.Cloud/node`.
- `Start-OpenJiboPlayground.ps1`
  Starts the direct local Jibo ASR/TTS Playground demo.
- `Invoke-CloudSmoke.ps1`
  Runs a few quick HTTP checks against a local OpenJibo cloud instance.
- `Invoke-OpenJiboMigration.ps1`
  Runs the PostgreSQL migration wrapper against the local or managed database targets.
- `Publish-OpenJiboManaged.ps1`
  Builds and pushes the managed Open Jibo image to Azure Container Registry.
- `Deploy-OpenJiboManaged.ps1`
  Deploys the first Azure Container Apps stack from the Bicep template under `infra/azure/container-apps/`. Use `-RunMigration` to apply schema changes and `-RunSmoke` to verify the deployed endpoint.
- `OPENJIBO_POSTGRES_PASSWORD`
  Required when running the self-hosted PostgreSQL stack locally or in CI so the database password stays out of source control.
- `Invoke-ProtocolFixture.ps1`
  Replays a sanitized HTTP fixture against a running local instance.
- `Get-WebSocketCaptureSummary.ps1`
  Summarizes captured websocket telemetry events and exported live-run fixtures from the .NET cloud.
- repo-root `captures/http/`
  Structured HTTP request/response telemetry for live robot startup comparison.
- repo-root `captures/websocket/`
  Structured websocket telemetry plus exported replay fixtures for live robot sessions.
- `Invoke-LiveJiboPrep.ps1`
  Runs a small readiness checklist before the first physical Jibo test against the .NET cloud.
- `Import-WebSocketCaptureFixture.ps1`
  Sanitizes an exported websocket capture fixture and copies it into the checked-in websocket fixture set.
- `New-CaptureBundle.ps1`
  Packages the capture root, capture index, and exported fixtures into a single zip bundle for group testing handoff.
- `start-dotnet-with-node-cert.sh`
  Starts the .NET API on Linux using the same PEM certificate material already used by the Node server.
- `invoke-live-jibo-prep.sh`
  Bash equivalent of the live-run prep checklist for Ubuntu.
- `get-websocket-capture-summary.sh`
  Bash summary helper for captured websocket telemetry and exported fixtures.
- `import-websocket-capture-fixture.py`
  Cross-platform import/sanitization helper for exported websocket fixtures.

See [docs/local-cloud-quickstart.md](../../docs/local-cloud-quickstart.md) for the full local setup guide.
