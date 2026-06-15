param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$KeyVaultName,

    [string]$ImageTag = "managed",
    [Parameter(Mandatory = $true)]
    [string]$RegistryName,
    [string]$TemplatePath = "infra/azure/container-apps/openjibo-managed.bicep",
    [string]$ParametersPath = "infra/azure/container-apps/openjibo-managed.parameters.json",
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

if ([string]::IsNullOrWhiteSpace($KeyVaultName)) {
    throw "KeyVaultName is required."
}

if ([string]::IsNullOrWhiteSpace($RegistryName)) {
    throw "RegistryName is required."
}

$RegistryLoginServer = "$RegistryName.azurecr.io"
$deploymentName = "openjibo-managed-{0}" -f ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())

$arguments = @(
    "deployment", "group", "create",
    "--resource-group", $ResourceGroupName,
    "--name", $deploymentName,
    "--template-file", $resolvedTemplatePath,
    "--parameters", "@$resolvedParametersPath",
    "--parameters", "registryLoginServer=$RegistryLoginServer",
    "--parameters", "keyVaultName=$KeyVaultName",
    "--output", "json"
)

Write-Host "Deploying Open Jibo managed Container Apps stack to resource group '$ResourceGroupName'"
$deploymentJson = az @arguments | ConvertFrom-Json

if ($RunMigration) {
    $stateConnectionString = az keyvault secret show --vault-name $KeyVaultName --name openjibo-state-connection-string --query value -o tsv
    $personalMemoryConnectionString = az keyvault secret show --vault-name $KeyVaultName --name openjibo-personal-memory-connection-string --query value -o tsv
    $migrationScript = Join-Path $repoRoot "scripts/cloud/Invoke-OpenJiboMigration.ps1"
    & $migrationScript `
        -Target all `
        -StateConnectionString $stateConnectionString `
        -PersonalMemoryConnectionString $personalMemoryConnectionString
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
