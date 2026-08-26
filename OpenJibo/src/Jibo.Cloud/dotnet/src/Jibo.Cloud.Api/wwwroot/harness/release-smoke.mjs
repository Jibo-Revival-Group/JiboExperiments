const DEFAULT_TIMEOUT_MS = 6000;

function assert(condition, message) {
  if (!condition) throw new Error(message);
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

async function expectRejectedSocket(WebSocketImpl, url, timeoutMs = DEFAULT_TIMEOUT_MS) {
  await new Promise((resolve, reject) => {
    const socket = new WebSocketImpl(url);
    const timer = setTimeout(() => {
      socket.close();
      reject(new Error("Missing-token WebSocket was not rejected promptly."));
    }, timeoutMs);
    socket.addEventListener("open", () => {
      clearTimeout(timer);
      socket.close();
      reject(new Error("Missing-token WebSocket unexpectedly opened."));
    }, { once: true });
    const rejected = () => {
      clearTimeout(timer);
      resolve();
    };
    socket.addEventListener("error", rejected, { once: true });
    socket.addEventListener("close", rejected, { once: true });
  });
}

export function createProtocolCaller(baseUrl, hostName = "api.openjibo.com", fetchImpl = globalThis.fetch) {
  return async (service, operation, body) => {
    const response = await fetchImpl(`${baseUrl.replace(/\/$/, "")}/`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Amz-Target": `${service}.${operation}`,
        "X-OpenJibo-Harness-Host": hostName,
        "X-OpenJibo-AppVersion": "1.0.20",
        "X-OpenJibo-Registration-Source": "release-smoke",
      },
      body: JSON.stringify(body),
    });
    const text = await response.text();
    const payload = text ? JSON.parse(text) : null;
    if (!response.ok) throw new Error(`${service}.${operation} returned HTTP ${response.status}: ${text}`);
    return payload;
  };
}

export async function runReleaseSmoke({
  baseUrl,
  protocolCall,
  WebSocketImpl = globalThis.WebSocket,
  robotPrefix = `release-smoke-${Date.now()}`,
  concurrency = 6,
  onStep = () => {},
}) {
  assert(baseUrl, "baseUrl is required.");
  assert(typeof protocolCall === "function", "protocolCall is required.");
  assert(typeof WebSocketImpl === "function", "A WebSocket implementation is required.");
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
    assert(token, "NewRobotToken did not return a token.");
    const robot = await protocolCall("Robot_20160225", "GetRobot", { id: deviceId });
    assert(robot, "GetRobot did not return the persisted fake robot.");
    return `persisted ${deviceId}`;
  });

  await runStep("Notification socket connect and reconnect", async () => {
    const url = websocketUrl(baseUrl, `/${encodeURIComponent(token)}`);
    const first = await openSocket(WebSocketImpl, url, "notification");
    await closeSocket(first);
    const second = await openSocket(WebSocketImpl, url, "notification reconnect");
    await closeSocket(second);
    return "connected twice with the issued token";
  });

  await runStep("CLIENT_ASR joke response order", async () => {
    const transID = `${robotPrefix}-joke`;
    const socket = await openSocket(WebSocketImpl,
      websocketUrl(baseUrl, `/v1/listen/${encodeURIComponent(token)}`), "NeoHub joke");
    try {
      const repliesPromise = readReplyTypes(socket, 3, "joke turn");
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
      websocketUrl(baseUrl, `/v1/listen/${encodeURIComponent(token)}-clock`), "NeoHub clock");
    try {
      socket.send("{not-valid-json");
      socket.send(JSON.stringify({
        type: "LISTEN",
        transID,
        data: { lang: "en-US", rules: ["clock/clock_menu"], mode: "CLIENT_NLU" },
      }));
      await new Promise((resolve) => setTimeout(resolve, 100));
      const repliesPromise = readReplyTypes(socket, 2, "clock turn");
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

  await runStep("Missing-token socket rejection", async () => {
    await expectRejectedSocket(WebSocketImpl, websocketUrl(baseUrl, "/"));
    return "connection rejected before WebSocket upgrade";
  });

  await runStep(`${concurrency} concurrent fake robot sockets`, async () => {
    const sockets = [];
    try {
      const tokens = await Promise.all(Array.from({ length: concurrency }, async (_, index) => {
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
          `concurrent robot ${index + 1}`))));
      await new Promise((resolve) => setTimeout(resolve, 500));
      assert(sockets.every((socket) => socket.readyState === 1),
        "One or more concurrent robot sockets closed unexpectedly.");
      return `${sockets.length} sockets remained open together`;
    } finally {
      await Promise.all(sockets.map(closeSocket));
    }
  });

  await runStep("Persisted robot survives socket sessions", async () => {
    const robot = await protocolCall("Robot_20160225", "GetRobot", { id: deviceId });
    assert(robot, "Persisted fake robot could not be read after socket reconnects.");
    return `read ${deviceId} after all socket sessions closed`;
  });

  return { ok: true, baseUrl, robotPrefix, concurrency, results };
}
