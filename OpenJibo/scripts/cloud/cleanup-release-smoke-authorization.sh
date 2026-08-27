#!/usr/bin/env bash
set -uo pipefail

resource_group=""
app_name=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --resource-group) resource_group="${2:-}"; shift 2 ;;
    --app-name) app_name="${2:-}"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "$resource_group" || -z "$app_name" ]]; then
  echo "--resource-group and --app-name are required." >&2
  exit 2
fi

failures=0
if ! az containerapp update \
  --resource-group "$resource_group" \
  --name "$app_name" \
  --set-env-vars "OpenJibo__ReleaseSmoke__Enabled=false" \
  --remove-env-vars OpenJibo__ReleaseSmoke__Secret OpenJibo__ReleaseSmoke__MaxConcurrentDevices \
  --output none; then
  echo "Failed to deploy the disabled release-smoke configuration." >&2
  failures=$((failures + 1))
fi

app_fqdn="$(az containerapp show \
  --resource-group "$resource_group" \
  --name "$app_name" \
  --query properties.configuration.ingress.fqdn \
  --output tsv 2>/dev/null || true)"
healthy=false
if [[ -n "$app_fqdn" ]]; then
  for _ in $(seq 1 30); do
    if curl --fail --silent --show-error "https://${app_fqdn}/health" >/dev/null; then
      healthy=true
      break
    fi
    sleep 5
  done
fi
if [[ "$healthy" != "true" ]]; then
  echo "Disabled release-smoke revision did not become healthy." >&2
  failures=$((failures + 1))
fi

secret_ref_count="$(az containerapp show \
  --resource-group "$resource_group" \
  --name "$app_name" \
  --query "length(properties.template.containers[0].env[?name=='OpenJibo__ReleaseSmoke__Secret'])" \
  --output tsv 2>/dev/null || echo 1)"
if [[ "$secret_ref_count" != "0" ]]; then
  echo "Release-smoke secret reference is still present; stored secret will not be removed." >&2
  failures=$((failures + 1))
elif [[ "$healthy" != "true" ]]; then
  echo "Stored release-smoke secret will be retained until a disabled revision is healthy." >&2
else
  secret_count="$(az containerapp secret list \
    --resource-group "$resource_group" \
    --name "$app_name" \
    --query "length([?name=='release-smoke-authorization'])" \
    --output tsv 2>/dev/null || echo 1)"
  if [[ "$secret_count" == "1" ]]; then
    if ! az containerapp secret remove \
      --resource-group "$resource_group" \
      --name "$app_name" \
      --secret-names release-smoke-authorization \
      --output none; then
      echo "Failed to remove the stored release-smoke authorization secret." >&2
      failures=$((failures + 1))
    fi
  elif [[ "$secret_count" != "0" ]]; then
    echo "Could not safely determine whether the release-smoke secret exists." >&2
    failures=$((failures + 1))
  fi
fi

exit "$failures"
