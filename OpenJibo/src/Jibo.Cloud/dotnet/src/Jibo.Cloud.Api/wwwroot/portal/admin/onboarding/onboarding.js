const SESSION_KEY = "openjibo_status_session";
const app = document.getElementById("app");

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function token() {
  return localStorage.getItem(SESSION_KEY);
}

async function apiFetch(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: {
      ...(options.body ? { "Content-Type": "application/json" } : {}),
      ...(token() ? { Authorization: `Bearer ${token()}` } : {}),
      ...(options.headers || {}),
    },
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.error || "Admin request failed.");
  return payload;
}

async function renderLogin(message = "") {
  app.innerHTML = `
    <div class="center-shell">
      <section class="card login-card">
        <p class="eyebrow">OpenJibo Admin</p>
        <h1>Onboarding and server tools</h1>
        <p class="lede">Enter the admin password to manage trusted servers and validate self-hosted setup.</p>
        <label for="password">Admin password</label>
        <input id="password" type="password" autocomplete="current-password">
        <div class="button-row"><button class="button primary" id="login" type="button">Open admin tools</button></div>
        ${message ? `<p class="status error">${escapeHtml(message)}</p>` : ""}
      </section>
    </div>`;
  const login = async () => {
    try {
      const payload = await apiFetch("/api/portal/status/login", {
        method: "POST",
        body: JSON.stringify({ password: document.getElementById("password").value }),
      });
      localStorage.setItem(SESSION_KEY, payload.portalSessionToken);
      await renderTools();
    } catch (error) {
      await renderLogin(error.message);
    }
  };
  document.getElementById("login").addEventListener("click", login);
  document.getElementById("password").addEventListener("keydown", (event) => {
    if (event.key === "Enter") login();
  });
  document.getElementById("password").focus();
}

function serverList(servers) {
  if (!servers.length) return `<li class="muted-row">No trusted servers registered.</li>`;
  return servers.map((server) => `
    <li>
      <strong>${escapeHtml(server.displayName || server.canonicalHost)}</strong>
      <span>${escapeHtml(server.canonicalHost)} · ${escapeHtml(server.serverKind)}</span>
      <div class="muted-row">${server.acceptsPublicConnections ? "Public" : "Private"} · ${server.participatesInCloudSync ? "Cloud sync" : "No cloud sync"} · ${server.isActive ? "Active" : "Revoked"}</div>
      ${server.isTrustRoot ? `<span class="badge success">Trust root</span>` : `
        <div class="button-row">
          <button class="button secondary compact server-action" data-host="${escapeHtml(server.canonicalHost)}" data-kind="${escapeHtml(server.serverKind)}" data-action="${server.isActive ? "revoke" : "reactivate"}" type="button">${server.isActive ? "Revoke" : "Reactivate"}</button>
          <button class="button secondary compact server-action" data-host="${escapeHtml(server.canonicalHost)}" data-kind="${escapeHtml(server.serverKind)}" data-action="mark-seen" type="button">Mark seen</button>
        </div>`}
    </li>`).join("");
}

async function renderTools(message = "", tone = "success") {
  let directory;
  try {
    directory = await apiFetch("/api/onboarding/trusted-servers");
  } catch (error) {
    localStorage.removeItem(SESSION_KEY);
    await renderLogin(error.message);
    return;
  }
  const servers = directory.servers || [];
  app.innerHTML = `
    <div class="status-shell">
      <section class="card status-hero">
        <div class="status-hero-top">
          <div><p class="status-kicker">OpenJibo Admin</p><h1>Onboarding and server tools</h1><p class="status-lede">Protected setup controls for client connectivity and fleet trust.</p></div>
          <div class="button-row" style="margin-top: 0;"><a class="secondary-button" href="/portal/status">Status</a><a class="secondary-button" href="/portal/admin/harness">Harness</a><a class="secondary-button" href="/portal">Customer portal</a><button class="button danger" id="signOut" type="button">Sign out</button></div>
        </div>
      </section>
      <div class="status-grid two">
        <section class="card panel tight">
          <div class="panel-header"><div><p class="eyebrow">Onboarding</p><h2>Trusted server registry</h2></div><span class="badge neutral">${servers.length} records</span></div>
          <p class="muted">Hosted servers must use HTTPS. Self-hosted servers are validated separately and never added to this registry.</p>
          <ul class="steps">${serverList(servers)}</ul>
          <div class="inline-form">
            <label for="serverHost">Server host</label><input id="serverHost" placeholder="api.example.openjibo.com">
            <label for="serverName">Display name</label><input id="serverName" placeholder="Example hosted server">
            <label for="serverKind">Server kind</label><select id="serverKind"><option value="managed">Managed</option><option value="hybrid">Hybrid</option></select>
            <label for="serverReason">Reason</label><input id="serverReason" placeholder="Operator-approved server">
            <button class="button primary" id="registerServer" type="button">Register trusted server</button>
          </div>
        </section>
        <section class="card panel tight">
          <p class="eyebrow">Onboarding</p><h2>Self-hosted entry</h2>
          <p class="muted">Validate a local self-hosted target or a private HTTPS hybrid server before client setup.</p>
          <div class="inline-form">
            <label for="selfHostedMode">Mode</label><select id="selfHostedMode"><option value="self-hosted">Self-hosted</option><option value="self-hosted-hybrid">Self-hosted hybrid</option></select>
            <label for="selfHostedHost">Host or URL</label><input id="selfHostedHost" placeholder="localhost:8080 or https://server.example">
            <button class="button primary" id="validateSelfHosted" type="button">Validate self-hosted path</button>
          </div>
          <pre id="validationResult" class="hidden"></pre>
        </section>
      </div>
      ${message ? `<p class="status ${tone}" style="margin-top: 1rem;">${escapeHtml(message)}</p>` : ""}
    </div>`;

  document.getElementById("signOut").addEventListener("click", () => {
    localStorage.removeItem(SESSION_KEY);
    renderLogin();
  });
  document.getElementById("registerServer").addEventListener("click", async () => {
    try {
      await apiFetch("/api/portal/trusted-servers", {
        method: "POST",
        body: JSON.stringify({
          canonicalHost: document.getElementById("serverHost").value.trim(),
          displayName: document.getElementById("serverName").value.trim(),
          serverKind: document.getElementById("serverKind").value,
          reason: document.getElementById("serverReason").value.trim(),
        }),
      });
      await renderTools("Trusted server registered.");
    } catch (error) { await renderTools(error.message, "error"); }
  });
  document.getElementById("validateSelfHosted").addEventListener("click", async () => {
    const result = document.getElementById("validationResult");
    try {
      const payload = await apiFetch("/api/onboarding/self-hosted/validate", {
        method: "POST",
        body: JSON.stringify({ serverMode: document.getElementById("selfHostedMode").value, serverHost: document.getElementById("selfHostedHost").value.trim() }),
      });
      result.textContent = JSON.stringify(payload, null, 2);
      result.classList.remove("hidden");
    } catch (error) { result.textContent = error.message; result.classList.remove("hidden"); }
  });
  document.querySelectorAll(".server-action").forEach((button) => button.addEventListener("click", async () => {
    try {
      await apiFetch("/api/portal/trusted-servers/lifecycle", {
        method: "POST",
        body: JSON.stringify({ canonicalHost: button.dataset.host, serverKind: button.dataset.kind, action: button.dataset.action }),
      });
      await renderTools(`Server ${button.dataset.action} completed.`);
    } catch (error) { await renderTools(error.message, "error"); }
  }));
}

if (token()) renderTools(); else renderLogin();
