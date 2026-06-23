#!/usr/bin/env bash
set -euo pipefail

resource_group_name=""
template_path="infra/azure/foundation/openjibo-managed-foundation.bicep"
state_connection_string=""
personal_memory_connection_string=""
open_weather_api_key=""
news_api_key=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --resource-group-name)
      resource_group_name="${2:-}"
      shift 2
      ;;
    --template-path)
      template_path="${2:-infra/azure/foundation/openjibo-managed-foundation.bicep}"
      shift 2
      ;;
    --state-connection-string)
      state_connection_string="${2:-}"
      shift 2
      ;;
    --personal-memory-connection-string)
      personal_memory_connection_string="${2:-}"
      shift 2
      ;;
    --open-weather-api-key)
      open_weather_api_key="${2:-}"
      shift 2
      ;;
    --news-api-key)
      news_api_key="${2:-}"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ -z "$resource_group_name" ]]; then
  echo "--resource-group-name is required" >&2
  exit 2
fi

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

if [[ ! -f "$resolved_template_path" ]]; then
  echo "Could not find Bicep template at $resolved_template_path" >&2
  exit 1
fi

deployment_name="openjibo-foundation-$(date -u +%s)"

echo "Deploying Open Jibo managed foundation to resource group '${resource_group_name}'"
deployment_json="$(az deployment group create \
  --resource-group "$resource_group_name" \
  --name "$deployment_name" \
  --template-file "$resolved_template_path" \
  --output json)"

python3 - "$deployment_json" "$state_connection_string" "$personal_memory_connection_string" "$open_weather_api_key" "$news_api_key" <<'PY'
import json
import subprocess
import sys

deployment_json = json.loads(sys.argv[1])
state_connection_string = sys.argv[2]
personal_memory_connection_string = sys.argv[3]
open_weather_api_key = sys.argv[4]
news_api_key = sys.argv[5]
outputs = deployment_json["properties"]["outputs"]
key_vault_name = outputs["keyVaultName"]["value"]

def set_secret(name: str, value: str) -> None:
    if value.strip():
        subprocess.run([
            "az", "keyvault", "secret", "set",
            "--vault-name", key_vault_name,
            "--name", name,
            "--value", value,
        ], check=True, stdout=subprocess.DEVNULL)

storage_connection_string = outputs["storageConnectionString"]["value"]
set_secret("openjibo-state-connection-string", state_connection_string or storage_connection_string)
set_secret("openjibo-personal-memory-connection-string", personal_memory_connection_string or storage_connection_string)
set_secret("openjibo-media-connection-string", storage_connection_string)
set_secret("openjibo-openweather-api-key", open_weather_api_key)
set_secret("openjibo-newsapi-key", news_api_key)

print(json.dumps(deployment_json, indent=2))
PY
