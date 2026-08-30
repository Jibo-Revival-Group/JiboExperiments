#!/usr/bin/env bash
set -euo pipefail

compose_path="docker-compose.yml"
workflow_path="../.github/workflows/openjibo-cloud-ci.yml"
migration_script_path="scripts/cloud/Invoke-OpenJiboMigration.ps1"
linux_migration_script_path="scripts/cloud/invoke-openjibo-migration.sh"
smoke_script_path="scripts/cloud/invoke-cloud-smoke.sh"
stack_script_path="scripts/cloud/invoke-openjibo-self-hosted-stack.sh"
postgres_init_script_path="scripts/cloud/postgres-init/01-create-databases.sh"
compose_env_bootstrap_script_path="scripts/cloud/initialize-openjibo-compose-env.sh"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/../.." && pwd)"

get_repo_file_text() {
  local relative_path="$1"
  local full_path="${repo_root}/${relative_path}"

  if [[ ! -f "$full_path" ]]; then
    echo "Missing required file: $full_path" >&2
    exit 1
  fi

  cat "$full_path"
}

compose_text="$(get_repo_file_text "$compose_path")"
workflow_text="$(get_repo_file_text "$workflow_path")"
migration_text="$(get_repo_file_text "$migration_script_path")"
linux_migration_text="$(get_repo_file_text "$linux_migration_script_path")"
smoke_text="$(get_repo_file_text "$smoke_script_path")"
stack_text="$(get_repo_file_text "$stack_script_path")"
postgres_init_text="$(get_repo_file_text "$postgres_init_script_path")"
compose_env_bootstrap_text="$(get_repo_file_text "$compose_env_bootstrap_script_path")"

required_compose_markers=(
  "services:"
  "migrate:"
  "api:"
  "postgres:"
  "smoke:"
  "OPENJIBO_POSTGRES_PASSWORD"
  "OpenJibo__State__Backend: PostgreSql"
  "OpenJibo__Deployment__Mode: self-hosted-isolated"
  "OpenJibo__AcceptedHosts__0: localhost"
  "OpenJibo__AcceptedHosts__1: 127.0.0.1"
  "OpenJibo__PersonalMemory__Backend: PostgreSql"
  "OpenJibo__Media__Backend: File"
  "OpenJibo__SelfHosted__AllowTokenlessSingleRobotHub"
  "healthcheck:"
  "/health"
)

required_migration_markers=(
  "--target"
  "--apply"
  "--preview"
  "PostgreSql"
)

required_smoke_markers=(
  "/health"
  "Account_20151111.Create"
  "Account_20151111.Login"
  "Loop_20160324.ListLoops"
  "OOBE_20161026.PrepareRobot"
  "OOBE_20161026.GetStatus"
  "OOBE_20161026.SetupRobot"
  "OOBE_20161026.VerifyConnection"
  "rollbackSnapshotId"
  "targetMode"
  "targetHost"
)

required_workflow_markers=(
  "working-directory: OpenJibo"
  "invoke-openjibo-self-hosted-stack.sh"
  "test-openjibo-self-hosted-deployment-contract.sh"
  "invoke-cloud-smoke.sh"
)

for marker in "${required_compose_markers[@]}"; do
  if [[ "$compose_text" != *"$marker"* ]]; then
    echo "Docker Compose file is missing expected marker: $marker" >&2
    exit 1
  fi
done

for marker in "${required_migration_markers[@]}"; do
  if [[ "$migration_text" != *"$marker"* ]]; then
    echo "Migration wrapper is missing expected marker: $marker" >&2
    exit 1
  fi
done

for marker in "${required_migration_markers[@]}"; do
  if [[ "$linux_migration_text" != *"$marker"* ]]; then
    echo "Linux migration wrapper is missing expected marker: $marker" >&2
    exit 1
  fi
done

for marker in "${required_smoke_markers[@]}"; do
  if [[ "$smoke_text" != *"$marker"* ]]; then
    echo "Smoke script is missing expected marker: $marker" >&2
    exit 1
  fi
done

if [[ "$stack_text" != *"initialize-openjibo-compose-env.sh"* ]]; then
  echo "Self-hosted stack launcher is missing env bootstrap helper reference." >&2
  exit 1
fi

if [[ "$stack_text" != *"compose up -d"* ]]; then
  echo "Self-hosted stack launcher is missing compose launch command." >&2
  exit 1
fi

if [[ "$stack_text" != *"migrate"* ]] || [[ "$stack_text" != *"api"* ]]; then
  echo "Self-hosted stack launcher is missing core service names." >&2
  exit 1
fi

for marker in "${required_workflow_markers[@]}"; do
  if [[ "$workflow_text" != *"$marker"* ]]; then
    echo "Workflow is missing expected marker: $marker" >&2
    exit 1
  fi
done

if [[ "$compose_text" == *"OPENJIBO_MEDIA_CONNECTION_STRING"* ]]; then
  echo "Self-hosted compose still references the managed media connection secret." >&2
  exit 1
fi

if [[ "$compose_text" != *"docker-entrypoint-initdb.d"* ]]; then
  echo "Self-hosted compose is missing the postgres initdb mount." >&2
  exit 1
fi

if [[ "$compose_text" != *"./scripts/cloud/postgres-init:/docker-entrypoint-initdb.d:ro"* ]]; then
  echo "Self-hosted compose postgres initdb mount points at the wrong path." >&2
  exit 1
fi

if [[ "$postgres_init_text" != *"openjibo_memory"* ]]; then
  echo "Self-hosted postgres init script is missing the memory database creation." >&2
  exit 1
fi

if [[ "$compose_env_bootstrap_text" != *"OPENJIBO_POSTGRES_PASSWORD"* ]]; then
  echo "Compose env bootstrap is missing the PostgreSQL password propagation logic." >&2
  exit 1
fi

echo "Self-hosted deployment contract checks passed."
