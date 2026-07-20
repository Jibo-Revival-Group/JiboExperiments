#!/bin/sh
set -eu

SCRIPT_VERSION="2026-07-19.3"
echo "plan-openjibo-conversion.sh $SCRIPT_VERSION" >&2

robot_root=""
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

if [ -z "$robot_root" ]; then
  echo "--robot-root is required" >&2
  exit 2
fi

if [ -z "$hub_hostname" ] && { [ "$target_mode" = "open-jibo" ] || [ "$target_mode" = "open-jibo-ai" ]; }; then
  hub_hostname="neohub.openjibo.com"
fi

script_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
audit_script="$script_dir/audit-openjibo-conversion.sh"
temp_audit_path="$(mktemp "${TMPDIR:-/tmp}/openjibo-conversion-audit.XXXXXX")"
tmp_js="$(mktemp "${TMPDIR:-/tmp}/plan-openjibo-conversion.XXXXXX")"

cleanup() {
  rm -f "$temp_audit_path" "$tmp_js"
}
trap cleanup EXIT

if [ "$strict" = true ]; then
  sh "$audit_script" --robot-root "$robot_root" --output-path "$temp_audit_path" --strict >/dev/null
else
  sh "$audit_script" --robot-root "$robot_root" --output-path "$temp_audit_path" >/dev/null
fi

cat > "$tmp_js" <<'NODE'
const fs = require("fs");
const path = require("path");

const auditPath = path.resolve(process.argv[2]);
const targetMode = process.argv[3];
const apiHostname = (process.argv[4] || "api.openjibo.com").trim() || "api.openjibo.com";
const hubHostname = (process.argv[5] || "").trim() || apiHostname;
const outputPath = (process.argv[6] || "").trim();
const strict = String(process.argv[7]).toLowerCase() === "true";

function ensureDir(dirPath) {
  if (!dirPath || fs.existsSync(dirPath)) return;
  ensureDir(path.dirname(dirPath));
  try {
    fs.mkdirSync(dirPath);
  } catch (error) {
    if (!fs.existsSync(dirPath)) throw error;
  }
}

const audit = JSON.parse(fs.readFileSync(auditPath, "utf8"));
const recommendations = (audit.Recommendations || []).filter(item => String(item).trim().length > 0);
const requiresAttention = recommendations.length > 0;
const canApply = !requiresAttention;
const existingMode = audit.Credentials && audit.Credentials.Region ? String(audit.Credentials.Region) : "unknown";

const proposedChanges = [
  {
    File: "/usr/local/etc/jibo-jetstream-service.json",
    Action: "add or update region-settings entries",
    Details: [
      "preserve stock region where possible",
      "write region-settings only under HubClient and remove conversion-created top-level duplicates",
      `add target mode region entry for ${targetMode}`,
      `set entrypoint_hostname to ${apiHostname}`,
      `set hub_hostname to ${hubHostname}`,
    ],
  },
  {
    File: "/usr/local/lib/libJiboServerService.so",
    Action: "apply the equal-length native compatibility-domain patch",
    Details: [
      "require the supported stock or already-patched MD5 before proceeding",
      "replace exactly two ASCII jibo.com byte sequences with jibo.pro",
      "preserve ELF layout and ARM instructions because both domains are eight bytes",
      "route open-jibo.jibo.pro and open-jibo-socket.jibo.pro to the managed Open Jibo service",
    ],
  },
  {
    File: "/usr/local/etc/jibo-server-service.json",
    Action: "retarget the notification socket suffix",
    Details: [
      "preserve the stock service layout where possible",
      "set NotificationSubsystem.serverURLSuffix to -socket.openjibo.com",
      "stage the socket host that matches the Open Jibo domain plan",
    ],
  },
  {
    File: "all discovered region_config.json copies",
    Action: "replace the stock region endpoint templates in every discovered copy",
    Details: [
      "change {service}.{region}.api.jibo.com to {service}.{region}.api.openjibo.com",
      "change https://api.jibo.com to https://api.openjibo.com",
      "change http://api.jibo.com:8080 to http://api.openjibo.com:8080",
      "change https://{region}.jibo.com to https://{region}.openjibo.com",
      "change http://{region}.jibo.com:8080 to http://{region}.openjibo.com:8080",
      "change the socket template to the Open Jibo socket suffix",
    ],
  },
  {
    File: "all discovered aws-sdk-all.js copies, including /opt/jibo/Jibo/Skills/@be/be and /opt/jibo/Jibo/Skills/oobe-config",
    Action: "replace the bundled region endpoint templates in every discovered copy",
    Details: [
      "mirror the region_config.json host substitutions inside the bundled SDK",
      "cover the live rootfs copies used by jibo-server-service, jibo-ota-updater, jibo-log-client, and jibo-ssm",
      "keep the bundle and JSON template aligned",
    ],
  },
  {
    File: "usr/local/bin/jibo-ssm/lib/skills-service-manager.js and other scanned jibo-ssm runtime JS files",
    Action: "replace the hardcoded server hostname builder inside the live jibo-ssm bundle",
    Details: [
      'replace stock and previously converted data.region hostname concatenations with fixed "api.openjibo.com"',
      'replace stock and previously converted this._wifiService.options.region hostname concatenations with fixed "api.openjibo.com"',
      "change API: 'api.jibo.com' to API: 'api.openjibo.com'",
      "keep credentials.region as open-jibo for Jetstream selection while the SSM HTTPS connectivity probe uses the canonical API host",
    ],
  },
  {
    File: "usr/local/bin/jibo-ssm/lib/skills-service-manager.js.map",
    Action: "replace the same runtime hostname strings inside the source map",
    Details: [
      "the source map embeds the same source text as the JS bundle in compact JSON form",
      "keep the map aligned with the patched runtime bundle so debugging points at the same hostnames",
    ],
  },
  {
    File: "usr/local/bin/jibo-sts/node_modules/jibo-service-clients/lib/jibo-service-clients.js and other scanned jibo-sts service-client JS files",
    Action: "replace remaining runtime hostname strings inside the shared jibo-sts service-client bundle",
    Details: [
      'replace any remaining ".jibo.com" runtime host fragments with ".openjibo.com"',
      "keep the shared service-client bundle aligned with the same OpenJibo hostname translation used by jibo-ssm",
    ],
  },
  {
    File: "usr/local/bin/jibo-sts/node_modules/jibo-service-clients/lib/jibo-service-clients.js.map",
    Action: "replace the same runtime hostname strings inside the service-client source map",
    Details: [
      "the source map may still preserve the old hostname fragments even after the JS bundle is patched",
      "keep the map aligned so stack traces and debugger views show the same OpenJibo endpoints",
    ],
  },
  {
    File: "/var/jibo/credentials.json",
    Action: "record the active region",
    Details: [
      "save the current stock region before any switch",
      "switch the region field only after backups and validation",
    ],
  },
  {
    File: "/skills/jibo/Jibo/Skills/oobe-config/config.json",
    Action: "mark first-boot/OOBE state",
    Details: [
      "keep the setup payload compatible with the classic QR decoder",
      "record first-boot pending state without destroying existing owner data",
      `record canonical API hostname ${apiHostname}`,
    ],
  },
];

const rollbackPlan = [
  "restore the recorded jetstream config snapshot",
  "restore the recorded jibo-server-service config snapshot",
  "restore /usr/local/lib/libJiboServerService.so from the hash-verified pre-conversion backup",
  "restore /var/jibo/credentials.json from the pre-conversion backup",
  "clear first-boot pending state if onboarding is abandoned",
  "leave the Open Jibo skill visible so the owner can retry conversion later",
];

const plan = {
  RobotRoot: audit.RobotRoot,
  TargetMode: targetMode,
  ApiHostname: apiHostname,
  HubHostname: hubHostname,
  NativeCompatibilityApiHostname: "open-jibo.jibo.pro",
  NativeCompatibilitySocketHostname: "open-jibo-socket.jibo.pro",
  ExistingMode: existingMode,
  RequiresAttention: requiresAttention,
  CanApply: canApply,
  AuditSummary: {
    JetstreamPath: audit.Files && audit.Files.Jetstream ? audit.Files.Jetstream : null,
    ServerServicePath: audit.Files && audit.Files.ServerService ? audit.Files.ServerService : null,
    CredentialsPath: audit.Files && audit.Files.Credentials ? audit.Files.Credentials : null,
    OobeConfigPath: audit.Files && audit.Files.OobeConfig ? audit.Files.OobeConfig : null,
    ServerLibraryPath: audit.Files && audit.Files.ServerLibrary ? audit.Files.ServerLibrary : null,
    SsmCount: audit.Files ? audit.Files.SsmCount : 0,
    Region: audit.Credentials ? audit.Credentials.Region : null,
    ServerServiceSuffix: audit.ServerService ? audit.ServerService.NotificationSubsystemSuffix : null,
    OobeServerRegion: audit.Oobe ? audit.Oobe.ServerRegion : null,
    OobeOtaFilter: audit.Oobe ? audit.Oobe.OtaFilter : null,
    NativeServerLibrary: audit.NativeServerLibrary || null,
    Recommendations: recommendations,
  },
  Backups: [
    audit.Files && audit.Files.Jetstream,
    audit.Files && audit.Files.ServerService,
    audit.Files && audit.Files.Credentials,
    audit.Files && audit.Files.OobeConfig,
    audit.Files && audit.Files.ServerLibrary,
    ...(audit.NodeBundles && audit.NodeBundles.RegionConfigFiles ? audit.NodeBundles.RegionConfigFiles : []),
    ...(audit.NodeBundles && audit.NodeBundles.AwsSdkAllFiles ? audit.NodeBundles.AwsSdkAllFiles : []),
    ...(audit.NodeBundles && audit.NodeBundles.JiboSsmRuntimeJsFiles ? audit.NodeBundles.JiboSsmRuntimeJsFiles : []),
  ].filter(Boolean),
  ProposedChanges: proposedChanges,
  RollbackPlan: rollbackPlan,
  Preconditions: [
    "verify the audit report is clean enough for the target device",
    "take a backup before any write helper runs",
    "confirm the conversion mode target with the owner",
    "confirm api.openjibo.com, open-jibo-socket.openjibo.com, and neohub.openjibo.com DNS/custom-domain routing is ready before physical robot conversion",
  ],
};

if (strict && !canApply) {
  throw new Error(`Conversion plan is not ready to apply: ${recommendations.join("; ")}`);
}

const json = JSON.stringify(plan, null, 2);
if (outputPath) {
  const resolvedOutput = path.resolve(outputPath);
  ensureDir(path.dirname(resolvedOutput));
  fs.writeFileSync(resolvedOutput, json);
  console.log(`Saved conversion plan to ${resolvedOutput}`);
} else {
  console.log(json);
}
NODE
node "$tmp_js" "$temp_audit_path" "$target_mode" "$api_hostname" "$hub_hostname" "$output_path" "$strict"
