#!/usr/bin/env node

import { existsSync, mkdirSync, writeFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const RELIABILITY_SIGNALS = new Set([
  "databaseFailures",
  "databasePendingRequests",
  "audioLimitRejections",
  "failedRequests",
  "exceptions",
  "containerRestarts",
]);

export function parseArgs(argv) {
  const values = {
    resourceGroup: "rg-openjibo-staging",
    containerApp: "openjibo-cloud",
    applicationInsights: "appi-openjibo-managed",
    expectedCommit: null,
    lookbackHours: 3,
    output: null,
  };
  const options = new Map([
    ["--resource-group", "resourceGroup"],
    ["--container-app", "containerApp"],
    ["--application-insights", "applicationInsights"],
    ["--expected-commit", "expectedCommit"],
    ["--lookback-hours", "lookbackHours"],
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
  values.lookbackHours = Number(values.lookbackHours);
  if (!values.help) {
    if (values.resourceGroup !== "rg-openjibo-staging")
      throw new Error("The soak checker is restricted to rg-openjibo-staging.");
    if (!/^[0-9a-f]{7,40}$/i.test(values.expectedCommit ?? ""))
      throw new Error("--expected-commit must be a 7-40 character Git commit.");
    if (!Number.isFinite(values.lookbackHours) || values.lookbackHours < 1 || values.lookbackHours > 24)
      throw new Error("--lookback-hours must be between 1 and 24.");
  }
  return values;
}

export function buildReliabilityQuery(revision, lookbackHours) {
  if (!/^[A-Za-z0-9-]+$/.test(revision)) throw new Error("Revision name was not Azure-safe.");
  if (!Number.isFinite(lookbackHours) || lookbackHours < 1 || lookbackHours > 24)
    throw new Error("Lookback hours must be between 1 and 24.");
  return `let databaseFailures = customMetrics
| where timestamp > ago(${lookbackHours}h)
| where cloud_RoleInstance startswith '${revision}/'
| where name == 'db.client.commands.failed'
| summarize Value=todouble(sum(value)), Samples=count()
| extend Signal='databaseFailures';
let databasePendingRequests = customMetrics
| where timestamp > ago(${lookbackHours}h)
| where cloud_RoleInstance startswith '${revision}/'
| where name == 'db.client.connections.pending_requests'
| summarize Value=todouble(max(value)), Samples=count()
| extend Signal='databasePendingRequests';
let audioLimitRejections = customMetrics
| where timestamp > ago(${lookbackHours}h)
| where cloud_RoleInstance startswith '${revision}/'
| where name == 'openjibo.audio.buffer_limit_rejections'
| summarize Value=todouble(sum(value)), Samples=count()
| extend Signal='audioLimitRejections';
let workingSetBytes = customMetrics
| where timestamp > ago(${lookbackHours}h)
| where cloud_RoleInstance startswith '${revision}/'
| where name == 'dotnet.process.memory.working_set'
| summarize Value=todouble(max(value)), Samples=count()
| extend Signal='workingSetBytes';
let failedRequests = requests
| where timestamp > ago(${lookbackHours}h)
| where cloud_RoleInstance startswith '${revision}/'
| summarize Value=todouble(countif(success == false)), Samples=count()
| extend Signal='failedRequests';
let exceptionSignals = exceptions
| where timestamp > ago(${lookbackHours}h)
| where cloud_RoleInstance startswith '${revision}/'
| summarize Value=todouble(count()), Samples=count()
| extend Signal='exceptions';
union databaseFailures, databasePendingRequests, audioLimitRejections, workingSetBytes, failedRequests, exceptionSignals
| project Signal, Value, Samples
| order by Signal asc`;
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
  if (result.status !== 0) {
    const operation = args.slice(0, 3).join(" ");
    throw new Error(`Azure CLI '${operation}' failed: ${(result.stderr || `exit ${result.status}`).trim()}`);
  }
  try { return JSON.parse(result.stdout || "null"); }
  catch (error) { throw new Error(`Azure CLI returned invalid JSON: ${error.message}`); }
}

function tableRows(payload) {
  const table = payload?.tables?.[0];
  if (!table?.columns || !Array.isArray(table.rows)) return [];
  return table.rows.map((row) => Object.fromEntries(table.columns.map((column, index) =>
    [column.name, row[index]])));
}

function envValue(container, name) {
  return container?.properties?.template?.containers?.[0]?.env?.find((item) => item.name === name)?.value ?? null;
}

function expectedImageTag(commit) { return `sha-${commit.toLowerCase().slice(0, 12)}`; }

export function assessSoakEvidence({ options, container, health, reliabilityRows, restartMetric }) {
  const problems = [];
  const appName = container?.name ?? null;
  const revision = container?.properties?.latestReadyRevisionName ?? null;
  const image = container?.properties?.template?.containers?.[0]?.image ?? null;
  const fqdn = container?.properties?.configuration?.ingress?.fqdn ?? null;
  const runningState = container?.properties?.runningStatus ?? null;
  const expectedTag = expectedImageTag(options.expectedCommit);
  const replayObservation = envValue(container, "OpenJibo__Security__SigV4ReplayObservation__Enabled");
  const signals = Object.fromEntries(reliabilityRows.map((row) => [String(row.Signal), {
    value: Number(row.Value), samples: Number(row.Samples),
  }]));
  signals.containerRestarts = {
    value: Number(restartMetric?.max ?? 0), samples: Number(restartMetric?.samples ?? 0),
  };

  if (!appName || !revision || !fqdn) problems.push("staging-container-app-incomplete");
  if (!image?.includes(`:${expectedTag}`)) problems.push("unexpected-image");
  if (String(replayObservation).toLowerCase() !== "true") problems.push("replay-observation-disabled");
  if (runningState && runningState !== "Running") problems.push("container-app-not-running");
  if (!health?.ok) problems.push("public-health-failed");
  for (const [name, signal] of Object.entries(signals)) {
    if (RELIABILITY_SIGNALS.has(name) && Number.isFinite(signal.value) && signal.value > 0)
      problems.push(`reliability-signal:${name}`);
  }

  return {
    generatedUtc: new Date().toISOString(),
    status: problems.length ? "failed" : "passed",
    scope: {
      resourceGroup: options.resourceGroup,
      containerApp: options.containerApp,
      applicationInsights: options.applicationInsights,
      expectedCommit: options.expectedCommit,
      expectedImageTag: expectedTag,
      lookbackHours: options.lookbackHours,
    },
    deployment: { appName, revision, image, fqdn, runningState, replayObservation },
    health,
    signals,
    problems,
  };
}

export async function collectSoakEvidence(options, azure = runAz, request = fetch) {
  const apps = azure(["containerapp", "list", "--resource-group", options.resourceGroup, "--output", "json"]);
  const matchingApps = Array.isArray(apps) ? apps.filter((app) => app.name === options.containerApp) : [];
  if (matchingApps.length !== 1)
    throw new Error(`Expected exactly one staging Container App named '${options.containerApp}'; found ${matchingApps.length}.`);
  const container = azure(["containerapp", "show", "--resource-group", options.resourceGroup,
    "--name", matchingApps[0].name, "--output", "json"]);
  const revision = container?.properties?.latestReadyRevisionName;
  const fqdn = container?.properties?.configuration?.ingress?.fqdn;
  if (!revision || !fqdn) throw new Error("The staging Container App did not expose a ready revision and FQDN.");

  let health;
  try {
    const response = await request(`https://${fqdn}/health`, {
      headers: { "User-Agent": "OpenJibo-Staging-Soak/1.0" }, signal: AbortSignal.timeout(15_000),
    });
    const body = await response.text();
    health = { ok: response.ok, statusCode: response.status, body: body.slice(0, 500) };
  } catch (error) { health = { ok: false, statusCode: null, error: error.message }; }

  const reliabilityPayload = azure(["monitor", "app-insights", "query", "--app",
    options.applicationInsights, "--resource-group", options.resourceGroup, "--analytics-query",
    buildReliabilityQuery(revision, options.lookbackHours), "--offset", `${options.lookbackHours}h`,
    "--output", "json"]);
  const resourceId = container.id;
  const restartPayload = azure(["monitor", "metrics", "list", "--resource", resourceId,
    "--metric", "RestartCount", "--aggregation", "Maximum", "--interval", "PT1H",
    "--filter", `RevisionName eq '${revision}'`, "--offset", `${options.lookbackHours}h`, "--output", "json"]);
  const restartPoints = (restartPayload?.value?.[0]?.timeseries ?? []).flatMap((series) => series.data ?? [])
    .map((point) => Number(point.maximum)).filter(Number.isFinite);
  const restartMetric = { samples: restartPoints.length,
    max: restartPoints.length ? Math.max(...restartPoints) : 0 };
  return assessSoakEvidence({ options, container, health, reliabilityRows: tableRows(reliabilityPayload), restartMetric });
}

function usage() {
  return `Usage: node scripts/cloud/openjibo-staging-soak-check.mjs --expected-commit SHA [options]\n\n` +
    `  --expected-commit SHA       Exact deployed commit expected in the image tag\n` +
    `  --resource-group NAME       Must be rg-openjibo-staging\n` +
    `  --container-app NAME        Runtime Container App (default openjibo-cloud)\n` +
    `  --application-insights NAME Application Insights component\n` +
    `  --lookback-hours 1-24       Reliability window (default 3)\n` +
    `  --output PATH               Write JSON evidence to this file\n` +
    `  --help                      Show this help\n`;
}

const invokedPath = process.argv[1]?.replaceAll("\\", "/");
if (invokedPath && fileURLToPath(import.meta.url).replaceAll("\\", "/") === invokedPath) {
  try {
    const options = parseArgs(process.argv.slice(2));
    if (options.help) process.stdout.write(usage());
    else {
      const evidence = await collectSoakEvidence(options);
      const output = `${JSON.stringify(evidence, null, 2)}\n`;
      if (options.output) {
        mkdirSync(dirname(resolve(options.output)), { recursive: true });
        writeFileSync(options.output, output, "utf8");
      } else process.stdout.write(output);
      if (evidence.status !== "passed") process.exitCode = 1;
    }
  } catch (error) { process.stderr.write(`${error.message}\n`); process.exitCode = 1; }
}
