#!/usr/bin/env bash
set -euo pipefail

run_migration=false
skip_build=false
image=""
image_specified=false

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
    --image)
      if [[ "$image_specified" == true ]]; then
        echo "--image may be supplied only once." >&2
        exit 2
      fi
      if [[ $# -lt 2 ]]; then
        echo "--image requires a registry/repository@sha256:<64 lowercase hex> value." >&2
        exit 2
      fi
      image="$2"
      image_specified=true
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

image_pattern='^(localhost(:[0-9]+)?|[a-z0-9]+([.-][a-z0-9]+)*(:[0-9]+)?)(/[a-z0-9]+([._-][a-z0-9]+)*)+@sha256:[0-9a-f]{64}$'
if [[ "$image_specified" == true ]]; then
  if [[ ! "$image" =~ $image_pattern ]]; then
    echo "--image must be a digest-pinned registry/repository@sha256:<64 lowercase hex> reference." >&2
    exit 2
  fi
  if [[ "$skip_build" == true ]]; then
    echo "--image already selects no-build mode; do not combine it with --skip-build." >&2
    exit 2
  fi
  export OPENJIBO_RUNTIME_IMAGE="$image"
else
  export OPENJIBO_RUNTIME_IMAGE="openjibo-cloud:self-hosted"
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/../.." && pwd)"

bash "${script_dir}/initialize-openjibo-compose-env.sh"

compose_args=(compose up -d)
if [[ "$image_specified" == true ]]; then
  compose_args+=(--no-build --pull missing)
elif [[ "$skip_build" != true ]]; then
  compose_args+=(--build)
fi

compose_args+=(postgres)
if [[ "$run_migration" == true ]]; then
  compose_args+=(migrate)
fi
compose_args+=(api)

cd "$repo_root"
if ! docker compose config --quiet >/dev/null 2>&1; then
  echo "Docker Compose configuration check failed; no services were started." >&2
  exit 1
fi
exec docker "${compose_args[@]}"
