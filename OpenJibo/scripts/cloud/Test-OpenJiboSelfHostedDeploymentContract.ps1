param(
    [string]$ComposePath = "docker-compose.yml",
    [string]$DockerfilePath = "Dockerfile",
    [string]$WorkflowPath = "../.github/workflows/openjibo-cloud-ci.yml",
    [string]$MigrationScriptPath = "scripts/cloud/Invoke-OpenJiboMigration.ps1",
    [string]$LinuxMigrationScriptPath = "scripts/cloud/invoke-openjibo-migration.sh",
    [string]$SmokeScriptPath = "scripts/cloud/Invoke-CloudSmoke.ps1",
    [string]$LinuxSmokeScriptPath = "scripts/cloud/invoke-cloud-smoke.sh",
    [string]$StackScriptPath = "scripts/cloud/invoke-openjibo-self-hosted-stack.sh",
    [string]$ComposeEnvBootstrapScriptPath = "scripts/cloud/initialize-openjibo-compose-env.sh",
    [string]$RunbookPath = "docs/self-hosted-runbook.md"
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

function Get-RepoFileText {
    param([string]$RelativePath)

    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $RelativePath))
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Missing required file: $fullPath"
    }

    return Get-Content -LiteralPath $fullPath -Raw
}

$composeText = Get-RepoFileText -RelativePath $ComposePath
$dockerfileText = Get-RepoFileText -RelativePath $DockerfilePath
$workflowText = Get-RepoFileText -RelativePath $WorkflowPath
$migrationText = Get-RepoFileText -RelativePath $MigrationScriptPath
$linuxMigrationText = Get-RepoFileText -RelativePath $LinuxMigrationScriptPath
$smokeText = Get-RepoFileText -RelativePath $SmokeScriptPath
$linuxSmokeText = Get-RepoFileText -RelativePath $LinuxSmokeScriptPath
$stackText = Get-RepoFileText -RelativePath $StackScriptPath
$composeEnvBootstrapText = Get-RepoFileText -RelativePath $ComposeEnvBootstrapScriptPath
$runbookText = Get-RepoFileText -RelativePath $RunbookPath

$requiredComposeMarkers = @(
    "services:",
    "migrate:",
    "api:",
    "postgres:",
    "smoke:",
    "OPENJIBO_POSTGRES_PASSWORD",
    "OpenJibo__State__Backend: PostgreSql",
    "OpenJibo__Deployment__Mode: self-hosted-isolated",
    "OpenJibo__AcceptedHosts__0: localhost",
    "OpenJibo__AcceptedHosts__1: 127.0.0.1",
    "OpenJibo__PersonalMemory__Backend: PostgreSql",
    "OpenJibo__Media__Backend: File",
    "healthcheck:",
    "/health"
)

$requiredMigrationMarkers = @(
    "--target",
    "--apply",
    "--preview",
    "PostgreSql"
)

$requiredSmokeMarkers = @(
    "/health",
    "Account_20151111.Create",
    "Account_20151111.Login",
    "Loop_20160324.ListLoops",
    "OOBE_20161026.PrepareRobot",
    "OOBE_20161026.GetStatus",
    "OOBE_20161026.SetupRobot",
    "OOBE_20161026.VerifyConnection",
    "rollbackSnapshotId",
    "targetMode",
    "targetHost"
)

$requiredWorkflowMarkers = @(
    "working-directory: OpenJibo",
    "invoke-openjibo-self-hosted-stack.sh",
    "test-openjibo-self-hosted-deployment-contract.sh",
    "invoke-cloud-smoke.sh",
    "Repeat startup with persisted volumes",
    "invoke-openjibo-self-hosted-stack.sh --skip-build"
)

foreach ($marker in $requiredComposeMarkers) {
    if ($composeText -notmatch [regex]::Escape($marker)) {
        throw "Docker Compose file is missing expected marker: $marker"
    }
}

foreach ($marker in $requiredMigrationMarkers) {
    if ($migrationText -notmatch [regex]::Escape($marker)) {
        throw "Migration wrapper is missing expected marker: $marker"
    }
}

foreach ($marker in $requiredMigrationMarkers) {
    if ($linuxMigrationText -notmatch [regex]::Escape($marker)) {
        throw "Linux migration wrapper is missing expected marker: $marker"
    }
}

foreach ($marker in $requiredSmokeMarkers) {
    if ($smokeText -notmatch [regex]::Escape($marker)) {
        throw "Smoke script is missing expected marker: $marker"
    }
}

foreach ($marker in $requiredSmokeMarkers) {
    if ($linuxSmokeText -notmatch [regex]::Escape($marker)) {
        throw "Linux smoke script is missing expected marker: $marker"
    }
}

if ($stackText -notmatch [regex]::Escape("initialize-openjibo-compose-env.sh")) {
    throw "Self-hosted stack launcher is missing env bootstrap helper reference."
}

if ($stackText -notmatch [regex]::Escape("compose up -d")) {
    throw "Self-hosted stack launcher is missing compose launch command."
}

if ($stackText -notmatch [regex]::Escape("migrate") -or $stackText -notmatch [regex]::Escape("api")) {
    throw "Self-hosted stack launcher is missing core service names."
}

foreach ($marker in $requiredWorkflowMarkers) {
    if ($workflowText -notmatch [regex]::Escape($marker)) {
        throw "Workflow is missing expected marker: $marker"
    }
}

if ($composeText -match [regex]::Escape("OPENJIBO_MEDIA_CONNECTION_STRING")) {
    throw "Self-hosted compose still references the managed media connection secret."
}

if ($composeText -notmatch [regex]::Escape("./scripts/cloud/postgres-init:/docker-entrypoint-initdb.d:ro")) {
    throw "Self-hosted compose postgres initdb mount points at the wrong path."
}

if ($composeEnvBootstrapText -notmatch [regex]::Escape("OPENJIBO_POSTGRES_PASSWORD")) {
    throw "Compose env bootstrap is missing the PostgreSQL password propagation logic."
}

function Assert-ContractPattern {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$FailureMessage
    )

    if (-not [regex]::IsMatch($Text, $Pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)) {
        throw $FailureMessage
    }
}

Assert-ContractPattern $composeText 'migrate:\s+.*?command:\s+.*?- /app/migrations/Jibo\.Cloud\.Migrations\.dll\s+.*?- --apply\s+.*?- --target\s+.*?- all' 'Migration service must apply all migrations with the checked-in migration binary.'
Assert-ContractPattern $composeText 'api:\s+.*?depends_on:\s+.*?migrate:\s+.*?condition: service_completed_successfully' 'API must wait for a successful migration service.'
Assert-ContractPattern $composeText 'migrate:\s+.*?depends_on:\s+.*?postgres:\s+.*?condition: service_healthy' 'Migration service must wait for healthy PostgreSQL.'
Assert-ContractPattern $composeText 'api:\s+.*?volumes:\s+.*?- api-data:/data' 'API must use the persistent api-data volume.'
Assert-ContractPattern $composeText 'postgres:\s+.*?volumes:\s+.*?- postgres-data:/var/lib/postgresql/data' 'PostgreSQL must use the persistent postgres-data volume.'

$requiredRunbookMarkers = @(
    "docker compose exec -T postgres pg_dump",
    "openjibo_state",
    "openjibo_memory",
    "-Preview",
    "--preview",
    "docker compose logs --no-color migrate",
    "docker compose logs --no-color postgres",
    "docker compose down -v",
    "28P01: password authentication failed",
    "\password openjibo"
)

foreach ($marker in $requiredRunbookMarkers) {
    if ($runbookText -notmatch [regex]::Escape($marker)) {
        throw "Self-hosted runbook is missing required migration/recovery guidance: $marker"
    }
}
$requiredDockerfileMarkers = @(
    "ARG ENABLE_LOCAL_WHISPER=true",
    'whisper-${ENABLE_LOCAL_WHISPER}',
    "ggml-org/whisper.cpp.git",
    "download-ggml-model.sh",
    "/usr/bin/whisper.cpp/build/bin"
)

foreach ($marker in $requiredDockerfileMarkers) {
    if ($dockerfileText -notmatch [regex]::Escape($marker)) {
        throw "Dockerfile is missing expected local Whisper build marker: $marker"
    }
}

$requiredComposeWhisperMarkers = @(
    'ENABLE_LOCAL_WHISPER: ${OPENJIBO_ENABLE_LOCAL_WHISPER:-true}',
    'WHISPER_MODEL: ${OPENJIBO_WHISPER_MODEL:-base.en}',
    "OPENJIBO_STT_WHISPER_CLI_PATH",
    "OPENJIBO_STT_WHISPER_MODEL_PATH"
)

foreach ($marker in $requiredComposeWhisperMarkers) {
    if ($composeText -notmatch [regex]::Escape($marker)) {
        throw "Self-hosted compose is missing expected local Whisper wiring marker: $marker"
    }
}

Write-Host "Self-hosted deployment contract checks passed."
