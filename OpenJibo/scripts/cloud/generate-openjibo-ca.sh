#!/usr/bin/env bash
# Generates the ONE shared OpenJibo CA + server certificate used by every
# robot pointed at this cloud.
#
# This is a one-time (per server identity) generation, not per-robot: every
# robot you convert/point gets the exact same openjibo-ca.crt copied onto it
# (see ../../../BEam/install-openjibo-ca.sh). There is no per-robot
# certificate material anywhere in this flow.
#
# Nothing further is required to make the .NET cloud use this cert: Program.cs
# auto-detects src/Jibo.Cloud/node/{cert,key}.pem at startup (via
# ConfigureDefaultKestrelEndpoints) and binds :443/:24605/:8765 from them
# directly — no ASPNETCORE_URLS, no PFX, no env vars, regardless of whether you
# run `dotnet run`, a published binary under systemd, or Docker.
#
# Usage:
#   scripts/cloud/generate-openjibo-ca.sh [extra SAN list]
#
#   extra SAN list: comma-separated openssl SAN entries to add to the server
#   cert, e.g. "IP:192.168.7.142,DNS:jibo.lan". Add the LAN IP or hostname
#   your robots actually reach this server on.
#
# Output (gitignored; see .gitignore):
#   src/Jibo.Cloud/node/tls/openjibo-ca.crt   - CA cert (copy to every robot)
#   src/Jibo.Cloud/node/tls/openjibo-ca.key   - CA key (server-side only)
#   src/Jibo.Cloud/node/cert.pem              - server cert (auto-loaded)
#   src/Jibo.Cloud/node/key.pem               - server key (auto-loaded)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
NODE_DIR="${REPO_ROOT}/src/Jibo.Cloud/node"
TLS_DIR="${NODE_DIR}/tls"

CA_KEY="${TLS_DIR}/openjibo-ca.key"
CA_CRT="${TLS_DIR}/openjibo-ca.crt"
CA_HASH="${TLS_DIR}/openjibo-ca.hash"
SERVER_KEY="${NODE_DIR}/key.pem"
SERVER_CRT="${NODE_DIR}/cert.pem"

CA_DAYS="${CA_DAYS:-7300}"     # ~20 years
CERT_DAYS="${CERT_DAYS:-3650}" # ~10 years

EXTRA_SANS="${1:-${EXTRA_SANS:-}}"
DEFAULT_SANS="DNS:api.jibo.com,DNS:api-socket.jibo.com,DNS:open-jibo-socket.openjibo.com,DNS:neohub.openjibo.com,DNS:neo-hub.jibo.com,DNS:open-jibo.jibo.pro,DNS:open-jibo-socket.jibo.pro,DNS:localhost,IP:127.0.0.1"
if [[ -n "${EXTRA_SANS}" ]]; then
  ALL_SANS="${DEFAULT_SANS},${EXTRA_SANS}"
else
  ALL_SANS="${DEFAULT_SANS}"
fi

mkdir -p "${TLS_DIR}"

if [[ -f "${CA_KEY}" && -f "${CA_CRT}" ]]; then
  echo "Reusing existing shared CA: ${CA_CRT}"
  echo "(delete ${TLS_DIR} first if you really want a fresh CA — every robot"
  echo " that already trusts the old one would need re-installing.)"
else
  echo "Generating the OpenJibo CA (one-time, shared across every robot)..."
  openssl genrsa -out "${CA_KEY}" 4096
  openssl req -x509 -new -nodes -key "${CA_KEY}" -sha256 -days "${CA_DAYS}" \
    -subj "/CN=OpenJibo Root CA" -out "${CA_CRT}"
fi

echo "Generating server certificate signed by the OpenJibo CA..."
echo " - SANs: ${ALL_SANS}"

SERVER_CSR="$(mktemp)"
SAN_CONF="$(mktemp)"
trap 'rm -f "${SERVER_CSR}" "${SAN_CONF}"' EXIT

cat > "${SAN_CONF}" <<EOF
[req]
distinguished_name = dn
req_extensions = ext
[dn]
[ext]
subjectAltName = ${ALL_SANS}
EOF

openssl genrsa -out "${SERVER_KEY}" 2048
openssl req -new -key "${SERVER_KEY}" -subj "/CN=OpenJibo Server" \
  -reqexts ext -config "${SAN_CONF}" -out "${SERVER_CSR}"
openssl x509 -req -in "${SERVER_CSR}" -CA "${CA_CRT}" -CAkey "${CA_KEY}" \
  -CAcreateserial -days "${CERT_DAYS}" -sha256 \
  -extfile "${SAN_CONF}" -extensions ext -out "${SERVER_CRT}"
rm -f "${TLS_DIR}/openjibo-ca.srl" 2>/dev/null || true

chmod 600 "${CA_KEY}" "${SERVER_KEY}"
chmod 644 "${CA_CRT}" "${SERVER_CRT}"

# Precompute the OpenSSL subject-hash so robots WITHOUT an openssl binary
# (e.g. BusyBox-only builds) can still create the CApath symlink the native
# NotificationSubsystem needs — install-openjibo-ca.sh fetches this alongside
# the cert instead of running `openssl x509 -hash` on the robot.
openssl x509 -hash -noout -in "${CA_CRT}" > "${CA_HASH}"
chmod 644 "${CA_HASH}"

echo ""
echo "Done."
echo "  CA cert (copy to every robot, same file each time): ${CA_CRT}"
echo "  CA key  (server-side only, never copy to a robot):  ${CA_KEY}"
echo "  Server cert/key (auto-loaded by the .NET cloud):    ${SERVER_CRT}"
echo ""
echo "This CA + server cert pair is the SAME for every robot pointed at this"
echo "server — there is no per-robot certificate to generate or manage."
echo ""
echo "The server also serves this CA cert at GET /openjibo-ca.crt (and its"
echo "precomputed hash at GET /openjibo-ca.hash) once running, so"
echo "BEam/install-openjibo-ca.sh on the robot can fetch both directly."
echo ""
echo "Nothing else to configure: the next time the .NET cloud starts — however"
echo "you launch it (dotnet run, a published binary, systemd, Docker, ...), no"
echo "ASPNETCORE_URLS/env vars/flags needed — it detects ${SERVER_CRT}"
echo "and ${SERVER_KEY} automatically and binds:"
echo "  https://0.0.0.0:443   (native NotificationSubsystem WSS hub, uses this cert)"
echo "  http://0.0.0.0:24605  (local credentials/JSC API)"
echo "  http://0.0.0.0:8765   (LAN credentials/JSC API)"
echo "Just restart the process (kill + relaunch exactly as you always do)."
