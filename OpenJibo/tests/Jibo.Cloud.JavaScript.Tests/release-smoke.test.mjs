import assert from "node:assert/strict";
import test from "node:test";
import {
  normalizeLoadOptions,
  runReleaseSmoke,
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
test("runReleaseSmoke holds quiet robots and completes rotating concurrent turns", async () => {
  const protocolCall = async (service, operation, body) => {
    if (service === "Notification_20160715" && operation === "NewRobotToken")
      return { token: `token-${body.deviceId}` };
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
