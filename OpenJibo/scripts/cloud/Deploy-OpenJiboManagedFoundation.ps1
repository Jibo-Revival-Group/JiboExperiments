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

$storageConnectionString = az storage account show-connection-string --resource-group $ResourceGroupName --name $outputs.storageAccountName.value --query connectionString --output tsv
$resolvedStateConnectionString = if ([string]::IsNullOrWhiteSpace($StateConnectionString)) { $storageConnectionString } else { $StateConnectionString }
$resolvedPersonalMemoryConnectionString = if ([string]::IsNullOrWhiteSpace($PersonalMemoryConnectionString)) { $storageConnectionString } else { $PersonalMemoryConnectionString }

if (-not [string]::IsNullOrWhiteSpace($resolvedStateConnectionString)) {
    az keyvault secret set --vault-name $outputs.keyVaultName.value --name openjibo-state-connection-string --value $resolvedStateConnectionString | Out-Null
}

if (-not [string]::IsNullOrWhiteSpace($resolvedPersonalMemoryConnectionString)) {
    az keyvault secret set --vault-name $outputs.keyVaultName.value --name openjibo-personal-memory-connection-string --value $resolvedPersonalMemoryConnectionString | Out-Null
}

if (-not [string]::IsNullOrWhiteSpace($storageConnectionString)) {
    az keyvault secret set --vault-name $outputs.keyVaultName.value --name openjibo-media-connection-string --value $storageConnectionString | Out-Null
}

if (-not [string]::IsNullOrWhiteSpace($OpenWeatherApiKey)) {
    az keyvault secret set --vault-name $outputs.keyVaultName.value --name openjibo-openweather-api-key --value $OpenWeatherApiKey | Out-Null
}

if (-not [string]::IsNullOrWhiteSpace($NewsApiKey)) {
    az keyvault secret set --vault-name $outputs.keyVaultName.value --name openjibo-newsapi-key --value $NewsApiKey | Out-Null
}

$deploymentJson
