param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$KeyVaultName,

    [string]$ImageTag = "managed",
    [string]$Location = "",
    [string]$ApiHostname = "api.openjibo.com",
    [Parameter(Mandatory = $true)]
    [string]$RegistryName,
    [string]$TemplatePath = "infra/azure/container-apps/openjibo-managed.bicep",
    [string]$ParametersPath = "infra/azure/container-apps/openjibo-managed.parameters.json",
    [switch]$RunMigration,
    [switch]$RunSmoke,
    [switch]$SkipHostnameBinding
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

function Get-PostgresServerNameFromConnectionString {
    param([string]$ConnectionString)

    foreach ($segment in $ConnectionString.Split(";")) {
        $parts = $segment.Split("=", 2)
        if ($parts.Length -eq 2 -and $parts[0].Trim().Equals("Host", [System.StringComparison]::OrdinalIgnoreCase)) {
            return ($parts[1].Trim().Split(".", 2))[0]
        }
    }

    throw "Could not determine the PostgreSQL server name from the connection string."
}

function Ensure-PostgresFirewallRule {
    param(
        [string]$ServerName,
        [string]$RuleName,
        [string]$IpAddress
    )

    $showArgs = @(
        "postgres", "flexible-server", "firewall-rule", "show",
        "--resource-group", $ResourceGroupName,
        "--name", $ServerName,
        "--rule-name", $RuleName,
        "--output", "none"
    )

    & az @showArgs *> $null
    if ($LASTEXITCODE -eq 0) {
        & az postgres flexible-server firewall-rule update `
            --resource-group $ResourceGroupName `
            --name $ServerName `
            --rule-name $RuleName `
            --start-ip-address $IpAddress `
            --end-ip-address $IpAddress `
            --output none | Out-Null
        return
    }

    & az postgres flexible-server firewall-rule create `
        --resource-group $ResourceGroupName `
        --name $ServerName `
        --rule-name $RuleName `
        --start-ip-address $IpAddress `
        --end-ip-address $IpAddress `
        --output none | Out-Null
}

$arguments = @(
    "deployment", "group", "create",
    "--resource-group", $ResourceGroupName,
    "--name", $deploymentName,
    "--template-file", $resolvedTemplatePath,
    "--parameters", "@$resolvedParametersPath",
    "--parameters", "registryLoginServer=$RegistryLoginServer",
    "--parameters", "keyVaultName=$KeyVaultName",
    "--parameters", "imageTag=$ImageTag",
    "--parameters", "apiHostname=$ApiHostname"
)

if (-not [string]::IsNullOrWhiteSpace($Location)) {
    $arguments += @("--parameters", "location=$Location")
}

$arguments += @("--output", "json")

Write-Host "Deploying Open Jibo managed Container Apps stack to resource group '$ResourceGroupName'"
$deploymentJson = az @arguments | ConvertFrom-Json

if (-not $SkipHostnameBinding -and -not [string]::IsNullOrWhiteSpace($ApiHostname)) {
    $containerAppName = $deploymentJson.properties.outputs.containerAppName.value
    $managedEnvironmentName = $deploymentJson.properties.outputs.managedEnvironmentName.value
    if ([string]::IsNullOrWhiteSpace($containerAppName)) {
        throw "Container app name was not returned from the deployment."
    }
    if ([string]::IsNullOrWhiteSpace($managedEnvironmentName)) {
        throw "Managed environment name was not returned from the deployment."
    }

    Write-Host "Binding '$ApiHostname' to Container App '$containerAppName'. DNS must point directly at the generated Container App hostname before Azure can issue the managed certificate."
    az containerapp hostname add `
        --resource-group $ResourceGroupName `
        --name $containerAppName `
        --hostname $ApiHostname `
        --output none
    az containerapp hostname bind `
        --resource-group $ResourceGroupName `
        --name $containerAppName `
        --hostname $ApiHostname `
        --environment $managedEnvironmentName `
        --validation-method CNAME `
        --output none

    $stateConnectionString = az keyvault secret show --vault-name $KeyVaultName --name openjibo-state-connection-string --query value -o tsv
    $postgresServerName = Get-PostgresServerNameFromConnectionString -ConnectionString $stateConnectionString
    $outboundIpAddresses = az containerapp env show `
        --resource-group $ResourceGroupName `
        --name $managedEnvironmentName `
        --query properties.outboundIpAddresses `
        --output json | ConvertFrom-Json

    if (-not $outboundIpAddresses -or $outboundIpAddresses.Count -eq 0) {
        throw "Container Apps environment did not report any outbound IP addresses."
    }

    foreach ($outboundIpAddress in $outboundIpAddresses) {
        if ([string]::IsNullOrWhiteSpace($outboundIpAddress)) {
            continue
        }

        $ruleName = "AllowContainerApps-{0}" -f $outboundIpAddress.Replace(".", "-")
        Ensure-PostgresFirewallRule -ServerName $postgresServerName -RuleName $ruleName -IpAddress $outboundIpAddress
    }
}

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
    $smokeBaseUrl = if (-not [string]::IsNullOrWhiteSpace($ApiHostname)) {
        "https://$ApiHostname"
    } else {
        $containerAppFqdn = $deploymentJson.properties.outputs.containerAppFqdn.value
        if ([string]::IsNullOrWhiteSpace($containerAppFqdn)) {
            throw "Container app FQDN was not returned from the deployment."
        }

        "https://$containerAppFqdn"
    }

    $smokeScript = Join-Path $repoRoot "scripts/cloud/Invoke-CloudSmoke.ps1"
    & $smokeScript -BaseUrl $smokeBaseUrl
}

$deploymentJson
