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
write_device(){ file=$1; platform=$2; rel=${3:-$release}; printf 'VERSION=1\nRESULT=complete\nRELEASE_SHA=%s\nPLATFORM=%s\nPASSED_PHASES=installed-pwa-launch,first-run-setup,display-accessibility-preferences,keyboard-resize,back-navigation,pwa-update,statement-file-picker,security-dialogs\n' "$rel" "$platform" > "$file"; chmod 600 "$file"; }

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

chmod 600 "$alerts"

# Each input class must reject checkout files hidden behind either parent alias.
ln -s "$deployment" "$temp/checkout-alias"
for kind in technical alerts plaid android ios; do
    case "$kind" in
        technical) original=$technical ;;
        alerts) original=$alerts ;;
        plaid) original=$plaid ;;
        android) original=$android ;;
        ios) original=$ios ;;
    esac
    cp "$original" "$deployment/$kind"
    chmod 600 "$deployment/$kind"
    for prefix in "$evidence/../deployment" "$temp/checkout-alias"; do
        case "$kind" in
            technical) technical="$prefix/$kind" ;;
            alerts) alerts="$prefix/$kind" ;;
            plaid) plaid="$prefix/$kind" ;;
            android) android="$prefix/$kind" ;;
            ios) ios="$prefix/$kind" ;;
        esac
        if TEST_IOS_FILE="$ios" run >/dev/null 2>&1; then
            fail "$kind checkout evidence was accepted through a parent alias"
        fi
        [ ! -e "$output" ] || fail "invalid input published combined evidence"
    done
    case "$kind" in
        technical) technical=$original ;;
        alerts) alerts=$original ;;
        plaid) plaid=$original ;;
        android) android=$original ;;
        ios) ios=$original ;;
    esac
done

original_output=$output
for output in "$evidence/../deployment/combined" "$temp/checkout-alias/combined"; do
    if run >/dev/null 2>&1; then fail "checkout output was accepted through a parent alias"; fi
done
[ ! -e "$deployment/combined" ] || fail "checkout output was written"
[ -z "$(find "$deployment" -name '*.tmp.*' -print)" ] || fail "refused output left a temporary file"
output=$original_output

# Outside symlink parents and dot segments are safe when resolved outside.
ln -s "$evidence" "$temp/outside-alias"
technical="$temp/outside-alias/../evidence/technical"
output="$temp/outside-alias/combined with spaces"
run >/dev/null || fail "valid outside evidence aliases were rejected"
[ -f "$evidence/combined with spaces" ] || fail "outside output alias was not published"

# Resolving a parent must not turn a final-component symlink into trusted input.
technical="$evidence/technical-link"
ln -s "$evidence/technical" "$technical"
output="$evidence/invalid-link-output"
if run >/dev/null 2>&1; then fail "final-component input symlink was accepted"; fi
[ ! -e "$output" ] || fail "symlink input published combined evidence"
technical="$evidence/technical"
output="$evidence/output-link"
ln -s "$evidence/missing-output" "$output"
if run >/dev/null 2>&1; then fail "final-component output symlink was accepted"; fi
[ ! -e "$evidence/missing-output" ] || fail "symlink output target was written"

printf '%s\n' 'Private-beta acceptance evidence regression tests passed.'
