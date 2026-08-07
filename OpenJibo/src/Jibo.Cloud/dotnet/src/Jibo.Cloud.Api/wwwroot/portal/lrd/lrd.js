const SESSION_KEY = "openjibo_status_session";
const PING_INTERVAL_MS = 5000;
const LOG_POLL_INTERVAL_MS = 1000;
const MAX_LOG_LINES = 500; // Maximum number of log lines to keep in DOM

let selectedRobotId = null;
let robots = [];
let pingInterval = null;
let logPollInterval = null;
let logOffset = 0;
let currentLogFile = null;
let robotStreamAbortController = null;
let logPollInFlight = false;
let skipLogPolling = false;

function setLogPollingEnabled(enabled) {
  skipLogPolling = !enabled;
  if (!enabled && logPollInterval) {
    clearInterval(logPollInterval);
    logPollInterval = null;
  } else if (enabled && !logPollInterval) {
    logPollInterval = setInterval(fetchServerLogs, LOG_POLL_INTERVAL_MS);
  }
}

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

async function fetchServerLogs() {
  if (skipLogPolling || logPollInFlight) return;
  logPollInFlight = true;
  try {
    const fileParam = currentLogFile ? `&fileName=${encodeURIComponent(currentLogFile)}` : "";
    const response = await apiFetch(`/api/portal/server/logs?offset=${logOffset}&lines=100${fileParam}`);
    console.log("Server logs response:", response);

    if (response.hasLogs && response.logs) {
      // Check if log file changed
      if (response.fileName && response.fileName !== currentLogFile) {
        currentLogFile = response.fileName;
        logOffset = 0;
        const rightContent = document.getElementById("rightLogContent");
        if (rightContent) {
          rightContent.innerHTML = '';
        }
      }

      if (response.logs) {
        appendServerLogs(response.logs);
      }

      logOffset = response.offset || 0;
    } else {
      console.log("No logs available, hasLogs:", response.hasLogs);
    }
  } catch (error) {
    console.error("Failed to fetch server logs:", error);
  } finally {
    logPollInFlight = false;
  }
}

function appendServerLogs(logText) {
  const rightContent = document.getElementById("rightLogContent");
  if (!rightContent) return;

  console.log("Appending server logs, length:", logText.length);

  // Check if user is at the bottom before adding new logs
  const isAtBottom = rightContent.scrollHeight - rightContent.scrollTop <= rightContent.clientHeight + 50;

  const lines = logText.split('\n');
  lines.forEach(line => {
    if (line.trim()) {
      const logLine = document.createElement('div');
      logLine.className = 'log-line';

      // Try to detect if line is JSON for better highlighting
      let language = 'plaintext';
      let cleanLine = line;

      if (line.trim().startsWith('{') && line.trim().endsWith('}')) {
        try {
          JSON.parse(line);
          language = 'json';
        } catch (e) {
          // Not valid JSON, keep as plaintext
        }
      }

      // Use PrismJS for syntax highlighting
      try {
        const highlighted = Prism.highlight(cleanLine, Prism.languages[language] || Prism.languages.plaintext, language);
        logLine.innerHTML = highlighted;
      } catch (e) {
        console.error("Prism highlight error:", e);
        logLine.textContent = cleanLine;
      }
      rightContent.appendChild(logLine);
    }
  });

  // Remove old lines to prevent DOM overload
  const logLines = rightContent.querySelectorAll('.log-line');
  if (logLines.length > MAX_LOG_LINES) {
    const linesToRemove = logLines.length - MAX_LOG_LINES;
    for (let i = 0; i < linesToRemove; i++) {
      rightContent.removeChild(logLines[i]);
    }
  }

  // Auto-scroll to bottom only if user was already at the bottom
  if (isAtBottom) {
    rightContent.scrollTop = rightContent.scrollHeight;
  }
}

function appendRobotLogs(logText) {
  const leftContent = document.getElementById("leftLogContent");
  if (!leftContent) return;

  // Check if user is at the bottom before adding new logs
  const isAtBottom = leftContent.scrollHeight - leftContent.scrollTop <= leftContent.clientHeight + 50;

  const lines = logText.split('\n');
  lines.forEach(line => {
    if (line.trim()) {
      const logLine = document.createElement('div');
      logLine.className = 'log-line';

      let language = 'plaintext';
      let cleanLine = line;

      if (line.trim().startsWith('{') && line.trim().endsWith('}')) {
        try {
          JSON.parse(line);
          language = 'json';
        } catch (e) {}
      }

      try {
        const highlighted = Prism.highlight(cleanLine, Prism.languages[language] || Prism.languages.plaintext, language);
        logLine.innerHTML = highlighted;
      } catch (e) {
        logLine.textContent = cleanLine;
      }
      leftContent.appendChild(logLine);
    }
  });

  const logLines = leftContent.querySelectorAll('.log-line');
  if (logLines.length > MAX_LOG_LINES) {
    const linesToRemove = logLines.length - MAX_LOG_LINES;
    for (let i = 0; i < linesToRemove; i++) {
      leftContent.removeChild(logLines[i]);
    }
  }

  if (isAtBottom) {
    leftContent.scrollTop = leftContent.scrollHeight;
  }
}

async function toggleRobotConnection() {
  const ipInput = document.getElementById("robotIpInput");
  const portInput = document.getElementById("robotPortInput");
  const schemeInput = document.getElementById("robotSchemeInput");
  const tokenInput = document.getElementById("robotTokenInput");
  const connectBtn = document.getElementById("robotConnectBtn");
  const leftContent = document.getElementById("leftLogContent");

  if (!ipInput || !connectBtn) return;

  if (robotStreamAbortController) {
    robotStreamAbortController.abort();
    robotStreamAbortController = null;
    connectBtn.textContent = "Connect";
    connectBtn.classList.remove("connected");
    if (leftContent) {
      const notice = document.createElement('div');
      notice.className = 'log-line';
      notice.style.color = '#ef4444';
      notice.textContent = '[Disconnected from robot log stream]';
      leftContent.appendChild(notice);
    }
    return;
  }

  const ip = ipInput.value.trim();
  const port = portInput ? portInput.value.trim() || "8765" : "8765";
  const scheme = schemeInput ? schemeInput.value : "http";
  const token = tokenInput ? tokenInput.value.trim() : "";

  if (!ip) {
    alert("Please enter a valid Robot IP address.");
    return;
  }

  if (leftContent) {
    leftContent.innerHTML = '';
  }

  const params = new URLSearchParams({ ip, scheme, port });
  const url = `/api/portal/lrd/stream?${params.toString()}`;
  console.log("Connecting to robot log stream at:", url);

  robotStreamAbortController = new AbortController();
  try {
    const headers = {};
    const portalToken = getSessionToken();
    if (portalToken) headers.Authorization = `Bearer ${portalToken}`;
    if (token) headers["X-Robot-Stream-Token"] = token;
    const response = await fetch(url, { headers, signal: robotStreamAbortController.signal });
    if (!response.ok || !response.body) throw new Error(`Robot stream failed (${response.status})`);
    connectBtn.textContent = "Disconnect";
    connectBtn.classList.add("connected");
    if (leftContent) appendRobotLogs(`[Connected to robot log stream through portal]`);
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let pending = "";
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      pending += decoder.decode(value, { stream: true });
      const events = pending.split("\n\n");
      pending = events.pop() || "";
      events.forEach(event => {
        const data = event.split("\n").filter(line => line.startsWith("data:")).map(line => line.slice(5).trimStart()).join("\n");
        if (data) appendRobotLogs(data);
      });
    }
  } catch (e) {
    if (e.name !== "AbortError") {
      console.error("Robot stream error:", e);
      if (leftContent) appendRobotLogs(`[Connection error / disconnected from robot stream]`);
    }
  } finally {
    if (robotStreamAbortController) robotStreamAbortController = null;
    connectBtn.textContent = "Connect";
    connectBtn.classList.remove("connected");
  }
}

function setupSplitResizer() {
  const resizer = document.getElementById("splitResizer");
  const leftPanel = document.querySelector(".left-panel");
  const rightPanel = document.querySelector(".right-panel");
  const container = document.querySelector(".split-container");

  if (!resizer || !leftPanel || !rightPanel || !container) return;
  // Mobile uses a vertical stack with equal-height panels; avoid applying the
  // desktop horizontal drag math to a touch/row layout.
  if (window.matchMedia("(max-width: 800px)").matches) return;

  let isResizing = false;

  resizer.addEventListener("mousedown", (e) => {
    isResizing = true;
    resizer.classList.add("active");
    document.body.style.cursor = "col-resize";
    document.body.style.userSelect = "none";
  });

  document.addEventListener("mousemove", (e) => {
    if (!isResizing) return;

    const containerRect = container.getBoundingClientRect();
    const containerWidth = containerRect.width;
    const newLeftWidth = e.clientX - containerRect.left;

    // Constrain to reasonable limits (20% - 80%)
    const minWidth = containerWidth * 0.2;
    const maxWidth = containerWidth * 0.8;

    if (newLeftWidth >= minWidth && newLeftWidth <= maxWidth) {
      const leftPercent = (newLeftWidth / containerWidth) * 100;
      const rightPercent = 100 - leftPercent;

      leftPanel.style.flex = `0 0 ${leftPercent}%`;
      rightPanel.style.flex = `0 0 ${rightPercent}%`;
    }
  });

  document.addEventListener("mouseup", () => {
    if (isResizing) {
      isResizing = false;
      resizer.classList.remove("active");
      document.body.style.cursor = "";
      document.body.style.userSelect = "";
    }
  });
}

async function initLogPollingToggle() {
  const toggle = document.getElementById("skipLogPollingLogsToggle");
  if (!toggle) return;

  try {
    const status = await apiFetch("/api/portal/server/logs/diagnostics-status");
    toggle.checked = !!status.disabled;
    setLogPollingEnabled(!toggle.checked);
  } catch (e) {
    console.error("Failed to fetch log polling diagnostics status:", e);
  }

  toggle.addEventListener("change", async () => {
    try {
      const res = await apiFetch("/api/portal/server/logs/toggle-diagnostics", {
        method: "POST"
      });
      toggle.checked = !!res.disabled;
      setLogPollingEnabled(!toggle.checked);
    } catch (e) {
      console.error("Failed to toggle log polling diagnostics:", e);
      toggle.checked = !toggle.checked;
      setLogPollingEnabled(!toggle.checked);
    }
  });
}

async function init() {
  const params = getUrlParams();
  selectedRobotId = params.deviceId;

  // Set up split resizer
  setupSplitResizer();

  // Set up robot select change handler
  const robotSelect = document.getElementById("robotSelect");
  if (robotSelect) {
    robotSelect.addEventListener("change", handleRobotSelectChange);
  }

  // Set up robot connect button handler
  const connectBtn = document.getElementById("robotConnectBtn");
  if (connectBtn) {
    connectBtn.addEventListener("click", toggleRobotConnection);
  }

  // Set up log polling diagnostics toggle
  await initLogPollingToggle();

  // Initial server ping check
  await measureServerPing();

  // Fetch robots and populate select
  await fetchRobots();

  // Initial server logs fetch
  await fetchServerLogs();

  // Start periodic ping checks
  pingInterval = setInterval(measureServerPing, PING_INTERVAL_MS);

  // Start periodic log polling
  if (!skipLogPolling) {
    logPollInterval = setInterval(fetchServerLogs, LOG_POLL_INTERVAL_MS);
  }
}

// Clean up on page unload
window.addEventListener("beforeunload", () => {
  if (pingInterval) {
    clearInterval(pingInterval);
  }
  if (logPollInterval) {
    clearInterval(logPollInterval);
  }
  if (robotStreamAbortController) {
    robotStreamAbortController.abort();
  }
});

init();
