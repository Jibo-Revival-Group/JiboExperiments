const SESSION_KEY = "openjibo_status_session";
const app = document.getElementById("app");
const REFRESH_INTERVAL_MS = 20000;
const ROBOT_PAGE_SIZE_OPTIONS = [10, 25, 50];

let includeHidden = false;
let robotSearchQuery = "";
let robotSortKey = "lastSeenUtc";
let robotSortDirection = "desc";
let robotPageSize = 10;
let robotPage = 1;
let recentSessionPageSize = 5;
let recentSessionPage = 1;
let autoRefreshEnabled = true;
let lastRefreshAt = null;
let lastRefreshError = "";
let previousSummary = null;
let latestSummary = null;
let bannerMessage = "";
let bannerTone = "success";
let refreshTimer = null;
let refreshInFlight = false;
let activeLogViewer = null;

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
    const error = new Error(payload.error || `Request failed (${response.status})`);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }

  return payload;
}

function formatDate(value) {
  if (!value) return "-";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "-";
  return date.toLocaleString();
}

function formatRelativeTime(value) {
  if (!value) return "-";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "-";

  const deltaSeconds = Math.max(0, Math.floor((Date.now() - date.getTime()) / 1000));
  if (deltaSeconds < 5) return "just now";
  if (deltaSeconds < 60) return `${deltaSeconds}s ago`;

  const deltaMinutes = Math.floor(deltaSeconds / 60);
  if (deltaMinutes < 60) return `${deltaMinutes}m ago`;

  const deltaHours = Math.floor(deltaMinutes / 60);
  if (deltaHours < 24) return `${deltaHours}h ago`;

  const deltaDays = Math.floor(deltaHours / 24);
  return `${deltaDays}d ago`;
}

function formatDuration(seconds) {
  if (seconds == null || Number.isNaN(Number(seconds))) return "-";

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
  if (value == null || value === "") return "-";
  const num = Number(value);
  if (Number.isNaN(num)) return "-";
  return num.toFixed(digits);
}

function statusBadge(presence) {
  const badges = {
    online: ["success", "Online"],
    sleeping: ["neutral", "Sleeping"],
    "recently-seen": ["neutral", "Recently seen"],
    offline: ["warning", "Offline"],
    "never-connected": ["warning", "Never connected"],
    inactive: ["warning", "Inactive"],
  };
  const [tone, label] = badges[presence] || ["neutral", "Unknown"];
  return `<span class="badge ${tone}">${label}</span>`;
}

function normalizeText(value) {
  return String(value || "").trim().toLowerCase();
}

function robotDisplayName(robot) {
  return robot.friendlyName || robot.robotId || robot.deviceId || "-";
}

function robotSelectorLabel(robot) {
  const primary = robot.robotId || robot.friendlyName || robot.deviceId || "-";
  const secondary = primary === robot.friendlyName
    ? robot.deviceId && robot.deviceId !== primary ? robot.deviceId : ""
    : robot.friendlyName || robot.deviceId || "";

  return secondary ? `${primary} · ${secondary}` : primary;
}

function robotSearchHaystack(robot) {
  return normalizeText([
    robot.friendlyName,
    robot.robotId,
    robot.deviceId,
    robot.registrationSource,
    robot.verifiedSerialNumber,
    robot.presence,
    robot.firmwareVersion,
    robot.applicationVersion,
  ].join(" "));
}

function robotPresenceRank(presence) {
  const rank = {
    online: 0,
    sleeping: 1,
    "recently-seen": 2,
    offline: 3,
    "never-connected": 4,
    inactive: 5,
  };

  return rank[presence] ?? 99;
}

function compareRobots(left, right) {
  const direction = robotSortDirection === "asc" ? 1 : -1;
  let comparison = 0;

  switch (robotSortKey) {
    case "name":
      comparison = normalizeText(robotDisplayName(left)).localeCompare(normalizeText(robotDisplayName(right)));
      break;
    case "presence":
      comparison = robotPresenceRank(left.presence) - robotPresenceRank(right.presence);
      break;
    case "sessions":
      comparison = (left.liveConnectionCount ?? 0) - (right.liveConnectionCount ?? 0);
      break;
    case "heartbeat":
      comparison = (left.lastHeartbeatAgeSeconds ?? Number.POSITIVE_INFINITY) -
        (right.lastHeartbeatAgeSeconds ?? Number.POSITIVE_INFINITY);
      break;
    case "lastSeenUtc":
    default:
      comparison = new Date(left.lastSeenUtc || 0).getTime() - new Date(right.lastSeenUtc || 0).getTime();
      break;
  }

  if (comparison === 0) {
    comparison = normalizeText(robotDisplayName(left)).localeCompare(normalizeText(robotDisplayName(right)));
  }

  return comparison * direction;
}

function robotRowSnapshot(robot) {
  return JSON.stringify({
    friendlyName: robot.friendlyName || "",
    robotId: robot.robotId || "",
    deviceId: robot.deviceId || "",
    presence: robot.presence || "",
    liveConnectionCount: robot.liveConnectionCount ?? 0,
    connectionKinds: [...(robot.connectionKinds || [])].join(","),
    lastSeenUtc: robot.lastSeenUtc || "",
    lastHeartbeatAgeSeconds: robot.lastHeartbeatAgeSeconds ?? null,
    firmwareVersion: robot.firmwareVersion || "",
    applicationVersion: robot.applicationVersion || "",
    registrationSource: robot.registrationSource || "",
    isHidden: Boolean(robot.isHidden),
  });
}

function sessionRowSnapshot(session) {
  return JSON.stringify({
    sessionId: session.sessionId || "",
    kind: session.kind || "",
    deviceId: session.deviceId || "",
    hostName: session.hostName || "",
    path: session.path || "",
    registeredDeviceId: session.registeredDeviceId || "",
    lastSeenUtc: session.lastSeenUtc || "",
    heartbeatAgeSeconds: session.heartbeatAgeSeconds ?? null,
  });
}

function buildChangeSet(currentRows = [], previousRows = [], keySelector, snapshotSelector) {
  const previousMap = new Map(previousRows.map((row) => [keySelector(row), snapshotSelector(row)]));
  const changed = new Set();

  currentRows.forEach((row) => {
    const key = keySelector(row);
    if (previousMap.get(key) !== snapshotSelector(row)) {
      changed.add(key);
    }
  });

  return changed;
}

function setStatusBanner(message = "", tone = "success") {
  bannerMessage = message;
  bannerTone = tone;
}

function renderRobotRows(robots = [], changedRobotIds = new Set()) {
  if (!robots.length) {
    return `<tr><td colspan="8" class="muted-row empty-state">No robot records match the current filters.</td></tr>`;
  }

  return robots.map((robot) => `
    <tr class="${changedRobotIds.has(robot.deviceId) ? "changed-row" : ""}">
      <td>
        <div class="mono">${escapeHtml(robotDisplayName(robot))}</div>
        <div class="muted-row">${escapeHtml(robot.deviceId || "-")}</div>
      </td>
      <td>${statusBadge(robot.presence)}</td>
      <td>
        <div class="mono">${escapeHtml(robot.robotId || "-")}</div>
        <div class="muted-row">${escapeHtml(robot.registrationSource || "unknown")}</div>
        ${robot.verifiedSerialNumber ? `<div class="muted-row">Verified serial: ${escapeHtml(robot.verifiedSerialNumber)}</div>` : ""}
      </td>
      <td>${robot.liveConnectionCount ?? 0} live<br><span class="muted-row">${escapeHtml((robot.connectionKinds || []).join(", ") || "-")}</span></td>
      <td>${formatDate(robot.lastSeenUtc)}</td>
      <td>${formatFloat(robot.lastHeartbeatAgeSeconds, 0)}s</td>
      <td>
        <div class="muted-row">${escapeHtml(robot.firmwareVersion || "-")}</div>
        <div class="muted-row">${escapeHtml(robot.applicationVersion || "-")}</div>
      </td>
      <td>
        <div class="row-actions">
          <button class="button secondary compact view-artifacts" data-device-id="${escapeHtml(robot.deviceId)}" data-robot-name="${escapeHtml(robotDisplayName(robot))}" type="button">Artifacts</button>
          <button class="button secondary compact open-lrd" data-device-id="${escapeHtml(robot.deviceId)}" data-robot-name="${escapeHtml(robotDisplayName(robot))}" type="button" title="Open in Live Robot Debugger">Open in LRD</button>
          <button class="button secondary compact archive-robot" data-device-id="${escapeHtml(robot.deviceId)}" data-hidden="${robot.isHidden ? "false" : "true"}" type="button">${robot.isHidden ? "Restore" : "Archive"}</button>
        </div>
      </td>
    </tr>
  `).join("");
}

function filterAndSortRobots(robots = []) {
  const query = normalizeText(robotSearchQuery);
  const filtered = query
    ? robots.filter((robot) => robotSearchHaystack(robot).includes(query))
    : [...robots];

  filtered.sort(compareRobots);
  return filtered;
}

function shouldAutoRefreshNow() {
  if (!autoRefreshEnabled || !getSessionToken()) return false;
  if (document.visibilityState !== "visible") return false;

  const active = document.activeElement;
  if (!active || active === document.body) return true;
  if (["INPUT", "SELECT", "TEXTAREA"].includes(active.tagName)) return false;

  return true;
}

function syncAutoRefreshTimer() {
  if (refreshTimer) {
    clearInterval(refreshTimer);
    refreshTimer = null;
  }

  if (shouldAutoRefreshNow()) {
    refreshTimer = setInterval(() => {
      if (shouldAutoRefreshNow()) {
        refreshStatus("", "success", { silent: true });
      }
    }, REFRESH_INTERVAL_MS);
  }
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
    await refreshStatus("Signed in.", "success", { force: true });
  } catch (error) {
    await renderLogin(error.message, true);
  }
}

async function renderLogin(message = "", isError = false) {
  syncAutoRefreshTimer();
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

async function setRobotArchive(deviceId, hidden) {
  await apiFetch(`/api/portal/status/robots/${encodeURIComponent(deviceId)}/archive`, {
    method: "POST",
    body: JSON.stringify({ hidden }),
  });
  await refreshStatus(hidden ? "Robot archived from the default view." : "Robot restored to the default view.");
}

async function openRobotArtifacts(deviceId, robotName) {
  activeLogViewer = { deviceId, robotName, loading: true, artifacts: [], unassignedCredentials: [], selected: null, error: "" };
  renderStatusView(latestSummary);
  try {
    const payload = await apiFetch(`/api/portal/status/robots/${encodeURIComponent(deviceId)}/artifacts`);
    activeLogViewer = { ...activeLogViewer, loading: false, artifacts: payload.artifacts || [], unassignedCredentials: payload.unassignedCredentials || [] };
  } catch (error) {
    activeLogViewer = { ...activeLogViewer, loading: false, error: error.message };
  }
  renderStatusView(latestSummary);
}

async function openArtifact(path) {
  if (!activeLogViewer) return;
  activeLogViewer = { ...activeLogViewer, loadingContent: true, error: "" };
  renderStatusView(latestSummary);
  try {
    const payload = await apiFetch(`/api/portal/status/robots/${encodeURIComponent(activeLogViewer.deviceId)}/artifacts/content?path=${encodeURIComponent(path)}`);
    activeLogViewer = { ...activeLogViewer, loadingContent: false, selected: payload };
  } catch (error) {
    activeLogViewer = { ...activeLogViewer, loadingContent: false, error: error.message };
  }
  renderStatusView(latestSummary);
}

async function claimArtifactCredential() {
  if (!activeLogViewer) return;
  const select = document.getElementById("artifactCredentialFingerprint");
  const fingerprint = select?.value;
  if (!fingerprint) return;
  activeLogViewer = { ...activeLogViewer, claiming: true, error: "" };
  renderStatusView(latestSummary);
  try {
    const payload = await apiFetch(`/api/portal/status/robots/${encodeURIComponent(activeLogViewer.deviceId)}/credential-bindings`, {
      method: "POST",
      body: JSON.stringify({ accessKeyFingerprint: fingerprint }),
    });
    await openRobotArtifacts(activeLogViewer.deviceId, activeLogViewer.robotName);
    await refreshStatus(`Credential claimed; ${payload.backfilledArtifacts || 0} stored artifact(s) attributed.`, "success", { silent: true });
  } catch (error) {
    activeLogViewer = { ...activeLogViewer, claiming: false, error: error.message };
    renderStatusView(latestSummary);
  }
}

async function swapArtifactCredentials() {
  if (!activeLogViewer) return;
  const firstAccessKeyFingerprint = document.getElementById("artifactCredentialSwapFirst")?.value;
  const secondAccessKeyFingerprint = document.getElementById("artifactCredentialSwapSecond")?.value;
  if (!firstAccessKeyFingerprint || !secondAccessKeyFingerprint || firstAccessKeyFingerprint === secondAccessKeyFingerprint) return;
  if (!window.confirm(`Swap the two credential bindings?\n\n${firstAccessKeyFingerprint} ↔ ${secondAccessKeyFingerprint}\n\nOnly AWS credential attribution and its prior credential-backfill artifacts will be corrected.`)) return;
  try {
    const result = await apiFetch("/api/portal/status/credential-bindings/swap", {
      method: "POST",
      body: JSON.stringify({ firstAccessKeyFingerprint, secondAccessKeyFingerprint, confirmed: true }),
    });
    await refreshStatus(`Credential bindings swapped; ${result.reassignedArtifacts || 0} backfilled artifact(s) corrected.`, "success", { force: true });
    await openRobotArtifacts(activeLogViewer.deviceId, activeLogViewer.robotName);
  } catch (error) {
    activeLogViewer = { ...activeLogViewer, error: error.message };
    renderStatusView(latestSummary);
  }
}

async function mergeRobotFromArtifactViewer() {
  if (!activeLogViewer) return;
  const sourceDeviceId = document.getElementById("artifactMergeSource")?.value;
  if (!sourceDeviceId) return;
  const targetDeviceId = activeLogViewer.deviceId;
  try {
    const preview = await apiFetch(`/api/portal/status/robots/${encodeURIComponent(sourceDeviceId)}/merge-preview?targetDeviceId=${encodeURIComponent(targetDeviceId)}`);
    if (!window.confirm(`Merge ${sourceDeviceId} into ${targetDeviceId}?\n\nMoves ${preview.sessionCount} session(s), ${preview.credentialBindingCount} credential binding(s), and ${preview.artifactCount} artifact(s). The source is archived. Household loops are not merged.`)) return;
    const result = await apiFetch(`/api/portal/status/robots/${encodeURIComponent(sourceDeviceId)}/merge`, {
      method: "POST",
      body: JSON.stringify({ targetDeviceId }),
    });
    await refreshStatus(`Robot merged; ${result.migratedArtifacts || 0} artifact(s) reassigned.`, "success", { force: true });
    await openRobotArtifacts(targetDeviceId, activeLogViewer.robotName);
  } catch (error) {
    activeLogViewer = { ...activeLogViewer, error: error.message };
    renderStatusView(latestSummary);
  }
}

function renderLogViewer() {
  if (!activeLogViewer) return "";
  const viewer = activeLogViewer;
  const items = viewer.artifacts || [];
  const list = viewer.loading
    ? `<p class="muted-row">Loading stored logs…</p>`
    : !items.length
      ? `<p class="muted-row">No stored log artifacts for this robot yet.</p>`
      : `<div class="log-list">${items.map((log) => `
          <button class="log-item view-artifact" data-path="${escapeHtml(log.path)}" type="button">
            <strong>${escapeHtml(log.category || "log")}${log.unassigned ? " · Unassigned" : ""}</strong>
            <span>${escapeHtml(formatDate(log.storedUtc))} · ${escapeHtml(log.contentLength || "?")} bytes</span>
            <small class="mono">${escapeHtml(log.path)}</small>
            ${log.identitySource ? `<small>Attributed by ${escapeHtml(log.identitySource)}${log.mergedFromDeviceId ? ` · merged from ${escapeHtml(log.mergedFromDeviceId)}` : ""}</small>` : ""}
          </button>`).join("")}</div>`;
  const audit = viewer.loading ? "" : renderArtifactAudit(items);
  const preview = viewer.loadingContent
    ? `<p class="muted-row">Loading log preview…</p>`
    : viewer.selected
      ? renderArtifactPreview(viewer.selected)
      : `<p class="muted-row">Select an artifact to inspect its decoded text preview.</p>`;
  const credentials = viewer.unassignedCredentials || [];
  const claim = credentials.length
    ? `<div class="artifact-claim"><label for="artifactCredentialFingerprint">Claim unassigned credential</label>
        <div class="button-row"><select id="artifactCredentialFingerprint">${credentials.map((credential) =>
          `<option value="${escapeHtml(credential.fingerprint)}">${escapeHtml(credential.fingerprint)} · ${escapeHtml(credential.artifactCount)} artifact(s)</option>`).join("")}</select>
          <button class="button secondary compact claim-artifact-credential" type="button" ${viewer.claiming ? "disabled" : ""}>${viewer.claiming ? "Claiming..." : "Claim to this robot"}</button></div></div>`
    : "";
  const mergeCandidates = (latestSummary?.inventory || []).filter((robot) => robot.deviceId !== viewer.deviceId && !robot.isHidden);
  const merge = mergeCandidates.length
    ? `<div class="artifact-claim"><label for="artifactMergeSource">Merge a duplicate into this robot</label>
        <div class="button-row"><select id="artifactMergeSource"><option value="">Choose source robot...</option>${mergeCandidates.map((robot) =>
          `<option value="${escapeHtml(robot.deviceId)}">${escapeHtml(robot.robotId || robot.friendlyName || robot.deviceId)}</option>`).join("")}</select>
          <button class="button secondary compact merge-artifact-robot" type="button">Preview merge</button></div></div>`
    : "";
  const allBindings = latestSummary?.credentialBindings || [];
  const currentBindings = allBindings.filter((binding) => binding.deviceId === viewer.deviceId);
  const otherBindings = allBindings.filter((binding) => binding.deviceId !== viewer.deviceId);
  const credentialSwap = currentBindings.length && otherBindings.length
    ? `<div class="artifact-claim"><label>Correct an AWS credential swap</label>
        <div class="button-row"><select id="artifactCredentialSwapFirst">${currentBindings.map((binding) =>
          `<option value="${escapeHtml(binding.accessKeyFingerprint)}">${escapeHtml(binding.accessKeyFingerprint)} · this robot</option>`).join("")}</select>
          <select id="artifactCredentialSwapSecond">${otherBindings.map((binding) =>
          `<option value="${escapeHtml(binding.accessKeyFingerprint)}">${escapeHtml(binding.accessKeyFingerprint)} · ${escapeHtml(binding.deviceId)}</option>`).join("")}</select>
          <button class="button secondary compact swap-artifact-credentials" type="button">Swap bindings</button></div></div>`
    : "";
  const advancedTools = [claim, credentialSwap, merge].filter(Boolean).join("");
  const advanced = advancedTools
    ? `<details class="advanced-identity-tools"><summary>Advanced identity tools</summary>
        <p class="muted-row">These actions change explicit administrator attribution only. They never infer ownership from traffic.</p>
        ${advancedTools}</details>`
    : "";

  return `
    <section class="card panel tight log-viewer" aria-live="polite">
      <div class="panel-header">
        <div><p class="eyebrow">Diagnostics</p><h2>Stored logs · ${escapeHtml(viewer.robotName)}</h2></div>
        <button class="button secondary compact close-log-viewer" type="button">Close</button>
      </div>
      ${viewer.error ? `<p class="status error">${escapeHtml(viewer.error)}</p>` : ""}
      ${advanced}
      ${audit}
      <div class="log-viewer-grid"><div>${list}</div><div class="log-preview-panel">${preview}</div></div>
    </section>`;
}

function renderArtifactAudit(items) {
  const totalBytes = items.reduce((total, item) => total + (Number(item.contentLength) || 0), 0);
  const asr = items.filter((item) => item.category === "asr" || item.artifactType === "websocket-asr-audio");
  const logs = items.filter((item) => item.source === "log");
  const media = items.filter((item) => item.source === "media" && !asr.includes(item));
  const unassigned = items.filter((item) => item.unassigned);
  const sources = [...new Set(items.map((item) => item.identitySource).filter(Boolean))];
  const formatBytes = (bytes) => bytes >= 1024 * 1024
    ? `${(bytes / (1024 * 1024)).toFixed(1)} MB`
    : bytes >= 1024 ? `${(bytes / 1024).toFixed(1)} KB` : `${bytes} bytes`;

  return `<section class="artifact-audit" aria-label="Artifact capture audit">
    <div><strong>${items.length}</strong><span>stored artifacts</span></div>
    <div><strong>${formatBytes(totalBytes)}</strong><span>captured data</span></div>
    <div><strong>${asr.length ? `${asr.length} captured` : "Not yet captured"}</strong><span>ASR audio</span></div>
    <div><strong>${logs.length}</strong><span>log uploads</span></div>
    <div><strong>${media.length}</strong><span>other media</span></div>
    <div><strong>${unassigned.length ? `${unassigned.length} needs review` : "All assigned"}</strong><span>attribution</span></div>
    ${sources.length ? `<p class="muted-row">Attribution sources: ${escapeHtml(sources.join(", "))}</p>` : ""}
  </section>`;
}

function renderArtifactPreview(artifact) {
  const summary = `<p class="muted-row">${escapeHtml(artifact.summary || artifact.contentType || "Artifact")}</p>`;
  if (artifact.kind === "image" && artifact.dataUrl)
    return `${summary}<img class="artifact-image" src="${artifact.dataUrl}" alt="Stored robot artifact">`;
  if (artifact.kind === "audio" && artifact.dataUrl)
    return `${summary}<audio class="artifact-audio" controls src="${artifact.dataUrl}"></audio>`;
  if (artifact.kind === "zip") {
    const entries = artifact.archiveEntries || [];
    return `${summary}<ul class="artifact-entries">${entries.map((entry) => `<li><span class="mono">${escapeHtml(entry.name)}</span> <span class="muted-row">${escapeHtml(entry.length)} bytes</span></li>`).join("")}</ul>`;
  }
  return `${summary}${artifact.text ? `<pre class="log-preview">${escapeHtml(artifact.text)}</pre>` : `<p class="muted-row">Preview unavailable for this binary artifact.</p>`}`;
}

function renderRecentSessions(rows = [], robots = [], changedSessionIds = new Set()) {
  if (!rows.length) {
    return `<li class="muted-row">No recent live sessions.</li>`;
  }

  return rows.map((session) => `
    <li class="${changedSessionIds.has(session.sessionId) ? "changed-row" : ""}">
      <strong>${escapeHtml(session.kind || "unknown")}</strong>
      <span>Observed runtime ID: ${escapeHtml(session.deviceId || "-")} · ${escapeHtml(session.hostName || "-")}${session.path ? ` · ${escapeHtml(session.path)}` : ""}</span>
      <div class="muted-row">${formatDate(session.lastSeenUtc)} · heartbeat ${formatFloat(session.heartbeatAgeSeconds, 0)}s ago</div>
      ${session.registeredDeviceId
        ? `<div class="muted-row">Explicit admin link: ${escapeHtml(session.registeredDeviceId)}</div>`
        : `<div class="muted-row">Unclaimed session — observed traffic never assigns a robot.</div>`}
      ${renderSessionBindingAudit(session.sessionBindingAudit)}
      <div class="button-row session-link-row">
        <select class="session-device-select" data-session-id="${escapeHtml(session.sessionId)}" aria-label="Robot record for live session">
          <option value="">${session.registeredDeviceId ? "Replace explicit link..." : "Link to robot..."}</option>${robots.filter((robot) => !robot.isHidden).map((robot) =>
            `<option value="${escapeHtml(robot.deviceId)}" ${robot.deviceId === session.registeredDeviceId ? "selected" : ""}>${escapeHtml(robotSelectorLabel(robot))}</option>`
          ).join("")}
        </select>
        <button class="button secondary compact link-session" data-session-id="${escapeHtml(session.sessionId)}" type="button">${session.registeredDeviceId ? "Replace" : "Link"}</button>
        ${session.registeredDeviceId ? `<button class="button secondary compact unlink-session" data-session-id="${escapeHtml(session.sessionId)}" type="button">Unlink</button>` : ""}
      </div>
    </li>
  `).join("");
}

function renderSessionBindingAudit(rawAudit) {
  if (!rawAudit) return "";
  try {
    const entries = JSON.parse(rawAudit);
    if (!Array.isArray(entries) || !entries.length) return "";
    const recent = entries.slice(-3).reverse();
    return `<details class="session-binding-audit"><summary>Link audit (${entries.length})</summary><ul>${recent.map((entry) =>
      `<li>${escapeHtml(formatDate(entry.OccurredUtc || entry.occurredUtc))} · ${escapeHtml(entry.Action || entry.action || "changed")} · ${escapeHtml(entry.DeviceId || entry.deviceId || entry.PreviousDeviceId || entry.previousDeviceId || "no robot")}</li>`
    ).join("")}</ul></details>`;
  } catch {
    return "";
  }
}

async function linkLiveSession(sessionId) {
  const select = document.querySelector(`.session-device-select[data-session-id="${CSS.escape(sessionId)}"]`);
  if (!select?.value) {
    await refreshStatus("Choose the inventory record that this live session belongs to.", "error", { preserveBanner: false });
    return;
  }

  if (!window.confirm(`Explicitly link this session to ${select.value}?\n\nObserved traffic will not change this link.`)) return;

  await apiFetch(`/api/portal/status/sessions/${encodeURIComponent(sessionId)}/link`, {
    method: "POST",
    body: JSON.stringify({ deviceId: select.value }),
  });
  await refreshStatus("Live session linked to the selected robot record.");
}

async function unlinkLiveSession(sessionId) {
  await apiFetch(`/api/portal/status/sessions/${encodeURIComponent(sessionId)}/link`, {
    method: "DELETE",
  });
  await refreshStatus("Live session link removed.");
}

function renderRobotControls(filteredCount, totalCount, totalPages) {
  const options = ROBOT_PAGE_SIZE_OPTIONS.map((size) => `
    <option value="${size}" ${robotPageSize === size ? "selected" : ""}>${size} / page</option>
  `).join("");

  const sortDirectionLabel = robotSortDirection === "asc" ? "Ascending" : "Descending";
  const sortDirectionClass = robotSortDirection === "asc" ? "secondary" : "secondary";

  return `
    <div class="robot-toolbar">
      <label class="toolbar-field">
        <span>Search robots</span>
        <input id="robotSearchInput" type="search" placeholder="Name, device ID, robot ID, version..." value="${escapeHtml(robotSearchQuery)}">
      </label>

      <label class="toolbar-field">
        <span>Sort by</span>
        <select id="robotSortSelect">
          <option value="lastSeenUtc" ${robotSortKey === "lastSeenUtc" ? "selected" : ""}>Last seen</option>
          <option value="heartbeat" ${robotSortKey === "heartbeat" ? "selected" : ""}>Heartbeat age</option>
          <option value="name" ${robotSortKey === "name" ? "selected" : ""}>Name</option>
          <option value="presence" ${robotSortKey === "presence" ? "selected" : ""}>Presence</option>
          <option value="sessions" ${robotSortKey === "sessions" ? "selected" : ""}>Live sessions</option>
        </select>
      </label>

      <label class="toolbar-field">
        <span>Rows per page</span>
        <select id="robotPageSizeSelect">
          ${options}
        </select>
      </label>

      <div class="toolbar-actions">
        <button class="button secondary compact" id="robotSortDirectionButton" type="button">${sortDirectionLabel}</button>
        <button class="button secondary compact" id="robotClearSearchButton" type="button">Clear search</button>
      </div>

      <div class="toolbar-summary">
        <span>${filteredCount} of ${totalCount} robots</span>
        <span>${totalPages} page${totalPages === 1 ? "" : "s"}</span>
      </div>
    </div>
  `;
}

function renderPagination(filteredCount) {
  const totalPages = Math.max(1, Math.ceil(filteredCount / robotPageSize));
  const currentPage = Math.min(robotPage, totalPages);
  const start = filteredCount ? ((currentPage - 1) * robotPageSize) + 1 : 0;
  const end = filteredCount ? Math.min(filteredCount, currentPage * robotPageSize) : 0;

  return `
    <div class="pagination-bar">
      <span class="muted-row">Showing ${start}-${end} of ${filteredCount}</span>
      <div class="pagination-actions">
        <button class="button secondary compact" id="robotPrevPageButton" type="button" ${currentPage <= 1 ? "disabled" : ""}>Prev</button>
        <span class="pagination-label">Page ${currentPage} of ${totalPages}</span>
        <button class="button secondary compact" id="robotNextPageButton" type="button" ${currentPage >= totalPages ? "disabled" : ""}>Next</button>
      </div>
    </div>
  `;
}

function renderRecentSessionPagination(filteredCount) {
  const totalPages = Math.max(1, Math.ceil(filteredCount / recentSessionPageSize));
  const currentPage = Math.min(recentSessionPage, totalPages);
  const start = filteredCount ? ((currentPage - 1) * recentSessionPageSize) + 1 : 0;
  const end = filteredCount ? Math.min(filteredCount, currentPage * recentSessionPageSize) : 0;

  return `
    <div class="pagination-bar">
      <span class="muted-row">Showing ${start}-${end} of ${filteredCount}</span>
      <div class="pagination-actions">
        <label class="toolbar-field compact-field">
          <span>Rows per page</span>
          <select id="recentSessionPageSizeSelect">
            <option value="5" ${recentSessionPageSize === 5 ? "selected" : ""}>5 / page</option>
            <option value="10" ${recentSessionPageSize === 10 ? "selected" : ""}>10 / page</option>
            <option value="25" ${recentSessionPageSize === 25 ? "selected" : ""}>25 / page</option>
          </select>
        </label>
        <button class="button secondary compact" id="recentSessionPrevPageButton" type="button" ${currentPage <= 1 ? "disabled" : ""}>Prev</button>
        <span class="pagination-label">Page ${currentPage} of ${totalPages}</span>
        <button class="button secondary compact" id="recentSessionNextPageButton" type="button" ${currentPage >= totalPages ? "disabled" : ""}>Next</button>
      </div>
    </div>
  `;
}

function renderStatusView(summary, previous = previousSummary) {
  const fleet = summary.fleet || {};
  const serverFleet = summary.serverFleet || {};
  const localServer = serverFleet.localServer || {};
  const networkFleet = serverFleet.network || {};
  const service = summary.service || {};
  const robots = summary.robots || [];
  const inventory = summary.inventory || robots;
  const recentSessions = summary.recentSessions || [];
  const previousRobots = previous?.robots || [];
  const previousRecentSessions = previous?.recentSessions || [];
  const changedRobotIds = buildChangeSet(robots, previousRobots, (robot) => robot.deviceId || robot.robotId || "", robotRowSnapshot);
  const changedSessionIds = buildChangeSet(recentSessions, previousRecentSessions, (session) => session.sessionId || "", sessionRowSnapshot);
  const activeElement = document.activeElement;
  const activeSnapshot = activeElement && ["INPUT", "SELECT", "TEXTAREA"].includes(activeElement.tagName)
    ? {
      id: activeElement.id,
      value: activeElement.value,
      selectionStart: typeof activeElement.selectionStart === "number" ? activeElement.selectionStart : null,
      selectionEnd: typeof activeElement.selectionEnd === "number" ? activeElement.selectionEnd : null,
    }
    : null;

  const filteredRobots = filterAndSortRobots(robots);
  const totalRobotPages = Math.max(1, Math.ceil(filteredRobots.length / robotPageSize));
  robotPage = Math.min(Math.max(robotPage, 1), totalRobotPages);
  const startIndex = filteredRobots.length ? (robotPage - 1) * robotPageSize : 0;
  const pageRobots = filteredRobots.slice(startIndex, startIndex + robotPageSize);

  const totalRecentSessionPages = Math.max(1, Math.ceil(recentSessions.length / recentSessionPageSize));
  recentSessionPage = Math.min(Math.max(recentSessionPage, 1), totalRecentSessionPages);
  const recentSessionStartIndex = recentSessions.length ? (recentSessionPage - 1) * recentSessionPageSize : 0;
  const pageRecentSessions = recentSessions.slice(recentSessionStartIndex, recentSessionStartIndex + recentSessionPageSize);

  const liveTone = lastRefreshError ? "warning" : "success";
  const liveLabel = lastRefreshError ? "Sync issue" : autoRefreshEnabled ? "Live updates" : "Static";
  const refreshLabel = lastRefreshAt ? `Updated ${formatRelativeTime(lastRefreshAt)}` : "Not yet refreshed";
  const errorBanner = lastRefreshError ? `<p class="status error">${escapeHtml(lastRefreshError)}</p>` : "";

  app.innerHTML = `
    <div class="status-shell">
      <section class="card status-hero">
        <div class="status-hero-top">
          <div class="status-title">
            <p class="status-kicker">OpenJibo Status</p>
            <h1>Fleet health at a glance</h1>
            <p class="status-lede">Live socket presence, recent activity, and a clean fleet inventory.</p>
          </div>
          <div class="button-row" style="margin-top: 0;">
            <a class="secondary-button" href="/portal/admin/onboarding">Onboarding</a>
            <a class="secondary-button" href="/portal/admin/harness">Harness</a>
            <a class="secondary-button" href="/portal">Customer portal</a>
            <button class="button secondary" id="refreshButton" type="button">Refresh</button>
            <button class="button danger" id="logoutButton" type="button">Sign out</button>
          </div>
        </div>

        <div class="status-meta">
          <span class="badge ${liveTone}">${escapeHtml(liveLabel)}</span>
          <span class="badge neutral">${escapeHtml(refreshLabel)}</span>
          <span class="badge neutral">${escapeHtml(REFRESH_INTERVAL_MS / 1000)}s polling</span>
          <span class="badge success">${escapeHtml(fleet.connectedRobots ?? 0)} online</span>
          <span class="badge neutral">${escapeHtml(fleet.sleepingRobots ?? 0)} sleeping</span>
          <span class="badge neutral">${escapeHtml(fleet.recentlySeenRobots ?? 0)} recently seen</span>
          <span class="badge neutral">${escapeHtml(networkFleet.connectedRobots ?? 0)} network online</span>
          <span class="badge neutral">${escapeHtml(fleet.registeredRobots ?? 0)} registered</span>
          <span class="badge warning">${escapeHtml(fleet.hiddenRobots ?? 0)} archived</span>
          <span class="badge warning">${escapeHtml(fleet.staleSessions ?? 0)} stale sessions</span>
          <span class="badge neutral">Uptime ${escapeHtml(service.uptimeLabel || "-")}</span>
        </div>

        <div class="stat-grid">
          <div class="stat-card">
            <span class="label">Online robots</span>
            <span class="value">${fleet.connectedRobots ?? 0}</span>
            <span class="detail">Open sockets or robot traffic in the last two minutes.</span>
          </div>
          <div class="stat-card">
            <span class="label">Visible robots</span>
            <span class="value">${fleet.visibleRobots ?? 0}</span>
            <span class="detail">${fleet.syntheticRobots ?? 0} known synthetic records.</span>
          </div>
          <div class="stat-card">
            <span class="label">Service uptime</span>
            <span class="value">${escapeHtml(service.uptimeLabel || "-")}</span>
            <span class="detail">Started ${formatDate(service.startedAtUtc)}.</span>
          </div>
          <div class="stat-card">
            <span class="label">Network fleet</span>
            <span class="value">${networkFleet.connectedRobots ?? 0}</span>
            <span class="detail">${networkFleet.reportingServers ?? 0}/${networkFleet.knownServers ?? 0} trusted servers reporting.</span>
          </div>
        </div>

        <div class="status-footer">
          <span>Generated ${formatDate(summary.generatedAtUtc)}</span>
          <span>This server ${escapeHtml(localServer.canonicalHost || "-")} · ${localServer.connectedRobots ?? 0} robots</span>
          <span>Persistence rev ${escapeHtml(summary.persistence?.revision ?? "-")}</span>
        </div>
      </section>

      <div class="status-grid">
        <section class="card panel tight">
          <div class="panel-header">
            <div>
              <p class="eyebrow">Fleet</p>
              <h2>Robot inventory on this server</h2>
            </div>
            <label class="toggle-control"><input id="includeHiddenToggle" type="checkbox" ${includeHidden ? "checked" : ""}> Show archived</label>
          </div>

          ${renderRobotControls(filteredRobots.length, robots.length, totalRobotPages)}

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
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                ${renderRobotRows(pageRobots, changedRobotIds)}
              </tbody>
            </table>
          </div>

          ${renderPagination(filteredRobots.length)}
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
            ${renderRecentSessions(pageRecentSessions, inventory, changedSessionIds)}
          </ol>
          ${renderRecentSessionPagination(recentSessions.length)}
          <div class="status-divider"></div>
          <div class="meta-list compact">
            <div class="meta-item"><span>Latest seen</span><span>${formatDate(fleet.latestSeenUtc)}</span></div>
            <div class="meta-item"><span>Oldest live session</span><span>${formatDate(fleet.oldestLiveSessionCreatedUtc)}</span></div>
            <div class="meta-item"><span>Stale sessions</span><span>${fleet.staleSessions ?? 0}</span></div>
            <div class="meta-item"><span>Average heartbeat age</span><span>${formatFloat(fleet.averageHeartbeatAgeSeconds, 0)}s</span></div>
          </div>
        </section>
      </div>

      ${renderLogViewer()}

      ${errorBanner}
      ${bannerMessage && !lastRefreshError ? `<p class="status ${bannerTone}" style="margin-top: 1rem;">${escapeHtml(bannerMessage)}</p>` : ""}
    </div>
  `;

  document.getElementById("refreshButton").addEventListener("click", () => refreshStatus("Status refreshed."));
  document.getElementById("logoutButton").addEventListener("click", logout);
  document.getElementById("includeHiddenToggle").addEventListener("change", (event) => {
    includeHidden = event.target.checked;
    robotPage = 1;
    refreshStatus();
  });
  document.getElementById("robotSearchInput").addEventListener("input", (event) => {
    robotSearchQuery = event.target.value;
    robotPage = 1;
    if (latestSummary) {
      renderStatusView(latestSummary);
    }
  });
  document.getElementById("robotSortSelect").addEventListener("change", (event) => {
    robotSortKey = event.target.value;
    robotPage = 1;
    if (latestSummary) {
      renderStatusView(latestSummary);
    }
  });
  document.getElementById("robotPageSizeSelect").addEventListener("change", (event) => {
    robotPageSize = Number(event.target.value) || 10;
    robotPage = 1;
    if (latestSummary) {
      renderStatusView(latestSummary);
    }
  });
  document.getElementById("robotSortDirectionButton").addEventListener("click", () => {
    robotSortDirection = robotSortDirection === "asc" ? "desc" : "asc";
    if (latestSummary) {
      renderStatusView(latestSummary);
    }
  });
  document.getElementById("robotClearSearchButton").addEventListener("click", () => {
    robotSearchQuery = "";
    robotPage = 1;
    if (latestSummary) {
      renderStatusView(latestSummary);
    }
  });
  document.getElementById("robotPrevPageButton").addEventListener("click", () => {
    robotPage = Math.max(1, robotPage - 1);
    if (latestSummary) {
      renderStatusView(latestSummary);
    }
  });
  document.getElementById("robotNextPageButton").addEventListener("click", () => {
    robotPage += 1;
    if (latestSummary) {
      renderStatusView(latestSummary);
    }
  });
  document.getElementById("recentSessionPrevPageButton").addEventListener("click", () => {
    recentSessionPage = Math.max(1, recentSessionPage - 1);
    if (latestSummary) {
      renderStatusView(latestSummary);
    }
  });
  document.getElementById("recentSessionNextPageButton").addEventListener("click", () => {
    recentSessionPage += 1;
    if (latestSummary) {
      renderStatusView(latestSummary);
    }
  });
  document.getElementById("recentSessionPageSizeSelect").addEventListener("change", (event) => {
    recentSessionPageSize = Number(event.target.value) || 5;
    recentSessionPage = 1;
    if (latestSummary) {
      renderStatusView(latestSummary);
    }
  });

  document.querySelectorAll(".archive-robot").forEach((button) => {
    button.addEventListener("click", () => setRobotArchive(button.dataset.deviceId, button.dataset.hidden === "true"));
  });
  document.querySelectorAll(".view-artifacts").forEach((button) => {
    button.addEventListener("click", () => openRobotArtifacts(button.dataset.deviceId, button.dataset.robotName));
  });
  document.querySelectorAll(".view-artifact").forEach((button) => {
    button.addEventListener("click", () => openArtifact(button.dataset.path));
  });
  document.querySelectorAll(".claim-artifact-credential").forEach((button) => {
    button.addEventListener("click", claimArtifactCredential);
  });
  document.querySelectorAll(".swap-artifact-credentials").forEach((button) => {
    button.addEventListener("click", swapArtifactCredentials);
  });
  document.querySelectorAll(".merge-artifact-robot").forEach((button) => {
    button.addEventListener("click", mergeRobotFromArtifactViewer);
  });
  document.querySelectorAll(".open-lrd").forEach((button) => {
    button.addEventListener("click", () => {
      const deviceId = button.dataset.deviceId;
      const robotName = button.dataset.robotName;
      window.open(`/portal/lrd?deviceId=${encodeURIComponent(deviceId)}&robotName=${encodeURIComponent(robotName)}`, '_blank');
    });
  });
  document.querySelectorAll(".close-log-viewer").forEach((button) => {
    button.addEventListener("click", () => {
      activeLogViewer = null;
      renderStatusView(latestSummary);
    });
  });
  document.querySelectorAll(".link-session").forEach((button) => {
    button.addEventListener("click", () => linkLiveSession(button.dataset.sessionId));
  });
  document.querySelectorAll(".unlink-session").forEach((button) => {
    button.addEventListener("click", () => unlinkLiveSession(button.dataset.sessionId));
  });

  if (activeSnapshot?.id) {
    const restored = document.getElementById(activeSnapshot.id);
    if (restored) {
      restored.focus();
      if (typeof restored.setSelectionRange === "function" && activeSnapshot.selectionStart != null && activeSnapshot.selectionEnd != null) {
        restored.setSelectionRange(activeSnapshot.selectionStart, activeSnapshot.selectionEnd);
      }
    }
  }
}

async function refreshStatus(message = "", tone = "success", options = {}) {
  if (refreshInFlight && !options.force) {
    return;
  }

  const hadRefreshError = Boolean(lastRefreshError);
  if (message || options.replaceBanner) {
    setStatusBanner(message, tone);
  }

  refreshInFlight = true;
  try {
    const summary = await apiFetch(`/api/portal/status/summary?includeHidden=${includeHidden}`);
    previousSummary = latestSummary;
    latestSummary = summary;
    lastRefreshAt = new Date().toISOString();
    lastRefreshError = "";
    if (message && !options.preserveBanner) {
      setStatusBanner(message, tone);
    } else if (hadRefreshError && !options.preserveBanner) {
      setStatusBanner("", "success");
    }
    renderStatusView(summary);
  } catch (error) {
    if (error.status === 401 || error.status === 403) {
      clearSessionToken();
      latestSummary = null;
      lastRefreshError = "";
      await renderLogin(error.message, true);
      return;
    }

    lastRefreshError = error.message;
    if (!latestSummary) {
      await renderLogin(error.message, true);
      return;
    }

    if (!options.preserveBanner) {
      setStatusBanner(error.message, "error");
    }
    renderStatusView(latestSummary);
  } finally {
    refreshInFlight = false;
    syncAutoRefreshTimer();
  }
}

async function logout() {
  const token = getSessionToken();
  clearSessionToken();
  previousSummary = null;
  latestSummary = null;
  lastRefreshError = "";
  lastRefreshAt = null;

  if (refreshTimer) {
    clearInterval(refreshTimer);
    refreshTimer = null;
  }

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
  document.addEventListener("visibilitychange", () => {
    syncAutoRefreshTimer();
    if (document.visibilityState === "visible" && getSessionToken()) {
      refreshStatus("", "success", { silent: true });
    }
  });
  document.addEventListener("focusin", syncAutoRefreshTimer);
  document.addEventListener("focusout", () => {
    setTimeout(syncAutoRefreshTimer, 0);
  });

  if (getSessionToken()) {
    previousSummary = null;
    await refreshStatus("", "success", { force: true, silent: true });
    return;
  }

  await renderLogin();
}

bootstrap();
