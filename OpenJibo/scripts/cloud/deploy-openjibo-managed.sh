#!/usr/bin/env bash
set -euo pipefail

resource_group_name=""
key_vault_name=""
registry_name=""
image_tag="managed"
location=""
api_hostname="api.openjibo.com"
template_path="infra/azure/container-apps/openjibo-managed.bicep"
parameters_path="infra/azure/container-apps/openjibo-managed.parameters.json"
run_migration=false
run_smoke=false
skip_hostname_binding=false

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
    --location)
      location="${2:-}"
      shift 2
      ;;
    --api-hostname)
      api_hostname="${2:-api.openjibo.com}"
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
    --skip-hostname-binding)
      skip_hostname_binding=true
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
deployment_args=(
  az deployment group create
  --resource-group "$resource_group_name"
  --name "$deployment_name"
  --template-file "$resolved_template_path"
  --parameters "@${resolved_parameters_path}"
  --parameters "registryLoginServer=${registry_login_server}"
  --parameters "keyVaultName=${key_vault_name}"
  --parameters "imageTag=${image_tag}"
  --parameters "apiHostname=${api_hostname}"
)

if [[ -n "$location" ]]; then
  deployment_args+=(--parameters "location=${location}")
fi

deployment_args+=(--output json)

deployment_json="$("${deployment_args[@]}")"

if [[ "$skip_hostname_binding" != true && -n "$api_hostname" ]]; then
  container_app_name="$(python3 - "$deployment_json" <<'PY'
import json
import sys

deployment_json = json.loads(sys.argv[1])
print(deployment_json["properties"]["outputs"]["containerAppName"]["value"])
PY
)"
  managed_environment_name="$(python3 - "$deployment_json" <<'PY'
import json
import sys

deployment_json = json.loads(sys.argv[1])
print(deployment_json["properties"]["outputs"]["managedEnvironmentName"]["value"])
PY
)"

  echo "Binding '${api_hostname}' to Container App '${container_app_name}'. DNS must point directly at the generated Container App hostname before Azure can issue the managed certificate." >&2
  az containerapp hostname add \
    --resource-group "$resource_group_name" \
    --name "$container_app_name" \
    --hostname "$api_hostname" \
    --output none
  az containerapp hostname bind \
    --resource-group "$resource_group_name" \
    --name "$container_app_name" \
    --hostname "$api_hostname" \
    --environment "$managed_environment_name" \
    --validation-method CNAME \
    --output none
fi

if [[ "$run_migration" == true ]]; then
  state_connection_string="$(az keyvault secret show --vault-name "$key_vault_name" --name openjibo-state-connection-string --query value -o tsv)"
  personal_memory_connection_string="$(az keyvault secret show --vault-name "$key_vault_name" --name openjibo-personal-memory-connection-string --query value -o tsv)"
  "${script_dir}/invoke-openjibo-migration.sh" \
    --target all \
    --state-connection "$state_connection_string" \
    --memory-connection "$personal_memory_connection_string"
fi

if [[ "$run_smoke" == true ]]; then
  smoke_base_url="$(python3 - "$deployment_json" "$api_hostname" <<'PY'
import json
import sys

api_hostname = sys.argv[2].strip()
if api_hostname:
    print(f"https://{api_hostname}")
    raise SystemExit(0)

deployment_json = json.loads(sys.argv[1])
container_app_fqdn = deployment_json["properties"]["outputs"]["containerAppFqdn"]["value"]
print(f"https://{container_app_fqdn}")
PY
)"

  if [[ -z "$smoke_base_url" ]]; then
    echo "Smoke base URL could not be resolved from the canonical API hostname or deployment output." >&2
    exit 1
  fi

  BASE_URL="$smoke_base_url" bash "${script_dir}/invoke-cloud-smoke.sh"
fi

printf '%s\n' "$deployment_json"
