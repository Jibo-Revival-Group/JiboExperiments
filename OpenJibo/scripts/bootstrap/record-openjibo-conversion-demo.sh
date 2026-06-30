#!/usr/bin/env bash
set -euo pipefail

source_root=""
overlay_root=""
target_mode="open-jibo"
base_url="${BASE_URL:-${BASEURL:-http://localhost:5000}}"
output_directory=""
strict=false
clean=false
skip_cloud_smoke=false

usage() {
  cat >&2 <<'USAGE'
Usage: record-openjibo-conversion-demo.sh --source-root <mounted-or-extracted-jibo-root> --overlay-root <writable-overlay> [options]

Builds a video-ready evidence bundle for an Open Jibo conversion dry run:
  1. round-trip conversion harness with rollback validation
  2. first-contact / identity-recognition filesystem inspection
  3. cloud smoke that seeds loop/member enrollment and recognition observation
  4. a single JSON manifest with the exact commands, outputs, blockers, and next video steps

Options:
  --target-mode <mode>          open-jibo, open-jibo-ai, open-jibo-self-hosted, or open-jibo-developer (default: open-jibo)
  --base-url <url>              OpenJibo cloud base URL for smoke (default: BASE_URL or http://localhost:5000)
  --output-directory <path>     Evidence output directory (default: mktemp)
  --strict                      Pass strict validation into the conversion harness
  --clean                       Recreate the overlay before running the harness
  --skip-cloud-smoke            Skip cloud HTTP smoke when the cloud is not running yet
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --source-root) source_root="${2:-}"; shift 2 ;;
    --overlay-root) overlay_root="${2:-}"; shift 2 ;;
    --target-mode) target_mode="${2:-open-jibo}"; shift 2 ;;
    --base-url) base_url="${2:-}"; shift 2 ;;
    --output-directory) output_directory="${2:-}"; shift 2 ;;
    --strict) strict=true; shift ;;
    --clean) clean=true; shift ;;
    --skip-cloud-smoke) skip_cloud_smoke=true; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 2 ;;
  esac
done

if [[ -z "$source_root" || -z "$overlay_root" ]]; then
  usage
  exit 2
fi
if [[ -z "$base_url" ]]; then
  echo "--base-url cannot be empty" >&2
  exit 2
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
demo_script="$script_dir/demo-openjibo-harness.sh"
inspect_script="$script_dir/inspect-openjibo-first-contact.sh"
cloud_smoke_script="$repo_root/scripts/cloud/invoke-cloud-smoke.sh"

if [[ -z "$output_directory" ]]; then
  output_directory="$(mktemp -d -t openjibo-conversion-video.XXXXXX)"
elif [[ "$output_directory" != /* ]]; then
  output_directory="$(pwd)/$output_directory"
fi
mkdir -p "$output_directory"

commands_path="$output_directory/demo-commands.txt"
manifest_path="$output_directory/conversion-video-manifest.json"
blockers_path="$output_directory/conversion-video-blockers.json"
harness_stdout="$output_directory/harness-demo.stdout.json"
first_contact_path="$output_directory/first-contact-inspection.json"
cloud_smoke_path="$output_directory/cloud-smoke.json"

harness_args=(--source-root "$source_root" --overlay-root "$overlay_root" --target-mode "$target_mode" --output-directory "$output_directory/harness")
if [[ "$strict" == true ]]; then harness_args+=(--strict); fi
if [[ "$clean" == true ]]; then harness_args+=(--clean); fi

{
  printf '%q ' "$demo_script" "${harness_args[@]}"; printf '\n'
  printf '%q ' "$inspect_script" --robot-root "$overlay_root" --output-path "$first_contact_path"; printf '\n'
  if [[ "$skip_cloud_smoke" == false ]]; then
    printf 'BASE_URL=%q %q\n' "$base_url" "$cloud_smoke_script"
  fi
} > "$commands_path"

"$demo_script" "${harness_args[@]}" | tee "$harness_stdout" >/dev/null
"$inspect_script" --robot-root "$overlay_root" --output-path "$first_contact_path" >/dev/null

cloud_smoke_status="skipped"
if [[ "$skip_cloud_smoke" == false ]]; then
  BASE_URL="$base_url" "$cloud_smoke_script" | tee "$cloud_smoke_path" >/dev/null
  cloud_smoke_status="passed"
fi

python3 - "$source_root" "$overlay_root" "$target_mode" "$base_url" "$output_directory" "$cloud_smoke_status" <<'PY'
import json
import pathlib
import sys
from datetime import datetime, timezone

source_root, overlay_root, target_mode, base_url, output_directory, cloud_smoke_status = sys.argv[1:7]
out = pathlib.Path(output_directory)

def load_json(path):
    p = out / path
    if not p.exists() or p.stat().st_size == 0:
        return None
    return json.loads(p.read_text())

harness = load_json("harness-demo.stdout.json")
first_contact = load_json("first-contact-inspection.json")
cloud_smoke = load_json("cloud-smoke.json") if cloud_smoke_status == "passed" else None

blockers = [
    {
        "area": "physical-device-write",
        "status": "operator-gated",
        "question": "Which real robot image/device variant should be used first for the filmed conversion: newer OOBE, stock 1.9.2, NTT, or MIT-special?",
        "why": "This script proves the disposable overlay and cloud path; writing a physical robot still needs explicit device selection and backup confirmation."
    },
    {
        "area": "awakening-assets",
        "status": "needs-review",
        "question": "Can we use the reported first-contact/body/yawn/audio assets in the public demo, or should the video avoid claiming full OOBE parity?",
        "why": "Filesystem inspection can find candidates, but safe asset reuse and exact replacement points require image-specific review."
    },
    {
        "area": "face-recognition",
        "status": "capture-needed",
        "question": "Can you capture a live robot session that exposes stable face/person identifiers, or should the video label recognition as cloud-seeded evidence only?",
        "why": "The cloud smoke records face enrollment and a recognition observation; true robot-side face/person mapping still depends on live capture fields."
    }
]

manifest = {
    "createdUtc": datetime.now(timezone.utc).isoformat(),
    "purpose": "Open Jibo conversion video evidence bundle",
    "sourceRoot": str(pathlib.Path(source_root).resolve()),
    "overlayRoot": str(pathlib.Path(overlay_root).resolve()),
    "targetMode": target_mode,
    "baseUrl": base_url,
    "outputs": {
        "commands": str(out / "demo-commands.txt"),
        "harness": str(out / "harness"),
        "harnessStdout": str(out / "harness-demo.stdout.json"),
        "firstContactInspection": str(out / "first-contact-inspection.json"),
        "cloudSmoke": str(out / "cloud-smoke.json") if cloud_smoke_status == "passed" else None,
        "blockers": str(out / "conversion-video-blockers.json"),
    },
    "evidence": {
        "harnessValidated": bool(harness and harness.get("Validated")),
        "firstContactCandidateCount": len(first_contact.get("CandidateSkillRoots", [])) if isinstance(first_contact, dict) else 0,
        "cloudSmokeStatus": cloud_smoke_status,
        "cloudSmokeSteps": [r.get("name") for r in cloud_smoke] if isinstance(cloud_smoke, list) else [],
        "identityRecognitionSmoke": any(r.get("name") == "LoopRecordRecognitionObservation" and r.get("success") for r in cloud_smoke) if isinstance(cloud_smoke, list) else False,
    },
    "videoChecklist": [
        "Show source snapshot and writable overlay paths, not a live robot root.",
        "Run the recorded commands from demo-commands.txt on camera.",
        "Show conversion audit/plan/apply/rollback manifests from the harness output.",
        "Show first-contact inspection candidates and call out any unresolved asset-review caveats.",
        "Show cloud smoke output, especially loop member enrollment and recognition observation persistence.",
        "Only move from overlay to physical robot after the blockers file is resolved."
    ],
    "blockers": blockers,
}
(out / "conversion-video-blockers.json").write_text(json.dumps(blockers, indent=2) + "\n")
(out / "conversion-video-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
print(json.dumps(manifest, indent=2))
PY
