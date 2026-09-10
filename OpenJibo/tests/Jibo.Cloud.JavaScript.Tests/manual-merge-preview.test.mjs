import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createContext, runInContext } from "node:vm";
import test from "node:test";

const source = readFileSync(new URL("../../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/wwwroot/portal/status/status.js", import.meta.url), "utf8")
  .replace(/^bootstrap\(\);\s*$/m, "");

test("manual merge submits the signed preview token that was reviewed", async () => {
  const context = createContext({
    document: { getElementById: id => id === "artifactMergeSource" ? { value: "duplicate" } : null },
    window: { confirm: () => true },
  });
  runInContext(source, context);
  runInContext(`
    activeLogViewer = { deviceId: "canonical", robotName: "Canonical" };
    const requests = [];
    apiFetch = (...args) => {
      requests.push(args);
      return Promise.resolve(requests.length === 1
        ? { sessionCount: 1, credentialBindingCount: 2, artifactCount: 3, previewToken: "signed-preview" }
        : { migratedArtifacts: 3, skippedArtifacts: [], partial: false });
    };
    refreshStatus = () => Promise.resolve();
    openRobotArtifacts = () => Promise.resolve();
    renderStatusView = () => {};
  `, context);

  await runInContext("mergeRobotFromArtifactViewer()", context);

  assert.equal(runInContext("requests.length", context), 2);
  const body = JSON.parse(runInContext("requests[1][1].body", context));
  assert.deepEqual(body, { targetDeviceId: "canonical", previewToken: "signed-preview" });
});

test("live session linking carries cross-replica observed identity evidence", async () => {
  const select = {
    value: "canonical",
    dataset: {
      observedDeviceId: "observed-runtime-id",
      lastSeenUtc: "2026-09-10T15:00:00Z",
    },
  };
  const context = createContext({
    document: { getElementById: () => null, querySelector: () => select },
    CSS: { escape: value => value },
    window: { confirm: () => true },
  });
  runInContext(source, context);
  runInContext(`
    const requests = [];
    apiFetch = (...args) => {
      requests.push(args);
      return Promise.resolve({ ok: true });
    };
    refreshStatus = () => Promise.resolve();
  `, context);

  await runInContext("linkLiveSession('session-on-peer-replica')", context);

  const body = JSON.parse(runInContext("requests[0][1].body", context));
  assert.deepEqual(body, {
    deviceId: "canonical",
    observedDeviceId: "observed-runtime-id",
    lastSeenUtc: "2026-09-10T15:00:00Z",
  });
});
