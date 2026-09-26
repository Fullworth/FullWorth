#!/bin/sh

set -eu

umask 077

fail()
{
    printf '%s\n' "AI evaluation runtime check failed: $1" >&2
    exit 1
}

env_file=${1:-.env.ai}

[ -f "$env_file" ] ||
    fail "environment file is missing."

[ ! -L "$env_file" ] ||
    fail "environment file must not be a symbolic link."

owner_id=$(stat -c '%u' "$env_file") ||
    fail "environment file ownership cannot be read."

mode=$(stat -c '%a' "$env_file") ||
    fail "environment file permissions cannot be read."

[ "$owner_id" = "$(id -u)" ] ||
    fail "environment file must be owned by the current account."

case "$mode" in
    ?00|??00) ;;
    *) fail "environment file must not grant permissions to group/other users." ;;
esac

read_value()
{
    key=$1

    count=$(awk -F= -v key="$key" '
        $1 == key { count++ }
        END { print count + 0 }
    ' "$env_file")

    [ "$count" -eq 1 ] ||
        fail "$key must appear exactly once."

    awk -v prefix="$key=" '
        index($0, prefix) == 1 {
            print substr($0, length(prefix) + 1)
            exit
        }
    ' "$env_file"
}

command -v curl >/dev/null 2>&1 ||
    fail "curl is required."

api_key=$(read_value FULLWORTH_LOCAL_AI_API_KEY)
model_alias=$(read_value FULLWORTH_LOCAL_AI_MODEL_ALIAS)

[ "${#api_key}" -ge 32 ] ||
    fail "local AI API key is too short."

printf '%s\n' "$api_key" |
    grep -Eq '^[A-Za-z0-9._~!@%+=,:/-]+$' ||
    fail "local AI API key contains unsupported characters."

[ "$model_alias" = "fullworth-local" ] ||
    fail "unexpected local AI runtime alias."

curl_config=$(mktemp)
response_file=$(mktemp)

cleanup()
{
    rm -f "$curl_config" "$response_file"
}

trap cleanup EXIT HUP INT TERM

chmod 600 "$curl_config" "$response_file"

cat > "$curl_config" <<EOF
header = "Authorization: Bearer $api_key"
EOF

health_ready=false
attempt=1

while [ "$attempt" -le 90 ]
do
    status=$(
        curl             --silent             --show-error             --output "$response_file"             --write-out '%{http_code}'             --connect-timeout 2             --max-time 3             http://127.0.0.1:8080/health             2>/dev/null ||
        true
    )

    if [ "$status" = "200" ] &&
       grep -Eq '"status"[[:space:]]*:[[:space:]]*"ok"' "$response_file"
    then
        health_ready=true
        break
    fi

    attempt=$((attempt + 1))
    sleep 2
done

[ "$health_ready" = "true" ] ||
    fail "llama.cpp did not become healthy on loopback."

smoke_request='{"model":"fullworth-local","messages":[{"role":"user","content":"Reply with exactly OK. /no_think"}],"temperature":0,"max_tokens":32,"stream":false}'

unauthorized_status=$(
    printf '%s' "$smoke_request" |
    curl         --silent         --show-error         --output "$response_file"         --write-out '%{http_code}'         --connect-timeout 2         --max-time 10         --header "Content-Type: application/json"         --data-binary @-         http://127.0.0.1:8080/v1/chat/completions
) || fail "unauthenticated boundary probe could not reach llama.cpp."

[ "$unauthorized_status" = "401" ] ||
    fail "protected inference endpoint accepted an unauthenticated request."

authorized_status=$(
    printf '%s' "$smoke_request" |
    curl         --silent         --show-error         --config "$curl_config"         --output "$response_file"         --write-out '%{http_code}'         --connect-timeout 2         --max-time 120         --header "Content-Type: application/json"         --data-binary @-         http://127.0.0.1:8080/v1/chat/completions
) || fail "authenticated inference probe failed to reach llama.cpp."

[ "$authorized_status" = "200" ] ||
    fail "authenticated inference probe did not succeed."

grep -Eq '"choices"[[:space:]]*:' "$response_file" ||
    fail "authenticated inference response is missing choices."

grep -Eq '"message"[[:space:]]*:' "$response_file" ||
    fail "authenticated inference response is missing a message."

printf '%s\n' "AI evaluation runtime check passed."
