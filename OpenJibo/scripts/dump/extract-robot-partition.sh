#!/usr/bin/env bash
# Reads files out of a raw Jibo eMMC dump (*.bin) WITHOUT root and WITHOUT
# mounting anything.
#
# Why this exists: several interesting partitions - most importantly the
# services partition (GPT name "services", where jibo-ssm / jibo-server-service
# live) - are mode 0750 root:10 on the robot. Loop-mounting the dump therefore
# still hides them from a normal user, which is why docs/loop-syncmanager-contract.md
# previously recorded the SyncManager sources as "not readable on this host".
#
# debugfs walks the ext4 metadata directly, so Unix ownership and permission
# bits do not apply at all, and e2fsprogs' unix_io accepts a "?offset=" suffix
# so the partition can be read in place - no dd carve, no scratch images, no
# sudo, no loop devices.
#
# Usage:
#   extract-robot-partition.sh table <dump.bin>
#   extract-robot-partition.sh ls    <dump.bin> <partition> <path>
#   extract-robot-partition.sh tree  <dump.bin> <partition> <path>
#   extract-robot-partition.sh get   <dump.bin> <partition> <path> [dest-file]
#   extract-robot-partition.sh rget  <dump.bin> <partition> <dir>  [dest-dir]
#   extract-robot-partition.sh shell <dump.bin> <partition>
#
#   <partition> is either an index (1..N) or a GPT partition name
#   (rootfsA, rootfsB, services, var, skills, ...).
#
# Extractions default to artifact-output/dumps/<dump-slug>/<partition>/, which
# is gitignored - robot dumps contain household PII and must never be committed.
#
# Examples:
#   extract-robot-partition.sh table "~/Documents/Jibos/Air-....bin"
#   extract-robot-partition.sh ls    "$DUMP" services /bin
#   extract-robot-partition.sh rget  "$DUMP" services /bin/jibo-ssm
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
OUT_ROOT="${OUT_ROOT:-${REPO_ROOT}/artifact-output/dumps}"

die() {
  echo "error: $*" >&2
  exit 1
}

require_tools() {
  for tool in debugfs sfdisk python3; do
    command -v "${tool}" >/dev/null || die "missing required tool: ${tool}"
  done
}

# Slug used for the output directory: "Air-Degree-Lunch-Canvas (Zane 2017 Jibo).bin"
# becomes "air-degree-lunch-canvas-zane-2017-jibo".
dump_slug() {
  basename "$1" .bin |
    tr '[:upper:]' '[:lower:]' |
    sed -e 's/[^a-z0-9]\+/-/g' -e 's/^-//' -e 's/-$//'
}

# Emits "<index> <name> <byte-offset> <byte-size>" per partition.
partition_rows() {
  sfdisk -J "$1" | python3 -c '
import json, sys

table = json.load(sys.stdin)["partitiontable"]
sector = table.get("sectorsize", 512)
for index, part in enumerate(table.get("partitions", []), 1):
    name = part.get("name") or f"p{index}"
    print(index, name, part["start"] * sector, part["size"] * sector)
'
}

# Resolves an index or GPT name to a byte offset.
partition_offset() {
  local dump="$1" wanted="$2"
  partition_rows "${dump}" | python3 -c '
import sys

wanted = sys.argv[1].strip().lower()
for line in sys.stdin:
    index, name, offset, _size = line.split()
    if wanted == index or wanted == name.lower():
        print(offset)
        break
else:
    sys.exit(1)
' "${wanted}" || die "no partition matching '${wanted}' (try: $0 table '${dump}')"
}

# debugfs writes its version banner to stderr on every call; keep it out of the way
# but still surface real failures.
run_debugfs() {
  local dump="$1" offset="$2" request="$3"
  local stderr_file output status
  stderr_file="$(mktemp)"
  set +e
  output="$(debugfs -R "${request}" "${dump}?offset=${offset}" 2>"${stderr_file}")"
  status=$?
  set -e
  # Drop the version banner, and the chown failures rdump emits for every file
  # because it tries to restore the robot's root:10 ownership as a normal user.
  # Neither affects the extracted contents, which is all we care about.
  grep -v -e '^debugfs [0-9]' -e 'while changing ownership of' "${stderr_file}" >&2 || true
  rm -f "${stderr_file}"
  printf '%s\n' "${output}"
  return "${status}"
}

cmd_table() {
  local dump="$1"
  [[ -f "${dump}" ]] || die "no such dump: ${dump}"
  printf '%-4s %-12s %14s %10s  %s\n' IDX NAME OFFSET SIZE FILESYSTEM
  while read -r index name offset size; do
    local fs
    fs="$(dd if="${dump}" bs=512 skip=$((offset / 512)) count=16 status=none | file -b - | cut -c1-60)"
    printf '%-4s %-12s %14s %9sM  %s\n' "p${index}" "${name}" "${offset}" "$((size / 1048576))" "${fs}"
  done < <(partition_rows "${dump}")
}

cmd_ls() {
  local dump="$1" partition="$2" path="$3" offset
  offset="$(partition_offset "${dump}" "${partition}")"
  run_debugfs "${dump}" "${offset}" "ls -l ${path}"
}

cmd_tree() {
  local dump="$1" partition="$2" path="$3" offset
  offset="$(partition_offset "${dump}" "${partition}")"
  run_debugfs "${dump}" "${offset}" "ls -l -R ${path}"
}

cmd_get() {
  local dump="$1" partition="$2" path="$3" dest="${4:-}" offset
  offset="$(partition_offset "${dump}" "${partition}")"
  if [[ -z "${dest}" ]]; then
    dest="${OUT_ROOT}/$(dump_slug "${dump}")/${partition}${path}"
  fi
  mkdir -p "$(dirname "${dest}")"
  run_debugfs "${dump}" "${offset}" "dump -p ${path} ${dest}" >/dev/null
  [[ -s "${dest}" ]] || die "extracted nothing for ${path} (wrong partition?)"
  echo "${dest}"
}

cmd_rget() {
  local dump="$1" partition="$2" dir="$3" dest="${4:-}" offset
  offset="$(partition_offset "${dump}" "${partition}")"
  if [[ -z "${dest}" ]]; then
    dest="${OUT_ROOT}/$(dump_slug "${dump}")/${partition}${dir}"
  fi
  # rdump recreates the directory itself under its target and refuses to write
  # into an existing one, so aim at the parent and clear any previous extraction.
  if [[ -e "${dest}" ]]; then
    [[ "${dest}" == "${OUT_ROOT}/"* ]] || die "destination already exists: ${dest}"
    rm -rf "${dest}"
  fi
  mkdir -p "$(dirname "${dest}")"
  run_debugfs "${dump}" "${offset}" "rdump ${dir} $(dirname "${dest}")" >/dev/null
  echo "${dest}"
}

cmd_shell() {
  local dump="$1" partition="$2" offset
  offset="$(partition_offset "${dump}" "${partition}")"
  exec debugfs "${dump}?offset=${offset}"
}

main() {
  require_tools
  local command="${1:-}"
  shift || true

  case "${command}" in
    table) [[ $# -eq 1 ]] || die "usage: $0 table <dump.bin>"; cmd_table "$@" ;;
    ls)    [[ $# -eq 3 ]] || die "usage: $0 ls <dump.bin> <partition> <path>"; cmd_ls "$@" ;;
    tree)  [[ $# -eq 3 ]] || die "usage: $0 tree <dump.bin> <partition> <path>"; cmd_tree "$@" ;;
    get)   [[ $# -ge 3 ]] || die "usage: $0 get <dump.bin> <partition> <path> [dest]"; cmd_get "$@" ;;
    rget)  [[ $# -ge 3 ]] || die "usage: $0 rget <dump.bin> <partition> <dir> [dest]"; cmd_rget "$@" ;;
    shell) [[ $# -eq 2 ]] || die "usage: $0 shell <dump.bin> <partition>"; cmd_shell "$@" ;;
    ""|-h|--help|help)
      sed -n '2,32p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
      ;;
    *) die "unknown command '${command}' (try: $0 --help)" ;;
  esac
}

main "$@"
