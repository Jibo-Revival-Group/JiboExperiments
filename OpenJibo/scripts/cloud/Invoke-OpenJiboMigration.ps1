param(
    [string]$ProjectPath = "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Migrations/Jibo.Cloud.Migrations.csproj",
    [ValidateSet("state", "personal-memory", "all")]
    [string]$Target = "all",
    [switch]$Preview,
    [switch]$VerboseOutput
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$resolvedProjectPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))

if (-not (Test-Path -LiteralPath $resolvedProjectPath)) {
    throw "Could not find migration project at $resolvedProjectPath"
}

$arguments = @(
    "run",
    "--project", $resolvedProjectPath,
    "--",
    "--target", $Target
)

if ($Preview) {
    $arguments += "--preview"
} else {
    $arguments += "--apply"
}

if ($VerboseOutput) {
    $arguments += "--verbose"
}

Write-Host "Running Open Jibo migrations for target '$Target'"
dotnet @arguments
