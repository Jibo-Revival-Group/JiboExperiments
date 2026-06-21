#!/usr/bin/env bash
set -euo pipefail

source_root=""
overlay_root=""
target_mode="open-jibo"
output_directory=""
apply=false
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

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
scaffold_script="$script_dir/scaffold-openjibo-harness.sh"
invoke_script="$script_dir/invoke-openjibo-conversion.sh"

if [[ -z "$output_directory" ]]; then
  output_directory="$(mktemp -d -t openjibo-harness.XXXXXX)"
elif [[ "$output_directory" != /* ]]; then
  output_directory="$(pwd)/$output_directory"
fi

scaffold_output="$output_directory/harness-scaffold.json"
invoke_output_directory="$output_directory/invoke"

scaffold_args=(--source-root "$source_root" --overlay-root "$overlay_root" --output-path "$scaffold_output")
if [[ "$clean" == true ]]; then
  scaffold_args+=(--clean)
fi

"$scaffold_script" "${scaffold_args[@]}" >/dev/null

invoke_args=(--robot-root "$overlay_root" --target-mode "$target_mode" --output-directory "$invoke_output_directory")
if [[ "$apply" == true ]]; then
  invoke_args+=(--apply)
fi
if [[ "$strict" == true ]]; then
  invoke_args+=(--strict)
fi

"$invoke_script" "${invoke_args[@]}" >/dev/null

node - "$source_root" "$overlay_root" "$target_mode" "$output_directory" "$scaffold_output" "$invoke_output_directory" <<'NODE'
const fs = require("fs");
const path = require("path");

const summary = {
  SourceRoot: path.resolve(process.argv[2]),
  OverlayRoot: path.resolve(process.argv[3]),
  TargetMode: process.argv[4],
  OutputDirectory: path.resolve(process.argv[5]),
  ScaffoldPath: path.resolve(process.argv[6]),
  InvokeOutputDirectory: path.resolve(process.argv[7]),
};

const json = JSON.stringify(summary, null, 2);
fs.writeFileSync(path.resolve(process.argv[5], "harness-summary.json"), json);
console.log(json);
NODE
