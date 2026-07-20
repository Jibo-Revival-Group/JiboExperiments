const ADMIN_SESSION_KEY = "openjibo_status_session";

async function ensureAdminAccess() {
  const token = localStorage.getItem(ADMIN_SESSION_KEY);
  if (token) {
    const response = await fetch("/api/portal/status/summary", {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (response.ok) return true;
  }

  document.body.innerHTML = `
    <main class="shell">
      <section class="card">
        <p class="eyebrow">OpenJibo Admin</p>
        <h1>Robot harness access</h1>
        <p class="lede">Enter the admin password to use robot protocol and conversion tools.</p>
        <label for="adminPassword">Admin password<input id="adminPassword" type="password" autocomplete="current-password"></label>
        <div class="actions"><button id="adminLogin" class="primary" type="button">Open harness</button></div>
        <p id="adminStatus" class="status hidden"></p>
      </section>
    </main>`;

  const login = async () => {
    const password = document.getElementById("adminPassword").value;
    const status = document.getElementById("adminStatus");
    const response = await fetch("/api/portal/status/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ password }),
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      status.textContent = payload.error || "Admin login failed.";
      status.className = "status error";
      return;
    }
    localStorage.setItem(ADMIN_SESSION_KEY, payload.portalSessionToken);
    window.location.reload();
  };
  document.getElementById("adminLogin").addEventListener("click", login);
  document.getElementById("adminPassword").addEventListener("keydown", (event) => {
    if (event.key === "Enter") login();
  });
  document.getElementById("adminPassword").focus();
  return false;
}

async function bootHarness() {
  if (!await ensureAdminAccess()) return;

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
  return $("targetMode").value === "open-jibo" || $("targetMode").value === "open-jibo-ai"
    ? "api.openjibo.com"
    : targetHost;
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
      "X-OpenJibo-Registration-Source": "browser-harness",
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
    reportedHostMappings: {
      "api.jibo.com": host,
      "api-socket.jibo.com": host,
      "open-jibo-socket.openjibo.com": host,
      "neo-hub.jibo.com": host,
      "neohub.openjibo.com": host,
    },
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
    await sendRobotCall("OOBE_20161026", "VerifyConnection", {
      token: tokenBox.value,
      requireLiveRobotProof: true,
      reportedConnectionHost: host,
      reportedHostMappings: {
        "api.jibo.com": host,
        "api-socket.jibo.com": host,
        "open-jibo-socket.openjibo.com": host,
        "neo-hub.jibo.com": host,
        "neohub.openjibo.com": host,
      },
    });
  } catch (error) { showStatus(error.message, "error"); }
});

loadPlan("AuditConversion");

$("adminSignOut").addEventListener("click", () => {
  localStorage.removeItem(ADMIN_SESSION_KEY);
  window.location.reload();
});
}

void bootHarness();
