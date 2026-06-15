param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [string]$TemplatePath = "infra/azure/foundation/openjibo-managed-foundation.bicep",
    [string]$StateConnectionString = "",
    [string]$PersonalMemoryConnectionString = "",
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
    "--output", "json"
)

Write-Host "Deploying Open Jibo managed foundation to resource group '$ResourceGroupName'"
$deploymentJson = az @arguments | ConvertFrom-Json
$outputs = $deploymentJson.properties.outputs

if (-not [string]::IsNullOrWhiteSpace($StateConnectionString)) {
    az keyvault secret set --vault-name $outputs.keyVaultName.value --name openjibo-state-connection-string --value $StateConnectionString | Out-Null
}

if (-not [string]::IsNullOrWhiteSpace($PersonalMemoryConnectionString)) {
    az keyvault secret set --vault-name $outputs.keyVaultName.value --name openjibo-personal-memory-connection-string --value $PersonalMemoryConnectionString | Out-Null
}

if (-not [string]::IsNullOrWhiteSpace($outputs.storageConnectionString.value)) {
    az keyvault secret set --vault-name $outputs.keyVaultName.value --name openjibo-media-connection-string --value $outputs.storageConnectionString.value | Out-Null
}

if (-not [string]::IsNullOrWhiteSpace($OpenWeatherApiKey)) {
    az keyvault secret set --vault-name $outputs.keyVaultName.value --name openjibo-openweather-api-key --value $OpenWeatherApiKey | Out-Null
}

if (-not [string]::IsNullOrWhiteSpace($NewsApiKey)) {
    az keyvault secret set --vault-name $outputs.keyVaultName.value --name openjibo-newsapi-key --value $NewsApiKey | Out-Null
}

$deploymentJson
