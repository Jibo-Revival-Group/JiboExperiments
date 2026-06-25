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
  Builds and pushes the managed Open Jibo image to Azure Container Registry using `az acr build`.
- `Deploy-OpenJiboManagedFoundation.ps1`
  Deploys the managed foundation resources, then seeds Key Vault secrets from the deployment outputs and supplied bootstrap values.
- `Deploy-OpenJiboManaged.ps1`
  Deploys the first Azure Container Apps stack from the Bicep template under `infra/azure/container-apps/`. Use `-RunMigration` to apply schema changes and `-RunSmoke` to verify the deployed endpoint. Use `-ApiHostname api.openjibo.com` to keep deploy smoke and runtime configuration aligned with the canonical robot-facing API hostname.
- `deploy-openjibo-managed-foundation.sh`
  Bash deploy wrapper for the managed foundation stack.
- `publish-openjibo-managed.sh`
  Bash build-and-push wrapper for the managed ACR image.
- `deploy-openjibo-managed.sh`
  Bash deploy wrapper for the managed Container Apps stack plus optional migration and smoke. Use `--api-hostname api.openjibo.com` to deploy and smoke the canonical managed API hostname; this is the default for the managed path.
- `Test-OpenJiboManagedDeploymentContract.ps1`
  Validates the managed deployment contract by checking the Bicep templates, workflow, and deploy scripts for expected markers before any Azure calls run.
- `test-openjibo-managed-deployment-contract.sh`
  Bash contract checker for the managed deployment path and workflow markers, including the canonical `api.openjibo.com` hostname path.
- `Test-OpenJiboSelfHostedDeploymentContract.ps1`
  Validates the self-hosted contract by checking the Compose file, migration wrapper, and smoke script before local CI brings up the stack.
- GitHub Actions `openjibo-cloud-managed-deploy`
  Manual workflow that deploys the foundation, builds the managed image, deploys the ACA stack, runs migrations, and smokes the deployed endpoint. The workflow defaults the robot-facing API hostname to `api.openjibo.com`.
- `OPENJIBO_POSTGRES_PASSWORD`
  Required when running the self-hosted PostgreSQL stack locally or in CI so the database password stays out of source control.
- `initialize-openjibo-compose-env.sh`
  Copies `.env.example` to `.env` when the compose env file is missing and keeps `OPENJIBO_POSTGRES_PASSWORD` in sync when the file already exists.
- `Initialize-OpenJiboComposeEnv.ps1`
  PowerShell equivalent of the compose env bootstrap helper, including password propagation into an existing `.env`.
- `invoke-openjibo-self-hosted-stack.sh`
  Starts the local self-hosted stack, bootstraps `.env`, and can include the migration service when requested.
- `Invoke-OpenJiboSelfHostedStack.ps1`
  PowerShell equivalent of the self-hosted stack launcher.
- `scripts/cloud/postgres-init/01-create-databases.sh`
  PostgreSQL init hook that creates the additional `openjibo_memory` database the migrator expects in self-hosted mode.
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
- `invoke-openjibo-migration.sh`
  Bash wrapper for the PostgreSQL migration runner so Linux container and self-hosted flows do not depend on PowerShell.
- `invoke-cloud-smoke.sh`
  Bash onboarding replay and health smoke for Linux CI and containerized self-hosted runs.
- `test-openjibo-self-hosted-deployment-contract.sh`
  Bash contract checker for the self-hosted compose/migration/smoke trio.
- `get-websocket-capture-summary.sh`
  Bash summary helper for captured websocket telemetry and exported live-run fixtures.
- `import-websocket-capture-fixture.py`
  Cross-platform import/sanitization helper for exported websocket fixtures.

See [docs/local-cloud-quickstart.md](../../docs/local-cloud-quickstart.md) for the full local setup guide.