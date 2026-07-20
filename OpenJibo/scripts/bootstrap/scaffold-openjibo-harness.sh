#!/usr/bin/env bash
set -euo pipefail

source_root=""
overlay_root=""
output_path=""
clean=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --source-root)
      source_root="${2:-}"
      shift 2
      ;;
    --overlay-root)
      overlay_root="${2:-}"
      shift 2
      ;;
    --output-path)
      output_path="${2:-}"
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

if [[ -z "$source_root" || -z "$overlay_root" ]]; then
  echo "--source-root and --overlay-root are required" >&2
  exit 2
fi

node - "$source_root" "$overlay_root" "$output_path" "$clean" <<'NODE'
const fs = require("fs");
const path = require("path");

const sourceRoot = path.resolve(process.argv[2]);
const overlayRoot = path.resolve(process.argv[3]);
const outputPath = (process.argv[4] || "").trim();
const clean = String(process.argv[5]).toLowerCase() === "true";

if (!fs.existsSync(sourceRoot)) {
  throw new Error(`Source root not found: ${sourceRoot}`);
}

const requiredItems = [
  {
    source: "4.var/jibo/credentials.json",
    target: "var/jibo/credentials.json",
    type: "file",
  },
  {
    source: "4.var/jibo/identity.json",
    target: "var/jibo/identity.json",
    type: "file",
  },
  {
    source: "4.var/jibo/mode.json",
    target: "var/jibo/mode.json",
    type: "file",
  },
  {
    source: "4.var/jibo/keys",
    target: "var/jibo/keys",
    type: "directory",
  },
  {
    source: "3.services/etc/jibo-jetstream-service.json",
    target: "usr/local/etc/jibo-jetstream-service.json",
    type: "file",
  },
  {
    source: "3.services/etc/jibo-system-manager.json",
    target: "usr/local/etc/jibo-system-manager.json",
    type: "file",
  },
  {
    source: "3.services/etc/jibo-server-service.json",
    target: "usr/local/etc/jibo-server-service.json",
    type: "file",
  },
  {
    source: "3.services/lib/libJiboServerService.so",
    target: "usr/local/lib/libJiboServerService.so",
    type: "file",
  },
  {
    source: "3.services/bin/jibo-ssm/lib/skills-service-manager.js",
    target: "usr/local/bin/jibo-ssm/lib/skills-service-manager.js",
    type: "file",
  },
  {
    source: "3.services/bin/jibo-ssm/lib/skills-service-manager.js.map",
    target: "usr/local/bin/jibo-ssm/lib/skills-service-manager.js.map",
    type: "file",
  },
  {
    source: "3.services/etc/jibo-ssm",
    target: "usr/local/etc/jibo-ssm",
    type: "directory",
  },
  {
    source: "5.skills/jibo/Jibo/Skills/oobe-config/config.json",
    target: "skills/jibo/Jibo/Skills/oobe-config/config.json",
    type: "file",
  },
  {
    source: "5.skills/jibo/Jibo/Skills/oobe-config/oobe-config.js",
    target: "skills/jibo/Jibo/Skills/oobe-config/oobe-config.js",
    type: "file",
  },
  {
    source: "5.skills/jibo/Jibo/Skills/@be/be/config/be-normal.json",
    target: "skills/jibo/Jibo/Skills/@be/be/config/be-normal.json",
    type: "file",
  },
  {
    source: "5.skills/jibo/Jibo/Skills/@be/be/config/be-oobe.json",
    target: "skills/jibo/Jibo/Skills/@be/be/config/be-oobe.json",
    type: "file",
  },
  {
    source: "5.skills/jibo/Jibo/Skills/@be/be/config/be-developer.json",
    target: "skills/jibo/Jibo/Skills/@be/be/config/be-developer.json",
    type: "file",
  },
  {
    source: "5.skills/jibo/Jibo/Skills/@be/be/config/be-int-developer.json",
    target: "skills/jibo/Jibo/Skills/@be/be/config/be-int-developer.json",
    type: "file",
  },
  {
    source: "0.rootfsA/boot/extlinux/extlinux.conf",
    target: "boot/extlinux/extlinux.conf",
    type: "file",
  },
  {
    source: "0.rootfsA/etc/fstab",
    target: "etc/fstab",
    type: "file",
  },
  {
    source: "0.rootfsA/etc/inittab",
    target: "etc/inittab",
    type: "file",
  },
];

const copied = [];
const missing = [];

function copyFile(src, dest) {
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.copyFileSync(src, dest);
  copied.push({ source: src, target: dest, type: "file" });
}

function copyDirectory(src, dest) {
  fs.cpSync(src, dest, { recursive: true, force: true, preserveTimestamps: true });
  copied.push({ source: src, target: dest, type: "directory" });
}

if (clean && fs.existsSync(overlayRoot)) {
  fs.rmSync(overlayRoot, { recursive: true, force: true });
}
fs.mkdirSync(overlayRoot, { recursive: true });

for (const item of requiredItems) {
  const sourcePath = path.resolve(sourceRoot, item.source);
  const targetPath = path.resolve(overlayRoot, item.target);
  if (!fs.existsSync(sourcePath)) {
    missing.push(item.source);
    continue;
  }
  const stat = fs.statSync(sourcePath);
  if (item.type === "directory" || stat.isDirectory()) {
    copyDirectory(sourcePath, targetPath);
  } else {
    copyFile(sourcePath, targetPath);
  }
}

if (missing.length > 0) {
  throw new Error(`Source tree is missing required harness inputs: ${missing.join(", ")}`);
}

const manifest = {
  SourceRoot: sourceRoot,
  OverlayRoot: overlayRoot,
  Clean: clean,
  CopiedCount: copied.length,
  CopiedItems: copied,
  NormalizedLayout: true,
};

const json = JSON.stringify(manifest, null, 2);
if (outputPath) {
  const resolvedOutput = path.resolve(outputPath);
  fs.mkdirSync(path.dirname(resolvedOutput), { recursive: true });
  fs.writeFileSync(resolvedOutput, json);
  console.log(`Saved harness scaffold to ${resolvedOutput}`);
} else {
  console.log(json);
}
NODE
