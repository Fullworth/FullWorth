#!/bin/sh
set -eu
root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp=$(mktemp -d); trap 'rm -rf "$temp"' EXIT HUP INT TERM
fail(){ printf '%s\n' "Acceptance evidence test failed: $1" >&2; exit 1; }
script="$root_dir/deploy/verify-private-beta-acceptance-evidence.sh"; sh -n "$script" || fail "invalid syntax"
release=1234567890abcdef1234567890abcdef12345678
deployment="$temp/deployment"; evidence="$temp/evidence"; bin="$temp/bin"; mkdir -p "$deployment" "$evidence" "$bin"
printf '%s\n' "$release" > "$deployment/.billwatch-release"
cat > "$bin/git" <<'EOF'
#!/bin/sh
case "$*" in *'rev-parse HEAD'*) printf '%s\n' "$TEST_RELEASE";; *'status --porcelain --untracked-files=no'*) :;; *) exit 1;; esac
EOF
chmod 700 "$bin/git"
write(){ file=$1; phases=$2; rel=${3:-$release}; printf 'VERSION=1\nRESULT=complete\nRELEASE_SHA=%s\nPASSED_PHASES=%s\n' "$rel" "$phases" > "$file"; chmod 600 "$file"; }
write_device(){ file=$1; platform=$2; rel=${3:-$release}; printf 'VERSION=1\nRESULT=complete\nRELEASE_SHA=%s\nPLATFORM=%s\nPASSED_PHASES=installed-pwa-launch,keyboard-resize,back-navigation,pwa-update,statement-file-picker,security-dialogs\n' "$rel" "$platform" > "$file"; chmod 600 "$file"; }

technical="$evidence/technical"; alerts="$evidence/alerts"; plaid="$evidence/plaid"; android="$evidence/android"; ios="$evidence/ios"; output="$evidence/acceptance"
write "$technical" 'internal-beta0,clean-host-recovery,controlled-reboot-recovery'
write "$alerts" 'operations-alert-observed,external-readiness-alert-observed'
write "$plaid" 'plaid-hosted-link-observed,plaid-update-completed,plaid-post-update-sync-active'
write_device "$android" android

run(){ env PATH="$bin:$PATH" TEST_RELEASE="$release" BILLWATCH_TECHNICAL_EVIDENCE_FILE="$technical" BILLWATCH_ALERT_PROOF_EVIDENCE_FILE="$alerts" BILLWATCH_PLAID_OBSERVATION_EVIDENCE_FILE="$plaid" BILLWATCH_ANDROID_DEVICE_EVIDENCE_FILE="$android" BILLWATCH_IOS_DEVICE_EVIDENCE_FILE="${TEST_IOS_FILE:-}" BILLWATCH_ACCEPTANCE_EVIDENCE_FILE="$output" sh "$script" "$deployment"; }

run >/dev/null || fail "valid same-release evidence was rejected"
[ "$(stat -c '%a' "$output")" = 600 ] || fail "output is not mode 600"
grep -qx 'PASSED_PHASES=machine-technical,alert-observation,plaid-observation,android-installed-device' "$output" || fail "Android combined phases are wrong"

rm -f "$output"
write_device "$ios" ios
TEST_IOS_FILE="$ios" run >/dev/null || fail "valid optional iOS evidence was rejected"
grep -qx 'PASSED_PHASES=machine-technical,alert-observation,plaid-observation,android-installed-device,ios-installed-device' "$output" || fail "iOS combined phases are wrong"

rm -f "$output"
if env PATH="$bin:$PATH" TEST_RELEASE="$release" BILLWATCH_TECHNICAL_EVIDENCE_FILE="$technical" BILLWATCH_ALERT_PROOF_EVIDENCE_FILE="$alerts" BILLWATCH_PLAID_OBSERVATION_EVIDENCE_FILE="$plaid" BILLWATCH_ACCEPTANCE_EVIDENCE_FILE="$output" sh "$script" "$deployment" >/dev/null 2>&1; then
    fail "acceptance passed without required Android installed-device evidence"
fi

write_device "$android" android 9999999990abcdef1234567890abcdef12345678
if run >/dev/null 2>&1; then fail "cross-release Android device evidence was accepted"; fi
write_device "$android" android

write "$plaid" 'plaid-hosted-link-observed,plaid-update-completed,plaid-post-update-sync-active' 9999999990abcdef1234567890abcdef12345678
if run >/dev/null 2>&1; then fail "cross-release Plaid evidence was accepted"; fi
write "$plaid" 'plaid-hosted-link-observed,plaid-update-completed,plaid-post-update-sync-active'

chmod 644 "$android"
if run >/dev/null 2>&1; then fail "weak Android evidence permissions were accepted"; fi
chmod 600 "$android"

chmod 644 "$alerts"
if run >/dev/null 2>&1; then fail "weak alert evidence permissions were accepted"; fi

printf '%s\n' 'Private-beta acceptance evidence regression tests passed.'
