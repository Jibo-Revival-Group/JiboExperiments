#!/usr/bin/env node

import { readFileSync } from "node:fs";
import http from "node:http";
import https from "node:https";
import WebSocket from "ws";
import {
  isPrivateTarget,
  normalizeProbeOptions,
  runJetstreamCompatibilityProbe,
} from "../../../scripts/cloud/jetstream-compatibility-probe.mjs";

function usage() {
  return `Usage:
  npm install --prefix src/Jibo.Cloud/node
  node src/Jibo.Cloud/node/invoke-jetstream-compatibility-probe.mjs [options]

Options:
  --entrypoint-url URL       HTTP(S) Account/CreateHubToken endpoint (default http://127.0.0.1:8080)
  --hub-url URL              WS(S) listen/proactive endpoint (derived from entrypoint)
  --notification-url URL     WS(S) notification endpoint (defaults to Hub endpoint)
  --mode MODE                authenticated, tokenless, or both (default authenticated)
  --robots COUNT             Authenticated fake robots, 1-20 (default 1)
  --expect-tokenless RESULT  accepted or rejected (default rejected)
  --device-prefix PREFIX     Diagnostic identity prefix (default jetstream-probe)
  --timeout-ms MS            Request/handshake timeout (default 6000)
  --hold-ms MS               Connection hold time (default 250)
  --local-address IP         Bind the primary probe sockets to this local address
  --secondary-local-address IP  Exercise the one-client guard from a second local address
  --skip-notification        Skip NewRobotToken and the notification socket
  --skip-turn                Connect Hub sockets without sending CLIENT_ASR
  --ca-file PATH             Trust an additional PEM certificate authority
  --allow-public-target      Permit a non-private endpoint (diagnostic state is created)
  --dangerously-allow-production  Permit a known OpenJibo production hostname
  --help                     Show this text
`;
}

function parseArgs(argv) {
  const values = {};
  const valueOptions = new Map([
    ["--entrypoint-url", "entrypointUrl"],
    ["--hub-url", "hubUrl"],
    ["--notification-url", "notificationUrl"],
    ["--mode", "mode"],
    ["--robots", "robots"],
    ["--expect-tokenless", "expectTokenless"],
    ["--device-prefix", "devicePrefix"],
    ["--timeout-ms", "timeoutMs"],
    ["--hold-ms", "holdMs"],
    ["--local-address", "localAddress"],
    ["--secondary-local-address", "secondaryLocalAddress"],
    ["--ca-file", "caFile"],
  ]);
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === "--help") values.help = true;
    else if (argument === "--skip-notification") values.includeNotification = false;
    else if (argument === "--skip-turn") values.sendTurn = false;
    else if (argument === "--allow-public-target") values.allowPublicTarget = true;
    else if (argument === "--dangerously-allow-production") values.allowProduction = true;
    else if (valueOptions.has(argument)) {
      const value = argv[++index];
      if (!value || value.startsWith("--")) throw new Error(`${argument} requires a value.`);
      values[valueOptions.get(argument)] = value;
    } else throw new Error(`Unknown option: ${argument}`);
  }
  return values;
}

function createProtocolFetch({ ca }) {
  return (url, options = {}) => new Promise((resolve, reject) => {
    const parsed = new URL(url);
    const transport = parsed.protocol === "https:" ? https : http;
    const request = transport.request(parsed, {
      method: options.method,
      headers: options.headers,
      ca: parsed.protocol === "https:" ? ca : undefined,
      signal: options.signal,
    }, (response) => {
      const chunks = [];
      response.on("data", (chunk) => chunks.push(chunk));
      response.once("error", reject);
      response.once("end", () => {
        const body = Buffer.concat(chunks).toString("utf8");
        resolve({
          ok: response.statusCode >= 200 && response.statusCode < 300,
          status: response.statusCode,
          text: async () => body,
        });
      });
    });
    request.once("error", reject);
    request.end(options.body);
  });
}

function createSocketClient({ ca }) {
  return {
    open({ url, headers = {}, label, timeoutMs, localAddress }) {
      return new Promise((resolve, reject) => {
        const socket = new WebSocket(url, {
          headers,
          handshakeTimeout: timeoutMs,
          localAddress,
          ca,
        });
        let settled = false;
        const fail = (error) => {
          if (settled) return;
          settled = true;
          socket.terminate();
          reject(error);
        };
        socket.once("open", () => {
          if (settled) return;
          settled = true;
          resolve(socket);
        });
        socket.once("unexpected-response", (_request, response) => {
          response.resume();
          const error = new Error(`${label} was rejected with HTTP ${response.statusCode}.`);
          error.statusCode = response.statusCode;
          fail(error);
        });
        socket.once("error", (error) => fail(new Error(`${label} failed: ${error.message}`)));
      });
    },

    send(socket, payload) {
      socket.send(payload);
    },

    readJson(socket, count, timeoutMs) {
      return new Promise((resolve, reject) => {
        const messages = [];
        const timer = setTimeout(() => {
          cleanup();
          reject(new Error(`Timed out waiting for ${count} WebSocket messages.`));
        }, timeoutMs);
        const onMessage = (data) => {
          try {
            messages.push(JSON.parse(data.toString()));
          } catch {
            return;
          }
          if (messages.length >= count) {
            cleanup();
            resolve(messages);
          }
        };
        const onClose = () => {
          cleanup();
          reject(new Error(`WebSocket closed after ${messages.length} of ${count} expected messages.`));
        };
        const cleanup = () => {
          clearTimeout(timer);
          socket.off("message", onMessage);
          socket.off("close", onClose);
        };
        socket.on("message", onMessage);
        socket.once("close", onClose);
      });
    },

    close(socket) {
      if (socket.readyState === WebSocket.CLOSED) return Promise.resolve();
      return new Promise((resolve) => {
        const timer = setTimeout(() => {
          socket.terminate();
          resolve();
        }, 1000);
        socket.once("close", () => {
          clearTimeout(timer);
          resolve();
        });
        socket.close(1000, "probe complete");
      });
    },
  };
}

const PRODUCTION_HOSTS = new Set([
  "api.openjibo.com",
  "neohub.openjibo.com",
  "open-jibo.jibo.pro",
  "open-jibo-socket.jibo.pro",
  "api.jibo.com",
]);

try {
  const cli = parseArgs(process.argv.slice(2));
  if (cli.help) {
    console.log(usage());
    process.exit(0);
  }
  const config = normalizeProbeOptions(cli);
  const hosts = [config.entrypointUrl.hostname, config.hubUrl.hostname, config.notificationUrl.hostname]
    .map((host) => host.toLowerCase().replace(/\.$/, ""));
  if (hosts.some((host) => !isPrivateTarget(host)) && !cli.allowPublicTarget) {
    throw new Error("Refusing a non-private target. Pass --allow-public-target only for an approved diagnostic host.");
  }
  if (hosts.some((host) => PRODUCTION_HOSTS.has(host)) && !cli.allowProduction) {
    throw new Error("Refusing a known production hostname. Use --dangerously-allow-production only with explicit approval.");
  }
  const ca = cli.caFile ? readFileSync(cli.caFile) : undefined;
  const result = await runJetstreamCompatibilityProbe(cli, {
    fetchImpl: createProtocolFetch({ ca }),
    socketClient: createSocketClient({ ca }),
    onStep: (step) => console.error(`${step.status.toUpperCase()}: ${step.name}${step.detail ? ` - ${step.detail}` : ""}`),
  });
  console.log(JSON.stringify(result, null, 2));
} catch (error) {
  console.error(error.stack || error.message);
  if (error.results) console.error(JSON.stringify(error.results, null, 2));
  process.exit(1);
}
