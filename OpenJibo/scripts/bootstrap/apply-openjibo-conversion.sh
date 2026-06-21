#!/usr/bin/env bash
set -euo pipefail

robot_root=""
plan_path=""
target_mode="open-jibo"
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

node - "$robot_root" "$plan_path" "$target_mode" "$output_path" "$strict" <<'NODE'
const fs = require("fs");
const path = require("path");

const robotRoot = path.resolve(process.argv[2]);
const planPath = path.resolve(process.argv[3]);
const targetMode = process.argv[4];
const outputPath = (process.argv[5] || "").trim();
const strict = String(process.argv[6]).toLowerCase() === "true";

if (!fs.existsSync(planPath)) {
  throw new Error(`Plan file not found at ${planPath}`);
}

const plan = JSON.parse(fs.readFileSync(planPath, "utf8"));
if (strict && !plan.CanApply) {
  const issues = ((plan.AuditSummary && plan.AuditSummary.Recommendations) || []).filter(item => String(item).trim().length > 0);
  throw new Error(`Conversion apply is not safe to run: ${issues.join("; ")}`);
}

const applyManifest = {
  RobotRoot: robotRoot,
  TargetMode: targetMode,
  SourcePlan: planPath,
  CanApply: Boolean(plan.CanApply),
  Backups: plan.Backups || [],
  ProposedChanges: plan.ProposedChanges || [],
  RollbackPlan: plan.RollbackPlan || [],
  Notes: [
    "This helper currently records an apply manifest and keeps the actual robot write step gated behind the predictive audit.",
    "It is safe to run on a staged robot root because it does not modify robot files yet.",
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
