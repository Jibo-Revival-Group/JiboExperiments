param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [string]$TemplatePath = "infra/azure/foundation/openjibo-managed-foundation.bicep",
    [Parameter(Mandatory = $true)]
    [string]$StateConnectionString,
    [Parameter(Mandatory = $true)]
    [string]$PersonalMemoryConnectionString,
    [Parameter(Mandatory = $true)]
    [string]$MediaConnectionString,
    [string]$OpenWeatherApiKey = "",
    [string]$NewsApiKey = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$resolvedTemplatePath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $TemplatePath))

if (-not (Test-Path -LiteralPath $resolvedTemplatePath)) {
    throw "Could not find Bicep template at $resolvedTemplatePath"
}

$deploymentName = "openjibo-foundation-{0}" -f ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())

$arguments = @(
    "deployment", "group", "create",
    "--resource-group", $ResourceGroupName,
    "--name", $deploymentName,
    "--template-file", $resolvedTemplatePath,
    "--parameters", "stateConnectionString=$StateConnectionString",
    "--parameters", "personalMemoryConnectionString=$PersonalMemoryConnectionString",
    "--parameters", "mediaConnectionString=$MediaConnectionString",
    "--parameters", "openWeatherApiKey=$OpenWeatherApiKey",
    "--parameters", "newsApiKey=$NewsApiKey",
    "--output", "json"
)

Write-Host "Deploying Open Jibo managed foundation to resource group '$ResourceGroupName'"
$deploymentJson = az @arguments | ConvertFrom-Json
$deploymentJson
