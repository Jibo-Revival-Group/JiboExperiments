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

$currentPrincipalId = ""
try {
    $accessToken = az account get-access-token --query accessToken --output tsv
    $payload = $accessToken.Split(".")[1]
    $payload = $payload.PadRight($payload.Length + ((4 - ($payload.Length % 4)) % 4), "=")
    $claimsJson = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload.Replace("-", "+").Replace("_", "/")))
    $currentPrincipalId = (ConvertFrom-Json $claimsJson).oid
}
catch {
    Write-Warning "Could not resolve current Azure principal object id: $_"
}

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

function Set-OpenJiboKeyVaultSecretSeedPolicyWithRetry {
    param(
        [string]$VaultName,
        [string]$PrincipalId
    )

    if ([string]::IsNullOrWhiteSpace($PrincipalId)) {
        return
    }

    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try {
            az keyvault set-policy --name $VaultName --object-id $PrincipalId --secret-permissions get list set delete | Out-Null
            return
        }
        catch {
            if ($attempt -eq 6) {
                throw
            }

            $waitSeconds = $attempt * 10
            Write-Warning "Key Vault access policy is not ready for principal '$PrincipalId' yet; retrying in $waitSeconds seconds."
            Start-Sleep -Seconds $waitSeconds
        }
    }
}

Set-OpenJiboKeyVaultSecretSeedPolicyWithRetry -VaultName $outputs.keyVaultName.value -PrincipalId $currentPrincipalId

$storageConnectionString = az storage account show-connection-string --resource-group $ResourceGroupName --name $outputs.storageAccountName.value --query connectionString --output tsv
$resolvedStateConnectionString = if ([string]::IsNullOrWhiteSpace($StateConnectionString)) { $storageConnectionString } else { $StateConnectionString }
$resolvedPersonalMemoryConnectionString = if ([string]::IsNullOrWhiteSpace($PersonalMemoryConnectionString)) { $storageConnectionString } else { $PersonalMemoryConnectionString }

function Set-OpenJiboKeyVaultSecretWithRetry {
    param(
        [string]$VaultName,
        [string]$Name,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return
    }

    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try {
            az keyvault secret set --vault-name $VaultName --name $Name --value $Value | Out-Null
            return
        }
        catch {
            if ($attempt -eq 6) {
                throw
            }

            $waitSeconds = $attempt * 10
            Write-Warning "Key Vault RBAC is not ready for secret '$Name' yet; retrying in $waitSeconds seconds."
            Start-Sleep -Seconds $waitSeconds
        }
    }
}

Set-OpenJiboKeyVaultSecretWithRetry -VaultName $outputs.keyVaultName.value -Name openjibo-state-connection-string -Value $resolvedStateConnectionString

Set-OpenJiboKeyVaultSecretWithRetry -VaultName $outputs.keyVaultName.value -Name openjibo-personal-memory-connection-string -Value $resolvedPersonalMemoryConnectionString

Set-OpenJiboKeyVaultSecretWithRetry -VaultName $outputs.keyVaultName.value -Name openjibo-media-connection-string -Value $storageConnectionString

Set-OpenJiboKeyVaultSecretWithRetry -VaultName $outputs.keyVaultName.value -Name openjibo-openweather-api-key -Value $OpenWeatherApiKey

Set-OpenJiboKeyVaultSecretWithRetry -VaultName $outputs.keyVaultName.value -Name openjibo-newsapi-key -Value $NewsApiKey

$deploymentJson
