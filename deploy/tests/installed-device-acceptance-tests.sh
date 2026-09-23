#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp=$(mktemp -d)
trap 'rm -rf "$temp"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Installed-device acceptance test failed: $1" >&2
    exit 1
}

script="$root_dir/deploy/record-installed-device-acceptance.sh"
[ -f "$script" ] || fail "recording script is missing."
sh -n "$script" || fail "recording script has invalid POSIX shell syntax."

release=1234567890abcdef1234567890abcdef12345678
deployment="$temp/deployment"
evidence_dir="$temp/evidence"
bin="$temp/bin"
mkdir -p "$deployment" "$evidence_dir" "$bin"
printf '%s\n' "$release" > "$deployment/.billwatch-release"

cat > "$bin/git" <<'EOF'
#!/bin/sh
set -eu
case "$*" in
    *'rev-parse HEAD'*) printf '%s\n' "${TEST_RELEASE:?}" ;;
    *'status --porcelain --untracked-files=no'*) printf '%s' "${TEST_STATUS:-}" ;;
    *) exit 1 ;;
esac
EOF
chmod 700 "$bin/git"

run_record()
{
    platform=$1
    output=$2
    shift 2

    env \
        PATH="$bin:$PATH" \
        TEST_RELEASE="$release" \
        BILLWATCH_INSTALLED_DEVICE_PLATFORM="$platform" \
        BILLWATCH_INSTALLED_DEVICE_EVIDENCE_FILE="$output" \
        "$@" \
        sh "$script" "$deployment"
}

android="$evidence_dir/android.state"
android_phrase='I completed the FullWorth installed-device acceptance checks on Android'

if run_record android "$android" >/dev/null 2>&1; then
    fail "recording succeeded without explicit opt-in."
fi

if run_record android "$android" \
    BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_ALLOW_RECORD=true >/dev/null 2>&1; then
    fail "recording succeeded without the exact human confirmation phrase."
fi

if run_record windows "$android" \
    BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_ALLOW_RECORD=true \
    BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_CONFIRMATION="$android_phrase" >/dev/null 2>&1; then
    fail "unsupported platform was accepted."
fi

run_record android "$android" \
    BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_ALLOW_RECORD=true \
    BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_CONFIRMATION="$android_phrase" >/dev/null ||
    fail "valid Android acceptance evidence was rejected."

[ -f "$android" ] || fail "Android evidence was not created."
[ "$(stat -c '%a' "$android")" = 600 ] || fail "Android evidence is not mode 600."
grep -Fxq "RELEASE_SHA=$release" "$android" || fail "Android evidence omitted release SHA."
grep -Fxq 'PLATFORM=android' "$android" || fail "Android evidence omitted platform."
grep -Fxq 'PASSED_PHASES=installed-pwa-launch,first-run-setup,display-accessibility-preferences,keyboard-resize,back-navigation,pwa-update,statement-file-picker,security-dialogs' "$android" ||
    fail "Android evidence omitted required phases."
if grep -Eiq 'confirmation|password|token|secret|account|screenshot|device[_-]?id' "$android"; then
    fail "Android evidence contains disallowed sensitive/attestation metadata."
fi

if run_record android "$android" \
    BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_ALLOW_RECORD=true \
    BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_CONFIRMATION="$android_phrase" >/dev/null 2>&1; then
    fail "existing evidence was overwritten."
fi

ios="$evidence_dir/ios.state"
ios_phrase='I completed the FullWorth installed-device acceptance checks on iOS'
run_record ios "$ios" \
    BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_ALLOW_RECORD=true \
    BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_CONFIRMATION="$ios_phrase" >/dev/null ||
    fail "valid iOS acceptance evidence was rejected."
grep -Fxq 'PLATFORM=ios' "$ios" || fail "iOS evidence omitted platform."

dirty="$evidence_dir/dirty.state"
TEST_STATUS=' M tracked-file' \
    PATH="$bin:$PATH" TEST_RELEASE="$release" \
    BILLWATCH_INSTALLED_DEVICE_PLATFORM=android \
    BILLWATCH_INSTALLED_DEVICE_EVIDENCE_FILE="$dirty" \
    BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_ALLOW_RECORD=true \
    BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_CONFIRMATION="$android_phrase" \
    sh "$script" "$deployment" >/dev/null 2>&1 &&
    fail "dirty deployment checkout was accepted."

symlink="$evidence_dir/symlink.state"
ln -s "$evidence_dir/target.state" "$symlink"
if run_record android "$symlink" \
    BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_ALLOW_RECORD=true \
    BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_CONFIRMATION="$android_phrase" >/dev/null 2>&1; then
    fail "symlinked evidence destination was accepted."
fi

printf '%s\n' 'Installed-device acceptance evidence regression tests passed.'
