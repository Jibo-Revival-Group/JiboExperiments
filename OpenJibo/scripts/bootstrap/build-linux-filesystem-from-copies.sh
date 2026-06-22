#!/usr/bin/env bash
set -euo pipefail

source_root=""
output_root=""
clean=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --source-root)
      source_root="${2:-}"
      shift 2
      ;;
    --output-root)
      output_root="${2:-}"
      shift 2
      ;;
    --clean)
      clean=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ -z "$source_root" || -z "$output_root" ]]; then
  echo "--source-root and --output-root are required" >&2
  exit 2
fi

node - "$source_root" "$output_root" "$clean" <<'NODE'
const fs = require("fs");
const path = require("path");

const sourceRoot = path.resolve(process.argv[2]);
const outputRoot = path.resolve(process.argv[3]);
const clean = String(process.argv[4]).toLowerCase() === "true";

function ensureSource(relativePath) {
  const candidate = path.resolve(sourceRoot, relativePath);
  if (!fs.existsSync(candidate)) {
    throw new Error(`Missing source path: ${relativePath}`);
  }
  return candidate;
}

function copyFile(src, dest) {
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.copyFileSync(src, dest);
}

function copyDirectory(src, dest) {
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.cpSync(src, dest, { recursive: true, force: true, preserveTimestamps: true });
}

if (clean && fs.existsSync(outputRoot)) {
  fs.rmSync(outputRoot, { recursive: true, force: true });
}
fs.mkdirSync(outputRoot, { recursive: true });

const layout = [
  { source: "0.rootfsA", target: "rootfsA", type: "directory" },
  { source: "1.rootfsB", target: "rootfsB", type: "directory" },
  { source: "3.services", target: "mounted/usr/local", type: "directory" },
  { source: "4.var", target: "mounted/var", type: "directory" },
  { source: "5.skills", target: "mounted/opt/jibo/Jibo/Skills", type: "directory" },
];

const mountedPaths = [
  { target: "mounted/usr/local", mountPoint: "usr/local" },
  { target: "mounted/var", mountPoint: "var" },
  { target: "mounted/opt/jibo/Jibo/Skills", mountPoint: "opt/jibo/Jibo/Skills" },
];

const copied = [];
const progressPath = path.resolve(outputRoot, "filesystem-progress.json");

function writeProgress(stage, extra = {}) {
  const payload = {
    SourceRoot: sourceRoot,
    OutputRoot: outputRoot,
    Stage: stage,
    CopiedCount: copied.length,
    ...extra,
  };
  fs.writeFileSync(progressPath, JSON.stringify(payload, null, 2));
}

writeProgress("start");
for (const item of layout) {
  const sourcePath = ensureSource(item.source);
  const targetPath = path.resolve(outputRoot, item.target);
  if (item.type === "directory") {
    copyDirectory(sourcePath, targetPath);
  } else {
    copyFile(sourcePath, targetPath);
  }
  copied.push({ source: sourcePath, target: targetPath, type: item.type });
  writeProgress(`copied:${item.target}`, { LastCopied: item.target });
}

const rootfsA = path.resolve(outputRoot, "rootfsA");
const mounted = path.resolve(outputRoot, "mounted");
for (const item of mountedPaths) {
  const mountSource = path.resolve(mounted, item.target);
  const mountTarget = path.resolve(rootfsA, item.mountPoint);
  if (!fs.existsSync(mountSource)) {
    throw new Error(`Mounted source missing after copy: ${item.target}`);
  }
  copyDirectory(mountSource, mountTarget);
  copied.push({ source: mountSource, target: mountTarget, type: "mount-overlay" });
  writeProgress(`overlay:${item.mountPoint}`, { LastOverlay: item.mountPoint });
}

const manifest = {
  SourceRoot: sourceRoot,
  OutputRoot: outputRoot,
  Clean: clean,
  RootFs: path.resolve(outputRoot, "rootfsA"),
  SecondaryRootFs: path.resolve(outputRoot, "rootfsB"),
  MountedOverlayRoot: mounted,
  Notes: [
    "rootfsA is treated as the primary Linux filesystem image",
    "rootfsB is preserved as the secondary OTA slot reference",
    "services, var, and skills are copied into mounted overlay folders and then merged over rootfsA",
  ],
  CopiedItems: copied,
};

const manifestPath = path.resolve(outputRoot, "filesystem-manifest.json");
fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2));
writeProgress("complete", { ManifestPath: manifestPath });
console.log(JSON.stringify(manifest, null, 2));
NODE
