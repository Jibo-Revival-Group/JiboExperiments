#!/usr/bin/env bash
set -euo pipefail

robot_root=""
plan_path=""
target_mode="open-jibo"
api_hostname="api.openjibo.com"
hub_hostname=""
output_path=""
strict=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --robot-root)
      robot_root="${2:-}"
      shift 2
      ;;
    --plan-path)
      plan_path="${2:-}"
      shift 2
      ;;
    --target-mode)
      target_mode="${2:-open-jibo}"
      shift 2
      ;;
    --api-hostname)
      api_hostname="${2:-api.openjibo.com}"
      shift 2
      ;;
    --hub-hostname)
      hub_hostname="${2:-}"
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

if [[ -z "$robot_root" || -z "$plan_path" ]]; then
  echo "--robot-root and --plan-path are required" >&2
  exit 2
fi

node - "$robot_root" "$plan_path" "$target_mode" "$api_hostname" "$hub_hostname" "$output_path" "$strict" <<'NODE'
const fs = require("fs");
const path = require("path");

const robotRoot = path.resolve(process.argv[2]);
const planPath = path.resolve(process.argv[3]);
const targetMode = process.argv[4];
const apiHostname = (process.argv[5] || "api.openjibo.com").trim() || "api.openjibo.com";
const hubHostname = (process.argv[6] || "").trim() || apiHostname;
const outputPath = (process.argv[7] || "").trim();
const strict = String(process.argv[8]).toLowerCase() === "true";

if (!fs.existsSync(planPath)) {
  throw new Error(`Plan file not found at ${planPath}`);
}

const plan = JSON.parse(fs.readFileSync(planPath, "utf8"));
if (strict && !plan.CanApply) {
  const issues = ((plan.AuditSummary && plan.AuditSummary.Recommendations) || []).filter(item => String(item).trim().length > 0);
  throw new Error(`Conversion apply is not safe to run: ${issues.join("; ")}`);
}

const applyOutputDirectory = outputPath
  ? path.dirname(path.resolve(outputPath))
  : path.dirname(planPath);
const backupRoot = path.resolve(applyOutputDirectory, "backups");
fs.mkdirSync(backupRoot, { recursive: true });

function candidatePaths(relativePaths) {
  return relativePaths.map(relativePath => path.resolve(robotRoot, relativePath));
}

function firstExisting(candidates) {
  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) return candidate;
  }
  return null;
}

function backupFile(filePath) {
  if (!filePath || !fs.existsSync(filePath)) return null;
  const relative = path.relative(robotRoot, filePath);
  const backupPath = path.resolve(backupRoot, relative);
  fs.mkdirSync(path.dirname(backupPath), { recursive: true });
  fs.copyFileSync(filePath, backupPath);
  return backupPath;
}

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, "utf8"));
}

function writeJson(filePath, value) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, JSON.stringify(value, null, 2));
}

const jetstreamPath = firstExisting(candidatePaths([
  "usr/local/etc/jibo-jetstream-service.json",
  "etc/jibo-jetstream-service.json",
]));
const credentialsPath = firstExisting(candidatePaths([
  "var/jibo/credentials.json",
]));
const oobeConfigPath = firstExisting(candidatePaths([
  "skills/jibo/Jibo/Skills/oobe-config/config.json",
  "opt/jibo/Jibo/Skills/oobe-config/config.json",
]));

if (!jetstreamPath || !credentialsPath || !oobeConfigPath) {
  throw new Error("Expected conversion files were not found on the robot root.");
}

const backups = {
  jetstream: backupFile(jetstreamPath),
  credentials: backupFile(credentialsPath),
  oobe: backupFile(oobeConfigPath),
};

const jetstream = readJson(jetstreamPath);
const currentRegion = readJson(credentialsPath).region || "api";
const oobe = readJson(oobeConfigPath);

jetstream["region-settings"] = jetstream["region-settings"] || jetstream.regions || {};
jetstream.regions = jetstream.regions || jetstream["region-settings"];

const baseRegion = jetstream["region-settings"].api || jetstream.regions.api || {
  hub_port: 443,
  entrypoint_port: 443,
};
const openJiboRegion = {
  ...baseRegion,
  hub_port: baseRegion.hub_port || 443,
  entrypoint_port: baseRegion.entrypoint_port || 443,
  hub_hostname: hubHostname,
  entrypoint_hostname: apiHostname,
};

jetstream["region-settings"]["open-jibo"] = openJiboRegion;
jetstream.regions["open-jibo"] = openJiboRegion;

oobe.serverRegion = oobe.serverRegion || currentRegion || "api";
oobe.otaFilter = oobe.otaFilter || "eau";
oobe.openJiboConversion = {
  enabled: true,
  targetMode,
  state: "pending",
  apiHostname,
  hubHostname,
  createdUtc: new Date().toISOString(),
  backupRoot,
};

writeJson(jetstreamPath, jetstream);
writeJson(oobeConfigPath, oobe);

const conversionMarkerPath = path.resolve(robotRoot, "var/jibo/identity/openjibo-conversion.json");
writeJson(conversionMarkerPath, {
  targetMode,
  state: "pending",
  sourceRegion: currentRegion,
  apiHostname,
  hubHostname,
  createdUtc: new Date().toISOString(),
  backups,
  robotRoot,
});

const applyManifest = {
  RobotRoot: robotRoot,
  TargetMode: targetMode,
  SourcePlan: planPath,
  CanApply: Boolean(plan.CanApply),
  ApiHostname: apiHostname,
  HubHostname: hubHostname,
  Backups: plan.Backups || [],
  BackupRoot: backupRoot,
  CreatedBackups: backups,
  ProposedChanges: plan.ProposedChanges || [],
  RollbackPlan: plan.RollbackPlan || [],
  Notes: [
    "This helper writes the minimal staged conversion state after taking backups.",
    "The active credentials region remains on the proven value until first-boot conversion completes.",
    "The staged open-jibo region points to the canonical Open Jibo API hostname.",
  ],
  WrittenFiles: [
    jetstreamPath,
    oobeConfigPath,
    conversionMarkerPath,
  ],
};

const json = JSON.stringify(applyManifest, null, 2);
if (outputPath) {
  const resolvedOutput = path.resolve(outputPath);
  fs.mkdirSync(path.dirname(resolvedOutput), { recursive: true });
  fs.writeFileSync(resolvedOutput, json);
  console.log(`Saved conversion apply manifest to ${resolvedOutput}`);
} else {
  console.log(json);
}
NODE