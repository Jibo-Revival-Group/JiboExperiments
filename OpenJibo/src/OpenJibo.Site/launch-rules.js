const robotNameInput = document.getElementById("robot-name");
const ruleFilesInput = document.getElementById("rule-files");
const robotForm = document.getElementById("robot-form");
const uploadButton = document.getElementById("upload-button");
const refreshButton = document.getElementById("refresh-button");
const statusEl = document.getElementById("status");
const rulesList = document.getElementById("rules-list");
const rulesSummary = document.getElementById("rules-summary");
const emptyState = document.getElementById("empty-state");
const dropzone = document.getElementById("dropzone");

const storageKey = "openjibo.robotFriendlyName";

function getRobotName() {
	return robotNameInput.value.trim();
}

function setStatus(message, kind = "info") {
	statusEl.textContent = message;
	statusEl.dataset.kind = kind;
}

function encodeRobotName(name) {
	return encodeURIComponent(name);
}

function apiUrl(name, suffix = "") {
	return `/api/public/robots/${encodeRobotName(name)}/launch-rules${suffix}`;
}

function formatBytes(size) {
	if (size < 1024) return `${size} B`;
	return `${(size / 1024).toFixed(1)} KB`;
}

function formatDate(value) {
	const date = new Date(value);
	return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function renderRules(robotName, rules) {
	rulesList.replaceChildren();
	rulesSummary.textContent = rules.length === 0
		? `No launch rules saved for ${robotName}.`
		: `${rules.length} launch rule${rules.length === 1 ? "" : "s"} saved for ${robotName}.`;
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
		viewButton.addEventListener("click", () => viewRule(robotName, rule.fileName));

		const deleteButton = document.createElement("button");
		deleteButton.type = "button";
		deleteButton.className = "button secondary compact danger";
		deleteButton.textContent = "Delete";
		deleteButton.addEventListener("click", () => deleteRule(robotName, rule.fileName));

		actions.append(viewButton, deleteButton);
		item.append(meta, actions);
		rulesList.append(item);
	}
}

function escapeHtml(value) {
	return value
		.replaceAll("&", "&amp;")
		.replaceAll("<", "&lt;")
		.replaceAll(">", "&gt;")
		.replaceAll('"', "&quot;");
}

async function loadRules(showMessage = false) {
	const robotName = getRobotName();
	if (!robotName) {
		renderRules("", []);
		setStatus("Enter your robot's friendly name first.", "error");
		return;
	}

	localStorage.setItem(storageKey, robotName);

	try {
		const response = await fetch(apiUrl(robotName));
		const payload = await response.json();
		if (!response.ok) throw new Error(payload.error || "Could not load launch rules.");

		renderRules(payload.robotFriendlyName, payload.rules || []);
		if (showMessage) setStatus(`Loaded launch rules for ${payload.robotFriendlyName}.`, "success");
		else setStatus("");
	} catch (error) {
		renderRules(robotName, []);
		setStatus(error.message, "error");
	}
}

async function uploadRules(event) {
	event.preventDefault();

	const robotName = getRobotName();
	const files = [...ruleFilesInput.files];
	if (!robotName) {
		setStatus("Enter your robot's friendly name first.", "error");
		return;
	}

	if (files.length === 0) {
		setStatus("Choose at least one .rule file to upload.", "error");
		return;
	}

	const formData = new FormData();
	for (const file of files) formData.append("files", file, file.name);

	uploadButton.disabled = true;
	setStatus("Uploading launch rules…");

	try {
		const response = await fetch(apiUrl(robotName), {
			method: "POST",
			body: formData
		});
		const payload = await response.json();
		if (!response.ok) throw new Error(payload.error || "Upload failed.");

		ruleFilesInput.value = "";
		await loadRules(false);
		setStatus(`Uploaded ${payload.uploaded.length} launch rule file(s) for ${payload.robotFriendlyName}.`, "success");
	} catch (error) {
		setStatus(error.message, "error");
	} finally {
		uploadButton.disabled = false;
	}
}

async function deleteRule(robotName, fileName) {
	if (!window.confirm(`Delete ${fileName} for ${robotName}?`)) return;

	try {
		const response = await fetch(apiUrl(robotName, `/${encodeURIComponent(fileName)}`), {
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

async function viewRule(robotName, fileName) {
	try {
		const response = await fetch(apiUrl(robotName, `/${encodeURIComponent(fileName)}`));
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

robotForm.addEventListener("submit", uploadRules);
refreshButton.addEventListener("click", () => loadRules(true));
robotNameInput.addEventListener("change", () => loadRules(false));

const savedName = localStorage.getItem(storageKey);
if (savedName) {
	robotNameInput.value = savedName;
	loadRules(false);
}

wireDropzone();
