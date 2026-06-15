param(
    [string]$ComposePath = "docker-compose.yml",
    [string]$MigrationScriptPath = "scripts/cloud/Invoke-OpenJiboMigration.ps1",
    [string]$SmokeScriptPath = "scripts/cloud/Invoke-CloudSmoke.ps1"
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
$migrationText = Get-RepoFileText -RelativePath $MigrationScriptPath
$smokeText = Get-RepoFileText -RelativePath $SmokeScriptPath

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

foreach ($marker in $requiredSmokeMarkers) {
    if ($smokeText -notmatch [regex]::Escape($marker)) {
        throw "Smoke script is missing expected marker: $marker"
    }
}

if ($composeText -match [regex]::Escape("OPENJIBO_MEDIA_CONNECTION_STRING")) {
    throw "Self-hosted compose still references the managed media connection secret."
}

Write-Host "Self-hosted deployment contract checks passed."
