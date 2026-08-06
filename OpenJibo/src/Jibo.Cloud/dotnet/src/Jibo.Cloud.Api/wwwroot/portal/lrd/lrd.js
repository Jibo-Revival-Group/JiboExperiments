const app = document.getElementById("app");
const robotInfo = document.getElementById("robotInfo");
const debugContent = document.getElementById("debugContent");

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function getUrlParams() {
  const params = new URLSearchParams(window.location.search);
  return {
    deviceId: params.get("deviceId"),
    robotName: params.get("robotName")
  };
}

function init() {
  const params = getUrlParams();
  
  if (!params.deviceId) {
    robotInfo.innerHTML = `<span class="error">Error: No robot ID provided</span>`;
    debugContent.innerHTML = `<p class="muted-row">Please access this page from the status panel with a robot selected.</p>`;
    return;
  }

  const displayName = params.robotName || params.deviceId;
  robotInfo.innerHTML = `Debugging robot: <strong>${escapeHtml(displayName)}</strong> (${escapeHtml(params.deviceId)})`;
  
  debugContent.innerHTML = `
    <div class="debug-section">
      <h2>Robot Information</h2>
      <div class="debug-info">
        <div><span class="label">Device ID:</span> <span class="value">${escapeHtml(params.deviceId)}</span></div>
        <div><span class="label">Robot Name:</span> <span class="value">${escapeHtml(params.robotName || "Unknown")}</span></div>
      </div>
    </div>
    <div class="debug-section">
      <h2>Live Debugging</h2>
      <p class="muted-row">Debugging features will be added here.</p>
    </div>
  `;
}

init();
