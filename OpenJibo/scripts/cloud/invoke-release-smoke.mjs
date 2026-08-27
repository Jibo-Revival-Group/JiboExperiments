#!/usr/bin/env node

import {
  createProtocolCaller,
  runReleaseSmoke,
  withDeploymentSmokeAuthorizationRetry,
} from "../../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/wwwroot/harness/release-smoke.mjs";

const baseUrl = process.env.BASE_URL || process.argv[2];
if (!baseUrl) {
  console.error("Set BASE_URL or pass the deployment base URL as the first argument.");
  process.exit(2);
}

const baseHost = new URL(baseUrl).hostname;
const isLocal = ["localhost", "127.0.0.1", "::1"].includes(baseHost);
const allowedHost = (process.env.OPENJIBO_RELEASE_SMOKE_ALLOWED_HOST || "").trim().toLowerCase().replace(/\.$/, "");
if (!allowedHost || !/^[a-z0-9.-]+$/.test(allowedHost)) {
  console.error("Set OPENJIBO_RELEASE_SMOKE_ALLOWED_HOST to the exact approved target hostname.");
  process.exit(2);
}
if (baseHost.toLowerCase().replace(/\.$/, "") !== allowedHost) {
  console.error("Release smoke target hostname does not match OPENJIBO_RELEASE_SMOKE_ALLOWED_HOST.");
  process.exit(2);
}
const productionHosts = new Set(["api.openjibo.com", "api.jibo.com", "open-jibo.jibo.pro"]);
if (productionHosts.has(baseHost.toLowerCase()) &&
    process.env.OPENJIBO_RELEASE_SMOKE_DANGEROUSLY_ALLOW_PRODUCTION !== "true") {
  console.error("Refusing to run release smoke against a production hostname. Set OPENJIBO_RELEASE_SMOKE_DANGEROUSLY_ALLOW_PRODUCTION=true only for an explicitly approved emergency run.");
  process.exit(2);
}
const releaseSmokeSecret = process.env.OPENJIBO_RELEASE_SMOKE_SECRET;
if (!releaseSmokeSecret) {
  console.error("Set OPENJIBO_RELEASE_SMOKE_SECRET to the deployment-scoped staging authorization secret.");
  process.exit(2);
}
const robotPrefix = process.env.TEST_ROBOT_ID || (isLocal ? "open-jibo-smoke-local" : null);
if (robotPrefix !== "open-jibo-smoke-staging") {
  console.error("Set TEST_ROBOT_ID to the fixed open-jibo-smoke-staging namespace.");
  process.exit(2);
}
const protocolHost = process.env.OPENJIBO_RELEASE_SMOKE_HOST ||
  (isLocal ? baseHost : "api.openjibo.com");

try {
  const protocolCall = withDeploymentSmokeAuthorizationRetry(
    createProtocolCaller(baseUrl, protocolHost, globalThis.fetch, releaseSmokeSecret));
  const result = await runReleaseSmoke({
    baseUrl,
    protocolCall,
    robotPrefix,
    concurrency: process.env.RELEASE_SMOKE_CONCURRENCY || 6,
    turnPercent: process.env.RELEASE_SMOKE_TURN_PERCENT || 25,
    turnRounds: process.env.RELEASE_SMOKE_TURN_ROUNDS || 1,
    holdMs: process.env.RELEASE_SMOKE_HOLD_MS || 500,
    roundIntervalMs: process.env.RELEASE_SMOKE_ROUND_INTERVAL_MS || 0,
    timeoutMs: process.env.RELEASE_SMOKE_TIMEOUT_MS || 6000,
    onStep: (step) => console.error(`${step.status.toUpperCase()}: ${step.name}${step.detail ? ` - ${step.detail}` : ""}`),
  });
  console.log(JSON.stringify(result, null, 2));
} catch (error) {
  console.error(error.stack || error.message);
  if (error.results) console.error(JSON.stringify(error.results, null, 2));
  process.exit(1);
}
