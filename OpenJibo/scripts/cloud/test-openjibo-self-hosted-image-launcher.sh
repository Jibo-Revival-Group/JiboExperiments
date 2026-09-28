#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fixture_base="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
fixture="$(mktemp -d "$fixture_base/openjibo-image-tests.XXXXXX")"
cleanup() {
  if [[ -d "$fixture" && "$fixture" == "$fixture_base"/openjibo-image-tests.* ]]; then
    rm -rf -- "$fixture"
  fi
}
trap cleanup EXIT
mkdir -p "$fixture/repo/scripts/cloud" "$fixture/bin"
cp "$script_dir/invoke-openjibo-self-hosted-stack.sh" "$fixture/repo/scripts/cloud/"
printf 'OPENJIBO_RUNTIME_IMAGE=example.invalid/poison:latest\n' > "$fixture/repo/.env"
printf 'template\n' > "$fixture/repo/.env.example"
cat > "$fixture/repo/scripts/cloud/initialize-openjibo-compose-env.sh" <<'INIT'
#!/usr/bin/env bash
printf 'initialized\n' >> "$TEST_LOG"
INIT
cat > "$fixture/bin/docker" <<'DOCKER'
#!/usr/bin/env bash
printf 'docker:%s:%s\n' "$OPENJIBO_RUNTIME_IMAGE" "$*" >> "$TEST_LOG"
printf '%s\n' "$PWD" >> "$TEST_CWD_LOG"
if [[ "$*" == 'compose config --quiet' ]]; then
  if [[ "${FAIL_CONFIG:-}" == true ]]; then
    printf 'resolved-compose-secret\n' >&2
    exit 41
  fi
  exit 0
fi
if [[ "${FAIL_UP:-}" == true ]]; then
  exit 42
fi
DOCKER
chmod +x "$fixture/bin/docker"
export PATH="$fixture/bin:$PATH"
export TEST_LOG="$fixture/calls.log"
export TEST_CWD_LOG="$fixture/docker-cwd.log"
launcher="$fixture/repo/scripts/cloud/invoke-openjibo-self-hosted-stack.sh"
digest="registry.example/openjibo/cloud@sha256:$(printf 'a%.0s' {1..64})"
expected_repo="$fixture/repo"

OPENJIBO_RUNTIME_IMAGE='example.invalid/poison:latest' bash "$launcher" --image "$digest" --run-migration
grep -Fx "docker:$digest:compose config --quiet" "$TEST_LOG" >/dev/null
grep -Fx "docker:$digest:compose up -d --no-build --pull missing postgres migrate api" "$TEST_LOG" >/dev/null
[[ "$(sed -n '1p' "$TEST_CWD_LOG")" == "$expected_repo" && "$(sed -n '2p' "$TEST_CWD_LOG")" == "$expected_repo" ]]

: > "$TEST_LOG"
OPENJIBO_RUNTIME_IMAGE='example.invalid/poison:latest' bash "$launcher"
grep -Fx 'docker:openjibo-cloud:self-hosted:compose config --quiet' "$TEST_LOG" >/dev/null
grep -Fx 'docker:openjibo-cloud:self-hosted:compose up -d --build postgres api' "$TEST_LOG" >/dev/null

: > "$TEST_LOG"
OPENJIBO_RUNTIME_IMAGE='example.invalid/poison:latest' bash "$launcher" --skip-build
grep -Fx 'docker:openjibo-cloud:self-hosted:compose config --quiet' "$TEST_LOG" >/dev/null
grep -Fx 'docker:openjibo-cloud:self-hosted:compose up -d postgres api' "$TEST_LOG" >/dev/null

: > "$TEST_LOG"
: > "$TEST_CWD_LOG"
if output="$(FAIL_CONFIG=true OPENJIBO_RUNTIME_IMAGE='example.invalid/poison:latest' bash "$launcher" --image "$digest" 2>&1)"; then
  echo 'Compose config failure was accepted.' >&2
  exit 1
fi
grep -Fx 'docker:registry.example/openjibo/cloud@sha256:'"$(printf 'a%.0s' {1..64})"':compose config --quiet' "$TEST_LOG" >/dev/null
if grep -F 'compose up' "$TEST_LOG" >/dev/null || grep -F 'resolved-compose-secret' <<< "$output" >/dev/null; then
  echo 'Compose config failure reached up or exposed resolved config output.' >&2
  exit 1
fi
grep -F 'Docker Compose configuration check failed' <<< "$output" >/dev/null
[[ "$(cat "$TEST_CWD_LOG")" == "$expected_repo" ]]

: > "$TEST_LOG"
if FAIL_UP=true OPENJIBO_RUNTIME_IMAGE='example.invalid/poison:latest' bash "$launcher" --image "$digest" >/dev/null 2>&1; then
  echo 'Docker up failure was accepted.' >&2
  exit 1
fi
grep -Fx "docker:$digest:compose config --quiet" "$TEST_LOG" >/dev/null
grep -Fx "docker:$digest:compose up -d --no-build --pull missing postgres api" "$TEST_LOG" >/dev/null

bad_images=(
  ''
  'registry.example/openjibo/cloud:latest'
  'https://registry.example/openjibo/cloud@sha256:'"$(printf 'a%.0s' {1..64})"
  'user:pass@registry.example/openjibo/cloud@sha256:'"$(printf 'a%.0s' {1..64})"
  'registry.example/openjibo/cloud@sha256:deadbeef'
  'registry.example/openjibo/cloud@sha256:'"$(printf 'A%.0s' {1..64})"
  'registry.example/openjibo/cloud@sha256:'"$(printf 'a%.0s' {1..64})"' extra'
)
for bad_image in "${bad_images[@]}"; do
  : > "$TEST_LOG"
  if bash "$launcher" --image "$bad_image" > /dev/null 2>&1; then
    echo "Invalid image was accepted: $bad_image" >&2
    exit 1
  fi
  if [[ -s "$TEST_LOG" ]]; then
    echo "Invalid image caused setup or Docker side effects: $bad_image" >&2
    exit 1
  fi
done

: > "$TEST_LOG"
if bash "$launcher" --image "$digest" --skip-build > /dev/null 2>&1; then
  echo 'Combined image and skip-build flags were accepted.' >&2
  exit 1
fi
[[ ! -s "$TEST_LOG" ]]

for bad_option in --image --unknown; do
  : > "$TEST_LOG"
  if bash "$launcher" "$bad_option" > /dev/null 2>&1; then
    echo "Bad option was accepted: $bad_option" >&2
    exit 1
  fi
  [[ ! -s "$TEST_LOG" ]]
done

: > "$TEST_LOG"
if bash "$launcher" --image "$digest" --image "$digest" > /dev/null 2>&1; then
  echo 'Repeated image option was accepted.' >&2
  exit 1
fi
[[ ! -s "$TEST_LOG" ]]
echo 'Bash image launcher checks passed.'
