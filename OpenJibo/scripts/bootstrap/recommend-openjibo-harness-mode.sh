#!/usr/bin/env bash
set -euo pipefail

goal="demo"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --goal)
      goal="${2:-demo}"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

node - "$goal" <<'NODE'
const goal = String(process.argv[2] || "demo").toLowerCase();

const options = {
  demo: {
    Mode: "Demo-OpenJiboHarness.ps1",
    Why: "Use the copied-volume demo filesystem first. It exercises scaffold -> apply -> rollback -> validation without VM boot complexity.",
  },
  roundtrip: {
    Mode: "Roundtrip-OpenJiboHarness.ps1",
    Why: "Use this when you want a single pass that scaffolds, applies, and rolls back, then validates the output artifacts.",
  },
  vm: {
    Mode: "VM later",
    Why: "Use a VM only when you need boot-time or kernel/runtime fidelity that the copied-volume harness cannot expose.",
  },
};

const selected = options[goal] || options.demo;
console.log(JSON.stringify({
  Goal: goal,
  RecommendedMode: selected.Mode,
  Why: selected.Why,
}, null, 2));
NODE
