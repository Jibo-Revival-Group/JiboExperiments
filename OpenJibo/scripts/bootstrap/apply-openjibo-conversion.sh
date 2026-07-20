#!/bin/sh
set -eu

SCRIPT_VERSION="2026-07-19.3"
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
const crypto = require("crypto");

const SERVER_LIBRARY_VARIANTS = [
  {
    Name: "v1",
    StockMd5: "ae82f1dd7407f8d74b287917cb9a8b24",
    PatchedMd5: "e55e18e92aa6365569f13214e0118745",
  },
  {
    Name: "v2-lastdance",
    StockMd5: "a863a238d6f2531446d0eb0d1d358c19",
    PatchedMd5: "688ec2940ed1fc7d1b86d2fd29bc6b30",
  },
];

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
  fs.chmodSync(backupPath, 384);
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

function patchSourceMapFile(filePath, replacements) {
  if (!filePath || !fs.existsSync(filePath)) return false;
  let raw = "";
  try {
    raw = fs.readFileSync(filePath, "utf8");
  } catch (error) {
    return false;
  }

  let map;
  try {
    map = JSON.parse(raw);
  } catch (error) {
    return patchTextFile(filePath, replacements);
  }

  if (!map || !Array.isArray(map.sourcesContent)) {
    return patchTextFile(filePath, replacements);
  }

  let changed = false;
  const nextSourcesContent = map.sourcesContent.map((content) => {
    if (typeof content !== "string") return content;
    let next = content;
    for (const [from, to] of replacements) {
      if (next.includes(from)) {
        next = next.split(from).join(to);
        changed = true;
      }
    }
    return next;
  });

  if (changed) {
    backupFile(filePath);
    map.sourcesContent = nextSourcesContent;
    fs.writeFileSync(filePath, JSON.stringify(map));
  }

  return changed;
}

function countBufferOccurrences(buffer, needle) {
  let count = 0;
  let offset = 0;
  while ((offset = buffer.indexOf(needle, offset)) !== -1) {
    count += 1;
    offset += needle.length;
  }
  return count;
}

function findServerLibraryVariant(md5) {
  for (const variant of SERVER_LIBRARY_VARIANTS) {
    if (variant.StockMd5 === md5 || variant.PatchedMd5 === md5) return variant;
  }
  return null;
}

function patchServerLibrary(filePath) {
  const stockDomain = Buffer.from("jibo.com", "ascii");
  const compatibilityDomain = Buffer.from("jibo.pro", "ascii");
  if (stockDomain.length !== compatibilityDomain.length) {
    throw new Error("Native compatibility-domain patch must remain length-preserving.");
  }

  const buffer = fs.readFileSync(filePath);
  const beforeMd5 = crypto.createHash("md5").update(buffer).digest("hex");
  const stockCount = countBufferOccurrences(buffer, stockDomain);
  const compatibilityCount = countBufferOccurrences(buffer, compatibilityDomain);
  const variant = findServerLibraryVariant(beforeMd5);

  if (variant && beforeMd5 === variant.PatchedMd5 && stockCount === 0 && compatibilityCount === 2) {
    return { Changed: false, BeforeMd5: beforeMd5, AfterMd5: beforeMd5, Replacements: 0, State: "already-patched", Variant: variant.Name };
  }
  if (!variant || beforeMd5 !== variant.StockMd5 || stockCount !== 2 || compatibilityCount !== 0) {
    throw new Error(`Unsupported libJiboServerService.so state: md5=${beforeMd5}, jibo.com=${stockCount}, jibo.pro=${compatibilityCount}`);
  }

  let replacements = 0;
  let offset = 0;
  while ((offset = buffer.indexOf(stockDomain, offset)) !== -1) {
    compatibilityDomain.copy(buffer, offset);
    replacements += 1;
    offset += stockDomain.length;
  }
  const afterMd5 = crypto.createHash("md5").update(buffer).digest("hex");
  if (replacements !== 2 || afterMd5 !== variant.PatchedMd5) {
    throw new Error(`Native server library patch verification failed: replacements=${replacements}, md5=${afterMd5}`);
  }
  fs.writeFileSync(filePath, buffer);
  return { Changed: true, BeforeMd5: beforeMd5, AfterMd5: afterMd5, Replacements: replacements, State: "patched", Variant: variant.Name };
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
const serverLibraryPath = firstExisting(candidatePaths([
  "usr/local/lib/libJiboServerService.so",
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
  "opt/jibo/Jibo/Skills/@be/be/node_modules/@jibo/jibo-server-client/dist/aws-sdk-all.js",
  "opt/jibo/Jibo/Skills/oobe-config/node_modules/@jibo/jibo-server-client/dist/aws-sdk-all.js",
]);
const jiboSsmRuntimeJsFiles = collectJsFilesUnder("usr/local/bin/jibo-ssm/lib");
const jiboStsRuntimeJsFiles = collectJsFilesUnder("usr/local/bin/jibo-sts/node_modules/jibo-service-clients/lib");
const jiboSsmRuntimeMapFiles = collectExisting([
  "usr/local/bin/jibo-ssm/lib/skills-service-manager.js.map",
]);
const jiboStsRuntimeMapFiles = collectExisting([
  "usr/local/bin/jibo-sts/node_modules/jibo-service-clients/lib/jibo-service-clients.js.map",
]);

if (!jetstreamPath || !serverServicePath || !credentialsPath || !oobeConfigPath || !serverLibraryPath) {
  throw new Error("Expected conversion files were not found on the robot root.");
}

const backups = {
  jetstream: backupFile(jetstreamPath),
  serverService: backupFile(serverServicePath),
  credentials: backupFile(credentialsPath),
  oobe: backupFile(oobeConfigPath),
  serverLibrary: backupFile(serverLibraryPath),
  regionConfigFiles: regionConfigFiles.map(backupFile).filter(Boolean),
  awsSdkAllFiles: awsSdkAllFiles.map(backupFile).filter(Boolean),
  jiboSsmRuntimeJsFiles: jiboSsmRuntimeJsFiles.map(backupFile).filter(Boolean),
  jiboSsmRuntimeMapFiles: jiboSsmRuntimeMapFiles.map(backupFile).filter(Boolean),
  jiboStsRuntimeJsFiles: jiboStsRuntimeJsFiles.map(backupFile).filter(Boolean),
  jiboStsRuntimeMapFiles: jiboStsRuntimeMapFiles.map(backupFile).filter(Boolean),
};

const jetstream = readJson(jetstreamPath);
const serverService = serverServicePath ? readJson(serverServicePath) : null;
const credentials = readJson(credentialsPath);
const currentRegion = credentials.region || "api";
const oobe = readJson(oobeConfigPath);
const nextCredentialsRegion = (targetMode === "open-jibo" || targetMode === "open-jibo-ai")
  ? "open-jibo"
  : (currentRegion || "api");

jetstream.HubClient = jetstream.HubClient || {};
const regionSettings = jetstream.HubClient["region-settings"] || jetstream["region-settings"] || jetstream.regions || {};
jetstream.HubClient["region-settings"] = regionSettings;
delete jetstream["region-settings"];
delete jetstream.regions;

const baseRegion = regionSettings.api || {
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

regionSettings["open-jibo"] = openJiboRegion;

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

credentials.region = nextCredentialsRegion;

writeJson(jetstreamPath, jetstream);
if (serverService && serverServicePath) {
  writeJson(serverServicePath, serverService);
}
writeJson(credentialsPath, credentials);
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
  ['data.region + ".jibo.com"', '"api.openjibo.com"'],
  ['data.region+".jibo.com"', '"api.openjibo.com"'],
  ['data.region + ".openjibo.com"', '"api.openjibo.com"'],
  ['data.region+".openjibo.com"', '"api.openjibo.com"'],
  ['this._wifiService.options.region + ".jibo.com"', '"api.openjibo.com"'],
  ['this._wifiService.options.region+".jibo.com"', '"api.openjibo.com"'],
  ['this._wifiService.options.region + ".openjibo.com"', '"api.openjibo.com"'],
  ['this._wifiService.options.region+".openjibo.com"', '"api.openjibo.com"'],
  ["API: 'api.jibo.com'", "API: 'api.openjibo.com'"],
  [".jibo.com", ".openjibo.com"],
];

const runtimeMapReplacements = [
  ['data.region + \\".jibo.com\\"', '\\"api.openjibo.com\\"'],
  ['data.region+\\".jibo.com\\"', '\\"api.openjibo.com\\"'],
  ['data.region + \\".openjibo.com\\"', '\\"api.openjibo.com\\"'],
  ['data.region+\\".openjibo.com\\"', '\\"api.openjibo.com\\"'],
  ['this._wifiService.options.region + \\".jibo.com\\"', '\\"api.openjibo.com\\"'],
  ['this._wifiService.options.region+\\".jibo.com\\"', '\\"api.openjibo.com\\"'],
  ['this._wifiService.options.region + \\".openjibo.com\\"', '\\"api.openjibo.com\\"'],
  ['this._wifiService.options.region+\\".openjibo.com\\"', '\\"api.openjibo.com\\"'],
  ["API: 'api.jibo.com'", "API: 'api.openjibo.com'"],
  [".jibo.com", ".openjibo.com"],
];
const runtimeSourceMapReplacements = runtimeJsReplacements.concat(runtimeMapReplacements);

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
  patchSourceMapFile(filePath, runtimeSourceMapReplacements);
}

for (const filePath of jiboStsRuntimeJsFiles) {
  patchTextFile(filePath, runtimeJsReplacements);
}

for (const filePath of jiboStsRuntimeMapFiles) {
  patchSourceMapFile(filePath, runtimeSourceMapReplacements);
}

const nativeServerLibraryPatch = patchServerLibrary(serverLibraryPath);

const conversionMarkerPath = path.resolve(robotRoot, "var/jibo/identity/openjibo-conversion.json");
writeJson(conversionMarkerPath, {
  targetMode,
  state: "pending",
  sourceRegion: currentRegion,
  apiHostname,
  hubHostname,
  notificationSocketSuffix: "-socket.openjibo.com",
  nativeCompatibilityApiHostname: "open-jibo.jibo.pro",
  nativeCompatibilitySocketHostname: "open-jibo-socket.jibo.pro",
  nativeServerLibraryPatch,
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
  NativeCompatibilityApiHostname: "open-jibo.jibo.pro",
  NativeCompatibilitySocketHostname: "open-jibo-socket.jibo.pro",
  NativeServerLibraryPatch: nativeServerLibraryPatch,
  Backups: plan.Backups || [],
  BackupRoot: backupRoot,
  CreatedBackups: backups,
  ProposedChanges: plan.ProposedChanges || [],
  RollbackPlan: plan.RollbackPlan || [],
  Notes: [
    "This helper writes the minimal staged conversion state after taking backups.",
    "The active credentials region is rewritten to open-jibo so the robot boots against the converted routing state.",
    "The staged open-jibo region points to the canonical Open Jibo API hostname.",
  "The staged notification subsystem suffix points the robot at open-jibo-socket.openjibo.com while the deployment binds neohub.openjibo.com separately.",
  "The helper also normalizes bundled jibo-server-client region templates in live robot bundles, including api, service-scoped api, and socket host forms.",
  "The helper now also normalizes the live jibo-ssm runtime bundle when it hardcodes region + .jibo.com or api.jibo.com.",
  "The nearby source map is parsed, backed up, and rewritten through sourcesContent so the embedded source text stays aligned with the JS bundle.",
  "The native server library is hash-gated and receives exactly two equal-length jibo.com to jibo.pro byte replacements for token signing and transport.",
  ],
  WrittenFiles: [
    jetstreamPath,
    serverServicePath,
    oobeConfigPath,
    serverLibraryPath,
    conversionMarkerPath,
  ].concat(regionConfigFiles, awsSdkAllFiles, jiboSsmRuntimeJsFiles, jiboSsmRuntimeMapFiles, jiboStsRuntimeJsFiles, jiboStsRuntimeMapFiles),
  WrittenFileRoles: {
    Jetstream: jetstreamPath,
    ServerService: serverServicePath,
    OobeConfig: oobeConfigPath,
    ServerLibrary: serverLibraryPath,
    ConversionMarker: conversionMarkerPath,
  },
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
