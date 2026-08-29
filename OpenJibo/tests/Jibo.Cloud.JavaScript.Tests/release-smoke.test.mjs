import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import test from "node:test";
import vm from "node:vm";
import {
  createProtocolCaller,
  normalizeLoadOptions,
  runReleaseSmoke,
  withDeploymentSmokeAuthorizationRetry,
} from "../../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/wwwroot/harness/release-smoke.mjs";

class FakeWebSocket {
  constructor(url) {
    this.url = url;
    this.readyState = 0;
    this.listeners = new Map();
    const path = new URL(url).pathname;
    queueMicrotask(() => {
      if (path === "/v1/listen") {
        this.readyState = 3;
        this.emit("error", { message: "rejected" });
        this.emit("close", {});
        return;
      }
      this.readyState = 1;
      this.emit("open", {});
    });
  }

  addEventListener(type, handler, options = {}) {
    const registrations = this.listeners.get(type) ?? [];
    registrations.push({ handler, once: options.once === true });
    this.listeners.set(type, registrations);
  }

  removeEventListener(type, handler) {
    this.listeners.set(type, (this.listeners.get(type) ?? [])
      .filter((registration) => registration.handler !== handler));
  }

  emit(type, event) {
    for (const registration of [...(this.listeners.get(type) ?? [])]) {
      registration.handler(event);
      if (registration.once) this.removeEventListener(type, registration.handler);
    }
  }

  send(text) {
    let message;
    try {
      message = JSON.parse(text);
    } catch {
      return;
    }

    if (message.type === "CLIENT_ASR") {
      this.reply({ type: "LISTEN" });
      this.reply({ type: "EOS" });
      this.reply({ type: "SKILL_ACTION", data: { skill: { id: "@be/joke" } } });
    } else if (message.type === "CLIENT_NLU") {
      this.reply({ type: "LISTEN", data: { nlu: { intent: message.data.intent } } });
      this.reply({ type: "EOS" });
    }
  }

  reply(payload) {
    queueMicrotask(() => this.emit("message", { data: JSON.stringify(payload) }));
  }

  close() {
    if (this.readyState >= 2) return;
    this.readyState = 2;
    queueMicrotask(() => {
      this.readyState = 3;
      this.emit("close", {});
    });
  }
}

test("conversion scripts preserve explicit self-hosted API and Hub endpoints", () => {
  const scriptUrls = [
    new URL("../../scripts/bootstrap/apply-openjibo-conversion.sh", import.meta.url),
    new URL("../../scripts/bootstrap/plan-openjibo-conversion.sh", import.meta.url),
  ];

  for (const scriptUrl of scriptUrls) {
    const source = readFileSync(scriptUrl, "utf8");
    const parserSource = source.match(/function parseEndpoint\(value, defaultPort\) \{[\s\S]*?\n\}/)?.[0];
    assert(parserSource, `${fileURLToPath(scriptUrl)} does not contain parseEndpoint.`);
    const context = {};
    vm.createContext(context);
    vm.runInContext(parserSource, context);

    const api = context.parseEndpoint("http://192.168.1.133:8080", 443);
    const hub = context.parseEndpoint("192.168.1.133:9000", api.port);
    const managed = context.parseEndpoint("neohub.openjibo.com", 443);

    assert.deepEqual({ ...api }, { hostname: "192.168.1.133", port: 8080, secure: false });
    assert.deepEqual({ ...hub }, { hostname: "192.168.1.133", port: 9000, secure: false });
    assert.deepEqual({ ...managed }, { hostname: "neohub.openjibo.com", port: 443, secure: true });
  }
});

test("normalizeLoadOptions validates and preserves bounded load controls", () => {
  assert.deepEqual(normalizeLoadOptions({
    robotCount: "20",
    turnPercent: "50",
    turnRounds: "4",
    holdMs: "1000",
    roundIntervalMs: "250",
    timeoutMs: "9000",
  }), {
    robotCount: 20,
    turnPercent: 50,
    turnRounds: 4,
    holdMs: 1000,
    roundIntervalMs: 250,
    timeoutMs: 9000,
  });
  assert.throws(() => normalizeLoadOptions({ robotCount: 0 }), /robotCount/);
  assert.throws(() => normalizeLoadOptions({ turnPercent: 101 }), /turnPercent/);
  assert.throws(() => normalizeLoadOptions({ turnRounds: "not-a-number" }), /turnRounds/);
});

test("createProtocolCaller classifies registration as deployment smoke", async () => {
  const requests = [];
  const call = createProtocolCaller("https://staging.example", "api.openjibo.com", async (url, options) => {
    requests.push({ url, options });
    return { ok: true, text: async () => "{}" };
  }, "test-smoke-secret");

  await call("Notification_20160715", "NewRobotToken", { deviceId: "open-jibo-smoke-staging-primary" });
  await call("Robot_20160225", "GetRobot", { id: "open-jibo-smoke-staging-primary" });

  assert.equal(requests[0].options.headers["X-OpenJibo-Registration-Source"], "deployment-smoke");
  assert.equal(requests[0].options.headers["X-OpenJibo-Release-Smoke-Secret"], "test-smoke-secret");
  assert.equal(requests[1].options.headers["X-OpenJibo-Registration-Source"], undefined);
  assert.equal(requests[1].options.headers["X-OpenJibo-Release-Smoke-Secret"], undefined);
});

test("managed registration tolerates bounded authorization rollout and no other failures", async () => {
  let calls = 0;
  const delays = [];
  const rolloutCall = withDeploymentSmokeAuthorizationRetry(async () => {
    calls += 1;
    if (calls < 3) {
      const error = new Error("not ready");
      error.status = 403;
      error.responseText = '{"message":"Deployment smoke is not authorized."}';
      throw error;
    }
    return { token: "ready-token" };
  }, {
    attempts: 3,
    intervalMs: 5,
    delay: async (milliseconds) => delays.push(milliseconds),
  });

  assert.deepEqual(await rolloutCall("Notification_20160715", "NewRobotToken", {}),
    { token: "ready-token" });
  assert.equal(calls, 3);
  assert.deepEqual(delays, [5, 5]);

  const wrongSecret = new Error("wrong secret");
  wrongSecret.status = 403;
  wrongSecret.responseText = '{"message":"Forbidden."}';
  const failClosed = withDeploymentSmokeAuthorizationRetry(async () => { throw wrongSecret; }, {
    attempts: 3,
    delay: async () => assert.fail("unrelated 403 must not be retried"),
  });
  await assert.rejects(failClosed("Notification_20160715", "NewRobotToken", {}), wrongSecret);
});

function runManagedCli(environment) {
  const script = fileURLToPath(new URL("../../scripts/cloud/invoke-release-smoke.mjs", import.meta.url));
  return spawnSync(process.execPath, [script], {
    encoding: "utf8",
    env: {
      ...process.env,
      OPENJIBO_RELEASE_SMOKE_DANGEROUSLY_ALLOW_PRODUCTION: "",
      ...environment,
    },
  });
}

test("managed release smoke requires an exact positively authorized hostname", () => {
  const missing = runManagedCli({
    BASE_URL: "https://staging.example:8443",
    OPENJIBO_RELEASE_SMOKE_SECRET: "test-smoke-secret",
    TEST_ROBOT_ID: "open-jibo-smoke-staging",
  });
  assert.equal(missing.status, 2);
  assert.match(missing.stderr, /OPENJIBO_RELEASE_SMOKE_ALLOWED_HOST/);

  const mismatch = runManagedCli({
    BASE_URL: "https://staging.example:8443",
    OPENJIBO_RELEASE_SMOKE_ALLOWED_HOST: "other.example",
    OPENJIBO_RELEASE_SMOKE_SECRET: "test-smoke-secret",
    TEST_ROBOT_ID: "open-jibo-smoke-staging",
  });
  assert.equal(mismatch.status, 2);
  assert.match(mismatch.stderr, /does not match/);
});

test("managed release smoke normalizes hostname case and ports before refusing production aliases", () => {
  for (const [url, allowedHost] of [
    ["https://API.OPENJIBO.COM:443", "api.openjibo.com"],
    ["https://api.jibo.com:8443", "API.JIBO.COM"],
    ["https://open-jibo.jibo.pro:443", "open-jibo.jibo.pro"],
  ]) {
    const result = runManagedCli({
      BASE_URL: url,
      OPENJIBO_RELEASE_SMOKE_ALLOWED_HOST: allowedHost,
      OPENJIBO_RELEASE_SMOKE_SECRET: "test-smoke-secret",
      TEST_ROBOT_ID: "open-jibo-smoke-staging",
    });
    assert.equal(result.status, 2);
    assert.match(result.stderr, /Refusing to run release smoke against a production hostname/);
  }
});

test("managed release smoke dangerous production override is explicit and still requires bounded identity", () => {
  const result = runManagedCli({
    BASE_URL: "https://api.openjibo.com:443",
    OPENJIBO_RELEASE_SMOKE_ALLOWED_HOST: "api.openjibo.com",
    OPENJIBO_RELEASE_SMOKE_SECRET: "test-smoke-secret",
    OPENJIBO_RELEASE_SMOKE_DANGEROUSLY_ALLOW_PRODUCTION: "true",
    TEST_ROBOT_ID: "",
  });

  assert.equal(result.status, 2);
  assert.match(result.stderr, /fixed open-jibo-smoke-staging namespace/);
});

test("managed release smoke requires a deployment-scoped secret", () => {
  const result = runManagedCli({
    BASE_URL: "https://staging.example",
    OPENJIBO_RELEASE_SMOKE_ALLOWED_HOST: "staging.example",
    OPENJIBO_RELEASE_SMOKE_SECRET: "",
    TEST_ROBOT_ID: "open-jibo-smoke-staging",
  });

  assert.equal(result.status, 2);
  assert.match(result.stderr, /OPENJIBO_RELEASE_SMOKE_SECRET/);
});

test("runReleaseSmoke holds quiet robots and completes rotating concurrent turns", async () => {
  const protocolCall = async (service, operation, body) => {
    if (service === "Notification_20160715" && operation === "NewRobotToken")
      return { token: `token-${body.deviceId}` };
    if (service === "Account_20160715" && operation === "CreateHubToken")
      return { token: `hub-${body.deviceId}` };
    if (service === "Robot_20160225" && operation === "GetRobot") return { id: body.id };
    throw new Error(`Unexpected protocol call ${service}.${operation}`);
  };

  const result = await runReleaseSmoke({
    baseUrl: "http://localhost:8080",
    protocolCall,
    WebSocketImpl: FakeWebSocket,
    robotPrefix: "test-load",
    concurrency: 4,
    turnPercent: 50,
    turnRounds: 2,
    holdMs: 0,
    roundIntervalMs: 0,
    timeoutMs: 500,
  });

  assert.equal(result.ok, true);
  assert.equal(result.load.robotCount, 4);
  assert.equal(result.load.activeTurnsPerRound, 2);
  assert.equal(result.load.completedTurns, 4);
  assert.equal(result.results.find((step) => step.name.includes("connected fake robots"))?.status, "passed");
  assert.equal(result.results.length, 7);
});
