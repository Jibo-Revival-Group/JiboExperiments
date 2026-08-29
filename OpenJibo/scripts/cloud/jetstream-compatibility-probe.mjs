import { createHash } from "node:crypto";
import net from "node:net";

const MODES = new Set(["authenticated", "tokenless", "both"]);
const TOKENLESS_EXPECTATIONS = new Set(["accepted", "rejected"]);

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function integer(value, name, minimum, maximum) {
  const parsed = Number(value);
  assert(Number.isInteger(parsed) && parsed >= minimum && parsed <= maximum,
    `${name} must be an integer from ${minimum} through ${maximum}.`);
  return parsed;
}

function normalizeBaseUrl(value, name, protocols) {
  let parsed;
  try {
    parsed = new URL(value);
  } catch {
    throw new Error(`${name} must be an absolute URL.`);
  }
  assert(protocols.includes(parsed.protocol), `${name} must use ${protocols.join(" or ")}.`);
  assert(!parsed.username && !parsed.password, `${name} must not contain credentials.`);
  parsed.hash = "";
  parsed.search = "";
  return parsed;
}

export function normalizeProbeOptions(options = {}) {
  const entrypointUrl = normalizeBaseUrl(options.entrypointUrl ?? "http://127.0.0.1:8080",
    "entrypointUrl", ["http:", "https:"]);
  const derivedHub = new URL(entrypointUrl);
  derivedHub.protocol = entrypointUrl.protocol === "https:" ? "wss:" : "ws:";
  const hubUrl = normalizeBaseUrl(options.hubUrl ?? derivedHub.href, "hubUrl", ["ws:", "wss:"]);
  const notificationUrl = normalizeBaseUrl(options.notificationUrl ?? hubUrl.href,
    "notificationUrl", ["ws:", "wss:"]);
  const mode = options.mode ?? "authenticated";
  const expectTokenless = options.expectTokenless ?? "rejected";
  assert(MODES.has(mode), "mode must be authenticated, tokenless, or both.");
  assert(TOKENLESS_EXPECTATIONS.has(expectTokenless), "expectTokenless must be accepted or rejected.");

  return {
    entrypointUrl,
    hubUrl,
    notificationUrl,
    mode,
    expectTokenless,
    robots: integer(options.robots ?? 1, "robots", 1, 20),
    timeoutMs: integer(options.timeoutMs ?? 6000, "timeoutMs", 250, 120000),
    holdMs: integer(options.holdMs ?? 250, "holdMs", 0, 60000),
    devicePrefix: String(options.devicePrefix ?? "jetstream-probe").trim(),
    includeNotification: options.includeNotification !== false,
    sendTurn: options.sendTurn !== false,
    localAddress: options.localAddress ? String(options.localAddress).trim() : undefined,
    secondaryLocalAddress: options.secondaryLocalAddress
      ? String(options.secondaryLocalAddress).trim()
      : undefined,
  };
}

export function isPrivateTarget(hostname) {
  const normalized = hostname.trim().replace(/^\[|\]$/g, "").toLowerCase();
  if (normalized === "localhost" || normalized.endsWith(".local")) return true;
  const family = net.isIP(normalized);
  if (family === 4) {
    const octets = normalized.split(".").map(Number);
    return octets[0] === 10 || octets[0] === 127 ||
      (octets[0] === 169 && octets[1] === 254) ||
      (octets[0] === 172 && octets[1] >= 16 && octets[1] <= 31) ||
      (octets[0] === 192 && octets[1] === 168);
  }
  if (family === 6) {
    return normalized === "::1" || normalized.startsWith("fe8") || normalized.startsWith("fe9") ||
      normalized.startsWith("fea") || normalized.startsWith("feb") ||
      normalized.startsWith("fc") || normalized.startsWith("fd");
  }
  return false;
}

export function tokenFingerprint(token) {
  if (!token) return "none";
  return createHash("sha256").update(token).digest("hex").slice(0, 12);
}

function endpointUrl(baseUrl, path) {
  const url = new URL(baseUrl);
  url.pathname = path;
  url.search = "";
  url.hash = "";
  return url.href;
}

async function delay(milliseconds) {
  if (milliseconds > 0) await new Promise((resolve) => setTimeout(resolve, milliseconds));
}

async function closeQuietly(socketClient, socket) {
  if (!socket) return;
  try {
    await socketClient.close(socket);
  } catch {
    // Preserve the primary probe result; close failures are diagnostic noise here.
  }
}

async function protocolCall(fetchImpl, entrypointUrl, service, operation, body, deviceId, timeoutMs) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetchImpl(endpointUrl(entrypointUrl, "/"), {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Amz-Target": `${service}.${operation}`,
        "X-Jibo-RobotId": deviceId,
        "X-OpenJibo-AppVersion": "1.0.20",
      },
      body: JSON.stringify(body),
      signal: controller.signal,
    });
    const text = await response.text();
    let payload = null;
    if (text) {
      try {
        payload = JSON.parse(text);
      } catch {
        throw new Error(`${service}.${operation} returned non-JSON HTTP ${response.status}.`);
      }
    }
    if (!response.ok) throw new Error(`${service}.${operation} returned HTTP ${response.status}.`);
    return payload;
  } catch (error) {
    if (error?.name === "AbortError") throw new Error(`${service}.${operation} timed out after ${timeoutMs} ms.`);
    throw error;
  } finally {
    clearTimeout(timer);
  }
}

async function runStep(results, onStep, name, action) {
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
}

function assertReplyOrder(messages, expected) {
  const actual = messages.map((message) => message?.type);
  assert(expected.every((type, index) => actual[index] === type),
    `Expected ${expected.join(" -> ")}, received ${actual.join(" -> ") || "no replies"}.`);
}

async function runAuthenticatedRobot(config, dependencies, deviceId) {
  const { fetchImpl, socketClient } = dependencies;
  let notification;
  let listen;
  let proactive;
  try {
    let robotToken;
    if (config.includeNotification) {
      const robot = await protocolCall(fetchImpl, config.entrypointUrl,
        "Notification_20150505", "NewRobotToken", { deviceId }, deviceId, config.timeoutMs);
      robotToken = robot?.token;
      assert(robotToken, "NewRobotToken response did not contain a token.");
    }

    const hub = await protocolCall(fetchImpl, config.entrypointUrl,
      "Account_20160715", "CreateHubToken", { deviceId }, deviceId, config.timeoutMs);
    const hubToken = hub?.token;
    assert(hubToken, "CreateHubToken response did not contain a token.");

    if (robotToken) {
      notification = await socketClient.open({
        url: endpointUrl(config.notificationUrl, `/${encodeURIComponent(robotToken)}`),
        label: `${deviceId} notification`,
        timeoutMs: config.timeoutMs,
        localAddress: config.localAddress,
      });
    }

    const authorization = { Authorization: `Bearer ${hubToken}` };
    listen = await socketClient.open({
      url: endpointUrl(config.hubUrl, "/v1/listen"),
      headers: authorization,
      label: `${deviceId} listen`,
      timeoutMs: config.timeoutMs,
      localAddress: config.localAddress,
    });
    proactive = await socketClient.open({
      url: endpointUrl(config.hubUrl, "/v1/proactive"),
      headers: authorization,
      label: `${deviceId} proactive`,
      timeoutMs: config.timeoutMs,
      localAddress: config.localAddress,
    });

    let replies = [];
    if (config.sendTurn) {
      const repliesPromise = socketClient.readJson(listen, 3, config.timeoutMs);
      socketClient.send(listen, JSON.stringify({
        type: "CLIENT_ASR",
        transID: `${deviceId}-${Date.now()}`,
        data: { text: "tell me a joke", rules: ["wake-word"] },
      }));
      replies = await repliesPromise;
      assertReplyOrder(replies, ["LISTEN", "EOS", "SKILL_ACTION"]);
    }

    await delay(config.holdMs);
    return {
      deviceId,
      robotTokenFingerprint: tokenFingerprint(robotToken),
      hubTokenFingerprint: tokenFingerprint(hubToken),
      notificationConnected: Boolean(notification),
      listenConnected: true,
      proactiveConnected: true,
      replyTypes: replies.map((message) => message.type),
    };
  } finally {
    await Promise.all([
      closeQuietly(socketClient, proactive),
      closeQuietly(socketClient, listen),
      closeQuietly(socketClient, notification),
    ]);
  }
}

async function expectSocketRejected(socketClient, options, description) {
  let socket;
  try {
    socket = await socketClient.open(options);
  } catch (error) {
    return { description, statusCode: error?.statusCode ?? null, rejected: true };
  }
  await closeQuietly(socketClient, socket);
  throw new Error(`${description} unexpectedly opened.`);
}

async function runTokenless(config, socketClient) {
  const listenOptions = {
    url: endpointUrl(config.hubUrl, "/v1/listen"),
    label: "tokenless listen",
    timeoutMs: config.timeoutMs,
    localAddress: config.localAddress,
  };
  if (config.expectTokenless === "rejected") {
    return expectSocketRejected(socketClient, listenOptions, "Tokenless listen socket");
  }

  let listen;
  let proactive;
  try {
    listen = await socketClient.open(listenOptions);
    proactive = await socketClient.open({
      url: endpointUrl(config.hubUrl, "/v1/proactive"),
      label: "tokenless proactive",
      timeoutMs: config.timeoutMs,
      localAddress: config.localAddress,
    });
    const third = await expectSocketRejected(socketClient, {
      ...listenOptions,
      label: "tokenless third socket",
    }, "Third tokenless socket from the active client");
    let secondary = null;
    if (config.secondaryLocalAddress) {
      secondary = await expectSocketRejected(socketClient, {
        ...listenOptions,
        label: "tokenless second client",
        localAddress: config.secondaryLocalAddress,
      }, "Tokenless socket from a second client address");
    }
    await delay(config.holdMs);
    return {
      listenConnected: true,
      proactiveConnected: true,
      thirdSocketRejected: third.rejected,
      secondClientRejected: secondary?.rejected ?? null,
    };
  } finally {
    await Promise.all([
      closeQuietly(socketClient, proactive),
      closeQuietly(socketClient, listen),
    ]);
  }
}

export async function runJetstreamCompatibilityProbe(options, dependencies = {}) {
  const config = normalizeProbeOptions(options);
  const fetchImpl = dependencies.fetchImpl ?? globalThis.fetch;
  const socketClient = dependencies.socketClient;
  const onStep = dependencies.onStep ?? (() => {});
  assert(typeof fetchImpl === "function", "A fetch implementation is required.");
  assert(socketClient && typeof socketClient.open === "function" &&
    typeof socketClient.close === "function" && typeof socketClient.send === "function" &&
    typeof socketClient.readJson === "function", "A WebSocket client adapter is required.");

  const results = [];
  const authenticated = [];
  let tokenless = null;
  if (config.mode === "authenticated" || config.mode === "both") {
    await runStep(results, onStep, `${config.robots} authenticated robot(s)`, async () => {
      authenticated.push(...await Promise.all(Array.from({ length: config.robots }, (_unused, index) =>
        runAuthenticatedRobot(config, { fetchImpl, socketClient }, `${config.devicePrefix}-${index + 1}`))));
      assert(new Set(authenticated.map((robot) => robot.hubTokenFingerprint)).size === authenticated.length,
        "Authenticated robots did not receive distinct Hub tokens.");
      return `${authenticated.length} robot(s) completed token and socket checks${config.sendTurn ? " with a turn" : ""}`;
    });
  }

  if (config.mode === "tokenless" || config.mode === "both") {
    tokenless = await runStep(results, onStep,
      `tokenless compatibility expected ${config.expectTokenless}`, () => runTokenless(config, socketClient));
  }

  return {
    endpoints: {
      entrypoint: config.entrypointUrl.origin,
      hub: config.hubUrl.origin,
      notification: config.notificationUrl.origin,
    },
    mode: config.mode,
    results,
    authenticated,
    tokenless,
  };
}
