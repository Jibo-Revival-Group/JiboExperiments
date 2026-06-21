#!/usr/bin/env bash
set -euo pipefail

robot_root=""
apply_path=""
output_path=""
strict=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --robot-root)
      robot_root="${2:-}"
      shift 2
      ;;
    --apply-path)
      apply_path="${2:-}"
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

if [[ -z "$robot_root" || -z "$apply_path" ]]; then
  echo "--robot-root and --apply-path are required" >&2
  exit 2
fi

node - "$robot_root" "$apply_path" "$output_path" "$strict" <<'NODE'
const fs = require("fs");
const path = require("path");

const robotRoot = path.resolve(process.argv[2]);
const applyPath = path.resolve(process.argv[3]);
const outputPath = (process.argv[4] || "").trim();
const strict = String(process.argv[5]).toLowerCase() === "true";

if (!fs.existsSync(applyPath)) {
  throw new Error(`Apply manifest not found at ${applyPath}`);
}

const manifest = JSON.parse(fs.readFileSync(applyPath, "utf8"));
const createdBackups = manifest.CreatedBackups || {};
const writtenFiles = Array.isArray(manifest.WrittenFiles) ? manifest.WrittenFiles : [];

function restoreFromBackup(backupPath, targetPath) {
  if (!backupPath) {
    if (strict) {
      throw new Error(`Missing backup for ${targetPath}`);
    }
    return false;
  }
  if (!fs.existsSync(backupPath)) {
    if (strict) {
      throw new Error(`Backup file not found: ${backupPath}`);
    }
    return false;
  }
  fs.mkdirSync(path.dirname(targetPath), { recursive: true });
  fs.copyFileSync(backupPath, targetPath);
  return true;
}

const jetstreamTarget = writtenFiles[0] || path.resolve(robotRoot, "usr/local/etc/jibo-jetstream-service.json");
const oobeTarget = writtenFiles[1] || path.resolve(robotRoot, "skills/jibo/Jibo/Skills/oobe-config/config.json");
const markerTarget = writtenFiles[2] || path.resolve(robotRoot, "var/jibo/identity/openjibo-conversion.json");
const credentialsTarget = path.resolve(robotRoot, "var/jibo/credentials.json");

const restored = [];
if (restoreFromBackup(createdBackups.jetstream, jetstreamTarget)) restored.push(jetstreamTarget);
if (restoreFromBackup(createdBackups.credentials, credentialsTarget)) restored.push(credentialsTarget);
if (restoreFromBackup(createdBackups.oobe, oobeTarget)) restored.push(oobeTarget);

const markerExisted = fs.existsSync(markerTarget);
if (fs.existsSync(markerTarget)) {
  fs.rmSync(markerTarget, { force: true });
}

const rollbackManifest = {
  RobotRoot: robotRoot,
  ApplyPath: applyPath,
  RestoredFiles: restored,
  RemovedFiles: markerExisted ? [markerTarget] : [],
  Notes: [
    "Restored staged conversion files from the apply manifest backups.",
    "Removed the staged conversion marker so the overlay returns to baseline behavior.",
  ],
};

const json = JSON.stringify(rollbackManifest, null, 2);
if (outputPath) {
  const resolvedOutput = path.resolve(outputPath);
  fs.mkdirSync(path.dirname(resolvedOutput), { recursive: true });
  fs.writeFileSync(resolvedOutput, json);
  console.log(`Saved conversion rollback manifest to ${resolvedOutput}`);
} else {
  console.log(json);
}
NODE
