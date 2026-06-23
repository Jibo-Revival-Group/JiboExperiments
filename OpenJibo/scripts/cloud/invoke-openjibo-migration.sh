#!/usr/bin/env bash
set -euo pipefail

project_path="src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Migrations/Jibo.Cloud.Migrations.csproj"
scripts_directory="src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Migrations/Migrations/PostgreSql"
target="all"
state_connection_string=""
personal_memory_connection_string=""
preview=false
verbose=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project-path)
      project_path="${2:-}"
      shift 2
      ;;
    --scripts-directory)
      scripts_directory="${2:-}"
      shift 2
      ;;
    --target)
      target="${2:-all}"
      shift 2
      ;;
    --state-connection)
      state_connection_string="${2:-}"
      shift 2
      ;;
    --memory-connection)
      personal_memory_connection_string="${2:-}"
      shift 2
      ;;
    --preview|--dry-run)
      preview=true
      shift
      ;;
    --verbose)
      verbose=true
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

resolve_path() {
  local candidate="$1"
  if [[ "$candidate" = /* ]]; then
    printf '%s\n' "$candidate"
  else
    printf '%s\n' "${repo_root}/${candidate}"
  fi
}

resolved_project_path="$(resolve_path "$project_path")"
resolved_scripts_directory="$(resolve_path "$scripts_directory")"

if [[ ! -f "$resolved_project_path" ]]; then
  echo "Could not find migration project at $resolved_project_path" >&2
  exit 1
fi

if [[ ! -d "$resolved_scripts_directory" ]]; then
  echo "Could not find migration scripts at $resolved_scripts_directory" >&2
  exit 1
fi

arguments=(
  run
  --project "$resolved_project_path"
  --
  --target "$target"
  --scripts "$resolved_scripts_directory"
)

if [[ -n "$state_connection_string" ]]; then
  arguments+=(--state-connection "$state_connection_string")
fi

if [[ -n "$personal_memory_connection_string" ]]; then
  arguments+=(--memory-connection "$personal_memory_connection_string")
fi

if [[ "$preview" == true ]]; then
  arguments+=(--preview)
else
  arguments+=(--apply)
fi

if [[ "$verbose" == true ]]; then
  arguments+=(--verbose)
fi

echo "Running Open Jibo migrations for target '$target'"
cd "$repo_root"
exec dotnet "${arguments[@]}"
