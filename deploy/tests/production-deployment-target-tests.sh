#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
helper="$root_dir/deploy/confirm-production-target.sh"

fail()
{
    printf '%s\n' "Production deployment target test failed: $1" >&2
    exit 1
}

[ -f "$helper" ] || fail "production target confirmation helper is missing."
sh -n "$helper" || fail "production target confirmation helper has invalid shell syntax."

sh "$helper" "40.160.137.55" "40.160.137.55" >/dev/null ||
    fail "a matching, syntactically safe production target was rejected."

if sh "$helper" "40.160.137.55" "198.51.100.20" >/dev/null 2>&1; then
    fail "a manually confirmed host that differs from the configured host was accepted."
fi

if sh "$helper" "" "" >/dev/null 2>&1; then
    fail "an empty production host was accepted."
fi

if sh "$helper" "40.160.137.55" "40.160.137.55;touch /tmp/pwned" >/dev/null 2>&1; then
    fail "a host value containing shell metacharacters was accepted."
fi

if sh "$helper" "$(awk 'BEGIN { for (i = 0; i < 254; i++) printf "a" }')" \
    "$(awk 'BEGIN { for (i = 0; i < 254; i++) printf "a" }')" >/dev/null 2>&1; then
    fail "an overlong production host value was accepted."
fi

printf '%s\n' "Production deployment target tests passed."
