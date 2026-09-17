#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Readiness alert test failed: $1" >&2
    exit 1
}

sender="$root_dir/deploy/send-readiness-alert.sh"
[ -f "$sender" ] || fail "sender script is missing."
sh -n "$sender" || fail "sender script has invalid POSIX shell syntax."

if BILLWATCH_READINESS_ALERT_WEBHOOK_URL='' sh "$sender" test API 1 >/dev/null 2>&1; then
    fail "sender accepted a missing webhook URL."
fi

if BILLWATCH_READINESS_ALERT_WEBHOOK_URL='http://alerts.example.test/hook' sh "$sender" test API 1 >/dev/null 2>&1; then
    fail "sender accepted a non-HTTPS webhook URL."
fi

printf '%s\n' 'External readiness alert regression tests passed.'
