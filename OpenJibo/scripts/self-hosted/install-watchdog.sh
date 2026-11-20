#!/usr/bin/env bash
# Installs the OpenJibo watchdog (run once, as root): every 5 minutes and 2 minutes after boot.
set -eu
SCRIPT="$(cd "$(dirname "$0")" && pwd)/openjibo-watchdog.sh"
chmod +x "$SCRIPT"
cat > /etc/cron.d/openjibo-watchdog <<CRON
SHELL=/bin/bash
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
*/5 * * * * root $SCRIPT
@reboot root sleep 120 && $SCRIPT
CRON
chmod 644 /etc/cron.d/openjibo-watchdog
echo "Installed /etc/cron.d/openjibo-watchdog -> $SCRIPT"
echo "Log: /var/log/openjibo-watchdog.log"
