#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source_script="$script_dir/initialize-openjibo-compose-env.sh"
temp_parent="${TMPDIR:-/tmp}"
temp_parent="$(cd "$temp_parent" && pwd -P)"
temp_root="$(mktemp -d "$temp_parent/openjibo-compose-env-test.XXXXXX")"
cleanup() {
  local actual_parent actual_name
  actual_parent="$(cd "$(dirname "$temp_root")" && pwd -P)"
  actual_name="$(basename "$temp_root")"
  [[ "$actual_parent" == "$temp_parent" && "$actual_name" =~ ^openjibo-compose-env-test\.[A-Za-z0-9]+$ ]] || {
    echo 'Refusing cleanup outside validated test temp directory.' >&2
    return 1
  }
  rm -rf -- "$temp_root"
}
trap cleanup EXIT

fail() { echo "FAIL: $*" >&2; exit 1; }
make_fixture() {
  local name="$1"
  local root="$temp_root/$name"
  mkdir -p "$root/scripts/cloud"
  cp "$source_script" "$root/scripts/cloud/initialize-openjibo-compose-env.sh"
  printf 'APP_MODE=fixture\nOPENJIBO_USER_ENCRYPT=template-marker-encrypt\nOPENJIBO_USER_SALT=template-marker-salt\n' > "$root/.env.example"
  printf '%s' "$root"
}
value() { sed -n "s/^$2=//p" "$1/.env"; }

first="$(make_fixture first)"
second="$(make_fixture second)"
first_output="$(env -u OPENJIBO_POSTGRES_PASSWORD bash "$first/scripts/cloud/initialize-openjibo-compose-env.sh" 2>&1)"
second_output="$(env -u OPENJIBO_POSTGRES_PASSWORD bash "$second/scripts/cloud/initialize-openjibo-compose-env.sh" 2>&1)"
first_key="$(value "$first" OPENJIBO_USER_ENCRYPT)"
first_salt="$(value "$first" OPENJIBO_USER_SALT)"
second_key="$(value "$second" OPENJIBO_USER_ENCRYPT)"
second_salt="$(value "$second" OPENJIBO_USER_SALT)"
[[ "$first_key" =~ ^[0-9a-f]{64}$ && "$first_salt" =~ ^[0-9a-f]{32}$ ]] || fail 'generated secrets have wrong format'
[[ "$first_key" != "$second_key" && "$first_salt" != "$second_salt" ]] || fail 'fresh initializations reused a secret'
[[ "$first_output$second_output" != *"$first_key"* && "$first_output$second_output" != *"$first_salt"* ]] || fail 'initializer printed generated secrets'
[[ "$first_output$second_output" != *"$second_key"* && "$first_output$second_output" != *"$second_salt"* ]] || fail 'initializer printed second install secrets'
! grep -q 'template-marker' "$first/.env" || fail 'template marker copied instead of replaced'

existing="$(make_fixture existing)"
printf 'OPENJIBO_USER_ENCRYPT=existing-ciphertext\nOPENJIBO_USER_SALT=existing-salt\nOPENJIBO_POSTGRES_PASSWORD=old-db\n' > "$existing/.env"
OPENJIBO_POSTGRES_PASSWORD=new-db bash "$existing/scripts/cloud/initialize-openjibo-compose-env.sh" >/dev/null
grep -qx 'OPENJIBO_USER_ENCRYPT=existing-ciphertext' "$existing/.env" || fail 'existing encryption value changed'
grep -qx 'OPENJIBO_USER_SALT=existing-salt' "$existing/.env" || fail 'existing salt changed'
grep -qx 'OPENJIBO_POSTGRES_PASSWORD=new-db' "$existing/.env" || fail 'PostgreSQL override behavior changed'

for kind in missing duplicate noncanonical; do
  fixture="$(make_fixture "bad-$kind")"
  if [[ "$kind" == missing ]]; then
    printf 'APP_MODE=fixture\nOPENJIBO_USER_ENCRYPT=marker\n' > "$fixture/.env.example"
  elif [[ "$kind" == duplicate ]]; then
    printf 'OPENJIBO_USER_ENCRYPT=a\nOPENJIBO_USER_ENCRYPT=b\nOPENJIBO_USER_SALT=s\n' > "$fixture/.env.example"
  else
    printf 'OPENJIBO_USER_ENCRYPT=a\nOPENJIBO_USER_SALT=s\n export OPENJIBO_USER_ENCRYPT = override\n' > "$fixture/.env.example"
  fi
  if bash "$fixture/scripts/cloud/initialize-openjibo-compose-env.sh" >/dev/null 2>&1; then
    fail "$kind template unexpectedly succeeded"
  fi
  [[ ! -e "$fixture/.env" ]] || fail "$kind template left a final .env"
done

for rng_kind in failure malformed; do
  fixture="$(make_fixture "rng-$rng_kind")"
  fake_bin="$temp_root/fake-openssl-$rng_kind"
  mkdir -p "$fake_bin"
  if [[ "$rng_kind" == failure ]]; then
    printf '#!/usr/bin/env bash\nexit 17\n' > "$fake_bin/openssl"
  else
    printf '#!/usr/bin/env bash\nprintf bad-output\n' > "$fake_bin/openssl"
  fi
  chmod +x "$fake_bin/openssl"
  if PATH="$fake_bin:$PATH" bash "$fixture/scripts/cloud/initialize-openjibo-compose-env.sh" >/dev/null 2>&1; then
    fail "$rng_kind RNG unexpectedly succeeded"
  fi
  [[ ! -e "$fixture/.env" ]] || fail "$rng_kind RNG failure left a final .env"
done

existing_no_rng="$(make_fixture existing-no-rng)"
printf 'OPENJIBO_USER_ENCRYPT=keep-key\nOPENJIBO_USER_SALT=keep-salt\n' > "$existing_no_rng/.env"
fake_bin="$temp_root/fake-openssl-existing"
mkdir -p "$fake_bin"
printf '#!/usr/bin/env bash\nexit 17\n' > "$fake_bin/openssl"
chmod +x "$fake_bin/openssl"
PATH="$fake_bin:$PATH" bash "$existing_no_rng/scripts/cloud/initialize-openjibo-compose-env.sh" >/dev/null
grep -qx 'OPENJIBO_USER_ENCRYPT=keep-key' "$existing_no_rng/.env" || fail 'existing env was not preserved without RNG'

race="$(make_fixture race)"
export TEST_REAL_LN="$(command -v ln)"
fake_bin="$temp_root/fake-ln"
mkdir -p "$fake_bin"
cat > "$fake_bin/ln" <<'FAKE_LN'
#!/usr/bin/env bash
for arg in "$@"; do target="$arg"; done
printf 'concurrent-winner\n' > "$target"
exec "$TEST_REAL_LN" "$@"
FAKE_LN
chmod +x "$fake_bin/ln"
if PATH="$fake_bin:$PATH" bash "$race/scripts/cloud/initialize-openjibo-compose-env.sh" >/dev/null 2>&1; then
  fail 'destination race unexpectedly succeeded'
fi
grep -qx 'concurrent-winner' "$race/.env" || fail 'destination race overwrote existing file'
[[ -z "$(find "$race" -maxdepth 1 -name '.env.tmp.*' -print -quit)" ]] || fail 'destination race left staged temp file'

echo 'PASS: shell compose env initializer offline tests'
