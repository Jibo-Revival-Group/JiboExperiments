import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createContext, runInContext } from "node:vm";
import test from "node:test";

const source = readFileSync(new URL("../../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/wwwroot/portal/status/status.js", import.meta.url), "utf8")
  .replace(/^bootstrap\(\);\s*$/m, "");

test("deployment readiness card escapes evidence and labels managed configuration", () => {
  const context = createContext({
    document: { getElementById: () => ({}) },
  });
  runInContext(source, context);

  const html = runInContext(`renderDeploymentStatus({
    revision: "api--safe<revision>",
    mode: "managed",
    canonicalApiHostname: "staging.openjibo.test",
    replica: "replica-1",
    managedConfigurationCompatible: false,
  })`, context);

  assert.match(html, /api--safe&lt;revision&gt;/);
  assert.match(html, /managed/);
  assert.match(html, /staging\.openjibo\.test/);
  assert.match(html, /Replica replica-1/);
  assert.match(html, /config check required/);
  assert.doesNotMatch(html, /safe<revision>/);
});
