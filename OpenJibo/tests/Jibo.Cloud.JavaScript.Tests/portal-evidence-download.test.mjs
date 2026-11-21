import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createContext, runInContext } from "node:vm";
import test from "node:test";

const portalRoot = new URL("../../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/wwwroot/portal/", import.meta.url);
const readScript = (name) => readFileSync(new URL(name, portalRoot), "utf8").replace(/^bootstrap\(\);\s*$/m, "");

test("the customer portal offers a JSON snapshot when no signed bundle exists", async () => {
  const context = createContext({
    document: { getElementById: () => ({}) },
    localStorage: { getItem: () => "robot-session" },
    fetch: async (path, { headers }) => {
      assert.equal(path, "/api/portal/identity-graph");
      assert.equal(headers.Authorization, "Bearer robot-session");
      return Response.json({ robotId: "selected-robot", deviceId: "selected-device", loopId: "selected-loop" });
    },
  });
  runInContext(readScript("portal.js"), context);

  const html = await runInContext("renderIdentityGraphPanel()", context);

  assert.match(html, /selected-robot/);
  assert.match(html, /Download identity snapshot/);
  assert.doesNotMatch(html, /Download evidence bundle|Record revocation|portalSessionToken/);
});

test("admin exports use the admin bearer token and save the response attachment", async () => {
  const link = { click() { this.clicked = true; }, remove() {} };
  const context = createContext({
    document: { getElementById: () => ({}), createElement: () => link, body: { append() {} } },
    localStorage: { getItem: (key) => key === "openjibo_status_session" ? "admin-session" : "robot-session" },
    URL: { createObjectURL: (blob) => `blob:${blob.size}`, revokeObjectURL() {} },
    setTimeout: (callback) => callback(),
    fetch: async (path, { headers }) => {
      assert.equal(path, "/api/portal/trusted-servers/admissions/export");
      assert.equal(headers.Authorization, "Bearer admin-session");
      return new Response("{}", { headers: { "Content-Disposition": "attachment; filename=admissions.json" } });
    },
  });
  runInContext(readScript("download.js"), context);
  runInContext(readScript("status/status.js"), context);

  await runInContext("exportSignedAdmissions()", context);

  assert.equal(link.download, "admissions.json");
  assert.equal(link.href, "blob:2");
  assert.ok(link.clicked);
});
