import assert from "node:assert/strict";
import test from "node:test";

import {
  assessSoakEvidence,
  buildReliabilityQuery,
  collectSoakEvidence,
  parseArgs,
} from "../../scripts/cloud/openjibo-staging-soak-check.mjs";

test("soak arguments are staging-only and require an exact commit", () => {
  const options = parseArgs(["--expected-commit", "7a39fc97d39929f950a0a5d78ce47fa7bc2fcaf2"]);
  assert.equal(options.resourceGroup, "rg-openjibo-staging");
  assert.equal(options.containerApp, "openjibo-cloud");
  assert.equal(options.lookbackHours, 3);
  assert.throws(() => parseArgs(["--expected-commit", "bad"]), /Git commit/);
  assert.throws(() => parseArgs(["--expected-commit", "7a39fc97d399", "--resource-group", "rg-openjibo-prod"]),
    /restricted/);
  assert.throws(() => parseArgs(["--expected-commit", "7a39fc97d39929f950a0a5d78ce47fa7bc2fcaf2", "--lookback-hours", "25"]),
    /between 1 and 24/);
});

test("reliability query is revision-scoped, bounded, and aggregate-only", () => {
  const query = buildReliabilityQuery("openjibo-cloud--0000058", 3);
  assert.match(query, /ago\(3h\)/);
  assert.match(query, /cloud_RoleInstance startswith 'openjibo-cloud--0000058\/'/);
  assert.match(query, /databasePendingRequests/);
  assert.match(query, /countif\(success == false\)/);
  assert.doesNotMatch(query, /robot|device|session[_ ]?id|transcript/i);
  assert.throws(() => buildReliabilityQuery("unsafe' revision", 3), /Azure-safe/);
});

function healthyContainer() {
  return {
    id: "/subscriptions/s/resourceGroups/rg-openjibo-staging/providers/Microsoft.App/containerapps/openjibo-cloud",
    name: "openjibo-cloud",
    properties: {
      latestReadyRevisionName: "openjibo-cloud--0000058",
      runningStatus: "Running",
      configuration: { ingress: { fqdn: "openjibo-cloud.example.azurecontainerapps.io",
        traffic: [{ latestRevision: true, weight: 100 }] } },
      template: { containers: [{ image: "registry/openjibo-cloud:sha-7a39fc97d399", env: [
        { name: "OpenJibo__Security__SigV4ReplayObservation__Enabled", value: "true" },
      ] }] },
    },
  };
}

function healthyRevision() {
  return { properties: { active: true, healthState: "Healthy", runningState: "Running" } };
}

const healthyPlatform = { samples: 3, max: 1 };

test("healthy expected revision passes without reliability signals", () => {
  const options = parseArgs(["--expected-commit", "7a39fc97d39929f950a0a5d78ce47fa7bc2fcaf2"]);
  const evidence = assessSoakEvidence({ options, container: healthyContainer(), revisionState: healthyRevision(),
    health: { ok: true, statusCode: 200, body: "ok" },
    reliabilityRows: [
      { Signal: "databasePendingRequests", Value: 0, Samples: 10 },
      { Signal: "failedRequests", Value: 0, Samples: 100 },
      { Signal: "workingSetBytes", Value: 1234, Samples: 10 },
    ], restartMetric: { max: 0, samples: 3 }, platformWorkingSet: healthyPlatform,
    platformReplicas: healthyPlatform });
  assert.equal(evidence.status, "passed");
  assert.deepEqual(evidence.problems, []);
  assert.equal(evidence.deployment.replayObservation, "true");
});

test("Azure's title-cased boolean environment values are accepted", () => {
  const options = parseArgs(["--expected-commit", "7a39fc97d39929f950a0a5d78ce47fa7bc2fcaf2"]);
  const container = healthyContainer();
  container.properties.template.containers[0].env[0].value = "True";
  const evidence = assessSoakEvidence({ options, container, revisionState: healthyRevision(),
    health: { ok: true, statusCode: 200 },
    reliabilityRows: [{ Signal: "workingSetBytes", Value: 1234, Samples: 3 }],
    restartMetric: { max: 0, samples: 0 }, platformWorkingSet: healthyPlatform,
    platformReplicas: healthyPlatform });
  assert.equal(evidence.status, "passed");
});

test("unexpected revision and reliability evidence fail the soak check", () => {
  const options = parseArgs(["--expected-commit", "7a39fc97d39929f950a0a5d78ce47fa7bc2fcaf2"]);
  const container = healthyContainer();
  container.properties.template.containers[0].image = "registry/openjibo-cloud:sha-7a39fc97d399-debug";
  const evidence = assessSoakEvidence({ options, container, revisionState: healthyRevision(),
    health: { ok: false, statusCode: 503 },
    reliabilityRows: [{ Signal: "exceptions", Value: 2, Samples: 2 }],
    restartMetric: { max: 1, samples: 3 }, platformWorkingSet: healthyPlatform,
    platformReplicas: healthyPlatform });
  assert.equal(evidence.status, "failed");
  assert.ok(evidence.problems.includes("unexpected-image"));
  assert.ok(evidence.problems.includes("public-health-failed"));
  assert.ok(evidence.problems.includes("reliability-signal:exceptions"));
  assert.ok(evidence.problems.includes("reliability-signal:containerRestarts"));
});

test("missing telemetry, running state, and traffic ownership fail closed", () => {
  const options = parseArgs(["--expected-commit", "7a39fc97d39929f950a0a5d78ce47fa7bc2fcaf2"]);
  const container = healthyContainer();
  delete container.properties.runningStatus;
  container.properties.configuration.ingress.traffic = [{ latestRevision: true, weight: 50 }];
  const evidence = assessSoakEvidence({ options, container,
    revisionState: { properties: { active: true, healthState: "Unknown", runningState: "Running" } },
    health: { ok: true, statusCode: 200 }, reliabilityRows: [],
    restartMetric: { max: 0, samples: 0 }, platformWorkingSet: { samples: 0, max: null },
    platformReplicas: { samples: 0, max: null } });
  assert.equal(evidence.status, "failed");
  assert.ok(evidence.problems.includes("container-app-not-running"));
  assert.ok(evidence.problems.includes("ready-revision-not-healthy"));
  assert.ok(evidence.problems.includes("ready-revision-does-not-own-traffic"));
  assert.ok(evidence.problems.includes("application-telemetry-unavailable"));
  assert.ok(evidence.problems.includes("platform-telemetry-unavailable"));
});

test("collector scopes Azure checks to one staging app and exact ready revision", async () => {
  const options = parseArgs(["--expected-commit", "7a39fc97d39929f950a0a5d78ce47fa7bc2fcaf2"]);
  const calls = [];
  const container = healthyContainer();
  const azure = (args) => {
    calls.push(args);
    if (args[0] === "containerapp" && args[1] === "list") return [
      { name: "openjibo-managed-api" }, { name: "openjibo-cloud" },
    ];
    if (args[0] === "containerapp" && args[1] === "show") return container;
    if (args[0] === "containerapp" && args[1] === "revision") return healthyRevision();
    if (args[0] === "monitor" && args[1] === "app-insights") return { tables: [{
      columns: [{ name: "Signal" }, { name: "Value" }, { name: "Samples" }],
      rows: [["failedRequests", 0, 3], ["workingSetBytes", 1234, 3]],
    }] };
    if (args[0] === "monitor" && args[1] === "metrics") {
      const metric = args[args.indexOf("--metric") + 1];
      const maximum = metric === "WorkingSetBytes" ? 1234 : metric === "Replicas" ? 1 : 0;
      return { value: [{ timeseries: [{ data: [{ maximum }] }] }] };
    }
    throw new Error(`Unexpected Azure call: ${args.join(" ")}`);
  };
  const request = async () => ({ ok: true, status: 200, text: async () => "ok" });
  const evidence = await collectSoakEvidence(options, azure, request);
  assert.equal(evidence.status, "passed");
  const showCall = calls.find((args) => args[0] === "containerapp" && args[1] === "show");
  assert.equal(showCall[showCall.indexOf("--name") + 1], "openjibo-cloud");
  const appInsightsCall = calls.find((args) => args[1] === "app-insights");
  assert.match(appInsightsCall[appInsightsCall.indexOf("--analytics-query") + 1],
    /openjibo-cloud--0000058\//);
  const metricsCalls = calls.filter((args) => args[1] === "metrics");
  assert.equal(metricsCalls.length, 3);
  for (const metricsCall of metricsCalls)
    assert.equal(metricsCall[metricsCall.indexOf("--filter") + 1],
      "RevisionName eq 'openjibo-cloud--0000058'");
});
