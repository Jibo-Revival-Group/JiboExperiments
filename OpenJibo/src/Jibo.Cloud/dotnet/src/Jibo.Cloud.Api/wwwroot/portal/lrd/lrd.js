const SESSION_KEY = "openjibo_status_session";
const PING_INTERVAL_MS = 5000;
const LOG_POLL_INTERVAL_MS = 1000;

let selectedRobotId = null;
let robots = [];
let pingInterval = null;
let logPollInterval = null;
let logOffset = 0;
let currentLogFile = null;

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

function ansiToHtml(text) {
  const ansiColors = {
    '30': '#000000', // black
    '31': '#cd0000', // red
    '32': '#00cd00', // green
    '33': '#cdcd00', // yellow
    '34': '#0000ee', // blue
    '35': '#cd00cd', // magenta
    '36': '#00cdcd', // cyan
    '37': '#e5e5e5', // white
    '90': '#7f7f7f', // bright black
    '91': '#ff0000', // bright red
    '92': '#00ff00', // bright green
    '93': '#ffff00', // bright yellow
    '94': '#5c5cff', // bright blue
    '95': '#ff00ff', // bright magenta
    '96': '#00ffff', // bright cyan
    '97': '#ffffff', // bright white
  };

  const ansiStyles = {
    '0': 'font-weight:normal;text-decoration:none;color:inherit',
    '1': 'font-weight:bold',
    '2': 'opacity:0.7',
    '3': 'font-style:italic',
    '4': 'text-decoration:underline',
    '7': 'background-color:#000;color:#fff',
  };

  let result = text;
  let ansiRegex = /\x1b\[([0-9;]*)m/g;

  result = result.replace(ansiRegex, (match, codes) => {
    const codeList = codes.split(';');
    let styles = [];

    codeList.forEach(code => {
      if (ansiColors[code]) {
        styles.push(`color:${ansiColors[code]}`);
      } else if (ansiStyles[code]) {
        styles.push(ansiStyles[code]);
      }
    });

    return styles.length > 0 ? `</span><span style="${styles.join(';')}">` : '</span><span>';
  });

  return `<span>${result}</span>`;
}

async function fetchServerLogs() {
  try {
    const response = await apiFetch(`/api/portal/server/logs?offset=${logOffset}&lines=100`);
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

      logOffset = response.offset || logOffset;
    }
  } catch (error) {
    console.error("Failed to fetch server logs:", error);
  }
}

function appendServerLogs(logText) {
  const rightContent = document.getElementById("rightLogContent");
  if (!rightContent) return;

  const lines = logText.split('\n');
  lines.forEach(line => {
    if (line.trim()) {
      const logLine = document.createElement('div');
      logLine.className = 'log-line';
      logLine.innerHTML = ansiToHtml(line);
      rightContent.appendChild(logLine);
    }
  });

  // Auto-scroll to bottom
  rightContent.scrollTop = rightContent.scrollHeight;
}

function setupSplitResizer() {
  const resizer = document.getElementById("splitResizer");
  const leftPanel = document.querySelector(".left-panel");
  const rightPanel = document.querySelector(".right-panel");
  const container = document.querySelector(".split-container");

  if (!resizer || !leftPanel || !rightPanel || !container) return;

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

  // Initial server ping check
  await measureServerPing();

  // Fetch robots and populate select
  await fetchRobots();

  // Initial server logs fetch
  await fetchServerLogs();

  // Start periodic ping checks
  pingInterval = setInterval(measureServerPing, PING_INTERVAL_MS);

  // Start periodic log polling
  logPollInterval = setInterval(fetchServerLogs, LOG_POLL_INTERVAL_MS);
}

// Clean up on page unload
window.addEventListener("beforeunload", () => {
  if (pingInterval) {
    clearInterval(pingInterval);
  }
  if (logPollInterval) {
    clearInterval(logPollInterval);
  }
});

init();
