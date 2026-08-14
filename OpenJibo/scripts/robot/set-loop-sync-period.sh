#!/bin/sh
# Lowers (or restores) SyncManager's periodic loop-roster resync interval on the
# robot. POSIX sh on purpose: the robot only has BusyBox ash.
#
# Only run this if /api/diagnostics/loop-sync shows LoopUpdated pushes being
# delivered and the robot still not re-syncing. When push works, the roster
# updates ~5s after a portal edit and this script is unnecessary.
#
# Background: LoopManager sets
#
#   const PERIODIC_SECONDS = 60 * 60 * 2;   // every 2 hours just in case
#                                           // we missed a notification
#
# in /usr/local/bin/jibo-ssm/lib/skills-service-manager.js and passes it to the
# SyncManager constructor as syncingPeriod. That 7200s fallback is why portal
# edits show up "about two hours later" when the notification never lands.
# Lowering it trades a little cloud chatter for a bounded worst case; it does
# not fix push, it just stops push failures from being invisible for 2 hours.
#
# The anchored "^const PERIODIC_SECONDS = " match hits exactly one line in the
# bundle. The other managers (Holiday, MediaList, Robot) declare
# SYNC_PERIODIC_SECONDS and are deliberately left alone.
#
# Usage (on the robot, as root):
#   set-loop-sync-period.sh                 # show current value
#   set-loop-sync-period.sh 60              # resync every 60s
#   set-loop-sync-period.sh 60 --restart    # ...and restart the system manager
#   set-loop-sync-period.sh --restore       # put the stock 7200s back
#
# Override the bundle path with SSM_BUNDLE=... for testing against a dump copy.
set -eu

BUNDLE="${SSM_BUNDLE:-/usr/local/bin/jibo-ssm/lib/skills-service-manager.js}"
BACKUP="${BUNDLE}.openjibo-orig"
MARKER='^const PERIODIC_SECONDS = '

die() {
  echo "error: $*" >&2
  exit 1
}

restart_ssm() {
  [ -x /etc/init.d/S78jibo-system-manager ] ||
    die "no /etc/init.d/S78jibo-system-manager; restart the system manager by hand"
  echo "restarting the system manager..."
  /etc/init.d/S78jibo-system-manager stop || true
  if [ -x /etc/init.d/S76openjibo-bootstrap ]; then
    /etc/init.d/S76openjibo-bootstrap start
  fi
  /etc/init.d/S78jibo-system-manager start
}

current_value() {
  sed -n "s/${MARKER}\(.*\);.*/\1/p" "${BUNDLE}" | head -n 1
}

[ -f "${BUNDLE}" ] || die "bundle not found: ${BUNDLE}"

matches="$(grep -c "${MARKER}" "${BUNDLE}" || true)"
[ "${matches}" = "1" ] ||
  die "expected exactly 1 PERIODIC_SECONDS declaration in ${BUNDLE}, found ${matches}"

seconds=""
restart="no"
for arg in "$@"; do
  case "${arg}" in
    --restart) restart="yes" ;;
    --restore) seconds="restore" ;;
    ''|*[!0-9]*) die "unexpected argument: ${arg}" ;;
    *) seconds="${arg}" ;;
  esac
done

if [ -z "${seconds}" ]; then
  echo "bundle:  ${BUNDLE}"
  echo "current: PERIODIC_SECONDS = $(current_value)"
  if [ -f "${BACKUP}" ]; then
    echo "backup:  ${BACKUP} (stock value preserved)"
  fi
  exit 0
fi

if [ "${seconds}" = "restore" ]; then
  [ -f "${BACKUP}" ] || die "no backup at ${BACKUP}; nothing to restore"
  cp "${BACKUP}" "${BUNDLE}"
  echo "restored: PERIODIC_SECONDS = $(current_value)"
  if [ "${restart}" = "yes" ]; then
    restart_ssm
  fi
  exit 0
fi

[ "${seconds}" -ge 10 ] 2>/dev/null ||
  die "refusing to sync more often than every 10s (got ${seconds})"

# Keep the first patch's backup: re-running must never overwrite the stock file
# with an already-patched one.
[ -f "${BACKUP}" ] || cp "${BUNDLE}" "${BACKUP}"

sed -i "s/${MARKER}.*;/const PERIODIC_SECONDS = ${seconds};/" "${BUNDLE}"
echo "patched: PERIODIC_SECONDS = $(current_value)  (stock backed up at ${BACKUP})"

if [ "${restart}" = "yes" ]; then
  restart_ssm
else
  echo "restart the system manager to pick it up:"
  echo "  /etc/init.d/S78jibo-system-manager stop"
  echo "  /etc/init.d/S78jibo-system-manager start"
fi
