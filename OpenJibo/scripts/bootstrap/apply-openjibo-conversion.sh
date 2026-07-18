#!/bin/sh
set -eu

SCRIPT_VERSION="2026-07-18.3"
echo "apply-openjibo-conversion.sh $SCRIPT_VERSION" >&2

robot_root=""
plan_path=""
target_mode="open-jibo"
api_hostname="api.openjibo.com"
hub_hostname=""
output_path=""
strict=false

while [ $# -gt 0 ]; do
  case "$1" in
    --robot-root)
      robot_root="${2:-}"
      shift 2
      ;;
    --plan-path)
      plan_path="${2:-}"
      shift 2
      ;;
    --target-mode)
      target_mode="${2:-open-jibo}"
      shift 2
      ;;
    --api-hostname)
      api_hostname="${2:-api.openjibo.com}"
      shift 2
      ;;
    --hub-hostname)
      hub_hostname="${2:-}"
      shift 2
      ;;
    --output-path)
      output_path="${2:-}"
      shift 2
      ;;
    --strict)
      strict=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [ -z "$robot_root" ] || [ -z "$plan_path" ]; then
  echo "--robot-root and --plan-path are required" >&2
  exit 2
fi

cat >&2 <<'EOF'
Physical-robot preflight:
  Before applying conversion writes on a real robot, run `jibo-mount --rw` so the partition mounts accept changes.
EOF

if [ -z "$hub_hostname" ] && { [ "$target_mode" = "open-jibo" ] || [ "$target_mode" = "open-jibo-ai" ]; }; then
  hub_hostname="neohub.openjibo.com"
fi

tmp_js="$(mktemp "${TMPDIR:-/tmp}/apply-openjibo-conversion.XXXXXX")"
cleanup() {
  rm -f "$tmp_js"
}
trap cleanup EXIT

cat > "$tmp_js" <<'NODE'
const fs = require("fs");
const path = require("path");

const robotRoot = path.resolve(process.argv[2]);
const planPath = path.resolve(process.argv[3]);
const targetMode = process.argv[4];
const apiHostname = (process.argv[5] || "api.openjibo.com").trim() || "api.openjibo.com";
const hubHostname = (process.argv[6] || "").trim() || apiHostname;
const outputPath = (process.argv[7] || "").trim();
const strict = String(process.argv[8]).toLowerCase() === "true";

function ensureDir(dirPath) {
  if (!dirPath || fs.existsSync(dirPath)) return;
  ensureDir(path.dirname(dirPath));
  try {
    fs.mkdirSync(dirPath);
  } catch (error) {
    if (!fs.existsSync(dirPath)) throw error;
  }
}

if (!fs.existsSync(planPath)) {
  throw new Error(`Plan file not found at ${planPath}`);
}

const plan = JSON.parse(fs.readFileSync(planPath, "utf8"));
if (strict && !plan.CanApply) {
  const issues = ((plan.AuditSummary && plan.AuditSummary.Recommendations) || []).filter(item => String(item).trim().length > 0);
  throw new Error(`Conversion apply is not safe to run: ${issues.join("; ")}`);
}

const applyOutputDirectory = outputPath
  ? path.dirname(path.resolve(outputPath))
  : path.dirname(planPath);
const backupRoot = path.resolve(applyOutputDirectory, "backups");
ensureDir(backupRoot);

function candidatePaths(relativePaths) {
  return relativePaths.map(relativePath => path.resolve(robotRoot, relativePath));
}

function firstExisting(candidates) {
  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) return candidate;
  }
  return null;
}

function backupFile(filePath) {
  if (!filePath || !fs.existsSync(filePath)) return null;
  const relative = path.relative(robotRoot, filePath);
  const backupPath = path.resolve(backupRoot, relative);
  ensureDir(path.dirname(backupPath));
  fs.writeFileSync(backupPath, fs.readFileSync(filePath));
  return backupPath;
}

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, "utf8"));
}

function writeJson(filePath, value) {
  ensureDir(path.dirname(filePath));
  fs.writeFileSync(filePath, JSON.stringify(value, null, 2));
}

function patchTextFile(filePath, replacements) {
  if (!filePath || !fs.existsSync(filePath)) return false;
  let text = fs.readFileSync(filePath, "utf8");
  let changed = false;
  for (const [from, to] of replacements) {
    if (text.includes(from)) {
      text = text.split(from).join(to);
      changed = true;
    }
  }
  if (changed) {
    backupFile(filePath);
    fs.writeFileSync(filePath, text);
  }
  return changed;
}

function collectExisting(relativePaths) {
  const found = [];
  for (const relativePath of relativePaths) {
    const candidate = path.resolve(robotRoot, relativePath);
    if (fs.existsSync(candidate)) found.push(candidate);
  }
  return found;
}

function collectJsFilesUnder(relativePath) {
  const base = path.resolve(robotRoot, relativePath);
  const found = [];
  if (!fs.existsSync(base)) return found;
  const stack = [base];
  while (stack.length > 0) {
    const current = stack.pop();
    for (const entry of fs.readdirSync(current)) {
      const candidate = path.resolve(current, entry);
      const stat = fs.statSync(candidate);
      if (stat.isDirectory()) {
        stack.push(candidate);
      } else if (stat.isFile() && entry.endsWith(".js")) {
        found.push(candidate);
      }
    }
  }
  return found;
}

const jetstreamPath = firstExisting(candidatePaths([
  "usr/local/etc/jibo-jetstream-service.json",
  "etc/jibo-jetstream-service.json",
]));
const serverServicePath = firstExisting(candidatePaths([
  "usr/local/etc/jibo-server-service.json",
  "etc/jibo-server-service.json",
]));
const credentialsPath = firstExisting(candidatePaths([
  "var/jibo/credentials.json",
]));
const oobeConfigPath = firstExisting(candidatePaths([
  "skills/jibo/Jibo/Skills/oobe-config/config.json",
  "opt/jibo/Jibo/Skills/oobe-config/config.json",
]));
const regionConfigFiles = collectExisting([
  "usr/lib/node_modules/@jibo/jibo-server-client/lib/region_config.json",
  "usr/lib/node/@jibo/jibo-server-client/lib/region_config.json",
  "usr/lib/node_modules/@jibo/jibo-ota-updater/node_modules/@jibo/jibo-server-client/lib/region_config.json",
  "usr/lib/node/@jibo/jibo-ota-updater/node_modules/@jibo/jibo-server-client/lib/region_config.json",
  "usr/lib/node_modules/@jibo/jibo-log-client/node_modules/@jibo/jibo-server-client/lib/region_config.json",
  "usr/lib/node/@jibo/jibo-log-client/node_modules/@jibo/jibo-server-client/lib/region_config.json",
  "usr/local/bin/jibo-ssm/node_modules/@jibo/jibo-server-client/lib/region_config.json",
  "opt/jibo/Jibo/Skills/@be/be/node_modules/@jibo/jibo-server-client/lib/region_config.json",
  "opt/jibo/Jibo/Skills/oobe-config/node_modules/@jibo/jibo-server-client/lib/region_config.json",
]);
const awsSdkAllFiles = collectExisting([
  "usr/lib/node_modules/@jibo/jibo-server-client/dist/aws-sdk-all.js",
  "usr/lib/node/@jibo/jibo-server-client/dist/aws-sdk-all.js",
  "usr/lib/node_modules/@jibo/jibo-ota-updater/node_modules/@jibo/jibo-server-client/dist/aws-sdk-all.js",
  "usr/lib/node/@jibo/jibo-ota-updater/node_modules/@jibo/jibo-server-client/dist/aws-sdk-all.js",
  "usr/lib/node_modules/@jibo/jibo-log-client/node_modules/@jibo/jibo-server-client/dist/aws-sdk-all.js",
  "usr/lib/node/@jibo/jibo-log-client/node_modules/@jibo/jibo-server-client/dist/aws-sdk-all.js",
  "usr/local/bin/jibo-ssm/node_modules/@jibo/jibo-server-client/dist/aws-sdk-all.js",
]);
const jiboSsmRuntimeJsFiles = collectJsFilesUnder("usr/local/bin/jibo-ssm/lib");
const jiboSsmRuntimeMapFiles = collectExisting([
  "usr/local/bin/jibo-ssm/lib/skills-service-manager.js.map",
]);

if (!jetstreamPath || !serverServicePath || !credentialsPath || !oobeConfigPath) {
  throw new Error("Expected conversion files were not found on the robot root.");
}

const backups = {
  jetstream: backupFile(jetstreamPath),
  serverService: backupFile(serverServicePath),
  credentials: backupFile(credentialsPath),
  oobe: backupFile(oobeConfigPath),
  regionConfigFiles: regionConfigFiles.map(backupFile).filter(Boolean),
  awsSdkAllFiles: awsSdkAllFiles.map(backupFile).filter(Boolean),
  jiboSsmRuntimeJsFiles: jiboSsmRuntimeJsFiles.map(backupFile).filter(Boolean),
  jiboSsmRuntimeMapFiles: jiboSsmRuntimeMapFiles.map(backupFile).filter(Boolean),
};

const jetstream = readJson(jetstreamPath);
const serverService = serverServicePath ? readJson(serverServicePath) : null;
const currentRegion = readJson(credentialsPath).region || "api";
const oobe = readJson(oobeConfigPath);

jetstream["region-settings"] = jetstream["region-settings"] || jetstream.regions || {};
jetstream.regions = jetstream.regions || jetstream["region-settings"];

const baseRegion = jetstream["region-settings"].api || jetstream.regions.api || {
  hub_port: 443,
  entrypoint_port: 443,
};
const openJiboRegion = {};
for (const key of Object.keys(baseRegion)) {
  openJiboRegion[key] = baseRegion[key];
}
openJiboRegion.hub_port = baseRegion.hub_port || 443;
openJiboRegion.entrypoint_port = baseRegion.entrypoint_port || 443;
openJiboRegion.hub_hostname = hubHostname;
openJiboRegion.entrypoint_hostname = apiHostname;

jetstream["region-settings"]["open-jibo"] = openJiboRegion;
jetstream.regions["open-jibo"] = openJiboRegion;

if (serverService && typeof serverService === "object") {
  serverService.NotificationSubsystem = serverService.NotificationSubsystem || {};
  serverService.NotificationSubsystem.serverURLSuffix = "-socket.openjibo.com";
}

oobe.serverRegion = oobe.serverRegion || currentRegion || "api";
oobe.otaFilter = oobe.otaFilter || "eau";
oobe.openJiboConversion = {
  enabled: true,
  targetMode,
  state: "pending",
  apiHostname,
  hubHostname,
  notificationSocketSuffix: "-socket.openjibo.com",
  createdUtc: new Date().toISOString(),
  backupRoot,
};

writeJson(jetstreamPath, jetstream);
if (serverService && serverServicePath) {
  writeJson(serverServicePath, serverService);
}
writeJson(oobeConfigPath, oobe);

const endpointReplacements = [
  ["{service}.{region}.api.jibo.com", "{service}.{region}.api.openjibo.com"],
  ["https://api.jibo.com", "https://api.openjibo.com"],
  ["http://api.jibo.com:8080", "http://api.openjibo.com:8080"],
  ["https://{region}.jibo.com", "https://{region}.openjibo.com"],
  ["http://{region}.jibo.com:8080", "http://{region}.openjibo.com:8080"],
  ["wss://{region}-socket.jibo.com", "wss://{region}-socket.openjibo.com"],
  ["ws://{region}-socket.jibo.com:8090", "ws://{region}-socket.openjibo.com:8090"],
];

const runtimeJsReplacements = [
  ['data.region + ".jibo.com"', 'data.region + ".openjibo.com"'],
  ['this._wifiService.options.region + ".jibo.com"', 'this._wifiService.options.region + ".openjibo.com"'],
  ["API: 'api.jibo.com'", "API: 'api.openjibo.com'"],
];

for (const filePath of regionConfigFiles) {
  patchTextFile(filePath, endpointReplacements);
}

for (const filePath of awsSdkAllFiles) {
  patchTextFile(filePath, endpointReplacements);
}

for (const filePath of jiboSsmRuntimeJsFiles) {
  patchTextFile(filePath, runtimeJsReplacements);
}

for (const filePath of jiboSsmRuntimeMapFiles) {
  patchTextFile(filePath, runtimeJsReplacements);
}

const conversionMarkerPath = path.resolve(robotRoot, "var/jibo/identity/openjibo-conversion.json");
writeJson(conversionMarkerPath, {
  targetMode,
  state: "pending",
  sourceRegion: currentRegion,
  apiHostname,
  hubHostname,
  notificationSocketSuffix: "-socket.openjibo.com",
  createdUtc: new Date().toISOString(),
  backups,
  robotRoot,
});

const applyManifest = {
  RobotRoot: robotRoot,
  TargetMode: targetMode,
  SourcePlan: planPath,
  CanApply: Boolean(plan.CanApply),
  ApiHostname: apiHostname,
  HubHostname: hubHostname,
  NotificationSocketSuffix: "-socket.openjibo.com",
  Backups: plan.Backups || [],
  BackupRoot: backupRoot,
  CreatedBackups: backups,
  ProposedChanges: plan.ProposedChanges || [],
  RollbackPlan: plan.RollbackPlan || [],
  Notes: [
    "This helper writes the minimal staged conversion state after taking backups.",
    "The active credentials region remains on the proven value until first-boot conversion completes.",
    "The staged open-jibo region points to the canonical Open Jibo API hostname.",
  "The staged notification subsystem suffix points the robot at open-jibo-socket.openjibo.com while the deployment binds neohub.openjibo.com separately.",
  "The helper also normalizes bundled jibo-server-client region templates in live robot bundles, including api, service-scoped api, and socket host forms.",
  "The helper now also normalizes the live jibo-ssm runtime bundle when it hardcodes region + .jibo.com or api.jibo.com.",
  ],
  WrittenFiles: [
    jetstreamPath,
    serverServicePath,
    oobeConfigPath,
    conversionMarkerPath,
  ].concat(regionConfigFiles, awsSdkAllFiles, jiboSsmRuntimeJsFiles, jiboSsmRuntimeMapFiles),
};

const json = JSON.stringify(applyManifest, null, 2);
if (outputPath) {
  const resolvedOutput = path.resolve(outputPath);
  ensureDir(path.dirname(resolvedOutput));
  fs.writeFileSync(resolvedOutput, json);
  console.log(`Saved conversion apply manifest to ${resolvedOutput}`);
} else {
  console.log(json);
}
NODE
node "$tmp_js" "$robot_root" "$plan_path" "$target_mode" "$api_hostname" "$hub_hostname" "$output_path" "$strict"
