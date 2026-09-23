#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
workflow="$root_dir/.github/workflows/android-internal-apk.yml"

fail()
{
    printf '%s\n' "Android internal APK workflow regression failed: $1" >&2
    exit 1
}

[ -f "$workflow" ] || fail "workflow is missing."

grep -Fq 'name: FullWorth Android Internal APK' "$workflow" ||
    fail "workflow name changed unexpectedly."
grep -Fq 'workflow_dispatch:' "$workflow" ||
    fail "manual internal-test build entry point is missing."
grep -Fq 'pull_request:' "$workflow" ||
    fail "pull-request internal-test build trigger is missing."
grep -Fq 'deploy/tests/android-internal-apk-workflow-tests.sh' "$workflow" ||
    fail "workflow changes are not self-covered by the pull-request path filter."
grep -Fq 'branches:' "$workflow" ||
    fail "master push trigger is missing."
grep -Fq -- '- master' "$workflow" ||
    fail "internal APK is not generated from master pushes."
grep -Fq 'permissions:' "$workflow" ||
    fail "workflow permissions are not explicit."
grep -Fq 'contents: read' "$workflow" ||
    fail "workflow repository permissions are not read-only."

if grep -Fq '${{ secrets.' "$workflow"; then
    fail "internal APK workflow must not consume persistent GitHub secrets."
fi

grep -Fq 'keytool -genkeypair' "$workflow" ||
    fail "ephemeral test signing identity is not generated."
grep -Fq '[Guid]::NewGuid()' "$workflow" ||
    fail "ephemeral signing password is not generated per run."
grep -Fq '::add-mask::' "$workflow" ||
    fail "ephemeral signing password is not masked."
grep -Fq 'validity 30' "$workflow" ||
    fail "internal signing certificate is not intentionally short-lived."
grep -Fq 'AndroidKeyStore=true' "$workflow" ||
    fail "publish is not configured to sign the installable APK."
grep -Fq 'AndroidPackageFormats=apk' "$workflow" ||
    fail "internal package format is not APK-only."
grep -Fq 'FullWorthApiBaseUrl=https://api.fullworth.org/' "$workflow" ||
    fail "internal APK is not pinned to the canonical HTTPS API."
grep -Fq '*-Signed.apk' "$workflow" ||
    fail "workflow does not require the signed APK output."
grep -Fq 'Get-FileHash -Algorithm SHA256' "$workflow" ||
    fail "workflow does not publish an APK integrity hash."
grep -Fq 'actions/upload-artifact@v4' "$workflow" ||
    fail "workflow does not publish the test package as a GitHub artifact."
grep -Fq 'actions/download-artifact@v5' "$workflow" ||
    fail "workflow does not retrieve the signed APK for emulator smoke testing."
grep -Fq 'android-apk-emulator-smoke.sh' "$workflow" ||
    fail "workflow does not install and launch the produced APK in an Android emulator."
grep -Fq 'com.companyname.billwatch' "$workflow" ||
    fail "workflow does not verify the compatibility Android application ID."
grep -Fq 'retention-days: 14' "$workflow" ||
    fail "internal APK retention is not bounded."
grep -Fq 'not production/Play-Store signed' "$workflow" ||
    fail "workflow summary does not clearly distinguish test signing from release signing."
grep -Fq 'Remove ephemeral signing material' "$workflow" ||
    fail "ephemeral keystore cleanup step is missing."

if grep -Eiq '(AndroidSigning(Store|Key)Pass=)[^"]*[A-Za-z0-9]{16,}' "$workflow"; then
    fail "workflow appears to contain a hard-coded signing password."
fi

printf '%s\n' "Android internal APK workflow regression passed."
