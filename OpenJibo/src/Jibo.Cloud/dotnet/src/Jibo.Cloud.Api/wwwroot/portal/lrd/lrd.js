const SESSION_KEY = "openjibo_status_session";
const PING_INTERVAL_MS = 5000;

let selectedRobotId = null;
let robots = [];
let pingInterval = null;

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

function getUrlParams() {
  const params = new URLSearchParams(window.location.search);
  return {
    deviceId: params.get("deviceId"),
    robotName: params.get("robotName")
  };
}

function updateServerConnectionStatus(connected) {
  const el = document.getElementById("serverConnectionStatus");
  if (el) {
    el.textContent = connected ? "Connected" : "Disconnected";
    el.style.color = connected ? "#10b981" : "#ef4444";
  }
}

function updateServerPing(pingMs) {
  const el = document.getElementById("serverPing");
  if (el) {
    el.textContent = pingMs !== null ? `${pingMs} ms` : "-- ms";
  }
}

function updateRobotConnectionStatus(connected) {
  const el = document.getElementById("robotConnectionStatus");
  if (el) {
    el.textContent = connected ? "Connected" : "Disconnected";
    el.style.color = connected ? "#10b981" : "#ef4444";
  }
}

function populateRobotSelect(robotList, selectedId) {
  const select = document.getElementById("robotSelect");
  if (!select) return;

  select.innerHTML = robotList.length
    ? robotList.map(robot =>
      `<option value="${escapeHtml(robot.deviceId)}" ${robot.deviceId === selectedId ? "selected" : ""}>
        ${escapeHtml(robot.robotId || robot.friendlyName || robot.deviceId)}
      </option>`
    ).join("")
    : '<option value="">No robots available</option>';
}

async function measureServerPing() {
  const start = performance.now();
  try {
    await apiFetch("/health");
    const pingMs = Math.round(performance.now() - start);
    updateServerPing(pingMs);
    updateServerConnectionStatus(true);
    return pingMs;
  } catch (error) {
    updateServerPing(null);
    updateServerConnectionStatus(false);
    return null;
  }
}

async function fetchRobots() {
  try {
    const summary = await apiFetch("/api/portal/status/summary");
    robots = summary.robots || [];
    populateRobotSelect(robots, selectedRobotId);
    updateRobotConnectionForSelected();
  } catch (error) {
    console.error("Failed to fetch robots:", error);
    robots = [];
    populateRobotSelect([], selectedRobotId);
  }
}

function updateRobotConnectionForSelected() {
  if (!selectedRobotId) {
    updateRobotConnectionStatus(false);
    return;
  }

  const robot = robots.find(r => r.deviceId === selectedRobotId);
  const isConnected = robot && (robot.presence === "online" || robot.presence === "sleeping");
  updateRobotConnectionStatus(isConnected);
}

function handleRobotSelectChange() {
  const select = document.getElementById("robotSelect");
  if (select) {
    selectedRobotId = select.value || null;
    updateRobotConnectionForSelected();
  }
}

async function init() {
  const params = getUrlParams();
  selectedRobotId = params.deviceId;

  // Set up robot select change handler
  const robotSelect = document.getElementById("robotSelect");
  if (robotSelect) {
    robotSelect.addEventListener("change", handleRobotSelectChange);
  }

  // Initial server ping check
  await measureServerPing();

  // Fetch robots and populate select
  await fetchRobots();

  // Start periodic ping checks
  pingInterval = setInterval(measureServerPing, PING_INTERVAL_MS);
}

// Clean up on page unload
window.addEventListener("beforeunload", () => {
  if (pingInterval) {
    clearInterval(pingInterval);
  }
});

init();
