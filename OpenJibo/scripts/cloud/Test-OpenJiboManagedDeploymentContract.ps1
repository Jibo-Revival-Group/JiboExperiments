param(
    [string]$FoundationTemplatePath = "infra/azure/foundation/openjibo-managed-foundation.bicep",
    [string]$ManagedTemplatePath = "infra/azure/container-apps/openjibo-managed.bicep",
    [string]$WorkflowPath = "../.github/workflows/openjibo-cloud-managed-deploy.yml",
    [string]$FoundationScriptPath = "scripts/cloud/Deploy-OpenJiboManagedFoundation.ps1",
    [string]$ManagedScriptPath = "scripts/cloud/Deploy-OpenJiboManaged.ps1",
    [string]$LinuxFoundationScriptPath = "scripts/cloud/deploy-openjibo-managed-foundation.sh",
    [string]$LinuxPublishScriptPath = "scripts/cloud/publish-openjibo-managed.sh",
    [string]$LinuxManagedScriptPath = "scripts/cloud/deploy-openjibo-managed.sh",
    [string]$SmokeScriptPath = "scripts/cloud/Invoke-CloudSmoke.ps1",
    [string]$LinuxSmokeScriptPath = "scripts/cloud/invoke-cloud-smoke.sh"
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

function Get-RepoFileText {
    param([string]$RelativePath)

    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $RelativePath))
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Missing required file: $fullPath"
    }

    return Get-Content -LiteralPath $fullPath -Raw
}

function Assert-ContainsMarker {
    param(
        [string]$Text,
        [string]$Marker,
        [string]$FailurePrefix
    )

    if ($Text -notmatch [regex]::Escape($Marker)) {
        throw "$FailurePrefix`: $Marker"
    }
}

$foundationText = Get-RepoFileText -RelativePath $FoundationTemplatePath
$managedText = Get-RepoFileText -RelativePath $ManagedTemplatePath
$workflowText = Get-RepoFileText -RelativePath $WorkflowPath
$foundationScriptText = Get-RepoFileText -RelativePath $FoundationScriptPath
$managedScriptText = Get-RepoFileText -RelativePath $ManagedScriptPath
$linuxFoundationScriptText = Get-RepoFileText -RelativePath $LinuxFoundationScriptPath
$linuxPublishScriptText = Get-RepoFileText -RelativePath $LinuxPublishScriptPath
$linuxManagedScriptText = Get-RepoFileText -RelativePath $LinuxManagedScriptPath
$smokeScriptText = Get-RepoFileText -RelativePath $SmokeScriptPath
$linuxSmokeScriptText = Get-RepoFileText -RelativePath $LinuxSmokeScriptPath

$requiredFoundationMarkers = @(
    "output keyVaultName string",
    "output registryName string",
    "output storageAccountName string",
    "resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01'",
    "param storageAccountName string = ''",
    "var resolvedStorageAccountName",
    "resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01'",
    "publicNetworkAccess: 'Enabled'",
    "accessPolicies: []",
    "enableRbacAuthorization: false",
    "param seedPrincipalObjectId string = ''",
    "resource keyVaultSecretSeedAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01'",
    "resource postgresServer 'Microsoft.DBforPostgreSQL/flexibleServers@2023-06-01-preview'",
    "resource postgresStateDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-06-01-preview'",
    "resource postgresPersonalMemoryDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-06-01-preview'",
    "resource postgresAllowAzureServicesFirewallRule 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2023-06-01-preview'",
    "param postgresDeploymentRunnerFirewallIpAddress string = ''",
    "output postgresFullyQualifiedDomainName string",
    "output postgresStateDatabaseName string",
    "output postgresPersonalMemoryDatabaseName string"
)

$requiredManagedMarkers = @(
    "param registryLoginServer string",
    "param keyVaultName string",
    "param apiHostname string = 'api.openjibo.com'",
    "OpenJibo__CanonicalApiHostname",
    "OpenJibo__CanonicalApiBaseUrl",
    "output canonicalApiHostname string",
    "output containerAppName string",
    "output managedEnvironmentName string",
    "secretRef: 'media-connection-string'",
    "containerapp env show",
    "firewall-rule create",
    "OPENJIBO_STATE_STORAGE_CONNECTION_STRING",
    "OPENJIBO_PERSONAL_MEMORY_STORAGE_CONNECTION_STRING",
    "OPENJIBO_MEDIA_STORAGE_CONNECTION_STRING",
    "keyVaultSecretBaseUrl",
    "environment().suffixes.keyvaultDns",
    "var logAnalyticsWorkspaceKey",
    "value: 'PostgreSql'",
    "value: 'AzureBlob'",
    "keyVaultContainerAppSecretAccessPolicy"
)

$requiredWorkflowMarkers = @(
    "shell: bash",
    "working-directory: OpenJibo",
    "deploy-openjibo-managed-foundation.sh",
    "deploy-openjibo-managed.sh",
    "publish-openjibo-managed.sh",
    "steps.foundation.outputs.registryName",
    "steps.foundation.outputs.keyVaultName",
    "inputs.location",
    "api_hostname",
    "api.openjibo.com",
    "--api-hostname",
    "--run-migration",
    "--run-smoke"
)

foreach ($marker in $requiredFoundationMarkers) {
    Assert-ContainsMarker -Text $foundationText -Marker $marker -FailurePrefix "Foundation template is missing expected marker"
}

foreach ($marker in $requiredManagedMarkers) {
    Assert-ContainsMarker -Text $managedText -Marker $marker -FailurePrefix "Managed template is missing expected marker"
}

foreach ($marker in $requiredWorkflowMarkers) {
    Assert-ContainsMarker -Text $workflowText -Marker $marker -FailurePrefix "Workflow is missing expected marker"
}

foreach ($marker in @("openjibo-media-connection-string", "openjibo-postgres-admin-password", "postgresFullyQualifiedDomainName", "Invoke-OpenJiboAzWithRetry", "seedPrincipalObjectId")) {
    Assert-ContainsMarker -Text $foundationScriptText -Marker $marker -FailurePrefix "Foundation script is missing expected marker"
}

foreach ($marker in @("RegistryName", "ApiHostname", "containerapp hostname add", "containerapp hostname bind", "SkipHostnameBinding")) {
    Assert-ContainsMarker -Text $managedScriptText -Marker $marker -FailurePrefix "Managed deploy script is missing expected marker"
}

foreach ($marker in @("managedEnvironmentName", "--environment", "--validation-method CNAME")) {
    Assert-ContainsMarker -Text $managedScriptText -Marker $marker -FailurePrefix "Managed deploy script is missing hostname binding environment marker"
}

foreach ($marker in @("seedPrincipalObjectId", "openjibo-media-connection-string", "openjibo-postgres-admin-password", "postgresFullyQualifiedDomainName", "run_command_with_retry")) {
    Assert-ContainsMarker -Text $linuxFoundationScriptText -Marker $marker -FailurePrefix "Linux foundation script is missing expected marker"
}

Assert-ContainsMarker -Text $linuxFoundationScriptText -Marker '"az", "storage", "account", "show-connection-string"' -FailurePrefix "Linux foundation script does not resolve the storage connection string outside Bicep outputs"
Assert-ContainsMarker -Text $linuxPublishScriptText -Marker "az acr build" -FailurePrefix "Linux publish script is missing the ACR build path"

foreach ($marker in @("--run-smoke", "--run-migration", "--api-hostname", "az containerapp hostname add", "az containerapp hostname bind", "--skip-hostname-binding")) {
    Assert-ContainsMarker -Text $linuxManagedScriptText -Marker $marker -FailurePrefix "Linux managed deploy script is missing expected marker"
}

foreach ($marker in @("managedEnvironmentName", "--environment", "--validation-method CNAME")) {
    Assert-ContainsMarker -Text $linuxManagedScriptText -Marker $marker -FailurePrefix "Linux managed deploy script is missing hostname binding environment marker"
}

if ($smokeScriptText -match [regex]::Escape('Host = "api.jibo.com"')) {
    throw "Managed smoke script still hardcodes the api.jibo.com host header."
}

foreach ($marker in @("Invoke-JsonRequestWithRetry")) {
    Assert-ContainsMarker -Text $smokeScriptText -Marker $marker -FailurePrefix "Managed smoke script is missing retry marker"
}

if ($linuxSmokeScriptText -match [regex]::Escape('"Host": "api.jibo.com"')) {
    throw "Linux smoke script still hardcodes the api.jibo.com host header."
}

Assert-ContainsMarker -Text $linuxManagedScriptText -Marker "--location" -FailurePrefix "Linux managed deploy script is missing the regional override path"
Assert-ContainsMarker -Text $managedScriptText -Marker "Location" -FailurePrefix "Managed deploy script is missing the regional override path"

$forbiddenMarkers = @(
    "OPENJIBO_MEDIA_CONNECTION_STRING",
    "OPENJIBO_STATE_CONNECTION_STRING",
    "OPENJIBO_PERSONAL_MEMORY_CONNECTION_STRING",
    "openjiboacr",
    "openjibokv",
    "-MediaConnectionString",
    "output storageConnectionString",
    "listKeys(storageAccount",
    "keyvault set-policy"
)

foreach ($marker in $forbiddenMarkers) {
    if ($workflowText -match [regex]::Escape($marker)) {
        throw "Workflow still references forbidden marker: $marker"
    }
    if ($foundationScriptText -match [regex]::Escape($marker)) {
        throw "Foundation script still references forbidden marker: $marker"
    }
}

if (Get-Command az -ErrorAction SilentlyContinue) {
    try {
        $null = & az bicep version 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Azure CLI Bicep support is available."
        }
    } catch {
        Write-Host "Azure CLI is available, but Bicep support is not installed."
    }
}

Write-Host "Managed deployment contract checks passed."
