#!/usr/bin/env bash
set -euo pipefail

source_root=""
overlay_root=""
target_mode="open-jibo"
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

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
roundtrip_script="$script_dir/roundtrip-openjibo-harness.sh"
validator_script="$script_dir/validate-openjibo-harness-roundtrip.sh"

if [[ -z "$output_directory" ]]; then
  output_directory="$(mktemp -d -t openjibo-demo.XXXXXX)"
elif [[ "$output_directory" != /* ]]; then
  output_directory="$(pwd)/$output_directory"
fi

roundtrip_args=(--source-root "$source_root" --overlay-root "$overlay_root" --target-mode "$target_mode" --output-directory "$output_directory")
if [[ "$strict" == true ]]; then
  roundtrip_args+=(--strict)
fi
if [[ "$clean" == true ]]; then
  roundtrip_args+=(--clean)
fi

"$roundtrip_script" "${roundtrip_args[@]}" >/dev/null
"$validator_script" --output-directory "$output_directory" >/dev/null

node - "$source_root" "$overlay_root" "$target_mode" "$output_directory" <<'NODE'
const fs = require("fs");
const path = require("path");

const report = {
  SourceRoot: path.resolve(process.argv[2]),
  OverlayRoot: path.resolve(process.argv[3]),
  TargetMode: process.argv[4],
  OutputDirectory: path.resolve(process.argv[5]),
  RoundTripPath: path.resolve(process.argv[5], "harness-roundtrip.json"),
  Validated: true,
};

fs.writeFileSync(path.resolve(process.argv[5], "harness-demo.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
NODE
