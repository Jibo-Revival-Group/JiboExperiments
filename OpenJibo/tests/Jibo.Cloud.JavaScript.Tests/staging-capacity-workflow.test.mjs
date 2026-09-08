import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";

const workflow = fs.readFileSync(
  new URL("../../../.github/workflows/openjibo-staging-capacity-sweep.yml", import.meta.url),
  "utf8");

test("staging capacity sweep is bounded, serial, and staging-only", () => {
  assert.match(workflow, /environment:\s*\n\s+name: openjibo-staging/);
  assert.match(workflow, /OPENJIBO_RESOURCE_GROUP\" != \"rg-openjibo-staging/);
  assert.match(workflow, /for tier in 6 10 15 20/);
  assert.match(workflow, /OpenJibo__ReleaseSmoke__MaxConcurrentDevices=20/);
  assert.match(workflow, /TEST_ROBOT_ID: open-jibo-smoke-staging/);
  assert.match(workflow, /syntheticIdentityUpperBound.*21/);
  assert.match(workflow, /cancel-in-progress: false/);
  assert.match(workflow, /TURN_ROUNDS: \$\{\{ inputs\.turn_rounds \}\}/);
  assert.match(workflow, /RELEASE_SMOKE_BOOTSTRAP_CONCURRENCY: "4"/);
  assert.match(workflow, /Staging must begin with release-smoke disabled/);
  assert.match(workflow, /openjibo-cloud\.\*\.azurecontainerapps\.io/);
  assert.match(workflow, /RunningAtMaxScale/);
  assert.doesNotMatch(workflow, /deploy-openjibo-managed|clone-openjibo-managed-databases|--run-migration/);
  assert.doesNotMatch(workflow, /docker\s+(?:build|push)|az\s+acr\s+build|az\s+deployment\s/);
  assert.doesNotMatch(workflow, /az\s+containerapp\s+revision\s+restart/);
});

test("staging capacity sweep retains evidence and always restores authorization and scale", () => {
  const configure = workflow.indexOf("- name: Enable bounded two-replica sweep");
  const run = workflow.indexOf("- name: Run serial 6, 10, 15, and 20 robot tiers");
  const telemetryRefresh = workflow.indexOf("- name: Refresh Azure login for telemetry");
  const report = workflow.indexOf("- name: Capture and enforce exact-revision telemetry");
  const cleanupRefresh = workflow.indexOf("- name: Refresh Azure login for cleanup");
  const cleanup = workflow.indexOf("- name: Disable temporary release smoke authorization");
  const restore = workflow.indexOf("- name: Restore staging scale");
  const verify = workflow.indexOf("- name: Verify staging invariants after cleanup");
  const upload = workflow.indexOf("- name: Upload staging capacity evidence");

  assert.ok(configure >= 0 && configure < run);
  assert.ok(run < telemetryRefresh && telemetryRefresh < report);
  assert.ok(report < cleanupRefresh && cleanupRefresh < cleanup);
  assert.ok(cleanup < restore && restore < verify && verify < upload);
  assert.equal((workflow.match(/uses: azure\/login@v3/g) ?? []).length, 3);
  assert.match(
    workflow,
    /- name: Refresh Azure login for cleanup\s+if: always\(\) && steps\.baseline\.outputs\.app_name != ''\s+uses: azure\/login@v3/s,
  );
  assert.match(workflow, /if: always\(\).*steps\.baseline\.outputs\.app_name/);
  assert.match(workflow, /cleanup-release-smoke-authorization\.sh/);
  assert.match(workflow, /tier-\$\{tier\}\.json/);
  assert.match(workflow, /tier-\$\{tier\}-window\.json/);
  assert.match(workflow, /aggregate-report\.json/);
  assert.match(workflow, /openjibo-capacity-tier-evidence\.mjs/);
  assert.match(workflow, /tier-database-evidence\.json/);
  assert.match(workflow, /tier-telemetry-incomplete/);
  assert.match(workflow, /az extension add --name application-insights --yes/);
  assert.match(workflow, /telemetry-not-yet-ingested[\s\S]*?exit 1/);
  assert.match(workflow, /report_revision.*steps\.probe\.outputs\.revision/s);
  assert.match(workflow, /evidence\.blockers\.includes\("reliability-signal-detected"\)/);
  assert.match(workflow, /touch .*reliability-signal-detected/);
  assert.match(workflow, /current_image.*steps\.baseline\.outputs\.image/s);
  assert.match(workflow, /stored_smoke_secrets.*!= \"0\"/s);
  assert.match(workflow, /actions\/upload-artifact@v4/);
});
