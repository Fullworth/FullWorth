#!/bin/sh

set -eu
LC_ALL=C
export LC_ALL

fail()
{
    printf '%s\n' "Production target confirmation failed: $1" >&2
    exit 65
}

configured_target=${1-}
confirmed_target=${2-}

for value in "$configured_target" "$confirmed_target"
do
    case "$value" in
        ''|*[!A-Za-z0-9.-]*)
            fail "the configured and confirmed SSH host must contain only letters, digits, dots, and hyphens."
            ;;
    esac

    [ "${#value}" -le 253 ] ||
        fail "the configured and confirmed SSH host must not exceed 253 characters."
done

[ "$configured_target" = "$confirmed_target" ] ||
    fail "the confirmed SSH host does not match the production environment target."

printf '%s\n' "Production SSH target confirmation passed."
