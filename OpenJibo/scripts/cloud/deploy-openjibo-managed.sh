#!/usr/bin/env bash
set -euo pipefail

resource_group_name=""
key_vault_name=""
registry_name=""
image_tag="managed"
template_path="infra/azure/container-apps/openjibo-managed.bicep"
parameters_path="infra/azure/container-apps/openjibo-managed.parameters.json"
run_migration=false
run_smoke=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --resource-group-name)
      resource_group_name="${2:-}"
      shift 2
      ;;
    --key-vault-name)
      key_vault_name="${2:-}"
      shift 2
      ;;
    --registry-name)
      registry_name="${2:-}"
      shift 2
      ;;
    --image-tag)
      image_tag="${2:-managed}"
      shift 2
      ;;
    --template-path)
      template_path="${2:-infra/azure/container-apps/openjibo-managed.bicep}"
      shift 2
      ;;
    --parameters-path)
      parameters_path="${2:-infra/azure/container-apps/openjibo-managed.parameters.json}"
      shift 2
      ;;
    --run-migration)
      run_migration=true
      shift
      ;;
    --run-smoke)
      run_smoke=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

for required_name in resource_group_name key_vault_name registry_name; do
  if [[ -z "${!required_name}" ]]; then
    echo "--${required_name//_/-} is required" >&2
    exit 2
  fi
done

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/../.." && pwd)"

resolve_path() {
  local candidate="$1"
  if [[ "$candidate" = /* ]]; then
    printf '%s\n' "$candidate"
  else
    printf '%s\n' "${repo_root}/${candidate}"
  fi
}

resolved_template_path="$(resolve_path "$template_path")"
resolved_parameters_path="$(resolve_path "$parameters_path")"

if [[ ! -f "$resolved_template_path" ]]; then
  echo "Could not find Bicep template at $resolved_template_path" >&2
  exit 1
fi

if [[ ! -f "$resolved_parameters_path" ]]; then
  echo "Could not find parameter file at $resolved_parameters_path" >&2
  exit 1
fi

registry_login_server="${registry_name}.azurecr.io"
deployment_name="openjibo-managed-$(date -u +%s)"

echo "Deploying Open Jibo managed Container Apps stack to resource group '${resource_group_name}'"
deployment_json="$(az deployment group create \
  --resource-group "$resource_group_name" \
  --name "$deployment_name" \
  --template-file "$resolved_template_path" \
  --parameters "@${resolved_parameters_path}" \
  --parameters "registryLoginServer=${registry_login_server}" \
  --parameters "keyVaultName=${key_vault_name}" \
  --output json)"

if [[ "$run_migration" == true ]]; then
  state_connection_string="$(az keyvault secret show --vault-name "$key_vault_name" --name openjibo-state-connection-string --query value -o tsv)"
  personal_memory_connection_string="$(az keyvault secret show --vault-name "$key_vault_name" --name openjibo-personal-memory-connection-string --query value -o tsv)"
  "${script_dir}/invoke-openjibo-migration.sh" \
    --target all \
    --state-connection "$state_connection_string" \
    --memory-connection "$personal_memory_connection_string"
fi

if [[ "$run_smoke" == true ]]; then
  container_app_fqdn="$(python3 - "$deployment_json" <<'PY'
import json
import sys

deployment_json = json.loads(sys.argv[1])
print(deployment_json["properties"]["outputs"]["containerAppFqdn"]["value"])
PY
)"

  if [[ -z "$container_app_fqdn" ]]; then
    echo "Container app FQDN was not returned from the deployment." >&2
    exit 1
  fi

  BASE_URL="https://${container_app_fqdn}" "${script_dir}/invoke-cloud-smoke.sh"
fi

printf '%s\n' "$deployment_json"
