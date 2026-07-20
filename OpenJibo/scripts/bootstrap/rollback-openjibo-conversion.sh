#!/bin/sh
set -eu

robot_root=""
apply_path=""
output_path=""
strict=false

while [ $# -gt 0 ]; do
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

if [ -z "$robot_root" ] || [ -z "$apply_path" ]; then
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
const writtenFileRoles = manifest.WrittenFileRoles || {};
const backupRoot = manifest.BackupRoot ? path.resolve(manifest.BackupRoot) : null;

function ensureDir(dirPath) {
  if (!dirPath || fs.existsSync(dirPath)) return;
  ensureDir(path.dirname(dirPath));
  try {
    fs.mkdirSync(dirPath);
  } catch (error) {
    if (!fs.existsSync(dirPath)) throw error;
  }
}

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
  ensureDir(path.dirname(targetPath));
  fs.writeFileSync(targetPath, fs.readFileSync(backupPath));
  return true;
}

function collectBackupPaths(value, found) {
  if (!value) return;
  if (typeof value === "string") {
    found.push(value);
    return;
  }
  if (Array.isArray(value)) {
    for (const item of value) collectBackupPaths(item, found);
    return;
  }
  if (typeof value === "object") {
    for (const key of Object.keys(value)) collectBackupPaths(value[key], found);
  }
}

const restored = [];
const backupPaths = [];
collectBackupPaths(createdBackups, backupPaths);
for (const backupPath of backupPaths) {
  if (!backupRoot) {
    if (strict) throw new Error("Apply manifest does not contain BackupRoot.");
    continue;
  }
  const relative = path.relative(backupRoot, path.resolve(backupPath));
  if (!relative || relative.indexOf("..") === 0 || path.isAbsolute(relative)) {
    if (strict) throw new Error(`Backup path is outside BackupRoot: ${backupPath}`);
    continue;
  }
  const targetPath = path.resolve(robotRoot, relative);
  if (restoreFromBackup(backupPath, targetPath)) restored.push(targetPath);
}

const markerTarget = writtenFileRoles.ConversionMarker || path.resolve(robotRoot, "var/jibo/identity/openjibo-conversion.json");
const markerExisted = fs.existsSync(markerTarget);
if (fs.existsSync(markerTarget)) {
  fs.unlinkSync(markerTarget);
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
  ensureDir(path.dirname(resolvedOutput));
  fs.writeFileSync(resolvedOutput, json);
  console.log(`Saved conversion rollback manifest to ${resolvedOutput}`);
} else {
  console.log(json);
}
NODE
