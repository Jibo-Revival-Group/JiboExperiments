#!/usr/bin/env bash
set -euo pipefail

source_root=""
overlay_root=""
target_mode="open-jibo"
api_hostname="api.openjibo.com"
hub_hostname=""
output_directory=""
strict=false
clean=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --source-root)
      source_root="${2:-}"
      shift 2
      ;;
    --overlay-root)
      overlay_root="${2:-}"
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
    --strict)
      strict=true
      shift
      ;;
    --clean)
      clean=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ -z "$source_root" || -z "$overlay_root" ]]; then
  echo "--source-root and --overlay-root are required" >&2
  exit 2
fi

if [[ -z "$hub_hostname" && ( "$target_mode" == "open-jibo" || "$target_mode" == "open-jibo-ai" ) ]]; then
  hub_hostname="neohub.openjibo.com"
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
scaffold_script="$script_dir/scaffold-openjibo-harness.sh"
run_script="$script_dir/run-openjibo-harness.sh"
rollback_script="$script_dir/rollback-openjibo-conversion.sh"

if [[ -z "$output_directory" ]]; then
  output_directory="$(mktemp -d -t openjibo-roundtrip.XXXXXX)"
elif [[ "$output_directory" != /* ]]; then
  output_directory="$(pwd)/$output_directory"
fi

scaffold_output="$output_directory/scaffold.json"
run_output_directory="$output_directory/run"
rollback_output="$output_directory/rollback.json"
apply_path="$run_output_directory/invoke/conversion-apply.json"

scaffold_args=(--source-root "$source_root" --overlay-root "$overlay_root" --output-path "$scaffold_output")
run_args=(--source-root "$source_root" --overlay-root "$overlay_root" --target-mode "$target_mode" --api-hostname "$api_hostname" --output-directory "$run_output_directory" --apply)
if [[ -n "$hub_hostname" ]]; then
  run_args+=(--hub-hostname "$hub_hostname")
fi
rollback_args=(--robot-root "$overlay_root" --apply-path "$apply_path" --output-path "$rollback_output")

if [[ "$clean" == true ]]; then
  scaffold_args+=(--clean)
  run_args+=(--clean)
fi
if [[ "$strict" == true ]]; then
  run_args+=(--strict)
  rollback_args+=(--strict)
fi

bash "$scaffold_script" "${scaffold_args[@]}" >/dev/null
bash "$run_script" "${run_args[@]}" >/dev/null
bash "$rollback_script" "${rollback_args[@]}" >/dev/null

node - "$source_root" "$overlay_root" "$target_mode" "$api_hostname" "${hub_hostname:-$api_hostname}" "$output_directory" "$scaffold_output" "$run_output_directory" "$apply_path" "$rollback_output" <<'NODE'
const fs = require("fs");
const path = require("path");

const summary = {
  SourceRoot: path.resolve(process.argv[2]),
  OverlayRoot: path.resolve(process.argv[3]),
  TargetMode: process.argv[4],
  ApiHostname: process.argv[5],
  HubHostname: process.argv[6],
  OutputDirectory: path.resolve(process.argv[7]),
  ScaffoldPath: path.resolve(process.argv[8]),
  RunOutputDirectory: path.resolve(process.argv[9]),
  ApplyPath: path.resolve(process.argv[10]),
  RollbackPath: path.resolve(process.argv[11]),
};

const json = JSON.stringify(summary, null, 2);
fs.writeFileSync(path.resolve(process.argv[7], "harness-roundtrip.json"), json);
console.log(json);
NODE
