#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
CAPTURE_PATH="${1:-${REPO_ROOT}/captures/websocket}"
exec python3 "${SCRIPT_DIR}/inspect-websocket-recognition-candidates.py" "${CAPTURE_PATH}" "${@:2}"
