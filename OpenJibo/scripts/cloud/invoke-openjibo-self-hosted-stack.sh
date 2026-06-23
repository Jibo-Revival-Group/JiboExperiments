#!/usr/bin/env bash
set -euo pipefail

run_migration=false
skip_build=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --run-migration)
      run_migration=true
      shift
      ;;
    --skip-build)
      skip_build=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/../.." && pwd)"

bash "${script_dir}/initialize-openjibo-compose-env.sh"

compose_args=(compose up -d)
if [[ "$skip_build" != true ]]; then
  compose_args+=(--build)
fi

compose_args+=(postgres)
if [[ "$run_migration" == true ]]; then
  compose_args+=(migrate)
fi
compose_args+=(api)

cd "$repo_root"
exec docker "${compose_args[@]}"
