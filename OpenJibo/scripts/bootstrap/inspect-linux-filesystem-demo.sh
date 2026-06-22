#!/usr/bin/env bash
set -euo pipefail

output_root=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output-root)
      output_root="${2:-}"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ -z "$output_root" ]]; then
  echo "--output-root is required" >&2
  exit 2
fi

node - "$output_root" <<'NODE'
const fs = require("fs");
const path = require("path");

const outputRoot = path.resolve(process.argv[2]);
const manifestPath = path.resolve(outputRoot, "filesystem-manifest.json");
const progressPath = path.resolve(outputRoot, "filesystem-progress.json");
const demoRoot = path.resolve(outputRoot, "demo-root");

function readJson(filePath) {
  if (!fs.existsSync(filePath)) return null;
  return JSON.parse(fs.readFileSync(filePath, "utf8"));
}

function existsInfo(filePath) {
  if (!fs.existsSync(filePath)) {
    return { exists: false };
  }
  const stat = fs.lstatSync(filePath);
  return {
    exists: true,
    isFile: stat.isFile(),
    isDirectory: stat.isDirectory(),
    isSymbolicLink: stat.isSymbolicLink(),
    size: stat.size,
  };
}

const manifest = readJson(manifestPath);
const progress = readJson(progressPath);

const report = {
  OutputRoot: outputRoot,
  Manifest: existsInfo(manifestPath),
  Progress: existsInfo(progressPath),
  DemoRoot: existsInfo(demoRoot),
  DemoRootTarget: manifest ? manifest.DemoRoot || null : null,
  RootFs: manifest ? manifest.RootFs || null : null,
  SecondaryRootFs: manifest ? manifest.SecondaryRootFs || null : null,
  MountedOverlayRoot: manifest ? manifest.MountedOverlayRoot || null : null,
  Stage: progress ? progress.Stage || null : null,
  CopiedCount: progress ? progress.CopiedCount || null : null,
};

console.log(JSON.stringify(report, null, 2));
NODE
