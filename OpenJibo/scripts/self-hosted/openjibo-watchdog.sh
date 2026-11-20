#!/usr/bin/env bash
# Optional health watchdog. Run as a user with access to this Docker Compose project.
# Schedule through the host scheduler if desired. Stopped services remain stopped.
set -euo pipefail

repo_dir="${OPENJIBO_REPO_DIR:-$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)}"
cd -- "$repo_dir"

# Prevent scheduled invocations from overlapping without creating a lock file.
exec 9< .
flock -n 9 || exit 0
compose=(docker compose --profile self-hosted)

postgres_id=$("${compose[@]}" ps --status running --quiet postgres)
api_id=$("${compose[@]}" ps --status running --quiet api)

if [[ ! $postgres_id =~ ^[[:xdigit:]]{12,64}$ || ! $api_id =~ ^[[:xdigit:]]{12,64}$ ]]; then
    exit 0
fi

postgres_health=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{end}}' "$postgres_id")
case "$postgres_health" in
    unhealthy)
        printf '%s\n' 'OpenJibo PostgreSQL is unhealthy. Restarting this project container.'
        docker restart "$postgres_id"
        exit 0
        ;;
    healthy) ;;
    *) exit 0 ;;
esac

api_address=$("${compose[@]}" port api 8080)
api_port=${api_address##*:}
if [[ ! $api_port =~ ^[0-9]+$ ]]; then
    printf '%s\n' 'Could not determine the OpenJibo API port.' >&2
    exit 1
fi

if ! curl --noproxy '*' --fail --silent --show-error --max-time 5 "http://127.0.0.1:$api_port/health/live" >/dev/null; then
    printf '%s\n' 'OpenJibo API is not responding. Restarting this project container.'
    docker restart "$api_id"
fi
