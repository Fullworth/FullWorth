#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
repair="$root_dir/deploy/repair-production-env.sh"
temp_dir=$(mktemp -d)

cleanup()
{
    rm -rf "$temp_dir"
}

trap cleanup EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Production environment repair regression failed: $1" >&2
    exit 1
}

[ -f "$repair" ] || fail "repair helper is missing."

single="$temp_dir/single.env"
cat > "$single" <<'EOF_SINGLE'
BILLWATCH_HOST=api.fullworth.test
BILLWATCH_DATABASE_RUNTIME_PASSWORD=runtime-password-sentinel-one
BILLWATCH_WEB_HOST=fullworth.test
EOF_SINGLE
chmod 600 "$single"

single_before=$(sha256sum "$single" | awk '{ print $1 }')
single_output=$(sh "$repair" "$single")
single_after=$(sha256sum "$single" | awk '{ print $1 }')

[ "$single_before" = "$single_after" ] ||
    fail "canonical input was modified."
printf '%s' "$single_output" | grep -Fq 'already canonical' ||
    fail "canonical input did not report a no-op."
if printf '%s' "$single_output" | grep -Fq 'runtime-password-sentinel-one'; then
    fail "canonical no-op output disclosed the credential."
fi

missing="$temp_dir/missing.env"
printf '%s\n%s' \
    'BILLWATCH_HOST=api.fullworth.test' \
    'BILLWATCH_WEB_HOST=fullworth.test' > "$missing"
chmod 600 "$missing"

missing_output=$(sh "$repair" "$missing")

[ "$(awk -F= '$1 == "BILLWATCH_DATABASE_RUNTIME_PASSWORD" { count++ } END { print count + 0 }' "$missing")" -eq 1 ] ||
    fail "missing runtime database credential was not created exactly once."
generated_value=$(awk -F= '$1 == "BILLWATCH_DATABASE_RUNTIME_PASSWORD" { print substr($0, length($1) + 2); exit }' "$missing")
printf '%s\n' "$generated_value" | grep -Eq '^[0-9a-f]{64}$' ||
    fail "generated runtime database credential is not a 256-bit lowercase hexadecimal value."
[ "$(stat -c '%a' "$missing")" = 600 ] ||
    fail "missing-credential repair did not preserve private file permissions."
grep -Fxq 'BILLWATCH_HOST=api.fullworth.test' "$missing" ||
    fail "missing-credential repair removed an unrelated setting."
grep -Fxq 'BILLWATCH_WEB_HOST=fullworth.test' "$missing" ||
    fail "missing-credential repair corrupted a file that lacked a trailing newline."
printf '%s' "$missing_output" | grep -Fq 'created securely without exposing its value' ||
    fail "missing-credential repair did not report the intended metadata-only result."
if printf '%s' "$missing_output" | grep -Fq "$generated_value"; then
    fail "missing-credential repair output disclosed the generated credential."
fi

duplicate="$temp_dir/duplicate.env"
cat > "$duplicate" <<'EOF_DUPLICATE'
# preserve comments and ordering
BILLWATCH_HOST=api.fullworth.test
BILLWATCH_DATABASE_RUNTIME_PASSWORD=runtime-password-sentinel-two
BILLWATCH_WEB_HOST=fullworth.test
BILLWATCH_DATABASE_RUNTIME_PASSWORD=runtime-password-sentinel-two
TRAILING_SETTING=preserved
EOF_DUPLICATE
chmod 600 "$duplicate"

duplicate_output=$(sh "$repair" "$duplicate")

[ "$(awk -F= '$1 == "BILLWATCH_DATABASE_RUNTIME_PASSWORD" { count++ } END { print count + 0 }' "$duplicate")" -eq 1 ] ||
    fail "identical duplicate credential entries were not collapsed."
[ "$(stat -c '%a' "$duplicate")" = 600 ] ||
    fail "repair did not preserve private file permissions."
grep -Fxq '# preserve comments and ordering' "$duplicate" ||
    fail "repair removed an unrelated comment."
grep -Fxq 'TRAILING_SETTING=preserved' "$duplicate" ||
    fail "repair removed an unrelated setting."
grep -Fxq 'BILLWATCH_DATABASE_RUNTIME_PASSWORD=runtime-password-sentinel-two' "$duplicate" ||
    fail "repair did not preserve the credential value."
if printf '%s' "$duplicate_output" | grep -Fq 'runtime-password-sentinel-two'; then
    fail "successful repair output disclosed the credential."
fi

conflicting="$temp_dir/conflicting.env"
cat > "$conflicting" <<'EOF_CONFLICTING'
BILLWATCH_DATABASE_RUNTIME_PASSWORD=runtime-password-sentinel-three-a
BILLWATCH_DATABASE_RUNTIME_PASSWORD=runtime-password-sentinel-three-b
EOF_CONFLICTING
chmod 600 "$conflicting"
conflicting_before=$(sha256sum "$conflicting" | awk '{ print $1 }')

set +e
conflicting_output=$(sh "$repair" "$conflicting" 2>&1)
conflicting_status=$?
set -e

[ "$conflicting_status" -ne 0 ] ||
    fail "conflicting duplicate credentials were accepted."
[ "$conflicting_before" = "$(sha256sum "$conflicting" | awk '{ print $1 }')" ] ||
    fail "conflicting input was modified."
printf '%s' "$conflicting_output" | grep -Fq 'conflicting duplicate values' ||
    fail "conflicting duplicates did not fail with the intended reason."
if printf '%s' "$conflicting_output" | grep -Eq 'runtime-password-sentinel-three-(a|b)'; then
    fail "conflicting duplicate failure disclosed a credential."
fi

empty="$temp_dir/empty.env"
cat > "$empty" <<'EOF_EMPTY'
BILLWATCH_DATABASE_RUNTIME_PASSWORD=
BILLWATCH_DATABASE_RUNTIME_PASSWORD=
EOF_EMPTY
chmod 600 "$empty"
empty_before=$(sha256sum "$empty" | awk '{ print $1 }')

set +e
empty_output=$(sh "$repair" "$empty" 2>&1)
empty_status=$?
set -e

[ "$empty_status" -ne 0 ] ||
    fail "empty duplicate credentials were accepted."
[ "$empty_before" = "$(sha256sum "$empty" | awk '{ print $1 }')" ] ||
    fail "empty duplicate input was modified."
if printf '%s' "$empty_output" | grep -Fq 'BILLWATCH_DATABASE_RUNTIME_PASSWORD='; then
    fail "empty duplicate failure printed environment content."
fi

target="$temp_dir/target.env"
link="$temp_dir/link.env"
printf '%s\n' 'BILLWATCH_DATABASE_RUNTIME_PASSWORD=runtime-password-sentinel-four' > "$target"
chmod 600 "$target"
ln -s "$target" "$link"

if sh "$repair" "$link" >/dev/null 2>&1; then
    fail "symlink environment file was accepted."
fi

overpermitted="$temp_dir/overpermitted.env"
printf '%s\n' 'BILLWATCH_DATABASE_RUNTIME_PASSWORD=runtime-password-sentinel-five' > "$overpermitted"
chmod 640 "$overpermitted"

if sh "$repair" "$overpermitted" >/dev/null 2>&1; then
    fail "group-readable environment file was accepted."
fi

printf '%s\n' "Production environment repair regression passed."
