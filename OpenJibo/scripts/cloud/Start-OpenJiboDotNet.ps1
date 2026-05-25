param(
    [string]$ProjectPath = "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/Jibo.Cloud.Api.csproj",
    [string]$LaunchProfile = "Jibo.Cloud.Api",
    [string]$CaptureRoot = "captures",
    [switch]$UseAzureBlobProfile,
    [switch]$NoLaunchProfile
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$resolvedProjectPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))
$resolvedCaptureRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $CaptureRoot))
$webSocketCaptureDirectory = Join-Path $resolvedCaptureRoot "websocket"
$httpCaptureDirectory = Join-Path $resolvedCaptureRoot "http"

if (-not (Test-Path -LiteralPath $resolvedProjectPath)) {
    throw "Could not find .NET API project at $resolvedProjectPath"
}

New-Item -ItemType Directory -Force -Path $webSocketCaptureDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $httpCaptureDirectory | Out-Null

$env:OpenJibo__Telemetry__DirectoryPath = $webSocketCaptureDirectory
$env:OpenJibo__ProtocolTelemetry__DirectoryPath = $httpCaptureDirectory

if ($UseAzureBlobProfile) {
    $LaunchProfile = "Jibo.Cloud.Api.AzureBlob"
}

Write-Host "Starting OpenJibo .NET cloud"
Write-Host " - project: $resolvedProjectPath"
Write-Host " - websocket captures: $webSocketCaptureDirectory"
Write-Host " - http captures: $httpCaptureDirectory"
if ($NoLaunchProfile) {
    Write-Host " - launch profile: disabled"
    dotnet run --project $resolvedProjectPath --no-launch-profile
} else {
    Write-Host " - launch profile: $LaunchProfile"
    dotnet run --project $resolvedProjectPath --launch-profile $LaunchProfile
}
