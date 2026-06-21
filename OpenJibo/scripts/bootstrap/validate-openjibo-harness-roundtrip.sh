#!/usr/bin/env bash
set -euo pipefail

output_directory=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output-directory)
      output_directory="${2:-}"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ -z "$output_directory" ]]; then
  echo "--output-directory is required" >&2
  exit 2
fi

node - "$output_directory" <<'NODE'
const fs = require("fs");
const path = require("path");
const crypto = require("crypto");

const outputDirectory = path.resolve(process.argv[2]);
const summaryPath = path.resolve(outputDirectory, "harness-roundtrip.json");

function assertExists(filePath, label) {
  if (!fs.existsSync(filePath)) {
    throw new Error(`${label} not found: ${filePath}`);
  }
  return filePath;
}

function assertFile(filePath, label) {
  const stat = fs.statSync(assertExists(filePath, label));
  if (!stat.isFile()) {
    throw new Error(`${label} is not a file: ${filePath}`);
  }
}

function fileHash(filePath) {
  return crypto.createHash("sha256").update(fs.readFileSync(filePath)).digest("hex");
}

function walkFiles(root) {
  const results = [];
  if (!fs.existsSync(root)) return results;
  const stack = [root];
  while (stack.length > 0) {
    const current = stack.pop();
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      const fullPath = path.join(current, entry.name);
      if (entry.isDirectory()) {
        stack.push(fullPath);
      } else if (entry.isFile()) {
        results.push(path.relative(root, fullPath).replace(/\\/g, "/"));
      }
    }
  }
  results.sort();
  return results;
}

assertFile(summaryPath, "Round-trip summary");
const summary = JSON.parse(fs.readFileSync(summaryPath, "utf8"));
const scaffoldSummaryPath = path.resolve(summary.ScaffoldPath);
const scaffold = JSON.parse(fs.readFileSync(assertExists(scaffoldSummaryPath, "Scaffold summary"), "utf8"));

assertFile(summary.ScaffoldPath, "Scaffold summary");
assertExists(summary.RunOutputDirectory, "Run output directory");
assertFile(path.resolve(summary.RunOutputDirectory, "harness-summary.json"), "Run summary");
assertFile(path.resolve(summary.RunOutputDirectory, "invoke", "conversion-audit.json"), "Conversion audit");
assertFile(path.resolve(summary.RunOutputDirectory, "invoke", "conversion-plan.json"), "Conversion plan");
assertFile(summary.RollbackPath, "Rollback summary");

if (summary.RollbackPath !== path.resolve(summary.RollbackPath)) {
  throw new Error("Rollback path normalization failed");
}

const sourceRoot = path.resolve(scaffold.SourceRoot);
const overlayRoot = path.resolve(scaffold.OverlayRoot);
const copiedItems = Array.isArray(scaffold.CopiedItems) ? scaffold.CopiedItems : [];

for (const item of copiedItems) {
  const sourcePath = path.resolve(sourceRoot, item.source.replace(/\\/g, "/"));
  const targetPath = path.resolve(overlayRoot, item.target.replace(/\\/g, "/"));
  const sourceStat = fs.statSync(sourcePath);
  const targetStat = fs.statSync(targetPath);
  if (item.type === "directory" || sourceStat.isDirectory()) {
    const sourceFiles = walkFiles(sourcePath);
    const targetFiles = walkFiles(targetPath);
    if (sourceFiles.length !== targetFiles.length) {
      throw new Error(`Directory file count mismatch for ${item.target}`);
    }
    for (let i = 0; i < sourceFiles.length; i++) {
      if (sourceFiles[i] !== targetFiles[i]) {
        throw new Error(`Directory relative path mismatch for ${item.target}: ${sourceFiles[i]} !== ${targetFiles[i]}`);
      }
      const sourceFile = path.join(sourcePath, sourceFiles[i]);
      const targetFile = path.join(targetPath, targetFiles[i]);
      if (fileHash(sourceFile) !== fileHash(targetFile)) {
        throw new Error(`Directory file hash mismatch for ${item.target}/${sourceFiles[i]}`);
      }
    }
  } else {
    if (!targetStat.isFile()) {
      throw new Error(`Target is not a file: ${targetPath}`);
    }
    if (fileHash(sourcePath) !== fileHash(targetPath)) {
      throw new Error(`File hash mismatch for ${item.target}`);
    }
  }
}

const report = {
  OutputDirectory: outputDirectory,
  SummaryPath: summaryPath,
  VerifiedRestoredHashes: true,
  Validated: true,
};

console.log(JSON.stringify(report, null, 2));
NODE
