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

assertFile(summaryPath, "Round-trip summary");
const summary = JSON.parse(fs.readFileSync(summaryPath, "utf8"));

assertFile(summary.ScaffoldPath, "Scaffold summary");
assertExists(summary.RunOutputDirectory, "Run output directory");
assertFile(path.resolve(summary.RunOutputDirectory, "harness-summary.json"), "Run summary");
assertFile(path.resolve(summary.RunOutputDirectory, "invoke", "conversion-audit.json"), "Conversion audit");
assertFile(path.resolve(summary.RunOutputDirectory, "invoke", "conversion-plan.json"), "Conversion plan");
assertFile(summary.RollbackPath, "Rollback summary");

const report = {
  OutputDirectory: outputDirectory,
  SummaryPath: summaryPath,
  Validated: true,
};

console.log(JSON.stringify(report, null, 2));
NODE
