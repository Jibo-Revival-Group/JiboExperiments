const DEFAULT_TIMEOUT_MS = 6000;
const DEFAULT_LOAD_OPTIONS = Object.freeze({
  robotCount: 6,
  turnPercent: 25,
  turnRounds: 1,
  holdMs: 500,
  roundIntervalMs: 0,
  timeoutMs: DEFAULT_TIMEOUT_MS,
});

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function boundedInteger(value, name, minimum, maximum) {
  const parsed = typeof value === "number" ? value : Number.parseInt(String(value), 10);
  assert(Number.isInteger(parsed) && parsed >= minimum && parsed <= maximum,
    `${name} must be an integer from ${minimum} through ${maximum}.`);
  return parsed;
}

export function normalizeLoadOptions(options = {}) {
  return {
    robotCount: boundedInteger(options.robotCount ?? options.concurrency ?? DEFAULT_LOAD_OPTIONS.robotCount,
      "robotCount", 1, 100),
    turnPercent: boundedInteger(options.turnPercent ?? DEFAULT_LOAD_OPTIONS.turnPercent,
      "turnPercent", 0, 100),
    turnRounds: boundedInteger(options.turnRounds ?? DEFAULT_LOAD_OPTIONS.turnRounds,
      "turnRounds", 1, 1000),
    holdMs: boundedInteger(options.holdMs ?? DEFAULT_LOAD_OPTIONS.holdMs,
      "holdMs", 0, 86_400_000),
    roundIntervalMs: boundedInteger(options.roundIntervalMs ?? DEFAULT_LOAD_OPTIONS.roundIntervalMs,
      "roundIntervalMs", 0, 3_600_000),
    timeoutMs: boundedInteger(options.timeoutMs ?? DEFAULT_LOAD_OPTIONS.timeoutMs,
      "timeoutMs", 100, 120_000),
  };
}

function delay(milliseconds) {
  return milliseconds > 0 ? new Promise((resolve) => setTimeout(resolve, milliseconds)) : Promise.resolve();
}

function percentile(values, fraction) {
  if (!values.length) return null;
  const sorted = [...values].sort((left, right) => left - right);
  return sorted[Math.ceil(fraction * sorted.length) - 1];
}

function websocketUrl(baseUrl, path) {
  const url = new URL(path, `${baseUrl.replace(/\/$/, "")}/`);
  url.protocol = url.protocol === "https:" ? "wss:" : "ws:";
  return url.toString();
}

function socketError(label, event) {
  return new Error(`${label} WebSocket failed${event?.message ? `: ${event.message}` : "."}`);
}

function openSocket(WebSocketImpl, url, label, timeoutMs = DEFAULT_TIMEOUT_MS) {
  return new Promise((resolve, reject) => {
    const socket = new WebSocketImpl(url);
    const timer = setTimeout(() => {
      socket.close();
      reject(new Error(`${label} WebSocket did not open within ${timeoutMs} ms.`));
    }, timeoutMs);
    socket.addEventListener("open", () => {
      clearTimeout(timer);
      resolve(socket);
    }, { once: true });
    socket.addEventListener("error", (event) => {
      clearTimeout(timer);
      reject(socketError(label, event));
    }, { once: true });
  });
}

function closeSocket(socket) {
  if (socket.readyState >= 2) return Promise.resolve();
  return new Promise((resolve) => {
    const timer = setTimeout(resolve, 1000);
    socket.addEventListener("close", () => {
      clearTimeout(timer);
      resolve();
    }, { once: true });
    socket.close(1000, "release-smoke-complete");
  });
}

function readReplyTypes(socket, expectedCount, label, timeoutMs = DEFAULT_TIMEOUT_MS) {
  return new Promise((resolve, reject) => {
    const replies = [];
    const timer = setTimeout(() => {
      cleanup();
      reject(new Error(`${label} received [${replies.map((reply) => reply.type).join(", ")}] before timing out.`));
    }, timeoutMs);
    const onMessage = (event) => {
      try {
        const reply = JSON.parse(typeof event.data === "string" ? event.data : String(event.data));
        replies.push(reply);
        if (replies.length >= expectedCount) {
          cleanup();
          resolve(replies);
        }
      } catch (error) {
        cleanup();
        reject(new Error(`${label} returned invalid JSON: ${error.message}`));
      }
    };
    const onClose = () => {
      cleanup();
      reject(new Error(`${label} closed before returning ${expectedCount} replies.`));
    };
    const cleanup = () => {
      clearTimeout(timer);
      socket.removeEventListener("message", onMessage);
      socket.removeEventListener("close", onClose);
    };
    socket.addEventListener("message", onMessage);
    socket.addEventListener("close", onClose, { once: true });
  });
}

function assertReplyOrder(replies, expected, label) {
  const actual = replies.map((reply) => reply.type);
  assert(actual.length === expected.length && expected.every((type, index) => actual[index] === type),
    `${label} expected ${expected.join(" -> ")} but received ${actual.join(" -> ")}.`);
}

async function expectConnectionScopedFallback(WebSocketImpl, url, timeoutMs = DEFAULT_TIMEOUT_MS) {
  const socket = await openSocket(WebSocketImpl, url, "Tokenless fallback", timeoutMs);
  await closeSocket(socket);
}

export function createProtocolCaller(baseUrl, hostName = "api.openjibo.com", fetchImpl = globalThis.fetch,
  releaseSmokeSecret = null) {
  return async (service, operation, body) => {
    const headers = {
      "Content-Type": "application/json",
      "X-Amz-Target": `${service}.${operation}`,
      "X-OpenJibo-Harness-Host": hostName,
      "X-OpenJibo-AppVersion": "1.0.20",
    };
    if (releaseSmokeSecret && service === "Notification_20160715" && operation === "NewRobotToken") {
      headers["X-OpenJibo-Registration-Source"] = "deployment-smoke";
      headers["X-OpenJibo-Release-Smoke-Secret"] = releaseSmokeSecret;
    }
    const response = await fetchImpl(`${baseUrl.replace(/\/$/, "")}/`, {
      method: "POST",
      headers,
      body: JSON.stringify(body),
    });
    const text = await response.text();
    const payload = text ? JSON.parse(text) : null;
    if (!response.ok) {
      const error = new Error(`${service}.${operation} returned HTTP ${response.status}: ${text}`);
      error.status = response.status;
      error.responseText = text;
      throw error;
    }
    return payload;
  };
}

export function withDeploymentSmokeAuthorizationRetry(protocolCall, {
  attempts = 12,
  intervalMs = 5000,
  delay = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds)),
} = {}) {
  return async (service, operation, body) => {
    const isRegistration = service === "Notification_20160715" && operation === "NewRobotToken";
    if (!isRegistration) return protocolCall(service, operation, body);

    for (let attempt = 1; ; attempt += 1) {
      try {
        return await protocolCall(service, operation, body);
      } catch (error) {
        const isAuthorizationRollout = error?.status === 403 &&
          error?.responseText?.includes("Deployment smoke is not authorized.");
        if (!isAuthorizationRollout || attempt >= attempts) throw error;
        await delay(intervalMs);
      }
    }
  };
}

export async function runReleaseSmoke({
  baseUrl,
  protocolCall,
  WebSocketImpl = globalThis.WebSocket,
  robotPrefix = `release-smoke-${Date.now()}`,
  concurrency = 6,
  turnPercent,
  turnRounds,
  holdMs,
  roundIntervalMs,
  timeoutMs,
  onStep = () => {},
}) {
  assert(baseUrl, "baseUrl is required.");
  assert(typeof protocolCall === "function", "protocolCall is required.");
  assert(typeof WebSocketImpl === "function", "A WebSocket implementation is required.");
  const loadOptions = normalizeLoadOptions({
    concurrency,
    turnPercent,
    turnRounds,
    holdMs,
    roundIntervalMs,
    timeoutMs,
  });
  const results = [];
  const runStep = async (name, action) => {
    const started = Date.now();
    onStep({ name, status: "running" });
    try {
      const detail = await action();
      const result = { name, status: "passed", durationMs: Date.now() - started, detail };
      results.push(result);
      onStep(result);
      return detail;
    } catch (error) {
      const result = { name, status: "failed", durationMs: Date.now() - started, detail: error.message };
      results.push(result);
      onStep(result);
      throw Object.assign(error, { results });
    }
  };

  let token;
  const deviceId = `${robotPrefix}-primary`;
  await runStep("Issue robot token and persist identity", async () => {
    const issued = await protocolCall("Notification_20160715", "NewRobotToken", {
      deviceId,
      robotId: deviceId,
    });
    token = issued?.token;
    assert(token, `NewRobotToken did not return a token: ${JSON.stringify(issued)}`);
    const robot = await protocolCall("Robot_20160225", "GetRobot", { id: deviceId });
    assert(robot, "GetRobot did not return the persisted fake robot.");
    return `persisted ${deviceId}`;
  });

  await runStep("Notification socket connect and reconnect", async () => {
    const url = websocketUrl(baseUrl, `/${encodeURIComponent(token)}`);
    const first = await openSocket(WebSocketImpl, url, "notification", loadOptions.timeoutMs);
    await closeSocket(first);
    const second = await openSocket(WebSocketImpl, url, "notification reconnect", loadOptions.timeoutMs);
    await closeSocket(second);
    return "connected twice with the issued token";
  });

  await runStep("CLIENT_ASR joke response order", async () => {
    const transID = `${robotPrefix}-joke`;
    const socket = await openSocket(WebSocketImpl,
      websocketUrl(baseUrl, `/v1/listen/${encodeURIComponent(token)}`), "NeoHub joke", loadOptions.timeoutMs);
    try {
      const repliesPromise = readReplyTypes(socket, 3, "joke turn", loadOptions.timeoutMs);
      socket.send(JSON.stringify({
        type: "CLIENT_ASR",
        transID,
        data: { text: "tell me a joke", rules: ["wake-word"] },
      }));
      const replies = await repliesPromise;
      assertReplyOrder(replies, ["LISTEN", "EOS", "SKILL_ACTION"], "joke turn");
      assert(replies[2]?.data?.skill?.id === "@be/joke", "Joke turn returned the wrong skill action.");
      return replies.map((reply) => reply.type).join(" -> ");
    } finally {
      await closeSocket(socket);
    }
  });

  await runStep("Malformed frame recovery and CLIENT_NLU clock turn", async () => {
    const transID = `${robotPrefix}-clock`;
    const socket = await openSocket(WebSocketImpl,
      websocketUrl(baseUrl, `/v1/listen/${encodeURIComponent(token)}-clock`), "NeoHub clock",
      loadOptions.timeoutMs);
    try {
      socket.send("{not-valid-json");
      socket.send(JSON.stringify({
        type: "LISTEN",
        transID,
        data: { lang: "en-US", rules: ["clock/clock_menu"], mode: "CLIENT_NLU" },
      }));
      await new Promise((resolve) => setTimeout(resolve, 100));
      const repliesPromise = readReplyTypes(socket, 2, "clock turn", loadOptions.timeoutMs);
      socket.send(JSON.stringify({
        type: "CLIENT_NLU",
        transID,
        data: { intent: "askForTime", rules: ["clock/clock_menu"], entities: { domain: "clock" } },
      }));
      const replies = await repliesPromise;
      assertReplyOrder(replies, ["LISTEN", "EOS"], "clock turn");
      assert(replies[0]?.data?.nlu?.intent === "askForTime", "Clock turn did not preserve the NLU intent.");
      return replies.map((reply) => reply.type).join(" -> ");
    } finally {
      await closeSocket(socket);
    }
  });

  await runStep("Tokenless connection-scoped fallback", async () => {
    await expectConnectionScopedFallback(WebSocketImpl, websocketUrl(baseUrl, "/v1/listen"),
      loadOptions.timeoutMs);
    return "connection accepted and closed cleanly";
  });

  let loadSummary;
  await runStep(`${loadOptions.robotCount} connected fake robots with concurrent turns`, async () => {
    const sockets = [];
    try {
      const tokens = await Promise.all(Array.from({ length: loadOptions.robotCount }, async (_, index) => {
        const concurrentDeviceId = `${robotPrefix}-concurrent-${index + 1}`;
        const issued = await protocolCall("Notification_20160715", "NewRobotToken", {
          deviceId: concurrentDeviceId,
          robotId: concurrentDeviceId,
        });
        assert(issued?.token, `Concurrent fake robot ${index + 1} did not receive a token.`);
        return issued.token;
      }));
      sockets.push(...await Promise.all(tokens.map((issuedToken, index) =>
        openSocket(WebSocketImpl, websocketUrl(baseUrl, `/${encodeURIComponent(issuedToken)}`),
          `concurrent robot ${index + 1}`, loadOptions.timeoutMs))));
      await delay(loadOptions.holdMs);
      assert(sockets.every((socket) => socket.readyState === 1),
        "One or more concurrent robot sockets closed unexpectedly.");

      const activeTurnsPerRound = loadOptions.turnPercent === 0
        ? 0
        : Math.max(1, Math.ceil(loadOptions.robotCount * loadOptions.turnPercent / 100));
      const durations = [];
      for (let round = 0; round < loadOptions.turnRounds; round += 1) {
        const selected = Array.from({ length: activeTurnsPerRound }, (_, index) =>
          (round * activeTurnsPerRound + index) % tokens.length);
        const roundDurations = await Promise.all(selected.map(async (robotIndex) => {
          const label = `load robot ${robotIndex + 1} round ${round + 1}`;
          const started = Date.now();
          const listenSocket = await openSocket(WebSocketImpl,
            websocketUrl(baseUrl, `/v1/listen/${encodeURIComponent(tokens[robotIndex])}`), label,
            loadOptions.timeoutMs);
          try {
            const repliesPromise = readReplyTypes(listenSocket, 3, label, loadOptions.timeoutMs);
            listenSocket.send(JSON.stringify({
              type: "CLIENT_ASR",
              transID: `${robotPrefix}-load-${round + 1}-${robotIndex + 1}`,
              data: { text: "tell me a joke", rules: ["wake-word"] },
            }));
            const replies = await repliesPromise;
            assertReplyOrder(replies, ["LISTEN", "EOS", "SKILL_ACTION"], label);
            assert(replies[2]?.data?.skill?.id === "@be/joke", `${label} returned the wrong skill action.`);
            return Date.now() - started;
          } finally {
            await closeSocket(listenSocket);
          }
        }));
        durations.push(...roundDurations);
        if (round + 1 < loadOptions.turnRounds) await delay(loadOptions.roundIntervalMs);
      }

      assert(sockets.every((socket) => socket.readyState === 1),
        "One or more quiet notification sockets closed while turns were running.");
      loadSummary = {
        ...loadOptions,
        activeTurnsPerRound,
        completedTurns: durations.length,
        turnLatencyMs: {
          min: durations.length ? Math.min(...durations) : null,
          p50: percentile(durations, 0.50),
          p95: percentile(durations, 0.95),
          max: durations.length ? Math.max(...durations) : null,
        },
      };
      return `${sockets.length} sockets; ${durations.length} turns; p95 ${loadSummary.turnLatencyMs.p95 ?? "n/a"} ms`;
    } finally {
      await Promise.all(sockets.map(closeSocket));
    }
  });

  await runStep("Persisted robot survives socket sessions", async () => {
    const robot = await protocolCall("Robot_20160225", "GetRobot", { id: deviceId });
    assert(robot, "Persisted fake robot could not be read after socket reconnects.");
    return `read ${deviceId} after all socket sessions closed`;
  });

  return { ok: true, baseUrl, robotPrefix, concurrency: loadOptions.robotCount, load: loadSummary, results };
}
