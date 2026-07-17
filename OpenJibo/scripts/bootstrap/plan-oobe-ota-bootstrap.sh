#!/usr/bin/env bash
set -euo pipefail

api_hostname="api.openjibo.com"
ntp_epoch="2017-06-01T00:00:00Z"
certificate_mode="external"
trace_bundle=""
output_path=""
strict=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --api-hostname)
      api_hostname="${2:-api.openjibo.com}"
      shift 2
      ;;
    --ntp-epoch)
      ntp_epoch="${2:-2017-06-01T00:00:00Z}"
      shift 2
      ;;
    --certificate-mode)
      certificate_mode="${2:-external}"
      shift 2
      ;;
    --trace-bundle)
      trace_bundle="${2:-}"
      shift 2
      ;;
    --output-path)
      output_path="${2:-}"
      shift 2
      ;;
    --strict)
      strict=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

node - "$api_hostname" "$ntp_epoch" "$certificate_mode" "$trace_bundle" "$output_path" "$strict" <<'NODE'
const fs = require("fs");
const path = require("path");

const apiHostname = (process.argv[2] || "api.openjibo.com").trim() || "api.openjibo.com";
const ntpEpoch = (process.argv[3] || "2017-06-01T00:00:00Z").trim();
const certificateMode = (process.argv[4] || "external").trim().toLowerCase();
const traceBundle = (process.argv[5] || "").trim();
const outputPath = (process.argv[6] || "").trim();
const strict = String(process.argv[7]).toLowerCase() === "true";

const blockers = [];
const warnings = [];

if (!/^[a-z0-9.-]+$/i.test(apiHostname)) blockers.push("invalid-api-hostname");
if (Number.isNaN(Date.parse(ntpEpoch))) blockers.push("invalid-ntp-epoch");
if (!["external", "lab-only", "none"].includes(certificateMode)) blockers.push("unsupported-certificate-mode");
if (certificateMode === "lab-only") {
  blockers.push("historical-certificate-provenance-unconfirmed");
  warnings.push("Do not commit historical *.jibo.com certificate or private-key material to this repository.");
}
if (!traceBundle) {
  blockers.push("missing-oobe-ota-trace-bundle");
} else if (!fs.existsSync(path.resolve(traceBundle))) {
  blockers.push("trace-bundle-not-found");
}

const dnsRecords = [
  "api.jibo.com",
  "api-socket.jibo.com",
  "neo-hub.jibo.com",
  "prod-api.jibo.com",
  "pool.ntp.org",
  "0.pool.ntp.org",
  "1.pool.ntp.org",
  "2.pool.ntp.org",
  "3.pool.ntp.org",
].map(name => ({ name, target: "bootstrap-appliance" }));

const requiredCaptures = [
  "OOBE_20161026.PrepareRobot",
  "OOBE_20161026.GetStatus",
  "OOBE_20161026.SetupRobot",
  "Update_20160225.GetUpdateFrom",
  "OTA asset HTTP GET with Content-Length and SHA-1 evidence",
  "post-update Robot.GetRobot or cloud-version proof",
];

const packageRules = [
  "build reproducible subsystem tarballs from legally sourced inputs",
  "exclude robot-unique identity, credential, certificate, and media files",
  "emit manifest Content-Length and SHA-1 for each asset before serving OTA metadata",
  "stage Open Jibo trust/host mapping so the robot can reach the selected cloud after reboot",
  "record rollback metadata before any package changes owner-visible state",
];

const plan = {
  Purpose: "Plan the OOBE static-DNS OTA bootstrap lane without bundling sensitive certificate material.",
  ApiHostname: apiHostname,
  NtpEpoch: ntpEpoch,
  CertificateMode: certificateMode,
  TraceBundle: traceBundle ? path.resolve(traceBundle) : null,
  CanProceed: blockers.length === 0,
  Blockers: blockers,
  Warnings: warnings,
  BootstrapServices: {
    QrStaticNetwork: ["ssid", "password", "staticIP", "netmask", "gateway", "dns1", "dns2", "accessToken"],
    DnsRecords: dnsRecords,
    TimeService: { protocol: "ntp", responseTime: ntpEpoch },
    Https: {
      mode: certificateMode,
      note: certificateMode === "external"
        ? "Certificate material must be supplied by the lab/operator outside the repository."
        : "No repository-bundled certificate material is planned.",
    },
    OtaMetadata: {
      endpoint: "Update_20160225.GetUpdateFrom",
      targetHostAfterConversion: apiHostname,
      requiredAssetMetadata: ["sha1", "contentLength", "subsystem", "fromVersion", "toVersion"],
    },
  },
  RequiredCaptures: requiredCaptures,
  PackageProvenanceRules: packageRules,
  OperatorQuestions: [
    "Can the historical certificate/key be used legally and safely in a lab-only helper?",
    "Does QR-provided DNS persist long enough across every reboot in the OTA sequence?",
    "Which subsystem package should carry the durable Open Jibo host/trust retargeting?",
    "How should simultaneous OOBE robots receive distinct tokens and rollback records?",
  ],
};

if (strict && !plan.CanProceed) {
  throw new Error(`OOBE OTA bootstrap plan is blocked: ${blockers.join("; ")}`);
}

const json = JSON.stringify(plan, null, 2);
if (outputPath) {
  const resolvedOutput = path.resolve(outputPath);
  fs.mkdirSync(path.dirname(resolvedOutput), { recursive: true });
  fs.writeFileSync(resolvedOutput, json);
  console.log(`Saved OOBE OTA bootstrap plan to ${resolvedOutput}`);
} else {
  console.log(json);
}
NODE
