#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/../.." && pwd)"
compose_dir="${repo_root}"
template_path="${compose_dir}/.env.example"
target_path="${compose_dir}/.env"

if [[ ! -f "$template_path" ]]; then
  echo "Missing compose env template: $template_path" >&2
  exit 1
fi

if [[ -f "$target_path" ]]; then
  echo "Compose env already exists: $target_path"
  exit 0
fi

cp "$template_path" "$target_path"
echo "Created compose env from template: $target_path"
