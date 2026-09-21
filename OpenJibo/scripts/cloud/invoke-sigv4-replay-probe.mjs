#!/usr/bin/env node

import { createHash, createHmac } from "node:crypto";
import { pathToFileURL } from "node:url";

const PRODUCTION_HOSTS = new Set(["api.openjibo.com", "api.jibo.com", "open-jibo.jibo.pro"]);

const sha256 = (value) => createHash("sha256").update(value).digest("hex");
const hmac = (key, value) => createHmac("sha256", key).update(value).digest();

export function createSignedReplayRequest({
  baseUrl,
  accessKeyId,
  secretAccessKey,
  releaseSmokeSecret,
  signedAt = new Date(),
  deviceId = "open-jibo-smoke-staging-primary",
}) {
  const url = new URL(baseUrl);
  const body = JSON.stringify({ deviceId });
  const timestamp = signedAt.toISOString().replace(/[:-]|\.\d{3}/g, "");
  const date = timestamp.slice(0, 8);
  const target = "Notification_20160715.NewRobotToken";
  const payloadHash = sha256(body);
  const signedHeaders = "host;x-amz-content-sha256;x-amz-date;x-amz-target";
  const canonicalHeaders = [
    `host:${url.host.toLowerCase()}`,
    `x-amz-content-sha256:${payloadHash}`,
    `x-amz-date:${timestamp}`,
    `x-amz-target:${target}`,
  ].join("\n");
  const canonicalRequest = ["POST", "/", "", `${canonicalHeaders}\n`, signedHeaders, payloadHash].join("\n");
  const scope = `${date}/us-east-1/jibo/aws4_request`;
  const stringToSign = ["AWS4-HMAC-SHA256", timestamp, scope, sha256(canonicalRequest)].join("\n");
  const dateKey = hmac(Buffer.from(`AWS4${secretAccessKey}`, "utf8"), date);
  const regionKey = hmac(dateKey, "us-east-1");
  const serviceKey = hmac(regionKey, "jibo");
  const signingKey = hmac(serviceKey, "aws4_request");
  const signature = createHmac("sha256", signingKey).update(stringToSign).digest("hex");

  return {
    body,
    headers: {
      Authorization: `AWS4-HMAC-SHA256 Credential=${accessKeyId}/${scope}, SignedHeaders=${signedHeaders}, Signature=${signature}`,
      "Content-Type": "application/json",
      "X-Amz-Content-Sha256": payloadHash,
      "X-Amz-Date": timestamp,
      "X-Amz-Target": target,
      "X-OpenJibo-Harness-Host": "api.openjibo.com",
      "X-OpenJibo-Registration-Source": "deployment-smoke",
      "X-OpenJibo-Release-Smoke-Secret": releaseSmokeSecret,
      Connection: "close",
    },
  };
}

export async function collectDistinctReplicaReplayEvidence({
  baseUrl,
  request,
  expectedRevision,
  attempts = 40,
  intervalMs = 250,
  fetchImpl = globalThis.fetch,
}) {
  const replicas = new Set();
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    const response = await fetchImpl(`${baseUrl.replace(/\/$/, "")}/`, {
      method: "POST",
      headers: request.headers,
      body: request.body,
      cache: "no-store",
    });
    const responseText = await response.text();
    if (!response.ok) throw new Error(`Signed replay probe returned HTTP ${response.status}.`);
    const instance = response.headers.get("X-OpenJibo-Replica-Instance");
    const revision = response.headers.get("X-OpenJibo-Replica-Revision");
    if (!instance || !revision) throw new Error("Signed replay probe response omitted authorized replica metadata.");
    if (expectedRevision && revision !== expectedRevision)
      throw new Error(`Signed replay probe reached unexpected revision ${revision}.`);
    replicas.add(instance);
    if (replicas.size >= 2) return { attempts: attempt, replicaCount: replicas.size, revision };
    if (attempt < attempts && intervalMs > 0)
      await new Promise((resolve) => setTimeout(resolve, intervalMs));
  }
  throw new Error("Signed replay probe did not reach two distinct replicas.");
}

export function validateProbeTarget(baseUrl, allowedHost) {
  const host = new URL(baseUrl).hostname.toLowerCase().replace(/\.$/, "");
  const approved = String(allowedHost ?? "").trim().toLowerCase().replace(/\.$/, "");
  if (!approved || host !== approved) throw new Error("Signed replay probe target does not match the approved host.");
  if (PRODUCTION_HOSTS.has(host)) throw new Error("Signed replay probe refuses production hosts.");
  return host;
}

async function main() {
  const baseUrl = process.env.BASE_URL;
  const accessKeyId = process.env.OPENJIBO_SIGV4_REPLAY_PROBE_ACCESS_KEY_ID;
  const secretAccessKey = process.env.OPENJIBO_SIGV4_REPLAY_PROBE_SECRET_ACCESS_KEY;
  const releaseSmokeSecret = process.env.OPENJIBO_RELEASE_SMOKE_SECRET;
  if (!baseUrl || !accessKeyId || !secretAccessKey || !releaseSmokeSecret)
    throw new Error("Set BASE_URL and the deployment-scoped replay probe and release-smoke secrets.");
  validateProbeTarget(baseUrl, process.env.OPENJIBO_RELEASE_SMOKE_ALLOWED_HOST);
  const request = createSignedReplayRequest({ baseUrl, accessKeyId, secretAccessKey, releaseSmokeSecret });
  const evidence = await collectDistinctReplicaReplayEvidence({
    baseUrl,
    request,
    expectedRevision: process.env.RELEASE_SMOKE_EXPECTED_REVISION || null,
    attempts: Number(process.env.RELEASE_SMOKE_REPLICA_ATTEMPTS || 40),
    intervalMs: Number(process.env.RELEASE_SMOKE_REPLICA_INTERVAL_MS || 250),
  });
  console.log(JSON.stringify({ status: "passed", ...evidence }));
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch((error) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}
