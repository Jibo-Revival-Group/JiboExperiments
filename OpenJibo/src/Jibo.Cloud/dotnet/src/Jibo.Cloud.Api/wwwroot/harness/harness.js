const $ = (id) => document.getElementById(id);
const responseBox = $("response");
const statusBox = $("status");
const tokenBox = $("preparedToken");

function showStatus(text, tone = "success") {
  statusBox.textContent = text;
  statusBox.className = `status ${tone}`;
}

function setCall(service, operation, body) {
  $("servicePrefix").value = service;
  $("operation").value = operation;
  $("bodyText").value = JSON.stringify(body, null, 2);
}

function readBody() {
  return JSON.parse($("bodyText").value || "{}");
}

function profileBody(overrides = {}) {
  return {
    deviceId: $("deviceId").value.trim() || "fake-jibo-001",
    targetMode: $("targetMode").value,
    targetHost: $("targetHost").value.trim(),
    rollbackSnapshotId: $("rollbackSnapshotId").value.trim(),
    firmwareVersion: $("firmwareVersion").value.trim(),
    applicationVersion: $("applicationVersion").value.trim(),
    stockMode: $("stockMode").value.trim(),
    distribution: $("distribution").value.trim(),
    requireBaselineAudit: $("requireBaselineAudit").checked,
    ...overrides,
  };
}

function expectedHost() {
  const targetHost = $("targetHost").value.trim();
  return $("targetMode").value === "open-jibo" ? "api.openjibo.com" : targetHost;
}

function setPreparedToken(token) {
  if (!token) return;
  tokenBox.value = token;
}

async function sendRobotCall(service, operation, body) {
  const res = await fetch("/", {
    method: $("method").value,
    headers: {
      "Content-Type": "application/json",
      "X-Amz-Target": `${service}.${operation}`,
      "X-OpenJibo-Harness-Host": $("hostName").value.trim(),
      "X-OpenJibo-AppVersion": "1.0.20",
    },
    body: JSON.stringify(body),
  });
  const text = await res.text();
  const payload = text ? JSON.parse(text) : null;
  responseBox.textContent = payload ? JSON.stringify(payload, null, 2) : "(empty response)";
  showStatus(`HTTP ${res.status} from ${service}.${operation}`, res.ok ? "success" : "error");
  return { res, payload };
}

function loadPlan(operation = "AuditConversion") {
  setCall("OOBE_20161026", operation, profileBody());
}

$("loadAudit").addEventListener("click", () => loadPlan("AuditConversion"));
$("loadPlan").addEventListener("click", () => loadPlan("PlanConversion"));
$("loadPrepare").addEventListener("click", () => setCall("OOBE_20161026", "PrepareRobot", profileBody({ loopId: $("loopId").value.trim() || "loop-fake-jibo" })));
$("loadSetup").addEventListener("click", () => setCall("OOBE_20161026", "SetupRobot", { token: tokenBox.value.trim() || "paste-prepared-token-here", id: $("deviceId").value.trim() || "fake-jibo-001" }));
$("loadStatus").addEventListener("click", () => setCall("OOBE_20161026", "GetStatus", profileBody({ token: tokenBox.value.trim() || undefined })));
$("loadRobot").addEventListener("click", () => setCall("Robot_20160225", "GetRobot", { id: $("deviceId").value.trim() || "fake-jibo-001" }));
$("loadVerifyConnection").addEventListener("click", () => {
  const host = expectedHost();
  setCall("OOBE_20161026", "VerifyConnection", {
    token: tokenBox.value.trim() || "paste-prepared-token-here",
    requireLiveRobotProof: true,
    reportedConnectionHost: host,
    reportedHostMappings: { "api.jibo.com": host, "api-socket.jibo.com": host, "neo-hub.jibo.com": host },
  });
});

$("sendButton").addEventListener("click", async () => {
  let body;
  try { body = readBody(); } catch (error) { showStatus(`Invalid JSON: ${error.message}`, "error"); return; }
  try {
    const { payload } = await sendRobotCall($("servicePrefix").value.trim(), $("operation").value.trim(), body);
    setPreparedToken(payload?.token);
  } catch (error) { showStatus(error.message, "error"); }
});

$("runConversionSmoke").addEventListener("click", async () => {
  try {
    const audit = await sendRobotCall("OOBE_20161026", "AuditConversion", profileBody());
    if (!audit.res.ok || audit.payload?.conversionReadiness?.blockers?.length) return showStatus("Audit found blockers; review the response before preparing a token.", "error");
    const prepare = await sendRobotCall("OOBE_20161026", "PrepareRobot", profileBody({ loopId: $("loopId").value.trim() || "loop-fake-jibo" }));
    setPreparedToken(prepare.payload?.token);
    if (!prepare.res.ok || !tokenBox.value) return showStatus("PrepareRobot did not return a token.", "error");
    const setup = await sendRobotCall("OOBE_20161026", "SetupRobot", { token: tokenBox.value, id: $("deviceId").value.trim() || "fake-jibo-001" });
    if (!setup.res.ok) return;
    const host = expectedHost();
    await sendRobotCall("OOBE_20161026", "VerifyConnection", { token: tokenBox.value, requireLiveRobotProof: true, reportedConnectionHost: host, reportedHostMappings: { "api.jibo.com": host, "api-socket.jibo.com": host, "neo-hub.jibo.com": host } });
  } catch (error) { showStatus(error.message, "error"); }
});

loadPlan("AuditConversion");
