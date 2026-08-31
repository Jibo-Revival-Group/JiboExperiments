import assert from "node:assert/strict";
import test from "node:test";
import { buildApplicationQuery, buildCapacityReport, collectCapacityReport, parseArgs, parseMemoryBytes, renderMarkdown,
  summarizePlatformMetric, tableRows } from "../../scripts/cloud/openjibo-capacity-report.mjs";

test("capacity arguments are bounded and default to staging", () => {
  const options = parseArgs([]);
  assert.equal(options.resourceGroup, "rg-openjibo-staging");
  assert.equal(options.days, 7);
  assert.equal(options.averageRobots, 2.5);
  assert.throws(() => parseArgs(["--days", "31"]), /1 through 30/);
  assert.throws(() => parseArgs(["--average-robots", "0"]), /greater than zero/);
});

test("application query uses a bounded literal window and aggregate-only metrics", () => {
  const query = buildApplicationQuery(7, "openjibo-cloud--0000075");
  assert.match(query, /ago\(7d\)/);
  assert.match(query, /cloud_RoleInstance startswith 'openjibo-cloud--0000075\/'/);
  assert.match(query, /db\.client\.connections\.usage\.total/);
  assert.match(query, /openjibo\.transport\.payload_bytes/);
  assert.doesNotMatch(query, /robot|device|session[_ ]?id/i);
  assert.throws(() => buildApplicationQuery(7, "unsafe' revision"), /Azure-safe/);
});

test("Azure table and platform metric payloads are normalized", () => {
  assert.deepEqual(tableRows({ tables: [{ columns: [{ name: "Metric" }, { name: "Max" }],
    rows: [["memory", 42]] }] }), [{ Metric: "memory", Max: 42 }]);
  assert.deepEqual(summarizePlatformMetric({ value: [{ name: { value: "RxBytes" }, unit: "Bytes",
    timeseries: [{ data: [{ total: 2 }, { total: 3 }, {}] }] }] }, "Total"),
  { name: "RxBytes", unit: "Bytes", samples: 2, total: 5, max: 3, average: 2.5 });
  assert.equal(parseMemoryBytes("2Gi"), 2 * 1024 ** 3);
  assert.equal(parseMemoryBytes("512Mi"), 512 * 1024 ** 2);
  assert.equal(parseMemoryBytes("invalid"), null);
});

function row(Metric, values = {}) {
  return { Metric, Samples: 10, P50: 1, P95: 2, Average: 1.5, Max: 3, Total: 4,
    First: "2026-08-24T00:00:00Z", Last: "2026-08-31T00:00:00Z", ...values };
}

test("capacity report computes headroom and keeps the claim explicitly bounded", () => {
  const options = parseArgs([]);
  const container = { properties: { latestReadyRevisionName: "revision-7", template: {
    containers: [{ image: "example/openjibo:sha-123", resources: { cpu: 1, memory: "2Gi" } }],
    scale: { minReplicas: 1, maxReplicas: 2 } } } };
  const applicationRows = [
    row("dotnet.process.memory.working_set", { P95: 256 * 1024 ** 2, Max: 512 * 1024 ** 2 }),
    row("db.client.connections.usage.total", { P95: 5, Max: 8 }),
    row("db.client.connections.pending_requests", { P95: 0, Max: 0 }),
    row("openjibo.persistence.postgresql.configured_max_connections.per_replica", { Max: 12 }),
    row("db.client.commands.duration", { P95: 0.08 }), row("db.client.commands.failed", { Total: 0 }),
    row("openjibo.audio.buffer_limit_rejections", { Total: 0 }),
    row("openjibo.persistence.cache.accesses.hit", { Total: 90 }),
    row("openjibo.persistence.cache.accesses.miss", { Total: 10 }),
    row("openjibo.transport.payload_bytes.in", { Total: 1000 }),
    row("openjibo.transport.payload_bytes.out", { Total: 500 })];
  const platform = { WorkingSetBytes: { max: 600 * 1024 ** 2, samples: 10 },
    UsageNanoCores: { max: 125_000_000 },
    RxBytes: { total: 2000 },
    TxBytes: { total: 1000 }, RestartCount: { max: 0, samples: 10 }, Requests: { total: 100 },
    Replicas: { max: 2 } };
  const report = buildCapacityReport({ options, container, applicationRows, platform,
    postgresMaxConnections: 50, configuredPoolCapacityPerReplica: 12 });
  assert.equal(report.evidence.classification, "representative-evidence");
  assert.deepEqual(report.evidence.blockers, []);
  assert.equal(report.memory.applicationMaxLimitRatio, 0.25);
  assert.equal(report.database.observedServerLimitRatio, 0.16);
  assert.equal(report.database.configuredPoolCapacityAtMaxScale, 24);
  assert.equal(report.database.configuredServerLimitRatio, 0.48);
  assert.equal(report.cpu.platformMaxAverageLimitRatio, 0.125);
  assert.equal(report.database.pendingRequestMax, 0);
  assert.equal(report.database.cacheHitRatio, 0.9);
  assert.equal(report.traffic.applicationToPlatformRatio, 0.5);
  assert.match(report.evidence.caveat, /not a linear fleet extrapolation/);
  assert.doesNotMatch(renderMarkdown(report), /pool\.name|robot[_-]?id/i);
});

test("short observation windows cannot become capacity claims", () => {
  const options = parseArgs([]);
  const report = buildCapacityReport({ options, container: { properties: { template: { containers: [
    { resources: { memory: "2Gi" } }], scale: {} } } }, applicationRows: [row(
      "dotnet.process.memory.working_set", { First: "2026-08-30T00:00:00Z", Last: "2026-08-31T00:00:00Z" })],
    platform: {}, postgresMaxConnections: null });
  assert.equal(report.evidence.classification, "insufficient-evidence");
  assert.ok(report.evidence.blockers.includes("observation-window-incomplete"));
  assert.ok(report.evidence.blockers.includes("representative-robot-activity-absent"));
  assert.ok(report.evidence.blockers.includes("required-telemetry-missing"));
});

test("thirty-day requests require eighty percent of the full requested window", () => {
  const options = parseArgs(["--days", "30"]);
  const timestamps = { First: "2026-08-24T00:00:00Z", Last: "2026-08-31T00:00:00Z" };
  const rows = [row("dotnet.process.memory.working_set", timestamps),
    row("db.client.connections.usage.total", timestamps),
    row("db.client.connections.pending_requests", { ...timestamps, Max: 0 }),
    row("db.client.commands.duration", timestamps), row("openjibo.transport.payload_bytes.in", timestamps)];
  const container = { properties: { template: { containers: [{ resources: { cpu: 1, memory: "2Gi" } }],
    scale: { maxReplicas: 2 } } } };
  const platform = { WorkingSetBytes: { max: 1, samples: 10 }, RestartCount: { max: 0, samples: 10 },
    RxBytes: { total: 2 }, TxBytes: { total: 2 } };
  const report = buildCapacityReport({ options, container, applicationRows: rows, platform,
    postgresMaxConnections: 50, configuredPoolCapacityPerReplica: 12 });
  assert.equal(report.evidence.classification, "insufficient-evidence");
  assert.ok(report.evidence.blockers.includes("observation-window-incomplete"));
});

test("collector scopes every Azure metric request to the exact ready revision", async () => {
  const calls = [];
  const azure = (args) => {
    calls.push(args);
    if (args[0] === "containerapp") return { id: "/subscriptions/s/resourceGroups/rg/providers/Microsoft.App/containerapps/app",
      properties: { latestReadyRevisionName: "openjibo-cloud--0000075", template: { containers: [{
        resources: { cpu: 1, memory: "2Gi" }, env: [{ name: "OpenJibo__Deployment__PostgreSqlServerName",
          value: "psql-staging" }] }], scale: { minReplicas: 1, maxReplicas: 2 } } } };
    if (args[0] === "monitor" && args[1] === "app-insights") return { tables: [] };
    if (args[0] === "monitor") return { value: [] };
    if (args[0] === "postgres") return { value: "50" };
    throw new Error(`Unexpected call: ${args.join(" ")}`);
  };
  await collectCapacityReport(parseArgs([]), azure);
  const appCall = calls.find((args) => args[1] === "app-insights");
  assert.match(appCall[appCall.indexOf("--analytics-query") + 1],
    /cloud_RoleInstance startswith 'openjibo-cloud--0000075\/'/);
  const platformCalls = calls.filter((args) => args[1] === "metrics");
  assert.equal(platformCalls.length, 7);
  for (const args of platformCalls)
    assert.equal(args[args.indexOf("--filter") + 1], "RevisionName eq 'openjibo-cloud--0000075'");
});
