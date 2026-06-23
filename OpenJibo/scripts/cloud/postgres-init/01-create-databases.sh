#!/usr/bin/env bash
set -euo pipefail

create_database_if_missing() {
  local database_name="$1"

  if psql --username "${POSTGRES_USER}" --dbname postgres -tAc \
      "SELECT 1 FROM pg_database WHERE datname = '${database_name}'" | grep -q 1; then
    echo "Database already exists: ${database_name}"
    return
  fi

  echo "Creating database: ${database_name}"
  psql --username "${POSTGRES_USER}" --dbname postgres -c "CREATE DATABASE \"${database_name}\";"
}

create_database_if_missing "openjibo_memory"
