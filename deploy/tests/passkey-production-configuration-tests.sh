#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM
umask 077

# Receive the existing production-operation suite's valid synthetic fixture.
# Never read the deployed host's environment or contact the running services.
fixture=${1:?Supply the synthetic valid environment fixture}
env_file="$temp_dir/passkeys.env"

fail()
{
    printf '%s\n' "Passkey production configuration regression failed: $1" >&2
    exit 1
}

reset_fixture()
{
    sed '/^BILLWATCH_PASSKEYS_ENABLED=/d' "$fixture" > "$env_file"
}

check_configuration()
{
    expected=$1
    sh "$root_dir/deploy/validate-production-env.sh" "$env_file" > /dev/null
    # Render the actual Compose model, including interpolation, without starting
    # containers. All credentials here are disposable fixture values.
    env -u BILLWATCH_PASSKEYS_ENABLED -u BILLWATCH_WEB_HOST docker compose \
        --env-file "$env_file" --file "$root_dir/compose.production.yml" \
        config --format json > "$temp_dir/config.json"
    python3 - "$temp_dir/config.json" "$expected" <<'PY'
import json
import sys

with open(sys.argv[1]) as stream:
    services = json.load(stream)["services"]
expected = sys.argv[2]
for service in ("api", "web"):
    assert services[service]["environment"]["Passkeys__Enabled"] == expected, service
assert services["api"]["environment"]["Passkeys__Origin"] == "https://app.fullworth.test"
PY
}

reset_fixture
check_configuration false
for value in false true ''
do
    reset_fixture
    printf '%s\n' "BILLWATCH_PASSKEYS_ENABLED=$value" >> "$env_file"
    check_configuration "${value:-false}"
done

for value in TRUE yes 1 'false true' '\$ENABLE_PASSKEYS' 'true#comment'
do
    reset_fixture
    printf '%s\n' "BILLWATCH_PASSKEYS_ENABLED=$value" >> "$env_file"
    if sh "$root_dir/deploy/validate-production-env.sh" "$env_file" > /dev/null 2>&1; then
        fail "invalid flag accepted"
    fi
done

for value in true false
do
    reset_fixture
    printf '%s\n' "BILLWATCH_PASSKEYS_ENABLED=$value" "BILLWATCH_PASSKEYS_ENABLED=$value" >> "$env_file"
    if sh "$root_dir/deploy/validate-production-env.sh" "$env_file" > /dev/null 2>&1; then
        fail "duplicate flag accepted"
    fi
done

printf '%s\n' "Passkey production configuration regressions passed."
