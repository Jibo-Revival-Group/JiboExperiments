param(
    [string]$FoundationTemplatePath = "infra/azure/foundation/openjibo-managed-foundation.bicep",
    [string]$ManagedTemplatePath = "infra/azure/container-apps/openjibo-managed.bicep",
    [string]$WorkflowPath = "../.github/workflows/openjibo-cloud-managed-deploy.yml",
    [string]$FoundationScriptPath = "scripts/cloud/Deploy-OpenJiboManagedFoundation.ps1",
    [string]$ManagedScriptPath = "scripts/cloud/Deploy-OpenJiboManaged.ps1",
    [string]$LinuxFoundationScriptPath = "scripts/cloud/deploy-openjibo-managed-foundation.sh",
    [string]$LinuxPublishScriptPath = "scripts/cloud/publish-openjibo-managed.sh",
    [string]$LinuxManagedScriptPath = "scripts/cloud/deploy-openjibo-managed.sh"
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

$foundationText = Get-RepoFileText -RelativePath $FoundationTemplatePath
$managedText = Get-RepoFileText -RelativePath $ManagedTemplatePath
$workflowText = Get-RepoFileText -RelativePath $WorkflowPath
$foundationScriptText = Get-RepoFileText -RelativePath $FoundationScriptPath
$managedScriptText = Get-RepoFileText -RelativePath $ManagedScriptPath
$linuxFoundationScriptText = Get-RepoFileText -RelativePath $LinuxFoundationScriptPath
$linuxPublishScriptText = Get-RepoFileText -RelativePath $LinuxPublishScriptPath
$linuxManagedScriptText = Get-RepoFileText -RelativePath $LinuxManagedScriptPath

$requiredFoundationMarkers = @(
    "output keyVaultName string",
    "output registryName string",
    "output storageAccountName string",
    "resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01'",
    "param storageAccountName string = ''",
    "var resolvedStorageAccountName",
    "resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01'",
    "publicNetworkAccess: 'Enabled'"
)

$requiredManagedMarkers = @(
    "param registryLoginServer string",
    "param keyVaultName string",
    "secretRef: 'media-connection-string'",
    "keyVaultUrl: 'https://",
    "var logAnalyticsWorkspaceKey",
    "value: 'AzureBlob'"
)

$requiredWorkflowMarkers = @(
    "shell: bash",
    "working-directory: OpenJibo",
    "deploy-openjibo-managed-foundation.sh",
    "deploy-openjibo-managed.sh",
    "publish-openjibo-managed.sh",
    "steps.foundation.outputs.registryName",
    "steps.foundation.outputs.keyVaultName"
)

$forbiddenMarkers = @(
    "OPENJIBO_MEDIA_CONNECTION_STRING",
    "OPENJIBO_STATE_CONNECTION_STRING",
    "OPENJIBO_PERSONAL_MEMORY_CONNECTION_STRING",
    "openjiboacr",
    "openjibokv",
    "-MediaConnectionString",
    "output storageConnectionString",
    "listKeys(storageAccount"
)

foreach ($marker in $requiredFoundationMarkers) {
    if ($foundationText -notmatch [regex]::Escape($marker)) {
        throw "Foundation template is missing expected marker: $marker"
    }
}

foreach ($marker in $requiredManagedMarkers) {
    if ($managedText -notmatch [regex]::Escape($marker)) {
        throw "Managed template is missing expected marker: $marker"
    }
}

foreach ($marker in $requiredWorkflowMarkers) {
    if ($workflowText -notmatch [regex]::Escape($marker)) {
        throw "Workflow is missing expected marker: $marker"
    }
}

if ($foundationScriptText -notmatch [regex]::Escape("openjibo-media-connection-string")) {
    throw "Foundation script does not seed the media connection string secret."
}

if ($foundationScriptText -notmatch [regex]::Escape("keyvault set-policy")) {
    throw "Foundation script does not grant the secret seed access policy after deployment."
}

if ($managedScriptText -notmatch [regex]::Escape("RegistryName")) {
    throw "Managed deploy script is missing the registry parameter path."
}

if ($linuxFoundationScriptText -notmatch [regex]::Escape("openjibo-media-connection-string")) {
    throw "Linux foundation script does not seed the media connection string secret."
}

if ($linuxFoundationScriptText -notmatch [regex]::Escape('"az", "storage", "account", "show-connection-string"')) {
    throw "Linux foundation script does not resolve the storage connection string outside Bicep outputs."
}

if ($linuxPublishScriptText -notmatch [regex]::Escape("az acr build")) {
    throw "Linux publish script is missing the ACR build path."
}

if ($linuxManagedScriptText -notmatch [regex]::Escape("--run-smoke")) {
    throw "Linux managed deploy script is missing the managed smoke path."
}

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
