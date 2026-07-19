const SESSION_KEY = "openjibo_status_session";
const app = document.getElementById("app");

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function getSessionToken() {
  return localStorage.getItem(SESSION_KEY);
}

function setSessionToken(token) {
  localStorage.setItem(SESSION_KEY, token);
}

function clearSessionToken() {
  localStorage.removeItem(SESSION_KEY);
}

async function apiFetch(path, options = {}) {
  const token = getSessionToken();
  const headers = {
    ...(options.body ? { "Content-Type": "application/json" } : {}),
    ...(options.headers || {}),
  };

  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  const response = await fetch(path, {
    ...options,
    headers,
  });

  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(payload.error || `Request failed (${response.status})`);
  }

  return payload;
}

function formatDate(value) {
  if (!value) return "—";
  return new Date(value).toLocaleString();
}

function formatDuration(seconds) {
  if (seconds == null || Number.isNaN(Number(seconds))) return "—";

  const total = Math.max(0, Math.floor(Number(seconds)));
  if (total >= 86400) {
    const days = Math.floor(total / 86400);
    const hours = Math.floor((total % 86400) / 3600);
    return `${days}d ${hours}h`;
  }

  if (total >= 3600) {
    const hours = Math.floor(total / 3600);
    const minutes = Math.floor((total % 3600) / 60);
    return `${hours}h ${minutes}m`;
  }

  if (total >= 60) {
    const minutes = Math.floor(total / 60);
    const secondsPart = total % 60;
    return `${minutes}m ${secondsPart}s`;
  }

  return `${total}s`;
}

function formatFloat(value, digits = 1) {
  if (value == null || value === "") return "—";
  const num = Number(value);
  if (Number.isNaN(num)) return "—";
  return num.toFixed(digits);
}

function statusBadge(isConnected, isActive) {
  if (isConnected) return `<span class="badge success">Connected</span>`;
  if (!isActive) return `<span class="badge warning">Inactive</span>`;
  return `<span class="badge neutral">Idle</span>`;
}

async function login(password) {
  if (!password) {
    await renderLogin("Enter the admin password.", true);
    return;
  }

  try {
    const payload = await apiFetch("/api/portal/status/login", {
      method: "POST",
      body: JSON.stringify({ password }),
    });
    setSessionToken(payload.portalSessionToken);
    await renderStatus();
  } catch (error) {
    await renderLogin(error.message, true);
  }
}

async function renderLogin(message = "", isError = false) {
  app.innerHTML = `
    <div class="center-shell">
      <section class="card login-card">
        <p class="eyebrow">OpenJibo Status</p>
        <h1>Admin access</h1>
        <p class="lede">Use the status password to open the fleet health view.</p>
        <label for="statusPassword">Admin password</label>
        <input id="statusPassword" type="password" autocomplete="current-password" placeholder="Enter password">
        <div class="button-row">
          <button class="button primary" id="loginButton" type="button">Open status dashboard</button>
          <a class="secondary-button" href="/portal">Back to portal</a>
        </div>
        ${message ? `<p class="status ${isError ? "error" : "success"}">${escapeHtml(message)}</p>` : ""}
      </section>
    </div>
  `;

  const input = document.getElementById("statusPassword");
  const button = document.getElementById("loginButton");

  button.addEventListener("click", () => login(input.value));
  input.addEventListener("keydown", (event) => {
    if (event.key === "Enter") login(input.value);
  });
  input.focus();
}

function renderRobotRows(robots = []) {
  if (!robots.length) {
    return `<tr><td colspan="7" class="muted-row">No robot records found yet.</td></tr>`;
  }

  return robots.map((robot) => `
    <tr>
      <td>
        <div class="mono">${escapeHtml(robot.friendlyName || robot.robotId || robot.deviceId || "—")}</div>
        <div class="muted-row">${escapeHtml(robot.deviceId || "—")}</div>
      </td>
      <td>${statusBadge(robot.connected, robot.isActive)}</td>
      <td>
        <div class="mono">${escapeHtml(robot.robotId || "—")}</div>
        <div class="muted-row">${escapeHtml((robot.sessionKinds || []).join(", ") || "—")}</div>
      </td>
      <td>${robot.sessionCount ?? 0}</td>
      <td>${formatDate(robot.lastSeenUtc)}</td>
      <td>${formatFloat(robot.lastHeartbeatAgeSeconds, 0)}s</td>
      <td>
        <div class="muted-row">${escapeHtml(robot.firmwareVersion || "—")}</div>
        <div class="muted-row">${escapeHtml(robot.applicationVersion || "—")}</div>
      </td>
    </tr>
  `).join("");
}

function renderRecentSessions(rows = []) {
  if (!rows.length) {
    return `<li class="muted-row">No recent live sessions.</li>`;
  }

  return rows.map((session) => `
    <li>
      <strong>${escapeHtml(session.kind || "unknown")}</strong>
      <span>${escapeHtml(session.deviceId || "—")} · ${escapeHtml(session.hostName || "—")}${session.path ? ` · ${escapeHtml(session.path)}` : ""}</span>
      <div class="muted-row">${formatDate(session.lastSeenUtc)} · heartbeat ${formatFloat(session.heartbeatAgeSeconds, 0)}s ago</div>
    </li>
  `).join("");
}

async function renderStatus(message = "", tone = "success") {
  let summary;
  try {
    summary = await apiFetch("/api/portal/status/summary");
  } catch (error) {
    clearSessionToken();
    await renderLogin(error.message, true);
    return;
  }

  const fleet = summary.fleet || {};
  const service = summary.service || {};
  const robots = summary.robots || [];
  const recentSessions = summary.recentSessions || [];

  app.innerHTML = `
    <div class="status-shell">
      <section class="card status-hero">
        <div class="status-hero-top">
          <div class="status-title">
            <p class="status-kicker">OpenJibo Status</p>
            <h1>Fleet health at a glance</h1>
            <p class="status-lede">Password-protected admin view for connected robots, live sessions, and service uptime.</p>
          </div>
          <div class="button-row" style="margin-top: 0;">
            <a class="secondary-button" href="/portal">Portal dashboard</a>
            <button class="button secondary" id="refreshButton" type="button">Refresh</button>
            <button class="button danger" id="logoutButton" type="button">Sign out</button>
          </div>
        </div>

        <div class="status-meta">
          <span class="badge success">${escapeHtml(fleet.connectedRobots ?? 0)} connected</span>
          <span class="badge neutral">${escapeHtml(fleet.registeredRobots ?? 0)} registered</span>
          <span class="badge warning">${escapeHtml(fleet.staleSessions ?? 0)} stale sessions</span>
          <span class="badge neutral">Uptime ${escapeHtml(service.uptimeLabel || "—")}</span>
        </div>

        <div class="stat-grid">
          <div class="stat-card">
            <span class="label">Connected robots</span>
            <span class="value">${fleet.connectedRobots ?? 0}</span>
            <span class="detail">Seen in the last five minutes.</span>
          </div>
          <div class="stat-card">
            <span class="label">Registered robots</span>
            <span class="value">${fleet.registeredRobots ?? 0}</span>
            <span class="detail">${fleet.activeRobots ?? 0} currently active.</span>
          </div>
          <div class="stat-card">
            <span class="label">Service uptime</span>
            <span class="value">${escapeHtml(service.uptimeLabel || "—")}</span>
            <span class="detail">Started ${formatDate(service.startedAtUtc)}.</span>
          </div>
          <div class="stat-card">
            <span class="label">Heartbeat age</span>
            <span class="value">${formatFloat(fleet.averageHeartbeatAgeSeconds, 0)}s</span>
            <span class="detail">Average of live sessions.</span>
          </div>
        </div>

        <div class="status-footer">
          <span>Generated ${formatDate(summary.generatedAtUtc)}</span>
          <span>Persistence rev ${escapeHtml(summary.persistence?.revision ?? "—")}</span>
        </div>
      </section>

      <div class="status-grid two">
        <section class="card panel tight">
          <div class="panel-header">
            <div>
              <p class="eyebrow">Fleet</p>
              <h2>Robot inventory</h2>
            </div>
            <span class="badge ${fleet.connectedRobots ? "success" : "warning"}">${fleet.liveSessions ?? 0} live sessions</span>
          </div>
          <div class="table-wrap">
            <table class="status-table">
              <thead>
                <tr>
                  <th>Robot</th>
                  <th>Status</th>
                  <th>Identity</th>
                  <th>Sessions</th>
                  <th>Last seen</th>
                  <th>Age</th>
                  <th>Version</th>
                </tr>
              </thead>
              <tbody>
                ${renderRobotRows(robots)}
              </tbody>
            </table>
          </div>
        </section>

        <section class="card panel tight">
          <div class="panel-header">
            <div>
              <p class="eyebrow">Traffic</p>
              <h2>Recent live sessions</h2>
            </div>
            <span class="badge neutral">${fleet.totalSessions ?? 0} tracked</span>
          </div>
          <ol class="steps">
            ${renderRecentSessions(recentSessions)}
          </ol>
          <div class="status-divider"></div>
          <div class="meta-list compact">
            <div class="meta-item"><span>Latest seen</span><span>${formatDate(fleet.latestSeenUtc)}</span></div>
            <div class="meta-item"><span>Oldest live session</span><span>${formatDate(fleet.oldestLiveSessionCreatedUtc)}</span></div>
            <div class="meta-item"><span>Stale sessions</span><span>${fleet.staleSessions ?? 0}</span></div>
            <div class="meta-item"><span>Average heartbeat age</span><span>${formatFloat(fleet.averageHeartbeatAgeSeconds, 0)}s</span></div>
          </div>
        </section>
      </div>

      ${message ? `<p class="status ${tone}" style="margin-top: 1rem;">${escapeHtml(message)}</p>` : ""}
    </div>
  `;

  document.getElementById("refreshButton").addEventListener("click", () => renderStatus("Status refreshed."));
  document.getElementById("logoutButton").addEventListener("click", logout);
}

async function logout() {
  const token = getSessionToken();
  clearSessionToken();

  if (token) {
    try {
      await apiFetch("/api/portal/logout", {
        method: "POST",
        body: JSON.stringify({ portalSessionToken: token }),
      });
    } catch {
      // Local sign-out should still win if revocation fails.
    }
  }

  await renderLogin();
}

async function bootstrap() {
  if (getSessionToken()) {
    await renderStatus();
    return;
  }

  await renderLogin();
}

bootstrap();
