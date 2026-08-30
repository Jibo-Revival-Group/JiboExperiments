import assert from "node:assert/strict";
import test from "node:test";
import {
  formatProbeStepDetail,
  isPrivateTarget,
  normalizeProbeOptions,
  probeErrorMessage,
  runJetstreamCompatibilityProbe,
  tokenFingerprint,
} from "../../scripts/cloud/jetstream-compatibility-probe.mjs";

test("aggregate connection failures retain useful diagnostics", () => {
  const ipv6 = Object.assign(new Error("connect ECONNREFUSED ::1:8080"), { code: "ECONNREFUSED" });
  const ipv4 = Object.assign(new Error("connect ECONNREFUSED 127.0.0.1:8080"), { code: "ECONNREFUSED" });
  assert.equal(probeErrorMessage(new AggregateError([ipv6, ipv4])),
    "connect ECONNREFUSED ::1:8080; connect ECONNREFUSED 127.0.0.1:8080");
});

test("step details render structured probe evidence instead of object coercion", () => {
  assert.equal(formatProbeStepDetail(null), "");
  assert.equal(formatProbeStepDetail("connected"), "connected");
  assert.equal(formatProbeStepDetail({ listenConnected: true, thirdSocketRejected: true }),
    '{"listenConnected":true,"thirdSocketRejected":true}');
});

class FakeSocketClient {
  constructor({ tokenlessAccepted = false } = {}) {
    this.tokenlessAccepted = tokenlessAccepted;
    this.opens = [];
    this.openedSockets = [];
    this.activeTokenless = 0;
    this.activeSockets = 0;
    this.maxActiveSockets = 0;
  }

  async open(options) {
    this.opens.push(options);
    const authorization = options.headers?.Authorization;
    const tokenlessHub = !authorization && new URL(options.url).pathname.startsWith("/v1/");
    if (tokenlessHub) {
      if (!this.tokenlessAccepted || this.activeTokenless >= 2) {
        const error = new Error("rejected");
        error.statusCode = 401;
        throw error;
      }
      this.activeTokenless += 1;
    }
    this.activeSockets += 1;
    this.maxActiveSockets = Math.max(this.maxActiveSockets, this.activeSockets);
    const socket = { options, tokenlessHub, readyState: 1 };
    this.openedSockets.push(socket);
    return socket;
  }

  send(socket, payload) {
    socket.sent ??= [];
    socket.sent.push(JSON.parse(payload));
  }

  async readJson(socket) {
    if (new URL(socket.options.url).pathname === "/v1/proactive") {
      return [{ type: "PROACTIVE", final: true, data: {} }];
    }
    return [
      { type: "LISTEN" },
      { type: "EOS" },
      { type: "SKILL_ACTION", data: { skill: { id: "@be/joke" } } },
    ];
  }

  async close(socket) {
    if (socket.tokenlessHub) this.activeTokenless -= 1;
    this.activeSockets -= 1;
    socket.readyState = 3;
  }
}

function fakeFetch() {
  let sequence = 0;
  const calls = [];
  const fetchImpl = async (url, options) => {
    calls.push({ url, options });
    const currentSequence = ++sequence;
    const target = options.headers["X-Amz-Target"];
    const kind = target.includes("NewRobotToken") ? "robot" : "hub";
    return {
      ok: true,
      status: 200,
      text: async () => JSON.stringify({ token: `${kind}-secret-${currentSequence}` }),
    };
  };
  return { calls, fetchImpl };
}

test("normalization derives local Hub defaults and validates bounded options", () => {
  const normalized = normalizeProbeOptions({
    entrypointUrl: "http://127.0.0.1:24605",
    robots: "2",
    mode: "both",
    expectTokenless: "accepted",
    notificationUrl: "ws://127.0.0.1:24606",
  });
  assert.equal(normalized.hubUrl.href, "ws://127.0.0.1:24605/");
  assert.equal(normalized.notificationUrl.href, "ws://127.0.0.1:24606/");
  assert.equal(normalized.robots, 2);
  assert.equal(normalized.sendListenTurn, true);
  assert.equal(normalized.sendProactiveTransaction, true);
  const proactiveOnly = normalizeProbeOptions({ sendListenTurn: false });
  assert.equal(proactiveOnly.sendListenTurn, false);
  assert.equal(proactiveOnly.sendProactiveTransaction, true);
  const handshakeOnly = normalizeProbeOptions({ sendTurn: false });
  assert.equal(handshakeOnly.sendListenTurn, false);
  assert.equal(handshakeOnly.sendProactiveTransaction, false);
  assert.throws(() => normalizeProbeOptions({ robots: 21 }), /1 through 20/);
  assert.throws(() => normalizeProbeOptions({ hubUrl: "http://localhost:8080" }), /ws: or wss:/);
});

test("private target classification covers LAN addresses without trusting arbitrary domains", () => {
  for (const host of ["localhost", "robot.local", "127.0.0.1", "10.0.0.80", "172.20.0.1", "192.168.1.10", "::1", "fd00::1"])
    assert.equal(isPrivateTarget(host), true, host);
  for (const host of ["example.com", "8.8.8.8", "203.0.113.10"])
    assert.equal(isPrivateTarget(host), false, host);
});

test("fingerprints are stable and never expose the token", () => {
  const token = "hub-super-secret-value";
  const fingerprint = tokenFingerprint(token);
  assert.equal(fingerprint.length, 12);
  assert.equal(fingerprint, tokenFingerprint(token));
  assert.equal(fingerprint.includes("secret"), false);
  assert.equal(tokenFingerprint(null), "none");
});

test("authenticated probe issues tokens and uses stock-style bearer Hub headers", async () => {
  const http = fakeFetch();
  const sockets = new FakeSocketClient();
  const result = await runJetstreamCompatibilityProbe({
    entrypointUrl: "http://127.0.0.1:8080",
    mode: "authenticated",
    robots: 2,
    holdMs: 0,
    notificationUrl: "ws://127.0.0.1:9090",
  }, { fetchImpl: http.fetchImpl, socketClient: sockets });

  assert.equal(result.authenticated.length, 2);
  assert.equal(http.calls.length, 4);
  assert(sockets.maxActiveSockets > 3, "Multiple robot socket lifetimes did not overlap.");
  assert.equal(new Set(result.authenticated.map((robot) => robot.hubTokenFingerprint)).size, 2);
  const hubSockets = sockets.opens.filter((item) => new URL(item.url).pathname.startsWith("/v1/"));
  assert.equal(hubSockets.length, 4);
  assert(hubSockets.every((item) => item.headers.Authorization.startsWith("Bearer hub-secret-")));
  assert(hubSockets.every((item) => !item.url.includes("secret")));
  const notificationSockets = sockets.opens.filter((item) => new URL(item.url).port === "9090");
  assert.equal(notificationSockets.length, 2);
  const notificationCalls = http.calls.filter((call) =>
    call.options.headers["X-Amz-Target"] === "Notification_20150505.NewRobotToken");
  assert.equal(notificationCalls.length, 2);
  assert.deepEqual(Object.keys(JSON.parse(notificationCalls[0].options.body)), ["deviceId"]);
  assert.deepEqual(result.authenticated.map((robot) => robot.proactiveReplyTypes),
    [["PROACTIVE"], ["PROACTIVE"]]);
  const proactiveSocketPayloads = sockets.openedSockets
    .filter((socket) => new URL(socket.options.url).pathname === "/v1/proactive")
    .map((socket) => socket.sent.map((message) => message.type));
  assert.deepEqual(proactiveSocketPayloads, [["TRIGGER", "CONTEXT"], ["TRIGGER", "CONTEXT"]]);
  const output = JSON.stringify(result);
  assert.equal(output.includes("super-secret"), false);
  assert.equal(output.includes("hub-secret"), false);
  assert.equal(output.includes("robot-secret"), false);
});

test("tokenless rejected mode records the expected handshake rejection", async () => {
  const result = await runJetstreamCompatibilityProbe({
    mode: "tokenless",
    expectTokenless: "rejected",
    holdMs: 0,
  }, { fetchImpl: fakeFetch().fetchImpl, socketClient: new FakeSocketClient() });
  assert.equal(result.tokenless.rejected, true);
  assert.equal(result.tokenless.statusCode, 401);
});

test("tokenless accepted mode holds two leases and requires the third rejection", async () => {
  const sockets = new FakeSocketClient({ tokenlessAccepted: true });
  const result = await runJetstreamCompatibilityProbe({
    mode: "tokenless",
    expectTokenless: "accepted",
    holdMs: 0,
  }, { fetchImpl: fakeFetch().fetchImpl, socketClient: sockets });
  assert.equal(result.tokenless.listenConnected, true);
  assert.equal(result.tokenless.proactiveConnected, true);
  assert.equal(result.tokenless.thirdSocketRejected, true);
  assert.deepEqual(result.tokenless.proactiveReplyTypes, ["PROACTIVE"]);
});

test("proactive-only mode skips CLIENT_ASR and still proves TRIGGER then CONTEXT", async () => {
  const sockets = new FakeSocketClient({ tokenlessAccepted: true });
  const result = await runJetstreamCompatibilityProbe({
    mode: "tokenless",
    expectTokenless: "accepted",
    sendListenTurn: false,
    holdMs: 0,
  }, { fetchImpl: fakeFetch().fetchImpl, socketClient: sockets });
  const listen = sockets.openedSockets.find((socket) =>
    new URL(socket.options.url).pathname === "/v1/listen");
  const proactive = sockets.openedSockets.find((socket) =>
    new URL(socket.options.url).pathname === "/v1/proactive");
  assert.equal(listen.sent, undefined);
  assert.deepEqual(proactive.sent.map((message) => message.type), ["TRIGGER", "CONTEXT"]);
  assert.deepEqual(result.tokenless.proactiveReplyTypes, ["PROACTIVE"]);
});
