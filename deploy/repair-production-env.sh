#!/bin/sh

set -eu

umask 077

fail()
{
    printf '%s\n' "Production environment repair failed: $1" >&2
    exit 64
}

env_file=${1:-.env.production}
key=BILLWATCH_DATABASE_RUNTIME_PASSWORD
temporary=

cleanup()
{
    status=$?
    trap - EXIT HUP INT TERM
    [ -z "$temporary" ] || rm -f "$temporary"
    exit "$status"
}

trap cleanup EXIT HUP INT TERM

[ -f "$env_file" ] || fail "environment file is missing."
[ ! -L "$env_file" ] || fail "environment file must not be a symbolic link."

owner_id=$(stat -c '%u' "$env_file") ||
    fail "environment file ownership cannot be read."
mode=$(stat -c '%a' "$env_file") ||
    fail "environment file permissions cannot be read."

[ "$owner_id" = "$(id -u)" ] ||
    fail "environment file must be owned by the deployment account."

case "$mode" in
    ?00|??00) ;;
    *) fail "environment file must not grant permissions to group/other users." ;;
esac

original_identity=$(stat -c '%d:%i:%u:%a' "$env_file") ||
    fail "environment file identity cannot be read."

count=$(awk -F= -v key="$key" '$1 == key { count++ } END { print count + 0 }' "$env_file")

if [ "$count" -le 1 ]; then
    printf '%s\n' "Production environment runtime database credential structure is already canonical."
    exit 0
fi

comparison_status=0
awk -v prefix="$key=" '
    index($0, prefix) == 1 {
        value = substr($0, length(prefix) + 1)

        if (value == "") {
            bad = 2
            exit
        }

        if (seen == 0) {
            first = value
            seen = 1
            next
        }

        if (value != first) {
            bad = 3
            exit
        }

        seen++
    }

    END {
        if (bad != 0) {
            exit bad
        }

        if (seen < 2) {
            exit 4
        }
    }
' "$env_file" || comparison_status=$?

case "$comparison_status" in
    0) ;;
    2) fail "$key has duplicate entries containing an empty value; refusing automatic repair." ;;
    3) fail "$key has conflicting duplicate values; refusing automatic repair." ;;
    *) fail "$key duplicate entries could not be compared safely." ;;
esac

temporary=$(mktemp "${env_file}.tmp.XXXXXX") ||
    fail "temporary environment file could not be created."

awk -F= -v key="$key" '
    $1 == key {
        if (seen > 0) {
            next
        }
        seen++
    }

    { print }
' "$env_file" > "$temporary" ||
    fail "canonical environment file could not be prepared."

chmod "$mode" "$temporary" ||
    fail "temporary environment file permissions could not be secured."

current_identity=$(stat -c '%d:%i:%u:%a' "$env_file") ||
    fail "environment file identity cannot be re-read."

[ "$current_identity" = "$original_identity" ] ||
    fail "environment file changed during repair; refusing to overwrite it."

repaired_count=$(awk -F= -v key="$key" '$1 == key { count++ } END { print count + 0 }' "$temporary")
[ "$repaired_count" -eq 1 ] ||
    fail "$key repair did not produce exactly one entry."

mv -f "$temporary" "$env_file" ||
    fail "canonical environment file could not be installed."
temporary=

printf '%s\n' "Production environment duplicate runtime database credential entry repaired without exposing its value."
