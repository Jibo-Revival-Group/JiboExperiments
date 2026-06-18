const ruleFilesInput = document.getElementById("rule-files");
const rulesForm = document.getElementById("rules-form");
const uploadButton = document.getElementById("upload-button");
const refreshButton = document.getElementById("refresh-button");
const statusEl = document.getElementById("status");
const rulesList = document.getElementById("rules-list");
const rulesSummary = document.getElementById("rules-summary");
const emptyState = document.getElementById("empty-state");
const dropzone = document.getElementById("dropzone");

const passwordStorageKey = "openjibo.launchRulesPassword";
const apiBase = "/api/admin/launch-rules";

function setStatus(message, kind = "info") {
	statusEl.textContent = message;
	statusEl.dataset.kind = kind;
}

function getPassword() {
	const saved = sessionStorage.getItem(passwordStorageKey);
	if (saved) return saved;

	const entered = window.prompt("Enter the launch rules admin password:");
	if (!entered) return null;

	sessionStorage.setItem(passwordStorageKey, entered);
	return entered;
}

function authHeaders() {
	const password = getPassword();
	if (!password) return null;

	return {
		Authorization: `Basic ${btoa(`admin:${password}`)}`
	};
}

function apiUrl(suffix = "") {
	return `${apiBase}${suffix}`;
}

function formatBytes(size) {
	if (size < 1024) return `${size} B`;
	return `${(size / 1024).toFixed(1)} KB`;
}

function formatDate(value) {
	const date = new Date(value);
	return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function escapeHtml(value) {
	return value
		.replaceAll("&", "&amp;")
		.replaceAll("<", "&lt;")
		.replaceAll(">", "&gt;")
		.replaceAll('"', "&quot;");
}

function renderRules(rules) {
	rulesList.replaceChildren();
	rulesSummary.textContent = rules.length === 0
		? "No global launch rules saved yet."
		: `${rules.length} global launch rule${rules.length === 1 ? "" : "s"} active for all robots.`;
	emptyState.hidden = rules.length > 0;

	for (const rule of rules) {
		const item = document.createElement("li");
		item.className = "rule-item";

		const meta = document.createElement("div");
		meta.className = "rule-meta";
		meta.innerHTML = `
			<strong>${escapeHtml(rule.fileName)}</strong>
			<span>${formatBytes(rule.sizeBytes)} · ${formatDate(rule.uploadedUtc)}</span>
		`;

		const actions = document.createElement("div");
		actions.className = "rule-actions";

		const viewButton = document.createElement("button");
		viewButton.type = "button";
		viewButton.className = "button secondary compact";
		viewButton.textContent = "View";
		viewButton.addEventListener("click", () => viewRule(rule.fileName));

		const deleteButton = document.createElement("button");
		deleteButton.type = "button";
		deleteButton.className = "button secondary compact danger";
		deleteButton.textContent = "Delete";
		deleteButton.addEventListener("click", () => deleteRule(rule.fileName));

		actions.append(viewButton, deleteButton);
		item.append(meta, actions);
		rulesList.append(item);
	}
}

async function authorizedFetch(url, options = {}) {
	const headers = authHeaders();
	if (!headers) {
		throw new Error("Admin password is required.");
	}

	const response = await fetch(url, {
		...options,
		headers: {
			...options.headers,
			...headers
		}
	});

	if (response.status === 401) {
		sessionStorage.removeItem(passwordStorageKey);
		throw new Error("Invalid admin password.");
	}

	return response;
}

async function loadRules(showMessage = false) {
	try {
		const response = await authorizedFetch(apiUrl());
		const payload = await response.json();
		if (!response.ok) throw new Error(payload.error || "Could not load launch rules.");

		renderRules(payload.rules || []);
		if (showMessage) setStatus("Loaded global launch rules.", "success");
		else setStatus("");
	} catch (error) {
		renderRules([]);
		setStatus(error.message, "error");
	}
}

async function uploadRules(event) {
	event.preventDefault();

	const files = [...ruleFilesInput.files];
	if (files.length === 0) {
		setStatus("Choose at least one .rule file to upload.", "error");
		return;
	}

	const formData = new FormData();
	for (const file of files) formData.append("files", file, file.name);

	uploadButton.disabled = true;
	setStatus("Uploading launch rules…");

	try {
		const response = await authorizedFetch(apiUrl(), {
			method: "POST",
			body: formData
		});
		const payload = await response.json();
		if (!response.ok) throw new Error(payload.error || "Upload failed.");

		ruleFilesInput.value = "";
		await loadRules(false);
		setStatus(`Uploaded ${payload.uploaded.length} launch rule file(s). They apply to all robots.`, "success");
	} catch (error) {
		setStatus(error.message, "error");
	} finally {
		uploadButton.disabled = false;
	}
}

async function deleteRule(fileName) {
	if (!window.confirm(`Delete ${fileName}?`)) return;

	try {
		const response = await authorizedFetch(apiUrl(`/${encodeURIComponent(fileName)}`), {
			method: "DELETE"
		});
		const payload = await response.json();
		if (!response.ok) throw new Error(payload.error || "Delete failed.");

		await loadRules(false);
		setStatus(`Deleted ${fileName}.`, "success");
	} catch (error) {
		setStatus(error.message, "error");
	}
}

async function viewRule(fileName) {
	try {
		const response = await authorizedFetch(apiUrl(`/${encodeURIComponent(fileName)}`));
		const payload = await response.json();
		if (!response.ok) throw new Error(payload.error || "Could not load file contents.");

		window.alert(payload.content);
	} catch (error) {
		setStatus(error.message, "error");
	}
}

function wireDropzone() {
	["dragenter", "dragover"].forEach((eventName) => {
		dropzone.addEventListener(eventName, (event) => {
			event.preventDefault();
			dropzone.classList.add("active");
		});
	});

	["dragleave", "drop"].forEach((eventName) => {
		dropzone.addEventListener(eventName, (event) => {
			event.preventDefault();
			dropzone.classList.remove("active");
		});
	});

	dropzone.addEventListener("drop", (event) => {
		if (event.dataTransfer?.files?.length) {
			ruleFilesInput.files = event.dataTransfer.files;
		}
	});
}

rulesForm.addEventListener("submit", uploadRules);
refreshButton.addEventListener("click", () => loadRules(true));
wireDropzone();
loadRules(false);
