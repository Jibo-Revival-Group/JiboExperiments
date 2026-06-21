#!/usr/bin/env bash
set -euo pipefail

robot_root=""
target_mode="open-jibo"
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
plan_args=(--robot-root "$robot_root" --target-mode "$target_mode" --output-path "$plan_path")

if [[ "$strict" == true ]]; then
  audit_args+=(--strict)
  plan_args+=(--strict)
fi

"$audit_script" "${audit_args[@]}" >/dev/null
"$plan_script" "${plan_args[@]}" >/dev/null

applied=false
if [[ "$apply" == true ]]; then
  apply_args=(--robot-root "$robot_root" --plan-path "$plan_path" --target-mode "$target_mode" --output-path "$apply_path")
  if [[ "$strict" == true ]]; then
    apply_args+=(--strict)
  fi
  "$apply_script" "${apply_args[@]}" >/dev/null
  applied=true
fi

node - "$robot_root" "$target_mode" "$output_directory" "$audit_path" "$plan_path" "$applied" "$apply_path" <<'NODE'
const path = require("path");

const summary = {
  RobotRoot: path.resolve(process.argv[2]),
  TargetMode: process.argv[3],
  OutputDirectory: path.resolve(process.argv[4]),
  AuditPath: path.resolve(process.argv[5]),
  PlanPath: path.resolve(process.argv[6]),
  Applied: String(process.argv[7]).toLowerCase() === "true",
};

if (summary.Applied) {
  summary.ApplyPath = path.resolve(process.argv[8]);
}

console.log(JSON.stringify(summary, null, 2));
NODE
