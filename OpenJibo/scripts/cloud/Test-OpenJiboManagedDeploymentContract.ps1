param(
    [string]$FoundationTemplatePath = "infra/azure/foundation/openjibo-managed-foundation.bicep",
    [string]$ManagedTemplatePath = "infra/azure/container-apps/openjibo-managed.bicep",
    [string]$WorkflowPath = ".github/workflows/openjibo-cloud-managed-deploy.yml",
    [string]$FoundationScriptPath = "scripts/cloud/Deploy-OpenJiboManagedFoundation.ps1",
    [string]$ManagedScriptPath = "scripts/cloud/Deploy-OpenJiboManaged.ps1"
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

$requiredFoundationMarkers = @(
    "output keyVaultName string",
    "output storageConnectionString string",
    "resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01'"
)

$requiredManagedMarkers = @(
    "param registryLoginServer string",
    "param keyVaultName string",
    "secretRef: 'media-connection-string'",
    "keyVaultUrl: 'https://",
    "var logAnalyticsWorkspaceKey"
)

$requiredWorkflowMarkers = @(
    "Deploy-OpenJiboManagedFoundation.ps1",
    "Deploy-OpenJiboManaged.ps1",
    "Publish-OpenJiboManaged.ps1",
    "OPENJIBO_STATE_CONNECTION_STRING",
    "OPENJIBO_PERSONAL_MEMORY_CONNECTION_STRING"
)

$forbiddenMarkers = @(
    "OPENJIBO_MEDIA_CONNECTION_STRING",
    "-MediaConnectionString"
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

if ($managedScriptText -notmatch [regex]::Escape("RegistryName")) {
    throw "Managed deploy script is missing the registry parameter path."
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
