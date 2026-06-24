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

current_principal_id="$(python3 -c 'import base64, json, subprocess, sys
try:
    token = subprocess.check_output(["az", "account", "get-access-token", "--query", "accessToken", "--output", "tsv"], text=True).strip()
    payload = token.split(".")[1]
    payload += "=" * (-len(payload) % 4)
    print(json.loads(base64.urlsafe_b64decode(payload.encode("utf-8"))).get("oid", ""))
except Exception as exc:
    print(f"Warning: could not resolve current Azure principal object id: {exc}", file=sys.stderr)')"

deployment_args=(
  az deployment group create
  --resource-group "$resource_group_name"
  --name "$deployment_name"
  --template-file "$resolved_template_path"
)

if [[ -n "$current_principal_id" ]]; then
  deployment_args+=(--parameters "secretSeedPrincipalId=${current_principal_id}")
fi

deployment_args+=(--output json)

echo "Deploying Open Jibo managed foundation to resource group '${resource_group_name}'" >&2
deployment_json="$("${deployment_args[@]}")"

python3 - "$deployment_json" "$resource_group_name" "$state_connection_string" "$personal_memory_connection_string" "$open_weather_api_key" "$news_api_key" "$current_principal_id" <<'PY'
import json
import subprocess
import sys
import time

deployment_json = json.loads(sys.argv[1])
resource_group_name = sys.argv[2]
state_connection_string = sys.argv[3]
personal_memory_connection_string = sys.argv[4]
open_weather_api_key = sys.argv[5]
news_api_key = sys.argv[6]
outputs = deployment_json["properties"]["outputs"]
key_vault_name = outputs["keyVaultName"]["value"]
storage_account_name = outputs["storageAccountName"]["value"]
current_principal_id = sys.argv[7]

def wait_for_secret_seed_rbac() -> None:
    if not current_principal_id.strip():
        return

    command = [
        "az", "role", "assignment", "list",
        "--assignee", current_principal_id,
        "--scope", f"/subscriptions/{subprocess.check_output(['az', 'account', 'show', '--query', 'id', '--output', 'tsv'], text=True).strip()}/resourceGroups/{resource_group_name}/providers/Microsoft.KeyVault/vaults/{key_vault_name}",
        "--query", "[?roleDefinitionName=='Key Vault Secrets Officer'] | length(@)",
        "--output", "tsv",
    ]
    for attempt in range(1, 7):
        count = subprocess.check_output(command, text=True).strip()
        if count and count != "0":
            return
        wait_seconds = attempt * 10
        print(
            f"Key Vault RBAC role assignment is not visible for principal '{current_principal_id}' yet; "
            f"retrying in {wait_seconds} seconds.",
            file=sys.stderr,
        )
        time.sleep(wait_seconds)

wait_for_secret_seed_rbac()

def set_secret(name: str, value: str) -> None:
    if not value.strip():
        return

    command = [
        "az", "keyvault", "secret", "set",
        "--vault-name", key_vault_name,
        "--name", name,
        "--value", value,
    ]
    for attempt in range(1, 7):
        try:
            subprocess.run(command, check=True, stdout=subprocess.DEVNULL)
            return
        except subprocess.CalledProcessError:
            if attempt == 6:
                raise
            wait_seconds = attempt * 10
            print(
                f"Key Vault RBAC is not ready for secret '{name}' yet; "
                f"retrying in {wait_seconds} seconds.",
                file=sys.stderr,
            )
            time.sleep(wait_seconds)

storage_connection_string = subprocess.check_output([
    "az", "storage", "account", "show-connection-string",
    "--resource-group", resource_group_name,
    "--name", storage_account_name,
    "--query", "connectionString",
    "--output", "tsv",
], text=True).strip()
set_secret("openjibo-state-connection-string", state_connection_string or storage_connection_string)
set_secret("openjibo-personal-memory-connection-string", personal_memory_connection_string or storage_connection_string)
set_secret("openjibo-media-connection-string", storage_connection_string)
set_secret("openjibo-openweather-api-key", open_weather_api_key)
set_secret("openjibo-newsapi-key", news_api_key)

print(json.dumps(deployment_json, indent=2))
PY
