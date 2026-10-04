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

assert_generated_secret()
{
    file=$1
    key=$2
    value=$(awk -F= -v key="$key" '$1 == key { print substr($0, length($1) + 2); exit }' "$file")
    printf '%s\n' "$value" | grep -Eq '^[0-9a-f]{64}$' ||
        fail "$key was not generated as a 256-bit lowercase hexadecimal value."
}

canonical="$temp_dir/canonical.env"
cat > "$canonical" <<'EOF_CANONICAL'
BILLWATCH_HOST=api.fullworth.test
BILLWATCH_DATABASE_RUNTIME_PASSWORD=runtime-password-sentinel-one
BILLWATCH_PARSER_AUTH_TOKEN=parser-token-sentinel-one
BILLWATCH_WEB_SESSION_REDIS_PASSWORD=redis-password-sentinel-one
BILLWATCH_WEB_HOST=fullworth.test
EOF_CANONICAL
chmod 600 "$canonical"

canonical_before=$(sha256sum "$canonical" | awk '{ print $1 }')
canonical_output=$(sh "$repair" "$canonical")
canonical_after=$(sha256sum "$canonical" | awk '{ print $1 }')

[ "$canonical_before" = "$canonical_after" ] ||
    fail "canonical input was modified."
printf '%s' "$canonical_output" | grep -Fq 'credential migration repair completed' ||
    fail "canonical input did not complete successfully."
for secret in runtime-password-sentinel-one parser-token-sentinel-one redis-password-sentinel-one
do
    if printf '%s' "$canonical_output" | grep -Fq "$secret"; then
        fail "canonical no-op output disclosed a credential."
    fi
done

missing="$temp_dir/missing.env"
printf '%s\n%s' \
    'BILLWATCH_HOST=api.fullworth.test' \
    'BILLWATCH_WEB_HOST=fullworth.test' > "$missing"
chmod 600 "$missing"

missing_output=$(sh "$repair" "$missing")

for key in \
    BILLWATCH_DATABASE_RUNTIME_PASSWORD \
    BILLWATCH_PARSER_AUTH_TOKEN \
    BILLWATCH_WEB_SESSION_REDIS_PASSWORD
do
    [ "$(awk -F= -v key="$key" '$1 == key { count++ } END { print count + 0 }' "$missing")" -eq 1 ] ||
        fail "$key was not created exactly once."
    assert_generated_secret "$missing" "$key"
done

[ "$(stat -c '%a' "$missing")" = 600 ] ||
    fail "missing-credential repair did not preserve private file permissions."
grep -Fxq 'BILLWATCH_HOST=api.fullworth.test' "$missing" ||
    fail "missing-credential repair removed an unrelated setting."
grep -Fxq 'BILLWATCH_WEB_HOST=fullworth.test' "$missing" ||
    fail "missing-credential repair corrupted a file that lacked a trailing newline."

for key in \
    BILLWATCH_DATABASE_RUNTIME_PASSWORD \
    BILLWATCH_PARSER_AUTH_TOKEN \
    BILLWATCH_WEB_SESSION_REDIS_PASSWORD
do
    printf '%s' "$missing_output" | grep -Fq "missing $key created securely without exposing its value" ||
        fail "$key missing-key repair did not report the intended metadata-only result."
    value=$(awk -F= -v key="$key" '$1 == key { print substr($0, length($1) + 2); exit }' "$missing")
    if printf '%s' "$missing_output" | grep -Fq "$value"; then
        fail "$key repair output disclosed the generated credential."
    fi
done

runtime_value=$(awk -F= '$1 == "BILLWATCH_DATABASE_RUNTIME_PASSWORD" { print substr($0, length($1) + 2); exit }' "$missing")
parser_value=$(awk -F= '$1 == "BILLWATCH_PARSER_AUTH_TOKEN" { print substr($0, length($1) + 2); exit }' "$missing")
redis_value=$(awk -F= '$1 == "BILLWATCH_WEB_SESSION_REDIS_PASSWORD" { print substr($0, length($1) + 2); exit }' "$missing")
[ "$runtime_value" != "$parser_value" ] ||
    fail "runtime database and parser credentials were generated identically."
[ "$runtime_value" != "$redis_value" ] ||
    fail "runtime database and Redis credentials were generated identically."
[ "$parser_value" != "$redis_value" ] ||
    fail "parser and Redis credentials were generated identically."

duplicate="$temp_dir/duplicate.env"
cat > "$duplicate" <<'EOF_DUPLICATE'
# preserve comments and ordering
BILLWATCH_DATABASE_RUNTIME_PASSWORD=runtime-password-sentinel-two
BILLWATCH_DATABASE_RUNTIME_PASSWORD=runtime-password-sentinel-two
BILLWATCH_PARSER_AUTH_TOKEN=parser-token-sentinel-two
BILLWATCH_PARSER_AUTH_TOKEN=parser-token-sentinel-two
BILLWATCH_WEB_SESSION_REDIS_PASSWORD=redis-password-sentinel-two
BILLWATCH_WEB_SESSION_REDIS_PASSWORD=redis-password-sentinel-two
TRAILING_SETTING=preserved
EOF_DUPLICATE
chmod 600 "$duplicate"

duplicate_output=$(sh "$repair" "$duplicate")

for key in \
    BILLWATCH_DATABASE_RUNTIME_PASSWORD \
    BILLWATCH_PARSER_AUTH_TOKEN \
    BILLWATCH_WEB_SESSION_REDIS_PASSWORD
do
    [ "$(awk -F= -v key="$key" '$1 == key { count++ } END { print count + 0 }' "$duplicate")" -eq 1 ] ||
        fail "$key identical duplicate entries were not collapsed."
done
[ "$(stat -c '%a' "$duplicate")" = 600 ] ||
    fail "duplicate repair did not preserve private file permissions."
grep -Fxq '# preserve comments and ordering' "$duplicate" ||
    fail "duplicate repair removed an unrelated comment."
grep -Fxq 'TRAILING_SETTING=preserved' "$duplicate" ||
    fail "duplicate repair removed an unrelated setting."
for secret in runtime-password-sentinel-two parser-token-sentinel-two redis-password-sentinel-two
do
    if printf '%s' "$duplicate_output" | grep -Fq "$secret"; then
        fail "duplicate repair output disclosed a credential."
    fi
done

conflicting="$temp_dir/conflicting.env"
cat > "$conflicting" <<'EOF_CONFLICTING'
BILLWATCH_DATABASE_RUNTIME_PASSWORD=runtime-password-sentinel-three
BILLWATCH_PARSER_AUTH_TOKEN=parser-token-sentinel-three-a
BILLWATCH_PARSER_AUTH_TOKEN=parser-token-sentinel-three-b
BILLWATCH_WEB_SESSION_REDIS_PASSWORD=redis-password-sentinel-three
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
    fail "conflicting input was modified before the conflict was rejected."
printf '%s' "$conflicting_output" | grep -Fq 'BILLWATCH_PARSER_AUTH_TOKEN has conflicting duplicate values' ||
    fail "conflicting parser duplicates did not fail with the intended reason."
if printf '%s' "$conflicting_output" | grep -Eq 'parser-token-sentinel-three-(a|b)'; then
    fail "conflicting duplicate failure disclosed a credential."
fi

target="$temp_dir/target.env"
link="$temp_dir/link.env"
cat > "$target" <<'EOF_TARGET'
BILLWATCH_DATABASE_RUNTIME_PASSWORD=runtime-password-sentinel-four
BILLWATCH_PARSER_AUTH_TOKEN=parser-token-sentinel-four
BILLWATCH_WEB_SESSION_REDIS_PASSWORD=redis-password-sentinel-four
EOF_TARGET
chmod 600 "$target"
ln -s "$target" "$link"

if sh "$repair" "$link" >/dev/null 2>&1; then
    fail "symlink environment file was accepted."
fi

overpermitted="$temp_dir/overpermitted.env"
cat > "$overpermitted" <<'EOF_OVERPERMITTED'
BILLWATCH_DATABASE_RUNTIME_PASSWORD=runtime-password-sentinel-five
BILLWATCH_PARSER_AUTH_TOKEN=parser-token-sentinel-five
BILLWATCH_WEB_SESSION_REDIS_PASSWORD=redis-password-sentinel-five
EOF_OVERPERMITTED
chmod 640 "$overpermitted"

if sh "$repair" "$overpermitted" >/dev/null 2>&1; then
    fail "group-readable environment file was accepted."
fi

printf '%s\n' "Production environment repair regression passed."
