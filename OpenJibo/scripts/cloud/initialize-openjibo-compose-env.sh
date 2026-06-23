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
  if [[ -n "${OPENJIBO_POSTGRES_PASSWORD:-}" ]]; then
    if grep -q '^OPENJIBO_POSTGRES_PASSWORD=' "$target_path"; then
      temp_path="$(mktemp)"
      awk -v password="$OPENJIBO_POSTGRES_PASSWORD" '
        BEGIN { replaced = 0 }
        /^OPENJIBO_POSTGRES_PASSWORD=/ { print "OPENJIBO_POSTGRES_PASSWORD=" password; replaced = 1; next }
        { print }
        END {
          if (!replaced) {
            print "OPENJIBO_POSTGRES_PASSWORD=" password
          }
        }
      ' "$target_path" > "$temp_path"
      mv "$temp_path" "$target_path"
    else
      printf '\nOPENJIBO_POSTGRES_PASSWORD=%s\n' "$OPENJIBO_POSTGRES_PASSWORD" >> "$target_path"
    fi
  fi
  exit 0
fi

cp "$template_path" "$target_path"
if [[ -n "${OPENJIBO_POSTGRES_PASSWORD:-}" ]]; then
  printf '\nOPENJIBO_POSTGRES_PASSWORD=%s\n' "$OPENJIBO_POSTGRES_PASSWORD" >> "$target_path"
fi
echo "Created compose env from template: $target_path"
