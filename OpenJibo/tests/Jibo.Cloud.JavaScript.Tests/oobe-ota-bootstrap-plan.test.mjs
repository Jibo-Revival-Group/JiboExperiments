import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repositoryRoot = resolve(fileURLToPath(new URL("../..", import.meta.url)));
const plannerPath = join(repositoryRoot, "scripts", "bootstrap", "plan-oobe-ota-bootstrap.sh");

function findBash() {
  const candidates = process.platform === "win32"
    ? [join(process.env.ProgramFiles || "C:\\Program Files", "Git", "bin", "bash.exe"), "bash"]
    : ["bash"];
  return candidates.find(candidate => {
    const result = spawnSync(candidate, ["--version"], { encoding: "utf8", windowsHide: true });
    return !result.error && result.status === 0;
  }) ?? null;
}

const bashPath = findBash();
const bashUnavailable = bashPath ? false : "Bash is required to run the OOBE OTA planner test.";

function scriptArgument(value) {
  if (process.platform !== "win32") return value;
  const normalized = value.replaceAll("\\", "/");
  const drivePath = normalized.match(/^([A-Za-z]):\/(.*)$/);
  return drivePath ? `/${drivePath[1].toLowerCase()}/${drivePath[2]}` : normalized;
}

function runPlanner(args) {
  return spawnSync(bashPath, [scriptArgument(plannerPath), ...args], {
    encoding: "utf8",
    windowsHide: true,
    env: { ...process.env, MSYS_NO_PATHCONV: "1" },
  });
}

test("OOBE OTA plan stays blocked by default and cannot be strict-approved by an existing path", {
  skip: bashUnavailable,
}, () => {
  const directory = mkdtempSync(join(tmpdir(), "openjibo-oobe-ota-plan-"));
  try {
    const arbitraryFile = join(directory, "arbitrary.txt");
    writeFileSync(arbitraryFile, "exists, but is not verified OTA evidence");

    const defaultResult = runPlanner([]);
    assert.equal(defaultResult.status, 0, defaultResult.stderr);
    const defaultPlan = JSON.parse(defaultResult.stdout);
    assert.equal(defaultPlan.PlanningOnly, true);
    assert.equal(defaultPlan.ExecutionScope,
      "offline planning only; no network or robot operations performed");
    assert.equal(defaultPlan.CanOfferUpdates, false);
    assert.equal(defaultPlan.CanProceed, false);
    assert.ok(defaultPlan.Blockers.includes("stock-ota-contract-unverified"));
    assert.ok(defaultPlan.Blockers.includes("lab-install-and-recovery-unverified"));
    assert.equal(defaultPlan.BootstrapServices.OtaMetadata.contractStatus, "unverified");
    assert.equal(defaultPlan.BootstrapServices.OtaMetadata.packageFormat, "unverified");

    const tracePathPlanResult = runPlanner(["--trace-bundle", arbitraryFile]);
    assert.equal(tracePathPlanResult.status, 0, tracePathPlanResult.stderr);
    const tracePathPlan = JSON.parse(tracePathPlanResult.stdout);
    assert.equal(tracePathPlan.CanProceed, false);
    assert.ok(!tracePathPlan.Blockers.includes("trace-bundle-not-found"));
    assert.ok(tracePathPlan.Warnings.some(warning => warning.includes("path-exists-only")));

    const strictResult = runPlanner(["--strict", "--trace-bundle", arbitraryFile]);
    assert.notEqual(strictResult.status, 0);
    assert.match(strictResult.stderr, /OOBE OTA bootstrap plan is blocked/);
    assert.match(strictResult.stderr, /stock-ota-contract-unverified/);
    assert.match(strictResult.stderr, /publisher-trust-and-digest-policy-unverified/);

    const arbitraryDirectory = join(directory, "arbitrary-trace-directory");
    mkdirSync(arbitraryDirectory);
    const strictDirectoryResult = runPlanner(["--strict", "--trace-bundle", arbitraryDirectory]);
    assert.notEqual(strictDirectoryResult.status, 0);
    assert.match(strictDirectoryResult.stderr, /OOBE OTA bootstrap plan is blocked/);
    assert.match(strictDirectoryResult.stderr, /stock-ota-contract-unverified/);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
