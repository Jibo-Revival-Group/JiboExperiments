#!/usr/bin/env bash
set -euo pipefail

foundation_template_path="infra/azure/foundation/openjibo-managed-foundation.bicep"
managed_template_path="infra/azure/container-apps/openjibo-managed.bicep"
workflow_path="../.github/workflows/openjibo-cloud-managed-deploy.yml"
foundation_script_path="scripts/cloud/Deploy-OpenJiboManagedFoundation.ps1"
managed_script_path="scripts/cloud/Deploy-OpenJiboManaged.ps1"
linux_foundation_script_path="scripts/cloud/deploy-openjibo-managed-foundation.sh"
linux_publish_script_path="scripts/cloud/publish-openjibo-managed.sh"
linux_managed_script_path="scripts/cloud/deploy-openjibo-managed.sh"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/../.." && pwd)"

get_repo_file_text() {
  local relative_path="$1"
  local full_path="${repo_root}/${relative_path}"

  if [[ ! -f "$full_path" ]]; then
    echo "Missing required file: $full_path" >&2
    exit 1
  fi

  cat "$full_path"
}

foundation_text="$(get_repo_file_text "$foundation_template_path")"
managed_text="$(get_repo_file_text "$managed_template_path")"
workflow_text="$(get_repo_file_text "$workflow_path")"
foundation_script_text="$(get_repo_file_text "$foundation_script_path")"
managed_script_text="$(get_repo_file_text "$managed_script_path")"
linux_foundation_script_text="$(get_repo_file_text "$linux_foundation_script_path")"
linux_publish_script_text="$(get_repo_file_text "$linux_publish_script_path")"
linux_managed_script_text="$(get_repo_file_text "$linux_managed_script_path")"

required_foundation_markers=(
  "output keyVaultName string"
  "output registryName string"
  "output storageAccountName string"
  "resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01'"
  "param storageAccountName string = ''"
  "var resolvedStorageAccountName"
  "resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01'"
  "publicNetworkAccess: 'Enabled'"
  "param seedPrincipalObjectId string = ''"
  "resource keyVaultSecretSeedAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01'"
)

required_managed_markers=(
  "param registryLoginServer string"
  "param keyVaultName string"
  "secretRef: 'media-connection-string'"
  "keyVaultUrl: 'https://"
  "var logAnalyticsWorkspaceKey"
  "value: 'AzureBlob'"
  "keyVaultContainerAppSecretAccessPolicy"
)

required_workflow_markers=(
  "shell: bash"
  "working-directory: OpenJibo"
  "deploy-openjibo-managed-foundation.sh"
  "deploy-openjibo-managed.sh"
  "publish-openjibo-managed.sh"
  "steps.foundation.outputs.registryName"
  "steps.foundation.outputs.keyVaultName"
)

for marker in "${required_foundation_markers[@]}"; do
  if [[ "$foundation_text" != *"$marker"* ]]; then
    echo "Foundation template is missing expected marker: $marker" >&2
    exit 1
  fi
done

for marker in "${required_managed_markers[@]}"; do
  if [[ "$managed_text" != *"$marker"* ]]; then
    echo "Managed template is missing expected marker: $marker" >&2
    exit 1
  fi
done

for marker in "${required_workflow_markers[@]}"; do
  if [[ "$workflow_text" != *"$marker"* ]]; then
    echo "Workflow is missing expected marker: $marker" >&2
    exit 1
  fi
done

if [[ "$foundation_script_text" != *"openjibo-media-connection-string"* ]]; then
  echo "Foundation script does not seed the media connection string secret." >&2
  exit 1
fi

if [[ "$foundation_script_text" != *"seedPrincipalObjectId"* ]]; then
  echo "Foundation script does not pass the secret seed access policy principal to the deployment." >&2
  exit 1
fi

if [[ "$managed_script_text" != *"RegistryName"* ]]; then
  echo "Managed deploy script is missing the registry parameter path." >&2
  exit 1
fi

if [[ "$linux_foundation_script_text" != *"seedPrincipalObjectId"* ]]; then
  echo "Linux foundation script does not pass the secret seed access policy principal to the deployment." >&2
  exit 1
fi

if [[ "$linux_foundation_script_text" != *"openjibo-media-connection-string"* ]]; then
  echo "Linux foundation script does not seed the media connection string secret." >&2
  exit 1
fi

storage_connection_marker='"az", "storage", "account", "show-connection-string"'
if [[ "$linux_foundation_script_text" != *"$storage_connection_marker"* ]]; then
  echo "Linux foundation script does not resolve the storage connection string outside Bicep outputs." >&2
  exit 1
fi

if [[ "$linux_publish_script_text" != *"az acr build"* ]]; then
  echo "Linux publish script is missing the ACR build path." >&2
  exit 1
fi

if [[ "$linux_managed_script_text" != *"--run-smoke"* ]]; then
  echo "Linux managed deploy script is missing the managed smoke path." >&2
  exit 1
fi

for forbidden_marker in "OPENJIBO_MEDIA_CONNECTION_STRING" "OPENJIBO_STATE_CONNECTION_STRING" "OPENJIBO_PERSONAL_MEMORY_CONNECTION_STRING" "openjiboacr" "openjibokv" "-MediaConnectionString" "output storageConnectionString" "listKeys(storageAccount" "keyvault set-policy"; do
  if [[ "$workflow_text" == *"$forbidden_marker"* ]]; then
    echo "Workflow still references forbidden marker: $forbidden_marker" >&2
    exit 1
  fi
  if [[ "$foundation_script_text" == *"$forbidden_marker"* ]]; then
    echo "Foundation script still references forbidden marker: $forbidden_marker" >&2
    exit 1
  fi
done

if command -v az >/dev/null 2>&1; then
  if az bicep version >/dev/null 2>&1; then
    echo "Azure CLI Bicep support is available."
  fi
fi

echo "Managed deployment contract checks passed."
