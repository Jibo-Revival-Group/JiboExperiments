#!/bin/sh
set -eu

SCRIPT_VERSION="2026-07-17.1"
echo "audit-openjibo-conversion.sh $SCRIPT_VERSION" >&2

robot_root=""
output_path=""
strict=false

while [ $# -gt 0 ]; do
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

if [ -z "$robot_root" ]; then
  echo "--robot-root is required" >&2
  exit 2
fi

tmp_js="$(mktemp "${TMPDIR:-/tmp}/audit-openjibo-conversion.XXXXXX")"
cleanup() {
  rm -f "$tmp_js"
}
trap cleanup EXIT

cat > "$tmp_js" <<'NODE'
const fs = require("fs");
const path = require("path");

const robotRoot = path.resolve(process.argv[2]);
const outputPath = (process.argv[3] || "").trim();
const strict = String(process.argv[4]).toLowerCase() === "true";

function readJsonFile(filePath) {
  if (!filePath || !fs.existsSync(filePath)) return null;
  try {
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
  } catch (error) {
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

function ensureDir(dirPath) {
  if (!dirPath || fs.existsSync(dirPath)) return;
  ensureDir(path.dirname(dirPath));
  try {
    fs.mkdirSync(dirPath);
  } catch (error) {
    if (!fs.existsSync(dirPath)) throw error;
  }
}

const jetstreamPath = resolveCandidate([
  "etc/jibo-jetstream-service.json",
  "usr/local/etc/jibo-jetstream-service.json",
]);
const serverServicePath = resolveCandidate([
  "etc/jibo-server-service.json",
  "usr/local/etc/jibo-server-service.json",
]);
const credentialsPath = resolveCandidate(["var/jibo/credentials.json"]);
const oobeConfigPath = resolveCandidate([
  "skills/jibo/Jibo/Skills/oobe-config/config.json",
  "opt/jibo/Jibo/Skills/oobe-config/config.json",
]);

function collectExisting(relativePaths) {
  const found = [];
  for (const relativePath of relativePaths) {
    const candidate = path.resolve(robotRoot, relativePath);
    if (fs.existsSync(candidate)) found.push(candidate);
  }
  return found;
}

const ssmFiles = [];
for (const base of [path.resolve(robotRoot, "etc/jibo-ssm"), path.resolve(robotRoot, "usr/local/etc/jibo-ssm")]) {
  if (!fs.existsSync(base)) continue;
  for (const name of fs.readdirSync(base)) {
    const candidate = path.resolve(base, name);
    if (fs.statSync(candidate).isFile() && name.endsWith(".json")) {
      ssmFiles.push(candidate);
    }
  }
}

const regionConfigFiles = collectExisting([
  "usr/lib/node_modules/@jibo/jibo-server-client/lib/region_config.json",
  "usr/lib/node/@jibo/jibo-server-client/lib/region_config.json",
  "usr/lib/node_modules/@jibo/jibo-ota-updater/node_modules/@jibo/jibo-server-client/lib/region_config.json",
  "usr/lib/node/@jibo/jibo-ota-updater/node_modules/@jibo/jibo-server-client/lib/region_config.json",
  "usr/lib/node_modules/@jibo/jibo-log-client/node_modules/@jibo/jibo-server-client/lib/region_config.json",
  "usr/lib/node/@jibo/jibo-log-client/node_modules/@jibo/jibo-server-client/lib/region_config.json",
  "usr/local/bin/jibo-ssm/node_modules/@jibo/jibo-server-client/lib/region_config.json",
  "opt/jibo/Jibo/Skills/@be/be/node_modules/@jibo/jibo-server-client/lib/region_config.json",
  "opt/jibo/Jibo/Skills/oobe-config/node_modules/@jibo/jibo-server-client/lib/region_config.json",
]);

const awsSdkAllFiles = collectExisting([
  "usr/lib/node_modules/@jibo/jibo-server-client/dist/aws-sdk-all.js",
  "usr/lib/node/@jibo/jibo-server-client/dist/aws-sdk-all.js",
  "usr/lib/node_modules/@jibo/jibo-ota-updater/node_modules/@jibo/jibo-server-client/dist/aws-sdk-all.js",
  "usr/lib/node/@jibo/jibo-ota-updater/node_modules/@jibo/jibo-server-client/dist/aws-sdk-all.js",
  "usr/lib/node_modules/@jibo/jibo-log-client/node_modules/@jibo/jibo-server-client/dist/aws-sdk-all.js",
  "usr/lib/node/@jibo/jibo-log-client/node_modules/@jibo/jibo-server-client/dist/aws-sdk-all.js",
  "usr/local/bin/jibo-ssm/node_modules/@jibo/jibo-server-client/dist/aws-sdk-all.js",
]);

const jetstream = readJsonFile(jetstreamPath);
const serverService = readJsonFile(serverServicePath);
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
if (!serverServicePath) recommendations.push("Add or mount jibo-server-service.json before conversion so the notification socket suffix can be staged.");
if (!credentialsPath) recommendations.push("Locate credentials.json before attempting any mode switch.");
if (!oobeConfigPath) recommendations.push("Confirm the oobe-config bundle before wiring first-boot behavior.");
if (!region) recommendations.push("Region is not set yet; that needs to be recorded before any write helper runs.");

const audit = {
  RobotRoot: robotRoot,
  Files: {
    Jetstream: jetstreamPath,
    ServerService: serverServicePath,
    Credentials: credentialsPath,
    OobeConfig: oobeConfigPath,
    SsmCount: ssmFiles.length,
    RegionConfigCount: regionConfigFiles.length,
    AwsSdkAllCount: awsSdkAllFiles.length,
  },
  Credentials: {
    Region: region || null,
    AccessKeyIdPresent: Boolean(accessKeyId && String(accessKeyId).trim()),
    SecretAccessKeyPresent: Boolean(secretAccessKey && String(secretAccessKey).trim()),
  },
  Jetstream: {
    RegionNames: jetstreamRegionNames,
  },
  ServerService: {
    NotificationSubsystemSuffix: getField(getField(serverService, "NotificationSubsystem"), "serverURLSuffix") || null,
  },
  Oobe: {
    ServerRegion: getField(oobeConfig, "serverRegion") || null,
    OtaFilter: getField(oobeConfig, "otaFilter") || null,
  },
  NodeBundles: {
    RegionConfigFiles: regionConfigFiles,
    AwsSdkAllFiles: awsSdkAllFiles,
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
  ensureDir(path.dirname(resolvedOutput));
  fs.writeFileSync(resolvedOutput, json);
  console.log(`Saved conversion audit to ${resolvedOutput}`);
} else {
  console.log(json);
}
NODE
node "$tmp_js" "$robot_root" "$output_path" "$strict"
node "$tmp_js" "$robot_root" "$output_path" "$strict"
