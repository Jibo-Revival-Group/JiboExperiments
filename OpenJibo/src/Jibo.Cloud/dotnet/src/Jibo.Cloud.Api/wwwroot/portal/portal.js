const SESSION_KEY = "openjibo_portal_session";

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

function renderLogin(message = "", isError = false) {
  app.innerHTML = `
    <div class="center-shell">
      <section class="card login-card">
        <p class="eyebrow">OpenJibo Portal</p>
        <h1>Sign in with Jibo</h1>
        <p class="lede">Say <strong>"Hey Jibo, verify me"</strong>, then enter the four-digit code Jibo speaks.</p>

        <label for="jiboCode">Verification code</label>
        <input id="jiboCode" type="text" inputmode="numeric" autocomplete="one-time-code" maxlength="8" placeholder="8042">

        <div class="button-row">
          <button class="button primary" id="loginButton" type="button">Continue to dashboard</button>
        </div>

        ${message ? `<p class="status ${isError ? "error" : "success"}">${escapeHtml(message)}</p>` : ""}
      </section>
    </div>
  `;

  const input = document.getElementById("jiboCode");
  const button = document.getElementById("loginButton");

  button.addEventListener("click", () => login(input.value.trim()));
  input.addEventListener("keydown", (event) => {
    if (event.key === "Enter") login(input.value.trim());
  });
  input.focus();
}

async function login(code) {
  if (!code) {
    renderLogin("Enter the verification code Jibo spoke.", true);
    return;
  }

  try {
    const payload = await apiFetch("/api/portal/jibo-verification/confirm", {
      method: "POST",
      body: JSON.stringify({ code }),
    });

    setSessionToken(payload.portalSessionToken);
    await renderDashboard();
  } catch (error) {
    renderLogin(error.message, true);
  }
}

function haStatusBadge(homeAssistant) {
  if (!homeAssistant?.linked) {
    return `<span class="badge neutral">Not paired</span>`;
  }

  if (homeAssistant.connected) {
    return `<span class="badge success">Connected</span>`;
  }

  return `<span class="badge warning">Linked, offline</span>`;
}

function renderHomeAssistantPanel(dashboard) {
  const ha = dashboard.homeAssistant || { linked: false, connected: false };

  if (!ha.linked) {
    return `
      <section class="card panel">
        <div class="panel-header">
          <div>
            <p class="eyebrow">Integration</p>
            <h2>Home Assistant</h2>
          </div>
          ${haStatusBadge(ha)}
        </div>
        <p class="muted">Pair Home Assistant so Jibo can control devices in your home.</p>
        <ol class="steps">
          <li>Install the OpenJibo integration in Home Assistant.</li>
          <li>Set the server URL to this OpenJibo server.</li>
          <li>Copy the pairing code from the Home Assistant notification.</li>
          <li>Enter it below to link this Jibo.</li>
        </ol>
        <label for="haCode">Home Assistant pairing code</label>
        <input id="haCode" type="text" autocomplete="off" maxlength="12" placeholder="ABC123">
        <div class="button-row">
          <button class="button primary" id="pairButton" type="button">Pair Home Assistant</button>
        </div>
        <p id="haActionStatus" class="status hidden"></p>
      </section>
    `;
  }

  return `
    <section class="card panel">
      <div class="panel-header">
        <div>
          <p class="eyebrow">Integration</p>
          <h2>Home Assistant</h2>
        </div>
        ${haStatusBadge(ha)}
      </div>

      <div class="meta-list">
        <div class="meta-item"><span>Status</span><span>${ha.connected ? "Connected to server" : "Waiting for Home Assistant to connect"}</span></div>
        <div class="meta-item"><span>Paired</span><span>${formatDate(ha.pairedAtUtc)}</span></div>
        <div class="meta-item"><span>Last seen</span><span>${formatDate(ha.lastSeenUtc)}</span></div>
        <div class="meta-item"><span>Instance ID</span><span>${escapeHtml(ha.haInstanceId || "—")}</span></div>
      </div>

      ${ha.connected ? "" : `
        <p class="status info" style="margin-top: 1rem;">
          Home Assistant is paired but not connected right now. Reload the integration or confirm the server URL in Home Assistant points to this server.
        </p>
      `}

      <div class="button-row">
        <button class="button danger" id="unpairButton" type="button">Unpair Home Assistant</button>
        <button class="button secondary" id="refreshButton" type="button">Refresh status</button>
      </div>
      <p id="haActionStatus" class="status hidden"></p>
    </section>
  `;
}

async function renderDashboard(message = "", tone = "success") {
  let dashboard;
  try {
    dashboard = await apiFetch("/api/portal/dashboard");
  } catch (error) {
    clearSessionToken();
    renderLogin(error.message, true);
    return;
  }

  app.innerHTML = `
    <div class="shell">
      <header class="dashboard-header">
        <div>
          <p class="eyebrow">OpenJibo Dashboard</p>
          <h1>${escapeHtml(dashboard.jiboFriendlyId || "Your Jibo")}</h1>
          <p class="muted">Manage integrations for this robot.</p>
        </div>
        <div class="button-row" style="margin-top: 0;">
          <button class="button secondary" id="logoutButton" type="button">Sign out</button>
        </div>
      </header>

      <div class="grid two">
        <section class="card panel">
          <p class="eyebrow">Robot</p>
          <h2>Jibo profile</h2>
          <div class="meta-list">
            <div class="meta-item"><span>Friendly ID</span><span>${escapeHtml(dashboard.jiboFriendlyId || "—")}</span></div>
            <div class="meta-item"><span>Device ID</span><span>${escapeHtml(dashboard.jiboDeviceId || "—")}</span></div>
            <div class="meta-item"><span>Portal session</span><span>${formatDate(dashboard.sessionExpiresAtUtc)}</span></div>
          </div>
        </section>

        ${renderHomeAssistantPanel(dashboard)}
      </div>

      ${message ? `<p class="status ${tone}" style="margin-top: 1rem;">${escapeHtml(message)}</p>` : ""}
    </div>
  `;

  document.getElementById("logoutButton").addEventListener("click", logout);

  const pairButton = document.getElementById("pairButton");
  if (pairButton) {
    pairButton.addEventListener("click", async () => {
      const haCode = document.getElementById("haCode").value.trim();
      const status = document.getElementById("haActionStatus");
      try {
        await apiFetch("/api/portal/home-assistant/link", {
          method: "POST",
          body: JSON.stringify({ haCode }),
        });
        await renderDashboard("Home Assistant paired successfully.");
      } catch (error) {
        status.textContent = error.message;
        status.className = "status error";
      }
    });
  }

  const unpairButton = document.getElementById("unpairButton");
  if (unpairButton) {
    unpairButton.addEventListener("click", async () => {
      const status = document.getElementById("haActionStatus");
      if (!window.confirm("Unpair Home Assistant from this Jibo?")) return;

      try {
        await apiFetch("/api/portal/home-assistant/link", { method: "DELETE" });
        await renderDashboard("Home Assistant unpaired.");
      } catch (error) {
        status.textContent = error.message;
        status.className = "status error";
      }
    });
  }

  const refreshButton = document.getElementById("refreshButton");
  if (refreshButton) {
    refreshButton.addEventListener("click", () => renderDashboard());
  }
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
      // Ignore logout failures once local session is cleared.
    }
  }

  renderLogin();
}

async function bootstrap() {
  if (getSessionToken()) {
    await renderDashboard();
    return;
  }

  renderLogin();
}

bootstrap();
