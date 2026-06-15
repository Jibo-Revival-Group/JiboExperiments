param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$RegistryLoginServer,

    [Parameter(Mandatory = $true)]
    [string]$RegistryUsername,

    [Parameter(Mandatory = $true)]
    [string]$RegistryPassword,

    [string]$ImageRepository = "openjibo-cloud",
    [string]$ImageTag = "managed",
    [string]$TemplatePath = "infra/azure/container-apps/openjibo-managed.bicep",
    [string]$ParametersPath = "infra/azure/container-apps/openjibo-managed.parameters.json",
    [string]$StateConnectionString,
    [string]$PersonalMemoryConnectionString,
    [string]$MediaConnectionString,
    [string]$OpenWeatherApiKey = "",
    [string]$NewsApiKey = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$resolvedTemplatePath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $TemplatePath))
$resolvedParametersPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ParametersPath))

if (-not (Test-Path -LiteralPath $resolvedTemplatePath)) {
    throw "Could not find Bicep template at $resolvedTemplatePath"
}

if (-not (Test-Path -LiteralPath $resolvedParametersPath)) {
    throw "Could not find parameter file at $resolvedParametersPath"
}

if ([string]::IsNullOrWhiteSpace($StateConnectionString)) {
    throw "StateConnectionString is required."
}

if ([string]::IsNullOrWhiteSpace($PersonalMemoryConnectionString)) {
    throw "PersonalMemoryConnectionString is required."
}

if ([string]::IsNullOrWhiteSpace($MediaConnectionString)) {
    throw "MediaConnectionString is required."
}

$deploymentName = "openjibo-managed-{0}" -f ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())

$arguments = @(
    "deployment", "group", "create",
    "--resource-group", $ResourceGroupName,
    "--name", $deploymentName,
    "--template-file", $resolvedTemplatePath,
    "--parameters", "@$resolvedParametersPath",
    "--parameters", "registryLoginServer=$RegistryLoginServer",
    "--parameters", "registryUsername=$RegistryUsername",
    "--parameters", "registryPassword=$RegistryPassword",
    "--parameters", "imageRepository=$ImageRepository",
    "--parameters", "imageTag=$ImageTag",
    "--parameters", "stateConnectionString=$StateConnectionString",
    "--parameters", "personalMemoryConnectionString=$PersonalMemoryConnectionString",
    "--parameters", "mediaConnectionString=$MediaConnectionString",
    "--parameters", "openWeatherApiKey=$OpenWeatherApiKey",
    "--parameters", "newsApiKey=$NewsApiKey"
)

Write-Host "Deploying Open Jibo managed Container Apps stack to resource group '$ResourceGroupName'"
az @arguments
