#!/bin/sh
set -eu

SCRIPT_VERSION="2026-07-17.1"
echo "invoke-openjibo-conversion.sh $SCRIPT_VERSION" >&2

robot_root=""
target_mode="open-jibo"
api_hostname="api.openjibo.com"
hub_hostname=""
output_directory=""
apply=false
strict=false

while [ $# -gt 0 ]; do
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

if [ -z "$robot_root" ]; then
  echo "--robot-root is required" >&2
  exit 2
fi

cat >&2 <<'EOF'
Physical-robot preflight:
  If this target is a real robot, run `jibo-mount --rw` before any audit, plan, or apply step that will write robot partitions.
EOF

if [ -z "$hub_hostname" ] && { [ "$target_mode" = "open-jibo" ] || [ "$target_mode" = "open-jibo-ai" ]; }; then
  hub_hostname="neohub.openjibo.com"
fi

script_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
audit_script="$script_dir/audit-openjibo-conversion.sh"
plan_script="$script_dir/plan-openjibo-conversion.sh"
apply_script="$script_dir/apply-openjibo-conversion.sh"

if [ -z "$output_directory" ]; then
  output_directory="$(mktemp -d "${TMPDIR:-/tmp}/openjibo-conversion.XXXXXX")"
else
  case "$output_directory" in
    /*) : ;;
    *) output_directory="$(pwd)/$output_directory" ;;
  esac
fi

mkdir -p "$output_directory"

audit_path="$output_directory/conversion-audit.json"
plan_path="$output_directory/conversion-plan.json"
apply_path="$output_directory/conversion-apply.json"

if [ "$strict" = true ]; then
  sh "$audit_script" --robot-root "$robot_root" --output-path "$audit_path" --strict >/dev/null
  if [ -n "$hub_hostname" ]; then
    sh "$plan_script" --robot-root "$robot_root" --target-mode "$target_mode" --api-hostname "$api_hostname" --hub-hostname "$hub_hostname" --output-path "$plan_path" --strict >/dev/null
  else
    sh "$plan_script" --robot-root "$robot_root" --target-mode "$target_mode" --api-hostname "$api_hostname" --output-path "$plan_path" --strict >/dev/null
  fi
else
  sh "$audit_script" --robot-root "$robot_root" --output-path "$audit_path" >/dev/null
  if [ -n "$hub_hostname" ]; then
    sh "$plan_script" --robot-root "$robot_root" --target-mode "$target_mode" --api-hostname "$api_hostname" --hub-hostname "$hub_hostname" --output-path "$plan_path" >/dev/null
  else
    sh "$plan_script" --robot-root "$robot_root" --target-mode "$target_mode" --api-hostname "$api_hostname" --output-path "$plan_path" >/dev/null
  fi
fi

applied=false
if [ "$apply" = true ]; then
  if [ -n "$hub_hostname" ]; then
    if [ "$strict" = true ]; then
      sh "$apply_script" --robot-root "$robot_root" --plan-path "$plan_path" --target-mode "$target_mode" --api-hostname "$api_hostname" --hub-hostname "$hub_hostname" --output-path "$apply_path" --strict >/dev/null
    else
      sh "$apply_script" --robot-root "$robot_root" --plan-path "$plan_path" --target-mode "$target_mode" --api-hostname "$api_hostname" --hub-hostname "$hub_hostname" --output-path "$apply_path" >/dev/null
    fi
  else
    if [ "$strict" = true ]; then
      sh "$apply_script" --robot-root "$robot_root" --plan-path "$plan_path" --target-mode "$target_mode" --api-hostname "$api_hostname" --output-path "$apply_path" --strict >/dev/null
    else
      sh "$apply_script" --robot-root "$robot_root" --plan-path "$plan_path" --target-mode "$target_mode" --api-hostname "$api_hostname" --output-path "$apply_path" >/dev/null
    fi
  fi
  applied=true
fi

tmp_js="$(mktemp "${TMPDIR:-/tmp}/invoke-openjibo-conversion.XXXXXX")"
cleanup() {
  rm -f "$tmp_js"
}
trap cleanup EXIT

cat > "$tmp_js" <<'NODE'
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
node "$tmp_js" "$robot_root" "$target_mode" "$api_hostname" "${hub_hostname:-$api_hostname}" "$output_directory" "$audit_path" "$plan_path" "$applied" "$apply_path"
node "$tmp_js" "$robot_root" "$target_mode" "$api_hostname" "${hub_hostname:-$api_hostname}" "$output_directory" "$audit_path" "$plan_path" "$applied" "$apply_path"
