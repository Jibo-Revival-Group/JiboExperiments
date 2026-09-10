import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createContext, runInContext } from "node:vm";
import test from "node:test";

const source = readFileSync(new URL("../../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/wwwroot/portal/status/status.js", import.meta.url), "utf8")
  .replace(/^bootstrap\(\);\s*$/m, "");

function harness(confirmResult = false) {
  const progress = { textContent: "" };
  let tick;
  let cleared = 0;
  let confirmations = 0;
  const context = createContext({
    document: { getElementById: () => progress },
    window: {
      setInterval: callback => { tick = callback; return 1; },
      clearInterval: () => { cleared++; },
      confirm: () => { confirmations++; return confirmResult; },
    },
  });
  runInContext(source, context);
  runInContext(`
    let requestCount = 0;
    let requests = [];
    let finishRequest;
    let failRequest;
    apiFetch = (...args) => { requests.push(args); requestCount++; return new Promise((resolve, reject) => { finishRequest = resolve; failRequest = reject; }); };
    renderStatusView = () => {};
    refreshStatus = () => Promise.resolve();
  `, context);
  return { context, progress, tick: () => tick(), cleared: () => cleared,
    confirmations: () => confirmations,
    read: expression => runInContext(expression, context) };
}

test("identity scan shows elapsed progress and blocks duplicate requests until completion", async () => {
  const view = harness();
  const pending = view.read('suggestRobotIdentity("robot-one")');
  assert.equal(view.read("identityScanDeviceId"), "robot-one");
  assert.match(view.read("bannerMessage"), /Scanning/);
  await view.read('suggestRobotIdentity("robot-one")');
  await view.read('suggestRobotIdentity("robot-two")');
  assert.equal(view.read("requestCount"), 1);
  view.read("identityScanStartedAt = Date.now() - 5000");
  view.tick();
  assert.match(view.progress.textContent, /5s elapsed/);
  view.read("finishRequest({ suggested: false })");
  await pending;
  assert.equal(view.read("identityScanDeviceId"), null);
  assert.equal(view.read("identityScanProgressTimer"), null);
  assert.equal(view.cleared(), 1);
});

test("failed identity scan clears busy state and allows a retry", async () => {
  const view = harness();
  const pending = view.read('suggestRobotIdentity("robot-one")');
  view.read('failRequest(new Error("Storage unavailable"))');
  await pending;
  assert.equal(view.read("identityScanDeviceId"), null);
  assert.equal(view.read("bannerTone"), "error");
  assert.match(view.read("bannerMessage"), /Storage unavailable/);
  const retry = view.read('suggestRobotIdentity("robot-one")');
  assert.equal(view.read("requestCount"), 2);
  view.read("finishRequest({ suggested: false })");
  await retry;
});

test("ambiguous identity scan requires manual review and never applies", async () => {
  const view = harness();
  const pending = view.read('suggestRobotIdentity("robot-one")');
  view.read(`finishRequest({
    suggested: true,
    proposedRobotId: "Alpha-Beta-Dodger-Quirk",
    action: "ambiguous",
    candidateTargetDeviceIds: ["canonical-a", "canonical-b"],
    evidence: []
  })`);
  await pending;

  assert.equal(view.read("requestCount"), 1);
  assert.equal(view.confirmations(), 0);
  assert.equal(view.read("bannerTone"), "error");
  assert.match(view.read("bannerMessage"), /Multiple active robot records/);
  assert.match(view.read("bannerMessage"), /canonical-a, canonical-b/);
});

test("identity apply is bound to the reviewed action and target", async () => {
  const view = harness(true);
  const pending = view.read('suggestRobotIdentity("robot-one")');
  view.read(`finishRequest({
    suggested: true,
    proposedRobotId: "Alpha-Beta-Dodger-Quirk",
    action: "merge",
    targetDeviceId: "canonical-a",
    evidence: []
  })`);
  await new Promise(resolve => setImmediate(resolve));

  assert.equal(view.read("requestCount"), 2);
  const body = JSON.parse(view.read("requests[1][1].body"));
  assert.deepEqual(body, {
    proposedRobotId: "Alpha-Beta-Dodger-Quirk",
    expectedAction: "merge",
    expectedTargetDeviceId: "canonical-a",
  });
  view.read("finishRequest({ ok: true })");
  await pending;
});
