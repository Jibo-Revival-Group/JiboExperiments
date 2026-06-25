#!/usr/bin/env bash
set -euo pipefail

demo_root=""
target_mode="open-jibo"
api_hostname="api.openjibo.com"
hub_hostname=""
output_directory=""
apply=false
strict=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --demo-root)
      demo_root="${2:-}"
      shift 2
      ;;
    --target-mode)
      target_mode="${2:-open-jibo}"
      shift 2
      ;;
    --api-hostname)
      api_hostname="${2:-api.openjibo.com}"
      shift 2
      ;;
    --hub-hostname)
      hub_hostname="${2:-}"
      shift 2
      ;;
    --output-directory)
      output_directory="${2:-}"
      shift 2
      ;;
    --apply)
      apply=true
      shift
      ;;
    --strict)
      strict=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ -z "$demo_root" ]]; then
  echo "--demo-root is required" >&2
  exit 2
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
invoke_script="$script_dir/invoke-openjibo-conversion.sh"

if [[ -z "$output_directory" ]]; then
  output_directory="$(mktemp -d -t openjibo-demo-root.XXXXXX)"
elif [[ "$output_directory" != /* ]]; then
  output_directory="$(pwd)/$output_directory"
fi

invoke_args=(--robot-root "$demo_root" --target-mode "$target_mode" --api-hostname "$api_hostname" --output-directory "$output_directory")
if [[ -n "$hub_hostname" ]]; then
  invoke_args+=(--hub-hostname "$hub_hostname")
fi
if [[ "$apply" == true ]]; then
  invoke_args+=(--apply)
fi
if [[ "$strict" == true ]]; then
  invoke_args+=(--strict)
fi

"$invoke_script" "${invoke_args[@]}" >/dev/null

node - "$demo_root" "$target_mode" "$api_hostname" "${hub_hostname:-$api_hostname}" "$output_directory" <<'NODE'
const fs = require("fs");
const path = require("path");

const summary = {
  DemoRoot: path.resolve(process.argv[2]),
  TargetMode: process.argv[3],
  ApiHostname: process.argv[4],
  HubHostname: process.argv[5],
  OutputDirectory: path.resolve(process.argv[6]),
  AuditPath: path.resolve(process.argv[6], "conversion-audit.json"),
  PlanPath: path.resolve(process.argv[6], "conversion-plan.json"),
};

const applyPath = path.resolve(process.argv[6], "conversion-apply.json");
if (fs.existsSync(applyPath)) {
  summary.ApplyPath = applyPath;
}

console.log(JSON.stringify(summary, null, 2));
NODE