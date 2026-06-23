param(
    [string]$ComposePath = "docker-compose.yml",
    [string]$WorkflowPath = "../.github/workflows/openjibo-cloud-ci.yml",
    [string]$MigrationScriptPath = "scripts/cloud/Invoke-OpenJiboMigration.ps1",
    [string]$LinuxMigrationScriptPath = "scripts/cloud/invoke-openjibo-migration.sh",
    [string]$SmokeScriptPath = "scripts/cloud/Invoke-CloudSmoke.ps1",
    [string]$LinuxSmokeScriptPath = "scripts/cloud/invoke-cloud-smoke.sh"
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
$workflowText = Get-RepoFileText -RelativePath $WorkflowPath
$migrationText = Get-RepoFileText -RelativePath $MigrationScriptPath
$linuxMigrationText = Get-RepoFileText -RelativePath $LinuxMigrationScriptPath
$smokeText = Get-RepoFileText -RelativePath $SmokeScriptPath
$linuxSmokeText = Get-RepoFileText -RelativePath $LinuxSmokeScriptPath

$requiredComposeMarkers = @(
    "services:",
    "migrate:",
    "api:",
    "postgres:",
    "smoke:",
    "OPENJIBO_POSTGRES_PASSWORD",
    "OpenJibo__State__Backend: PostgreSql",
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
    "OOBE_20161026.SetupRobot"
)

$requiredWorkflowMarkers = @(
    "working-directory: OpenJibo",
    "test-openjibo-self-hosted-deployment-contract.sh",
    "invoke-cloud-smoke.sh"
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

foreach ($marker in $requiredWorkflowMarkers) {
    if ($workflowText -notmatch [regex]::Escape($marker)) {
        throw "Workflow is missing expected marker: $marker"
    }
}

if ($composeText -match [regex]::Escape("OPENJIBO_MEDIA_CONNECTION_STRING")) {
    throw "Self-hosted compose still references the managed media connection secret."
}

Write-Host "Self-hosted deployment contract checks passed."
