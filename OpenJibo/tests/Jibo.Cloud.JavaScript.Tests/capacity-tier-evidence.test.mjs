import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import {
  buildTierEvidence,
  buildTierMetricQuery,
  loadTierWindows,
} from "../../scripts/cloud/openjibo-capacity-tier-evidence.mjs";

function withWindows(callback) {
  const directory = mkdtempSync(join(tmpdir(), "openjibo-tier-evidence-"));
  try {
    writeFileSync(join(directory, "tier-6-window.json"), JSON.stringify({
      tier: 6, startedUtc: "2026-09-07T12:00:00Z", completedUtc: "2026-09-07T12:01:10Z",
    }));
    writeFileSync(join(directory, "tier-10-window.json"), JSON.stringify({
      tier: 10, startedUtc: "2026-09-07T12:01:10Z", completedUtc: "2026-09-07T12:02:20Z",
    }));
    callback(directory);
  } finally { rmSync(directory, { recursive: true, force: true }); }
}

test("tier metric query is exact-revision, bounded, and privacy safe", () => withWindows((directory) => {
  const query = buildTierMetricQuery("openjibo-cloud--0000102", loadTierWindows(directory));
  assert.match(query, /datetime\(2026-09-07T12:00:00\.000Z\)/);
  assert.match(query, /datetime\(2026-09-07T12:02:20\.000Z\)/);
  assert.match(query, /cloud_RoleInstance startswith 'openjibo-cloud--0000102\/'/);
  assert.match(query, /Pool in \('cloud_state','personal_memory'\)/);
  assert.doesNotMatch(query, /device|robot|account|transcript|connection.string/i);
}));

test("tier evidence attributes pool pressure to its exact window", () => withWindows((directory) => {
  const windows = loadTierWindows(directory);
  const rows = [
    { Timestamp: "2026-09-07T12:00:30Z", Metric: "db.client.connections.usage", Value: 3,
      Pool: "cloud_state", State: "used", Instance: "revision/replica-a" },
    { Timestamp: "2026-09-07T12:00:30Z", Metric: "db.client.connections.pending_requests", Value: 0,
      Pool: "cloud_state", State: "", Instance: "revision/replica-a" },
    { Timestamp: "2026-09-07T12:01:10Z", Metric: "db.client.connections.usage", Value: 4,
      Pool: "cloud_state", State: "used", Instance: "revision/replica-a" },
    { Timestamp: "2026-09-07T12:01:30Z", Metric: "db.client.connections.usage", Value: 8,
      Pool: "cloud_state", State: "used", Instance: "revision/replica-b" },
    { Timestamp: "2026-09-07T12:01:30Z", Metric: "db.client.connections.pending_requests", Value: 2,
      Pool: "cloud_state", State: "", Instance: "revision/replica-b" },
    { Timestamp: "2026-09-07T12:01:30Z", Metric: "db.client.commands.executing", Value: 8,
      Pool: "cloud_state", State: "", Instance: "revision/replica-b" },
  ];
  const evidence = buildTierEvidence({ revision: "revision", windows, rows });
  assert.equal(evidence.coverage.complete, true);
  assert.equal(evidence.reliability.pendingRequestMax, 2);
  assert.deepEqual(evidence.reliability.pendingTiers, [10]);
  assert.equal(evidence.tiers[0].summary.cloudStateUsedMax, 3);
  assert.equal(evidence.tiers[1].summary.cloudStateUsedMax, 8);
  assert.equal(evidence.tiers[1].summary.cloudStatePendingMax, 2);
  assert.equal(evidence.tiers[1].summary.cloudStateExecutingMax, 8);
}));

test("tier windows are chronological and shared boundary samples belong to the later tier", () =>
  withWindows((directory) => {
    const windows = loadTierWindows(directory);
    const evidence = buildTierEvidence({ revision: "revision", windows, rows: [{
      Timestamp: "2026-09-07T12:01:10Z", Metric: "db.client.connections.usage", Value: 4,
      Pool: "cloud_state", State: "used", Instance: "revision/replica-a",
    }] });
    assert.deepEqual(windows.map((window) => window.tier), [6, 10]);
    assert.equal(evidence.tiers[0].summary.sampleCount, 0);
    assert.equal(evidence.tiers[1].summary.sampleCount, 1);
  }));

test("tier evidence keeps missing metric coverage explicit", () => withWindows((directory) => {
  const evidence = buildTierEvidence({ revision: "revision", windows: loadTierWindows(directory), rows: [] });
  assert.equal(evidence.coverage.complete, false);
  assert.deepEqual(evidence.coverage.missingTiers, [6, 10]);
  assert.equal(evidence.reliability.pendingRequestMax, null);
}));

test("active connection gauges corroborate zero pending requests in short tier windows", () =>
  withWindows((directory) => {
    const rows = ["2026-09-07T12:00:30Z", "2026-09-07T12:01:30Z"].map((Timestamp) => ({
      Timestamp, Metric: "db.client.connections.usage", Value: 1,
      Pool: "cloud_state", State: "used", Instance: "revision/replica-a",
    }));
    const evidence = buildTierEvidence({ revision: "revision", windows: loadTierWindows(directory), rows });
    assert.equal(evidence.coverage.complete, true);
    assert.equal(evidence.reliability.pendingRequestMax, 0);
    assert.deepEqual(evidence.reliability.inferredZeroTiers, [6, 10]);
  }));
