param(
    [string]$ProjectPath = "src/Playground/Playground.csproj"
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$resolvedProjectPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))

if (-not (Test-Path -LiteralPath $resolvedProjectPath)) {
    throw "Could not find Playground project at $resolvedProjectPath"
}

Write-Host "Starting OpenJibo Playground"
Write-Host " - project: $resolvedProjectPath"
Write-Host " - mode: direct local Jibo ASR/TTS client"
Write-Host ""
Write-Host "When prompted, enter the Jibo IP address on your local network."

dotnet run --project $resolvedProjectPath
