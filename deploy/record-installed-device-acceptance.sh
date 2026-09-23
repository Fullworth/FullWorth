#!/bin/sh

set -eu
umask 077

deployment_directory=${1:-}
platform=${BILLWATCH_INSTALLED_DEVICE_PLATFORM:-}
evidence_file=${BILLWATCH_INSTALLED_DEVICE_EVIDENCE_FILE:-}
allow_record=${BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_ALLOW_RECORD:-false}
confirmation=${BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_CONFIRMATION:-}

fail()
{
    printf '%s\n' "Installed-device acceptance evidence refused: $1" >&2
    exit "${2:-64}"
}

[ -n "$deployment_directory" ] || fail "usage: $0 <deployment-directory>"
[ -d "$deployment_directory" ] || fail "deployment directory does not exist: $deployment_directory" 66
deployment_directory=$(cd "$deployment_directory" && pwd -P)

case "$platform" in
    android)
        required_confirmation='I completed the FullWorth installed-device acceptance checks on Android'
        ;;
    ios)
        required_confirmation='I completed the FullWorth installed-device acceptance checks on iOS'
        ;;
    *)
        fail "BILLWATCH_INSTALLED_DEVICE_PLATFORM must be android or ios."
        ;;
esac

[ "$allow_record" = true ] ||
    fail "set BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_ALLOW_RECORD=true only after completing the checks." 77

[ "$confirmation" = "$required_confirmation" ] ||
    fail "set BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_CONFIRMATION to the exact platform confirmation phrase." 77

[ -n "$evidence_file" ] || fail "BILLWATCH_INSTALLED_DEVICE_EVIDENCE_FILE is required."
case "$evidence_file" in
    /*) ;;
    *) fail "installed-device evidence path must be absolute." ;;
esac
case "$evidence_file" in
    "$deployment_directory"|"$deployment_directory"/*)
        fail "installed-device evidence must live outside the deployment checkout."
        ;;
esac
[ ! -L "$evidence_file" ] || fail "installed-device evidence path must not be a symbolic link." 73
[ ! -e "$evidence_file" ] || fail "installed-device evidence already exists; refusing to overwrite it." 73
[ -d "$(dirname "$evidence_file")" ] || fail "installed-device evidence directory does not exist." 66

release_file="$deployment_directory/.billwatch-release"
[ -f "$release_file" ] && [ ! -L "$release_file" ] ||
    fail "verified release marker is missing or unsafe." 66
release_sha=$(cat "$release_file")
printf '%s\n' "$release_sha" | grep -Eq '^[0-9a-f]{40}$' ||
    fail "verified release marker is malformed." 65

head_sha=$(git -C "$deployment_directory" rev-parse HEAD)
[ "$head_sha" = "$release_sha" ] ||
    fail "deployment checkout HEAD does not match the verified release." 65
[ -z "$(git -C "$deployment_directory" status --porcelain --untracked-files=no)" ] ||
    fail "deployment checkout has tracked modifications." 65

phases='installed-pwa-launch,first-run-setup,display-accessibility-preferences,keyboard-resize,back-navigation,pwa-update,statement-file-picker,security-dialogs'

temporary=$(mktemp "${evidence_file}.tmp.XXXXXX")
trap 'rm -f "${temporary:-}"' EXIT HUP INT TERM
{
    printf 'VERSION=1\n'
    printf 'RESULT=complete\n'
    printf 'RELEASE_SHA=%s\n' "$release_sha"
    printf 'COMPLETED_AT_UTC=%s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
    printf 'PLATFORM=%s\n' "$platform"
    printf 'PASSED_PHASES=%s\n' "$phases"
} > "$temporary"
chmod 600 "$temporary"
ln "$temporary" "$evidence_file" ||
    fail "could not publish installed-device evidence without overwriting an existing file." 73
rm -f "$temporary"
trap - EXIT HUP INT TERM

printf 'Release-pinned %s installed-device acceptance evidence recorded for %s.\n' "$platform" "$release_sha"
printf '%s\n' 'This record is an explicit human attestation. It contains no device identifier, account data, screenshot, statement content, credential, token, or provider response.'
