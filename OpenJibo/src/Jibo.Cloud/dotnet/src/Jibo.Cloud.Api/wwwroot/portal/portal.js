const confirmJiboButton = document.getElementById("confirmJibo");
const jiboCodeInput = document.getElementById("jiboCode");
const jiboStatus = document.getElementById("jiboStatus");
const haCodeInput = document.getElementById("haCode");
const linkHomeAssistantButton = document.getElementById("linkHomeAssistant");
const haStatus = document.getElementById("haStatus");
const linksList = document.getElementById("linksList");

let jiboVerificationToken = null;

function showStatus(element, message, isError = false) {
  element.textContent = message;
  element.classList.remove("hidden", "success", "error");
  element.classList.add(isError ? "error" : "success");
}

async function postJson(url, body) {
  const response = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(payload.error || `Request failed (${response.status})`);
  }

  return payload;
}

async function loadLinks() {
  const response = await fetch("/api/portal/home-assistant/links");
  const payload = await response.json();
  const links = payload.links || [];

  if (links.length === 0) {
    linksList.innerHTML = "<li>No Home Assistant links yet.</li>";
    return;
  }

  linksList.innerHTML = links
    .map((link) => `<li><strong>${escapeHtml(link.jiboFriendlyId)}</strong> linked at ${new Date(link.pairedAtUtc).toLocaleString()}</li>`)
    .join("");
}

function escapeHtml(value) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

confirmJiboButton.addEventListener("click", async () => {
  jiboStatus.classList.add("hidden");

  try {
    const payload = await postJson("/api/portal/jibo-verification/confirm", {
      code: jiboCodeInput.value.trim(),
    });

    jiboVerificationToken = payload.jiboVerificationToken;
    showStatus(jiboStatus, `Jibo verified: ${payload.jiboFriendlyId}`);
  } catch (error) {
    showStatus(jiboStatus, error.message, true);
  }
});

linkHomeAssistantButton.addEventListener("click", async () => {
  haStatus.classList.add("hidden");

  if (!jiboVerificationToken) {
    showStatus(haStatus, "Verify your Jibo first.", true);
    return;
  }

  try {
    const payload = await postJson("/api/portal/home-assistant/link", {
      jiboVerificationToken,
      haCode: haCodeInput.value.trim(),
    });

    showStatus(haStatus, `Linked ${payload.jiboFriendlyId} with Home Assistant.`);
    jiboVerificationToken = null;
    haCodeInput.value = "";
    jiboCodeInput.value = "";
    await loadLinks();
  } catch (error) {
    showStatus(haStatus, error.message, true);
  }
});

loadLinks().catch(() => {
  linksList.innerHTML = "<li>Could not load linked integrations.</li>";
});
