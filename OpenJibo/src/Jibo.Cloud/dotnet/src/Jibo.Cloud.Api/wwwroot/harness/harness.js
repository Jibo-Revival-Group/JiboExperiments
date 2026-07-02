const $ = (id) => document.getElementById(id);
const responseBox = $("response");
const statusBox = $("status");
function showStatus(text, tone = "success") { statusBox.textContent = text; statusBox.className = `status ${tone}`; }
function setCall(service, operation, body) { $("servicePrefix").value = service; $("operation").value = operation; $("bodyText").value = JSON.stringify(body, null, 2); }
$("loadStatus").addEventListener("click", () => setCall("OOBE_20161026", "GetStatus", { deviceId: "fake-jibo-001", targetMode: "open-jibo-self-hosted", targetHost: "jibo.local.test" }));
$("loadRobot").addEventListener("click", () => setCall("Robot_20160225", "GetRobot", { id: "fake-jibo-001" }));
$("loadVerifyConnection").addEventListener("click", () => setCall("OOBE_20161026", "VerifyConnection", { token: "paste-prepared-token-here", reportedConnectionHost: "jibo.local.test", reportedHostMappings: { "api.jibo.com": "jibo.local.test", "api-socket.jibo.com": "jibo.local.test", "neo-hub.jibo.com": "jibo.local.test" } }));
$("sendButton").addEventListener("click", async () => {
  let body;
  try { body = JSON.parse($("bodyText").value || "{}"); } catch (error) { showStatus(`Invalid JSON: ${error.message}`, "error"); return; }
  const service = $("servicePrefix").value.trim();
  const operation = $("operation").value.trim();
  const path = "/";
  try {
    const res = await fetch(path, { method: $("method").value, headers: { "Content-Type": "application/json", "X-Amz-Target": `${service}.${operation}`, "X-OpenJibo-Harness-Host": $("hostName").value.trim(), "X-OpenJibo-AppVersion": "1.0.20" }, body: JSON.stringify(body) });
    const text = await res.text();
    responseBox.textContent = text ? JSON.stringify(JSON.parse(text), null, 2) : "(empty response)";
    showStatus(`HTTP ${res.status} from ${service}.${operation}`, res.ok ? "success" : "error");
  } catch (error) { showStatus(error.message, "error"); }
});
