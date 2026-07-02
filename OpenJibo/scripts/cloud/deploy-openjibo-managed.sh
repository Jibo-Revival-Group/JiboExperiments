#!/usr/bin/env bash
set -euo pipefail

resource_group_name=""
key_vault_name=""
registry_name=""
image_tag="managed"
location=""
api_hostname="api.openjibo.com"
enable_azure_speech=true
azure_speech_region=""
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
    --enable-azure-speech)
      enable_azure_speech=true
      shift
      ;;
    --disable-azure-speech)
      enable_azure_speech=false
      shift
      ;;
    --azure-speech-region)
      azure_speech_region="${2:-eastus}"
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

parse_postgres_server_name() {
  python3 - "$1" <<'PY'
import sys

connection_string = sys.argv[1]
for segment in connection_string.split(";"):
    key, _, value = segment.partition("=")
    if key.strip().lower() == "host" and value.strip():
        print(value.strip().split(".", 1)[0])
        raise SystemExit(0)

raise SystemExit("Could not determine the PostgreSQL server name from the connection string.")
PY
}

ensure_postgres_firewall_rule() {
  local postgres_server_name="$1"
  local rule_name="$2"
  local ip_address="$3"
  local help_text=""
  local server_flag=""
  local rule_flag=""

  help_text="$(az postgres flexible-server firewall-rule create -h 2>&1 || true)"
  if grep -q -- '--server-name' <<<"$help_text"; then
    server_flag="--server-name"
  else
    server_flag="--name"
  fi

  if grep -q -- '--rule-name' <<<"$help_text"; then
    rule_flag="--rule-name"
  else
    rule_flag="--name"
  fi

  if az postgres flexible-server firewall-rule show \
    --resource-group "$resource_group_name" \
    "$server_flag" "$postgres_server_name" \
    "$rule_flag" "$rule_name" \
    --output none >/dev/null 2>&1; then
    az postgres flexible-server firewall-rule update \
      --resource-group "$resource_group_name" \
      "$server_flag" "$postgres_server_name" \
      "$rule_flag" "$rule_name" \
      --start-ip-address "$ip_address" \
      --end-ip-address "$ip_address" \
      --output none
  else
    az postgres flexible-server firewall-rule create \
      --resource-group "$resource_group_name" \
      "$server_flag" "$postgres_server_name" \
      "$rule_flag" "$rule_name" \
      --start-ip-address "$ip_address" \
      --end-ip-address "$ip_address" \
      --output none
  fi
}

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

if [[ "$enable_azure_speech" == true ]]; then
  deployment_args+=(--parameters "enableAzureSpeech=true")
  azure_speech_subscription_key="$(az keyvault secret show --vault-name "$key_vault_name" --name azure-speech-subscription-key --query value -o tsv)"
  if [[ -z "$azure_speech_subscription_key" ]]; then
    echo "Could not read azure-speech-subscription-key from Key Vault '$key_vault_name'." >&2
    exit 1
  fi
  deployment_args+=(--parameters "azureSpeechSubscriptionKey=${azure_speech_subscription_key}")
  if [[ -n "$azure_speech_region" ]]; then
    deployment_args+=(--parameters "azureSpeechRegion=${azure_speech_region}")
  fi
fi

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

  state_connection_string="$(az keyvault secret show --vault-name "$key_vault_name" --name openjibo-state-connection-string --query value -o tsv)"
  postgres_server_name="$(parse_postgres_server_name "$state_connection_string")"
  environment_json="$(
    az containerapp env show \
    --resource-group "$resource_group_name" \
    --name "$managed_environment_name" \
    --output json \
    --only-show-errors 2>/dev/null || true
  )"

  while IFS= read -r outbound_ip; do
    if [[ -z "$outbound_ip" ]]; then
      continue
    fi

    rule_name="AllowContainerApps-${outbound_ip//./-}"
    ensure_postgres_firewall_rule "$postgres_server_name" "$rule_name" "$outbound_ip"
  done < <(
    python3 - "$environment_json" <<'PY'
import json
import sys
import re

text = sys.argv[1].strip()
if not text:
    raise SystemExit(0)

document = json.loads(text)
ips = []
ip_pattern = re.compile(r"\b(?:\d{1,3}\.){3}\d{1,3}\b")

def collect(value):
    if isinstance(value, str):
        if ip_pattern.fullmatch(value.strip()):
            ips.append(value)
        return
    if isinstance(value, list):
        for item in value:
            collect(item)
        return
    if isinstance(value, dict):
        for key, child in value.items():
            if key in {"outboundIpAddresses", "staticIp", "staticIpAddress"}:
                collect(child)
            else:
                collect(child)

collect(document)

for outbound_ip in dict.fromkeys(ips):
    print(outbound_ip)
PY
  )

  sleep 30
fi

if [[ "$run_migration" == true ]]; then
  state_connection_string="$(az keyvault secret show --vault-name "$key_vault_name" --name openjibo-state-connection-string --query value -o tsv)"
  personal_memory_connection_string="$(az keyvault secret show --vault-name "$key_vault_name" --name openjibo-personal-memory-connection-string --query value -o tsv)"
  bash "${script_dir}/invoke-openjibo-migration.sh" \
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
