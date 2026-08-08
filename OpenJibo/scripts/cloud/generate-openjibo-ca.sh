#!/usr/bin/env bash
# Generates the ONE shared OpenJibo CA + server certificate used by every
# robot pointed at this cloud, and a ready-to-use PFX for Kestrel.
#
# This is a one-time (per server identity) generation, not per-robot: every
# robot you convert/point gets the exact same openjibo-ca.crt copied onto it
# (see ../../../BEam/install-openjibo-ca.sh). There is no per-robot
# certificate material anywhere in this flow.
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
#   src/Jibo.Cloud/node/cert.pem              - server cert (Kestrel)
#   src/Jibo.Cloud/node/key.pem               - server key (Kestrel)
#   .tmp/openjibo-ca-cert.pfx                 - PFX for Kestrel, any launcher
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
PFX_OUT="${PFX_OUT:-${REPO_ROOT}/.tmp/openjibo-ca-cert.pfx}"
PFX_PASSWORD_FILE="${PFX_OUT}.password"

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
mkdir -p "$(dirname "${PFX_OUT}")"

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

echo "Packaging PFX for Kestrel..."
PFX_PASSWORD="$(openssl rand -hex 16)"
openssl pkcs12 -export -out "${PFX_OUT}" -inkey "${SERVER_KEY}" -in "${SERVER_CRT}" \
  -passout "pass:${PFX_PASSWORD}"
printf '%s\n' "${PFX_PASSWORD}" > "${PFX_PASSWORD_FILE}"
chmod 600 "${PFX_OUT}" "${PFX_PASSWORD_FILE}"

echo ""
echo "Done."
echo "  CA cert (copy to every robot, same file each time): ${CA_CRT}"
echo "  CA key  (server-side only, never copy to a robot):  ${CA_KEY}"
echo "  Server cert/key (Kestrel):                          ${SERVER_CRT}"
echo "  PFX for Kestrel:                                    ${PFX_OUT}"
echo "  PFX password (also saved to ${PFX_PASSWORD_FILE}):"
echo "    ${PFX_PASSWORD}"
echo ""
echo "This CA + server cert pair is the SAME for every robot pointed at this"
echo "server — there is no per-robot certificate to generate or manage."
echo ""
echo "The server also serves this CA cert at GET /openjibo-ca.crt (and its"
echo "precomputed hash at GET /openjibo-ca.hash) once running, so"
echo "BEam/install-openjibo-ca.sh on the robot can fetch both directly."
echo ""
echo "To use it, regardless of how you launch the .NET cloud (dotnet run,"
echo "systemd, Docker, ...), set these standard Kestrel env vars:"
echo "  ASPNETCORE_URLS=\"https://0.0.0.0:443;http://0.0.0.0:24605;http://0.0.0.0:8765\""
echo "  ASPNETCORE_Kestrel__Certificates__Default__Path=${PFX_OUT}"
echo "  ASPNETCORE_Kestrel__Certificates__Default__Password=${PFX_PASSWORD}"
echo ""
echo "If you already use scripts/cloud/start-dotnet-with-node-cert.sh, no further"
echo "action is needed — it reads cert.pem/key.pem from these same default paths."
