param(
    [switch]$RunMigration,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

& (Join-Path $PSScriptRoot "Initialize-OpenJiboComposeEnv.ps1") -RepoRoot $repoRoot

$composeArgs = @("compose", "up", "-d")
if (-not $SkipBuild) {
    $composeArgs += "--build"
}

$composeArgs += "postgres"
if ($RunMigration) {
    $composeArgs += "migrate"
}
$composeArgs += "api"

Push-Location $repoRoot
try {
    & docker @composeArgs
}
finally {
    Pop-Location
}
