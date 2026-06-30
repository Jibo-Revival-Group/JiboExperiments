#!/usr/bin/env bash
set -euo pipefail

robot_root=""
target_mode="open-jibo"
api_hostname="api.openjibo.com"
hub_hostname=""
output_directory=""
apply=false
strict=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --robot-root)
      robot_root="${2:-}"
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

if [[ -z "$robot_root" ]]; then
  echo "--robot-root is required" >&2
  exit 2
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
audit_script="$script_dir/audit-openjibo-conversion.sh"
plan_script="$script_dir/plan-openjibo-conversion.sh"
apply_script="$script_dir/apply-openjibo-conversion.sh"

if [[ -z "$output_directory" ]]; then
  output_directory="$(mktemp -d -t openjibo-conversion.XXXXXX)"
elif [[ "$output_directory" != /* ]]; then
  output_directory="$(pwd)/$output_directory"
fi

mkdir -p "$output_directory"

audit_path="$output_directory/conversion-audit.json"
plan_path="$output_directory/conversion-plan.json"
apply_path="$output_directory/conversion-apply.json"

audit_args=(--robot-root "$robot_root" --output-path "$audit_path")
plan_args=(--robot-root "$robot_root" --target-mode "$target_mode" --api-hostname "$api_hostname" --output-path "$plan_path")

if [[ -n "$hub_hostname" ]]; then
  plan_args+=(--hub-hostname "$hub_hostname")
fi

if [[ "$strict" == true ]]; then
  audit_args+=(--strict)
  plan_args+=(--strict)
fi

bash "$audit_script" "${audit_args[@]}" >/dev/null
bash "$plan_script" "${plan_args[@]}" >/dev/null

applied=false
if [[ "$apply" == true ]]; then
  apply_args=(--robot-root "$robot_root" --plan-path "$plan_path" --target-mode "$target_mode" --api-hostname "$api_hostname" --output-path "$apply_path")
  if [[ -n "$hub_hostname" ]]; then
    apply_args+=(--hub-hostname "$hub_hostname")
  fi
  if [[ "$strict" == true ]]; then
    apply_args+=(--strict)
  fi
  bash "$apply_script" "${apply_args[@]}" >/dev/null
  applied=true
fi

node - "$robot_root" "$target_mode" "$api_hostname" "${hub_hostname:-$api_hostname}" "$output_directory" "$audit_path" "$plan_path" "$applied" "$apply_path" <<'NODE'
const path = require("path");

const summary = {
  RobotRoot: path.resolve(process.argv[2]),
  TargetMode: process.argv[3],
  ApiHostname: process.argv[4],
  HubHostname: process.argv[5],
  OutputDirectory: path.resolve(process.argv[6]),
  AuditPath: path.resolve(process.argv[7]),
  PlanPath: path.resolve(process.argv[8]),
  Applied: String(process.argv[9]).toLowerCase() === "true",
};

if (summary.Applied) {
  summary.ApplyPath = path.resolve(process.argv[10]);
}

console.log(JSON.stringify(summary, null, 2));
NODE