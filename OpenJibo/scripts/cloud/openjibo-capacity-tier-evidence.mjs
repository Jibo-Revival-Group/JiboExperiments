#!/usr/bin/env node

import { existsSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const METRICS = new Set([
  "db.client.connections.pending_requests",
  "db.client.connections.usage",
  "db.client.commands.executing",
]);
const POOLS = new Set(["cloud_state", "personal_memory"]);
const STATES = new Set(["idle", "used", ""]);

function parseArgs(argv) {
  const values = {
    resourceGroup: "rg-openjibo-staging",
    applicationInsights: "appi-openjibo-managed",
    revision: null,
    windowsDir: null,
    output: null,
  };
  const options = new Map([
    ["--resource-group", "resourceGroup"],
    ["--application-insights", "applicationInsights"],
    ["--revision", "revision"],
    ["--windows-dir", "windowsDir"],
    ["--output", "output"],
  ]);
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === "--help") values.help = true;
    else if (options.has(argument)) {
      const value = argv[++index];
      if (!value || value.startsWith("--")) throw new Error(`${argument} requires a value.`);
      values[options.get(argument)] = value;
    } else throw new Error(`Unknown option: ${argument}`);
  }
  if (!values.help) {
    if (!values.revision || !/^[A-Za-z0-9-]+$/.test(values.revision))
      throw new Error("--revision must be an Azure-safe revision name.");
    if (!values.windowsDir) throw new Error("--windows-dir is required.");
  }
  return values;
}

export function loadTierWindows(directory) {
  const files = readdirSync(directory)
    .filter((name) => /^tier-\d+-window\.json$/.test(name));
  if (!files.length) throw new Error("No tier window files were found.");
  const windows = files.map((name) => {
    const value = JSON.parse(readFileSync(join(directory, name), "utf8"));
    const tier = Number(value.tier);
    const startedMs = Date.parse(value.startedUtc);
    const completedMs = Date.parse(value.completedUtc);
    if (!Number.isInteger(tier) || tier < 1 || !Number.isFinite(startedMs) ||
        !Number.isFinite(completedMs) || completedMs < startedMs)
      throw new Error(`Tier window ${name} is invalid.`);
    return { tier, startedUtc: new Date(startedMs).toISOString(),
      completedUtc: new Date(completedMs).toISOString(), startedMs, completedMs };
  }).sort((left, right) => left.startedMs - right.startedMs);
  for (let index = 1; index < windows.length; index += 1) {
    if (windows[index].startedMs < windows[index - 1].completedMs)
      throw new Error("Tier windows must not overlap.");
  }
  return windows;
}

export function buildTierMetricQuery(revision, windows) {
  if (!/^[A-Za-z0-9-]+$/.test(revision)) throw new Error("Revision name was not Azure-safe.");
  if (!windows.length) throw new Error("At least one tier window is required.");
  const started = windows.reduce((value, item) => Math.min(value, item.startedMs), Infinity);
  const completed = windows.reduce((value, item) => Math.max(value, item.completedMs), -Infinity);
  return `customMetrics
| where timestamp between (datetime(${new Date(started).toISOString()}) .. datetime(${new Date(completed).toISOString()}))
| where cloud_RoleInstance startswith '${revision}/'
| where name in ('db.client.connections.pending_requests','db.client.connections.usage','db.client.commands.executing')
| extend Pool=tostring(customDimensions['pool.name']), State=tostring(customDimensions['state'])
| where Pool in ('cloud_state','personal_memory')
| project Timestamp=timestamp, Metric=name, Value=value, Pool, State, Instance=cloud_RoleInstance
| order by Timestamp asc`;
}

function tableRows(payload) {
  const table = payload?.tables?.[0];
  if (!table?.columns || !Array.isArray(table.rows)) return [];
  return table.rows.map((row) => Object.fromEntries(table.columns.map((column, index) =>
    [column.name, row[index]])));
}

function finite(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

export function buildTierEvidence({ revision, windows, rows }) {
  const normalized = rows.flatMap((row) => {
    const timestampMs = Date.parse(row.Timestamp);
    const value = finite(row.Value);
    const pool = String(row.Pool ?? "");
    const state = String(row.State ?? "");
    if (!Number.isFinite(timestampMs) || value === null || !METRICS.has(row.Metric) ||
        !POOLS.has(pool) || !STATES.has(state)) return [];
    return [{ timestampUtc: new Date(timestampMs).toISOString(), timestampMs,
      metric: row.Metric, value, pool, state, instance: String(row.Instance ?? "") }];
  });
  const max = (samples, predicate) => {
    const values = samples.filter(predicate).map((sample) => sample.value);
    return values.length ? Math.max(...values) : null;
  };
  const contains = (window, index, timestampMs) => timestampMs >= window.startedMs &&
    (index === windows.length - 1 ? timestampMs <= window.completedMs : timestampMs < window.completedMs);
  const tiers = windows.map((window, windowIndex) => {
    const samples = normalized.filter((sample) => sample.timestampMs >= window.startedMs &&
      contains(window, windowIndex, sample.timestampMs));
    const pendingRequestSampleCount = samples.filter((sample) =>
      sample.metric === "db.client.connections.pending_requests").length;
    const connectionUsageSampleCount = samples.filter((sample) =>
      sample.metric === "db.client.connections.usage").length;
    const summary = {
      sampleCount: samples.length,
      pendingRequestSampleCount,
      connectionUsageSampleCount,
      pendingRequestsInferredZero: pendingRequestSampleCount === 0 && connectionUsageSampleCount > 0,
      cloudStatePendingMax: max(samples, (sample) => sample.pool === "cloud_state" &&
        sample.metric === "db.client.connections.pending_requests"),
      personalMemoryPendingMax: max(samples, (sample) => sample.pool === "personal_memory" &&
        sample.metric === "db.client.connections.pending_requests"),
      cloudStateUsedMax: max(samples, (sample) => sample.pool === "cloud_state" &&
        sample.metric === "db.client.connections.usage" && sample.state === "used"),
      personalMemoryUsedMax: max(samples, (sample) => sample.pool === "personal_memory" &&
        sample.metric === "db.client.connections.usage" && sample.state === "used"),
      cloudStateExecutingMax: max(samples, (sample) => sample.pool === "cloud_state" &&
        sample.metric === "db.client.commands.executing"),
      personalMemoryExecutingMax: max(samples, (sample) => sample.pool === "personal_memory" &&
        sample.metric === "db.client.commands.executing"),
    };
    return { tier: window.tier, startedUtc: window.startedUtc, completedUtc: window.completedUtc,
      summary, samples: samples.map(({ timestampMs: _timestampMs, ...sample }) => sample) };
  });
  const unattributedSamples = normalized.filter((sample) => !windows.some((window, windowIndex) =>
    contains(window, windowIndex, sample.timestampMs)))
    .map(({ timestampMs: _timestampMs, ...sample }) => sample);
  const pendingTiers = tiers.filter((tier) =>
    (tier.summary.cloudStatePendingMax ?? 0) > 0 || (tier.summary.personalMemoryPendingMax ?? 0) > 0)
    .map((tier) => tier.tier);
  const coverageComplete = tiers.every((tier) => tier.summary.connectionUsageSampleCount > 0);
  const observedPendingMax = max(normalized, (sample) =>
    sample.metric === "db.client.connections.pending_requests");
  return {
    generatedUtc: new Date().toISOString(),
    revision,
    coverage: { complete: coverageComplete,
      missingTiers: tiers.filter((tier) => tier.summary.connectionUsageSampleCount === 0)
        .map((tier) => tier.tier) },
    reliability: { pendingRequestMax: observedPendingMax ?? (coverageComplete ? 0 : null), pendingTiers,
      inferredZeroTiers: tiers.filter((tier) => tier.summary.pendingRequestsInferredZero)
        .map((tier) => tier.tier) },
    tiers,
    unattributedSamples,
  };
}

function runAz(args) {
  let executable = "az";
  let executableArgs = args;
  if (process.platform === "win32") {
    const lookup = spawnSync("where.exe", ["az.cmd"], { encoding: "utf8", windowsHide: true });
    const launcher = lookup.status === 0 ? lookup.stdout.split(/\r?\n/).find(Boolean)?.trim() : null;
    const python = launcher ? resolve(dirname(launcher), "..", "python.exe") : null;
    if (!python || !existsSync(python))
      throw new Error("Azure CLI's bundled Python executable could not be resolved from az.cmd.");
    executable = python;
    executableArgs = ["-IBm", "azure.cli", ...args];
  }
  const result = spawnSync(executable, executableArgs,
    { encoding: "utf8", windowsHide: true, maxBuffer: 16 * 1024 * 1024 });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error((result.stderr || `Azure CLI exited ${result.status}.`).trim());
  try { return JSON.parse(result.stdout || "null"); }
  catch (error) { throw new Error(`Azure CLI returned invalid JSON: ${error.message}`); }
}

export function collectTierEvidence(options, azure = runAz) {
  const windows = loadTierWindows(options.windowsDir);
  const payload = azure(["monitor", "app-insights", "query", "--app", options.applicationInsights,
    "--resource-group", options.resourceGroup, "--analytics-query",
    buildTierMetricQuery(options.revision, windows), "--offset", "1d", "--output", "json"]);
  return buildTierEvidence({ revision: options.revision, windows, rows: tableRows(payload) });
}

function usage() {
  return `Usage: node scripts/cloud/openjibo-capacity-tier-evidence.mjs [options]\n\n` +
    `  --resource-group NAME       Azure resource group (default rg-openjibo-staging)\n` +
    `  --application-insights NAME Application Insights component (default appi-openjibo-managed)\n` +
    `  --revision NAME             Exact capacity probe revision\n` +
    `  --windows-dir PATH          Directory containing tier-*-window.json files\n` +
    `  --output PATH               Write JSON evidence to this file\n` +
    `  --help                      Show this help\n`;
}

const invokedPath = process.argv[1]?.replaceAll("\\", "/");
if (invokedPath && fileURLToPath(import.meta.url).replaceAll("\\", "/") === invokedPath) {
  try {
    const options = parseArgs(process.argv.slice(2));
    if (options.help) process.stdout.write(usage());
    else {
      const evidence = collectTierEvidence(options);
      const output = `${JSON.stringify(evidence, null, 2)}\n`;
      if (options.output) writeFileSync(options.output, output, "utf8"); else process.stdout.write(output);
    }
  } catch (error) { process.stderr.write(`${error.message}\n`); process.exitCode = 1; }
}
