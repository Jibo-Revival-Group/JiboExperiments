#!/usr/bin/env node

import {
  createProtocolCaller,
  runReleaseSmoke,
} from "../../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/wwwroot/harness/release-smoke.mjs";

const baseUrl = process.env.BASE_URL || process.argv[2];
if (!baseUrl) {
  console.error("Set BASE_URL or pass the deployment base URL as the first argument.");
  process.exit(2);
}

const baseHost = new URL(baseUrl).hostname;
const protocolHost = process.env.OPENJIBO_RELEASE_SMOKE_HOST ||
  (["localhost", "127.0.0.1", "::1"].includes(baseHost) ? baseHost : "api.openjibo.com");

try {
  const result = await runReleaseSmoke({
    baseUrl,
    protocolCall: createProtocolCaller(baseUrl, protocolHost),
    robotPrefix: process.env.TEST_ROBOT_ID || `release-smoke-${Date.now()}-${process.pid}`,
    concurrency: Number.parseInt(process.env.RELEASE_SMOKE_CONCURRENCY || "6", 10),
    onStep: (step) => console.error(`${step.status.toUpperCase()}: ${step.name}${step.detail ? ` - ${step.detail}` : ""}`),
  });
  console.log(JSON.stringify(result, null, 2));
} catch (error) {
  console.error(error.stack || error.message);
  if (error.results) console.error(JSON.stringify(error.results, null, 2));
  process.exit(1);
}
