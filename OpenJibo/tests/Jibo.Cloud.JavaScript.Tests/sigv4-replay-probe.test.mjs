import assert from "node:assert/strict";
import test from "node:test";
import {
  collectDistinctReplicaReplayEvidence,
  createSignedReplayRequest,
  provePersistedReplay,
  validateProbeTarget,
} from "../../scripts/cloud/invoke-sigv4-replay-probe.mjs";

const requestOptions = {
  baseUrl: "https://staging.example.test",
  accessKeyId: "probe-access",
  secretAccessKey: "probe-secret",
  releaseSmokeSecret: "smoke-secret",
  signedAt: new Date("2026-09-21T12:34:56Z"),
};

test("signed replay request is deterministic and binds the expected operation", () => {
  const first = createSignedReplayRequest(requestOptions);
  const second = createSignedReplayRequest(requestOptions);
  assert.deepEqual(second, first);
  assert.equal(first.body, '{"deviceId":"open-jibo-smoke-staging-primary"}');
  assert.match(first.headers.Authorization, /Credential=probe-access\/20260921\/us-east-1\/jibo\/aws4_request/);
  assert.equal(first.headers["X-Amz-Target"], "Notification_20160715.NewRobotToken");
  assert.equal(first.headers["X-OpenJibo-Registration-Source"], "deployment-smoke");
});

test("replay evidence requires two distinct authorized replicas", async () => {
  const request = createSignedReplayRequest(requestOptions);
  const instances = ["replica-a", "replica-a", "replica-b"];
  const calls = [];
  const evidence = await collectDistinctReplicaReplayEvidence({
    baseUrl: requestOptions.baseUrl,
    request,
    expectedRevision: "revision-1",
    intervalMs: 0,
    fetchImpl: async (_url, options) => {
      calls.push({ body: options.body, authorization: options.headers.Authorization });
      const instance = instances.shift();
      return new Response("{}", { headers: {
        "X-OpenJibo-Replica-Instance": instance,
        "X-OpenJibo-Replica-Revision": "revision-1",
      } });
    },
  });
  assert.deepEqual(evidence, { attempts: 3, replicaCount: 2, revision: "revision-1" });
  assert.equal(new Set(calls.map((call) => call.body)).size, 1);
  assert.equal(new Set(calls.map((call) => call.authorization)).size, 1);
});

test("replay evidence fails closed on missing metadata or a stale revision", async () => {
  const request = createSignedReplayRequest(requestOptions);
  await assert.rejects(() => collectDistinctReplicaReplayEvidence({
    baseUrl: requestOptions.baseUrl,
    request,
    intervalMs: 0,
    fetchImpl: async () => new Response("{}"),
  }), /omitted authorized replica metadata/);
  await assert.rejects(() => collectDistinctReplicaReplayEvidence({
    baseUrl: requestOptions.baseUrl,
    request,
    expectedRevision: "revision-2",
    intervalMs: 0,
    fetchImpl: async () => new Response("{}", { headers: {
      "X-OpenJibo-Replica-Instance": "replica-a",
      "X-OpenJibo-Replica-Revision": "revision-1",
    } }),
  }), /unexpected revision revision-1/);
});

test("target validation rejects mismatches and production", () => {
  assert.equal(validateProbeTarget("https://staging.example.test", "staging.example.test"),
    "staging.example.test");
  assert.throws(() => validateProbeTarget("https://staging.example.test", "other.example.test"),
    /does not match/);
  assert.throws(() => validateProbeTarget("https://api.openjibo.com", "api.openjibo.com"),
    /refuses production/);
});

test("persistence proof requires two prior observations", async () => {
  const request = createSignedReplayRequest(requestOptions);
  const proof = await provePersistedReplay({
    baseUrl: requestOptions.baseUrl,
    request,
    fetchImpl: async () => new Response(JSON.stringify({
      verified: true,
      priorObservationCount: 2,
    })),
  });
  assert.deepEqual(proof, { verified: true, priorObservationCount: 2 });
  await assert.rejects(() => provePersistedReplay({
    baseUrl: requestOptions.baseUrl,
    request,
    fetchImpl: async () => new Response(JSON.stringify({
      verified: true,
      priorObservationCount: 1,
    })),
  }), /did not confirm two prior observations/);
});
