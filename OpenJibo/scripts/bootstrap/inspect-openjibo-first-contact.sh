#!/usr/bin/env bash
set -euo pipefail

robot_root=""
output_path=""
max_matches="40"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --robot-root)
      robot_root="${2:-}"
      shift 2
      ;;
    --output-path)
      output_path="${2:-}"
      shift 2
      ;;
    --max-matches)
      max_matches="${2:-40}"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ -z "$robot_root" ]]; then
  echo "--robot-root is required" >&2
  exit 2
fi

node - "$robot_root" "$output_path" "$max_matches" <<'NODE'
const fs = require("fs");
const path = require("path");

const robotRoot = path.resolve(process.argv[2]);
const outputPath = (process.argv[3] || "").trim();
const maxMatches = Number.parseInt(process.argv[4] || "40", 10) || 40;

const skillNames = ["first-contact", "oobe", "oobe-config", "group-oobe", "name-learning", "name_learning", "voice-training", "face-training"];
const keywordPatterns = [
  /@be\/first-contact/i,
  /first[-_]contact/i,
  /name_learning/i,
  /name[-_]learning/i,
  /pronoun_/i,
  /WhoAmI/i,
  /voice[-_ ]?training/i,
  /face[-_ ]?training/i,
  /peoplePresent/i,
  /identity/i,
];
const safeExtensions = new Set([".js", ".json", ".mim", ".xml", ".txt", ".md", ".coffee", ".yaml", ".yml", ".sh"]);
const skipDirs = new Set([".git", "node_modules", "proc", "sys", "dev", "run", "tmp"]);

function exists(p) { return fs.existsSync(p); }
function rel(p) { return path.relative(robotRoot, p).split(path.sep).join("/") || "."; }
function statSafe(p) { try { return fs.statSync(p); } catch { return null; } }
function readText(p) { try { return fs.readFileSync(p, "utf8"); } catch { return ""; } }
function listDirSafe(p) { try { return fs.readdirSync(p, { withFileTypes: true }); } catch { return []; } }

function findSkillRoots() {
  const roots = [];
  const bases = ["skills", "opt/jibo/Jibo/Skills", "usr/local/lib/jibo/skills", "usr/local/share/jibo/skills"];
  for (const base of bases) {
    const absoluteBase = path.join(robotRoot, base);
    if (!exists(absoluteBase)) continue;
    const stack = [absoluteBase];
    while (stack.length) {
      const current = stack.pop();
      const entries = listDirSafe(current);
      const baseName = path.basename(current).toLowerCase();
      const hasPackage = entries.some(e => e.isFile() && ["package.json", "skill.json", "manifest.json"].includes(e.name));
      const nameHit = skillNames.some(name => baseName.includes(name));
      if (hasPackage && nameHit) roots.push(current);
      for (const entry of entries) {
        if (!entry.isDirectory() || skipDirs.has(entry.name)) continue;
        const next = path.join(current, entry.name);
        const depth = rel(next).split("/").length;
        if (depth <= 8) stack.push(next);
      }
    }
  }
  return [...new Set(roots)].sort();
}

function scanKeywords() {
  const matches = [];
  const roots = ["skills", "opt/jibo/Jibo/Skills", "usr/local/lib/jibo", "usr/local/share/jibo", "var/jibo", "etc", "usr/local/etc"]
    .map(p => path.join(robotRoot, p)).filter(exists);
  const stack = [...roots];
  while (stack.length && matches.length < maxMatches) {
    const current = stack.pop();
    const st = statSafe(current);
    if (!st) continue;
    if (st.isDirectory()) {
      for (const entry of listDirSafe(current)) {
        if (skipDirs.has(entry.name)) continue;
        stack.push(path.join(current, entry.name));
      }
      continue;
    }
    if (!st.isFile() || st.size > 1024 * 1024) continue;
    if (!safeExtensions.has(path.extname(current))) continue;
    const text = readText(current);
    const lines = text.split(/\r?\n/);
    for (let index = 0; index < lines.length && matches.length < maxMatches; index++) {
      const line = lines[index];
      const hit = keywordPatterns.find(pattern => pattern.test(line));
      if (hit) matches.push({ File: rel(current), Line: index + 1, Keyword: String(hit), Preview: line.trim().slice(0, 220) });
    }
  }
  return matches;
}

function summarizeRoot(root) {
  const files = listDirSafe(root).filter(e => e.isFile()).map(e => e.name).sort();
  const dirs = listDirSafe(root).filter(e => e.isDirectory()).map(e => e.name).sort();
  const packageJson = ["package.json", "skill.json", "manifest.json"].map(name => path.join(root, name)).find(exists);
  let declaredName = null;
  if (packageJson) {
    try { declaredName = JSON.parse(readText(packageJson)).name || null; } catch { declaredName = null; }
  }
  return { Path: rel(root), DeclaredName: declaredName, Manifest: packageJson ? rel(packageJson) : null, TopLevelFiles: files.slice(0, 20), TopLevelDirectories: dirs.slice(0, 20) };
}

const firstContact = findSkillRoots();
const keywordMatches = scanKeywords();
const report = {
  RobotRoot: robotRoot,
  FirstContactCandidates: firstContact.map(summarizeRoot),
  KeywordMatches: keywordMatches,
  ConversionUse: {
    AwakeningTemplate: firstContact.length > 0 ? "candidate skill roots found; review scenes/assets before copying behavior into Open Jibo onboarding" : "no first-contact skill root found in this image layout",
    IdentityTrainingHooks: keywordMatches.some(m => /name_learning|name[-_]learning|pronoun_|WhoAmI/i.test(m.Preview)) ? "name/pronoun/WhoAmI hooks found for targeted extraction review" : "no name/pronoun/WhoAmI hooks found by keyword scan",
    RecognitionHooks: keywordMatches.some(m => /peoplePresent|identity|face|voice/i.test(m.Preview)) ? "identity/recognition terms found; correlate with live websocket/log captures" : "no identity/recognition terms found by keyword scan",
  },
  NextActions: [
    "Review FirstContactCandidates manifests and scene files before selecting safe awakening assets.",
    "Use KeywordMatches to inspect name_learning, pronoun_, and WhoAmI code paths without bulk-copying the stock OOBE skill.",
    "Pair this filesystem report with inspect-websocket-recognition-candidates.py output from a live regression capture before claiming stable recognition IDs.",
  ],
};

const json = JSON.stringify(report, null, 2);
if (outputPath) {
  const resolvedOutput = path.resolve(outputPath);
  fs.mkdirSync(path.dirname(resolvedOutput), { recursive: true });
  fs.writeFileSync(resolvedOutput, json);
  console.log(`Saved first-contact inspection to ${resolvedOutput}`);
} else {
  console.log(json);
}
NODE
