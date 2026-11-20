#!/usr/bin/env bash
# OpenJibo self-hosted watchdog: keeps the server answering Jibo without manual fixes.
# Run as root from cron every 5 minutes and at boot (see install-watchdog.sh).
#  - disk over the limit  -> clear Docker build cache, old images and container logs
#  - database unhealthy or API not answering -> bring the stack back up / restart it
#  - HTTPS front (openjibo-https) stopped    -> start it
set -u
REPO_DIR="${OPENJIBO_REPO_DIR:-$(cd "$(dirname "$0")/../.." && pwd)}"
DISK_LIMIT="${OPENJIBO_WATCHDOG_DISK_LIMIT:-85}"
LOG="${OPENJIBO_WATCHDOG_LOG:-/var/log/openjibo-watchdog.log}"
COMPOSE=(docker compose --project-directory "$REPO_DIR" -f "$REPO_DIR/docker-compose.yml" --profile self-hosted)

log() { echo "$(date '+%F %T') $*" >> "$LOG"; }

# Keep this log small.
if [ -f "$LOG" ] && [ "$(wc -l < "$LOG")" -gt 2000 ]; then
  tail -n 500 "$LOG" > "$LOG.tmp" && mv "$LOG.tmp" "$LOG"
fi

# 1. Disk space (the database crashes when the disk is full).
used=$(df --output=pcent / | tail -1 | tr -dc '0-9')
if [ "${used:-0}" -ge "$DISK_LIMIT" ]; then
  log "disk at ${used}% (limit ${DISK_LIMIT}%), cleaning up"
  docker builder prune -af >/dev/null 2>&1
  docker image prune -f >/dev/null 2>&1
  sh -c 'truncate -s 0 /var/lib/docker/containers/*/*-json.log' 2>/dev/null
  journalctl --vacuum-size=200M >/dev/null 2>&1
  docker exec openjibo-api-1 sh -c 'find /app/captures -type f -mtime +1 -delete' >/dev/null 2>&1
  log "disk now at $(df --output=pcent / | tail -1 | tr -d ' ')"
fi

# 2. Database health.
pg_state=$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' openjibo-postgres-1 2>/dev/null || echo missing)
# 3. API health.
api_ok=no
curl -fsS --max-time 5 http://127.0.0.1:8080/health >/dev/null 2>&1 && api_ok=yes

if [ "$pg_state" != "healthy" ] || [ "$api_ok" != "yes" ]; then
  log "unhealthy: postgres=$pg_state api=$api_ok, running compose up"
  "${COMPOSE[@]}" up -d >> "$LOG" 2>&1
  sleep 20
  pg_state=$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' openjibo-postgres-1 2>/dev/null || echo missing)
  if [ "$pg_state" != "healthy" ]; then
    log "postgres still $pg_state, restarting it"
    "${COMPOSE[@]}" restart postgres >> "$LOG" 2>&1
    sleep 20
  fi
  if ! curl -fsS --max-time 5 http://127.0.0.1:8080/health >/dev/null 2>&1; then
    log "api still not answering, restarting it"
    "${COMPOSE[@]}" restart api >> "$LOG" 2>&1
  fi
fi

# 4. HTTPS front for Jibo (Caddy on 443, started outside compose).
if docker inspect openjibo-https >/dev/null 2>&1; then
  if [ "$(docker inspect -f '{{.State.Running}}' openjibo-https)" != "true" ]; then
    log "openjibo-https stopped, starting it"
    docker start openjibo-https >> "$LOG" 2>&1
  fi
  docker update --restart unless-stopped openjibo-https >/dev/null 2>&1
fi
exit 0
