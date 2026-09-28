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

encrypt_count="$(awk '/^OPENJIBO_USER_ENCRYPT=/ { count++ } END { print count + 0 }' "$template_path")"
salt_count="$(awk '/^OPENJIBO_USER_SALT=/ { count++ } END { print count + 0 }' "$template_path")"
noncanonical_count="$(awk '/^[[:space:]]*(export[[:space:]]+)?OPENJIBO_USER_(ENCRYPT|SALT)([[:space:]]|=)/ && $0 !~ /^OPENJIBO_USER_(ENCRYPT|SALT)=/ { count++ } END { print count + 0 }' "$template_path")"
if [[ "$encrypt_count" != 1 || "$salt_count" != 1 || "$noncanonical_count" != 0 ]]; then
  echo "Compose env template must contain exactly one user encryption key and salt assignment." >&2
  exit 1
fi
if ! command -v openssl >/dev/null 2>&1; then
  echo "OpenSSL is required to initialize user encryption secrets." >&2
  exit 1
fi
if ! encryption_secret="$(openssl rand -hex 32 2>/dev/null)" ||
   [[ ! "$encryption_secret" =~ ^[0-9a-f]{64}$ ]]; then
  echo "Could not generate the user encryption secret." >&2
  exit 1
fi
if ! salt_secret="$(openssl rand -hex 16 2>/dev/null)" ||
   [[ ! "$salt_secret" =~ ^[0-9a-f]{32}$ ]]; then
  echo "Could not generate the user encryption salt." >&2
  exit 1
fi

temp_path="$(mktemp "${compose_dir}/.env.tmp.XXXXXX")"
cleanup() {
  if [[ -n "${temp_path:-}" && -e "$temp_path" ]]; then
    rm -f -- "$temp_path"
  fi
}
trap cleanup EXIT

while IFS= read -r line || [[ -n "$line" ]]; do
  case "$line" in
    OPENJIBO_USER_ENCRYPT=*) printf 'OPENJIBO_USER_ENCRYPT=%s\n' "$encryption_secret" ;;
    OPENJIBO_USER_SALT=*) printf 'OPENJIBO_USER_SALT=%s\n' "$salt_secret" ;;
    *) printf '%s\n' "$line" ;;
  esac
done < "$template_path" > "$temp_path"
if [[ -n "${OPENJIBO_POSTGRES_PASSWORD:-}" ]]; then
  printf '\nOPENJIBO_POSTGRES_PASSWORD=%s\n' "$OPENJIBO_POSTGRES_PASSWORD" >> "$temp_path"
fi

if ! ln -T -- "$temp_path" "$target_path" 2>/dev/null; then
  echo "Could not install compose env; an existing file was preserved." >&2
  exit 1
fi
rm -f -- "$temp_path"
temp_path=""
echo "Created compose env from template: $target_path"
