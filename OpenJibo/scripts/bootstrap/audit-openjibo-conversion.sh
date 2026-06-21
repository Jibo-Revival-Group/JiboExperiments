#!/usr/bin/env bash
set -euo pipefail

robot_root=""
output_path=""
strict=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --robot-root)
      robot_root="${2:-}"
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

if [[ -z "$robot_root" ]]; then
  echo "--robot-root is required" >&2
  exit 2
fi

node - "$robot_root" "$output_path" "$strict" <<'NODE'
const fs = require("fs");
const path = require("path");

const robotRoot = path.resolve(process.argv[2]);
const outputPath = (process.argv[3] || "").trim();
const strict = String(process.argv[4]).toLowerCase() === "true";

function readJsonFile(filePath) {
  if (!filePath || !fs.existsSync(filePath)) return null;
  try {
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
  } catch {
    return null;
  }
}

function getField(object, name) {
  return object && typeof object === "object" ? object[name] : null;
}

function resolveCandidate(relativePaths) {
  for (const relativePath of relativePaths) {
    const candidate = path.resolve(robotRoot, relativePath);
    if (fs.existsSync(candidate)) return candidate;
  }
  return null;
}

const jetstreamPath = resolveCandidate([
  "etc/jibo-jetstream-service.json",
  "usr/local/etc/jibo-jetstream-service.json",
]);
const credentialsPath = resolveCandidate(["var/jibo/credentials.json"]);
const oobeConfigPath = resolveCandidate([
  "skills/jibo/Jibo/Skills/oobe-config/config.json",
  "opt/jibo/Jibo/Skills/oobe-config/config.json",
]);

const ssmFiles = [];
for (const base of [path.resolve(robotRoot, "etc/jibo-ssm"), path.resolve(robotRoot, "usr/local/etc/jibo-ssm")]) {
  if (!fs.existsSync(base)) continue;
  for (const entry of fs.readdirSync(base, { withFileTypes: true })) {
    if (entry.isFile() && entry.name.endsWith(".json")) ssmFiles.push(path.resolve(base, entry.name));
  }
}

const jetstream = readJsonFile(jetstreamPath);
const credentials = readJsonFile(credentialsPath);
const oobeConfig = readJsonFile(oobeConfigPath);

const region = getField(credentials, "region");
const accessKeyId = getField(credentials, "accessKeyId");
const secretAccessKey = getField(credentials, "secretAccessKey");

const jetstreamRegionNames = [];
if (jetstream && typeof jetstream === "object" && jetstream.regions && typeof jetstream.regions === "object") {
  jetstreamRegionNames.push(...Object.keys(jetstream.regions));
}

const recommendations = [];
if (!jetstreamPath) recommendations.push("Add or mount a jetstream region config file before conversion.");
if (!credentialsPath) recommendations.push("Locate credentials.json before attempting any mode switch.");
if (!oobeConfigPath) recommendations.push("Confirm the oobe-config bundle before wiring first-boot behavior.");
if (!region) recommendations.push("Region is not set yet; that needs to be recorded before any write helper runs.");

const audit = {
  RobotRoot: robotRoot,
  Files: {
    Jetstream: jetstreamPath,
    Credentials: credentialsPath,
    OobeConfig: oobeConfigPath,
    SsmCount: ssmFiles.length,
  },
  Credentials: {
    Region: region || null,
    AccessKeyIdPresent: Boolean(accessKeyId && String(accessKeyId).trim()),
    SecretAccessKeyPresent: Boolean(secretAccessKey && String(secretAccessKey).trim()),
  },
  Jetstream: {
    RegionNames: jetstreamRegionNames,
  },
  Oobe: {
    ServerRegion: getField(oobeConfig, "serverRegion") || null,
    OtaFilter: getField(oobeConfig, "otaFilter") || null,
  },
  Recommendations: recommendations,
  CanProceed: recommendations.length === 0,
  BlockingIssues: recommendations,
};

if (strict && !audit.CanProceed) {
  throw new Error(`Conversion audit is not predictive-safe: ${recommendations.join("; ")}`);
}

const json = JSON.stringify(audit, null, 2);
if (outputPath) {
  const resolvedOutput = path.resolve(outputPath);
  fs.mkdirSync(path.dirname(resolvedOutput), { recursive: true });
  fs.writeFileSync(resolvedOutput, json);
  console.log(`Saved conversion audit to ${resolvedOutput}`);
} else {
  console.log(json);
}
NODE
