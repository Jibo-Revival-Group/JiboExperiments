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
    [string]$NewsApiKey = "",
    [switch]$RunMigration,
    [switch]$RunSmoke
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
    "--parameters", "newsApiKey=$NewsApiKey",
    "--output", "json"
)

Write-Host "Deploying Open Jibo managed Container Apps stack to resource group '$ResourceGroupName'"
$deploymentJson = az @arguments | ConvertFrom-Json

if ($RunMigration) {
    $migrationScript = Join-Path $repoRoot "scripts/cloud/Invoke-OpenJiboMigration.ps1"
    & $migrationScript `
        -Target all `
        -StateConnectionString $StateConnectionString `
        -PersonalMemoryConnectionString $PersonalMemoryConnectionString
}

if ($RunSmoke) {
    $containerAppFqdn = $deploymentJson.properties.outputs.containerAppFqdn.value
    if ([string]::IsNullOrWhiteSpace($containerAppFqdn)) {
        throw "Container app FQDN was not returned from the deployment."
    }

    $smokeScript = Join-Path $repoRoot "scripts/cloud/Invoke-CloudSmoke.ps1"
    & $smokeScript -BaseUrl "https://$containerAppFqdn"
}

$deploymentJson
