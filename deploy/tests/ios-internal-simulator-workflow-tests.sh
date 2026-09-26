#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
workflow="$root_dir/.github/workflows/ios-internal-simulator.yml"
smoke_script="$root_dir/deploy/tests/ios-simulator-smoke.sh"
docs="$root_dir/IOS_INTERNAL_TESTING.md"

fail()
{
    printf '%s\n' "iOS internal simulator workflow regression failed: $1" >&2
    exit 1
}

[ -f "$workflow" ] ||
    fail "workflow is missing."
[ -f "$smoke_script" ] ||
    fail "iOS simulator smoke script is missing."
[ -f "$docs" ] ||
    fail "iOS internal testing documentation is missing."

grep -Fq 'name: FullWorth iOS Internal Simulator App' "$workflow" ||
    fail "workflow name changed unexpectedly."
grep -Fq 'workflow_dispatch:' "$workflow" ||
    fail "manual iOS build entry point is missing."
grep -Fq 'pull_request:' "$workflow" ||
    fail "pull-request iOS build trigger is missing."
grep -Fq -- '- master' "$workflow" ||
    fail "master push trigger is missing."
grep -Fq 'contents: read' "$workflow" ||
    fail "workflow repository permissions are not read-only."
grep -Fq 'runs-on: macos-latest' "$workflow" ||
    fail "iOS build is not running on a macOS runner."
grep -Fq 'dotnet workload install maui-ios --skip-manifest-update' "$workflow" ||
    fail "MAUI iOS workload installation is missing."
grep -Fq 'RuntimeIdentifier=iossimulator-x64' "$workflow" ||
    fail "workflow is not explicitly simulator-only."
grep -Fq 'FullWorthApiBaseUrl=https://api.fullworth.org/' "$workflow" ||
    fail "iOS simulator build is not pinned to the canonical HTTPS API."
grep -Fq 'ios-simulator-smoke.sh' "$workflow" ||
    fail "workflow does not install and launch the built app in Simulator."
grep -Fq 'actions/upload-artifact@v7' "$workflow" ||
    fail "workflow does not publish a simulator artifact."
grep -Fq 'retention-days: 14' "$workflow" ||
    fail "iOS simulator artifact retention is not bounded."
grep -Fq 'shasum -a 256' "$workflow" ||
    fail "workflow does not publish an artifact integrity hash."
grep -Fq 'not signed for a physical iPhone' "$workflow" ||
    fail "workflow summary does not clearly distinguish simulator output from a device build."

if grep -Fq '${{ secrets.' "$workflow"; then
    fail "simulator-only workflow must not consume persistent GitHub secrets."
fi

if grep -Eiq '(codesign|provisioning|certificate|p12|mobileprovision).*(password|secret|base64)' "$workflow"; then
    fail "simulator workflow appears to contain signing-secret handling."
fi

grep -Fq 'xcrun simctl create' "$smoke_script" ||
    fail "smoke does not create an isolated simulator."
grep -Fq 'while [ "$attempt" -lt 90 ]' "$smoke_script" ||
    fail "Simulator readiness wait is not bounded."
grep -Fq 'within 180 seconds' "$smoke_script" ||
    fail "Simulator readiness timeout is not explicit."
grep -Fq 'xcrun simctl install' "$smoke_script" ||
    fail "smoke does not install FullWorth."
grep -Fq 'xcrun simctl launch' "$smoke_script" ||
    fail "smoke does not launch FullWorth."
grep -Fq 'xcrun simctl get_app_container' "$smoke_script" ||
    fail "smoke does not verify the launched app container."
grep -Fq 'xcrun simctl delete' "$smoke_script" ||
    fail "temporary Simulator cleanup is missing."

sh -n "$smoke_script" ||
    fail "iOS simulator smoke script has invalid shell syntax."

grep -Fq 'cannot be installed on a physical iPhone' "$docs" ||
    fail "documentation does not state the simulator artifact boundary."
grep -Fq 'Issue #258' "$docs" ||
    fail "documentation does not keep the iOS PWA issue separate."

printf '%s\n' "iOS internal simulator workflow regression passed."
