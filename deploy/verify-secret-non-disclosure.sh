#!/bin/sh

set -eu

deployment_directory="${1:-}"
api_origin="${2:-}"
web_origin="${3:-}"

fail()
{
    printf '%s\n' "Secret non-disclosure verification failed: $1" >&2
    exit "${2:-1}"
}

[ -n "$deployment_directory" ] ||
    fail "a FullWorth deployment directory is required." 64

[ -n "$api_origin" ] ||
    fail "the public API origin is required." 64

[ -n "$web_origin" ] ||
    fail "the public web origin is required." 64

case "$api_origin:$web_origin" in
    https://*:https://*) ;;
    *) fail "public verification origins must use HTTPS." 64 ;;
esac

deployment_directory="$(cd "$deployment_directory" && pwd -P)"
environment_file="$deployment_directory/.env.production"
compose_file="$deployment_directory/compose.production.yml"

[ -f "$environment_file" ] &&
[ ! -L "$environment_file" ] ||
    fail "the protected production environment file is missing or unsafe." 66

[ -f "$compose_file" ] ||
    fail "compose.production.yml was not found." 66

for command_name in curl docker python3
do
    command -v "$command_name" >/dev/null 2>&1 ||
        fail "required command is unavailable: $command_name" 69
done

case "${BILLWATCH_SECRET_VERIFICATION_ALLOW_INSECURE:-false}" in
    true|false) ;;
    *) fail "BILLWATCH_SECRET_VERIFICATION_ALLOW_INSECURE must be true or false." 64 ;;
esac

work_directory="$(mktemp -d)"
trap 'rm -rf "$work_directory"' EXIT HUP INT TERM
observed_file="$work_directory/observable-output.bin"
: > "$observed_file"

curl_request()
{
    request_name=$1
    expected_status=$2
    shift 2

    headers_file="$work_directory/$request_name.headers"
    body_file="$work_directory/$request_name.body"
    status_file="$work_directory/$request_name.status"

    if [ "${BILLWATCH_SECRET_VERIFICATION_ALLOW_INSECURE:-false}" = true ]; then
        insecure_option=--insecure
    else
        insecure_option=
    fi

    status="$(
        curl \
            --silent \
            --show-error \
            --max-time 30 \
            $insecure_option \
            --dump-header "$headers_file" \
            --output "$body_file" \
            --write-out '%{http_code}' \
            "$@"
    )" ||
        fail "the $request_name probe could not reach the public boundary." 69

    printf '%s' "$status" > "$status_file"

    case "$status" in
        $expected_status) ;;
        *) fail "the $request_name probe returned unexpected HTTP status $status." 69 ;;
    esac

    for artifact in "$headers_file" "$body_file" "$status_file"
    do
        cat "$artifact" >> "$observed_file"
        printf '\n' >> "$observed_file"
    done
}

curl_request \
    api-live \
    200 \
    "$api_origin/health/live"

curl_request \
    api-ready \
    200 \
    "$api_origin/health/ready"

curl_request \
    web-live \
    200 \
    "$web_origin/health/live"

curl_request \
    web-ready \
    200 \
    "$web_origin/health/ready"

curl_request \
    api-error \
    400 \
    --request POST \
    --header 'Content-Type: application/json' \
    --data-binary '{' \
    "$api_origin/api/auth/login?useCookies=false"

curl_request \
    api-not-found \
    404 \
    "$api_origin/api/fullworth-secret-non-disclosure-probe"

docker compose \
    --env-file "$environment_file" \
    --file "$compose_file" \
    logs \
    --no-color \
    api parser-worker web web-session-cache edge database \
    >> "$observed_file" 2>&1 ||
    fail "production service logs could not be collected." 69

if ! python3 - "$environment_file" "$observed_file" <<'PY'
import base64
import os
import pathlib
import sys
import urllib.parse

environment_path = pathlib.Path(sys.argv[1])
observed_path = pathlib.Path(sys.argv[2])

secret_names = (
    "BILLWATCH_DATABASE_PASSWORD",
    "BILLWATCH_DATABASE_RUNTIME_PASSWORD",
    "BILLWATCH_PARSER_AUTH_TOKEN",
    "BILLWATCH_WEB_SESSION_REDIS_PASSWORD",
    "PLAID_SECRET",
    "RESTIC_PASSWORD",
    "STRIPE_SECRET_KEY",
    "STRIPE_WEBHOOK_SECRET",
    "FULLWORTH_GOOGLE_CLIENT_SECRET",
    "FULLWORTH_APPLE_CLIENT_SECRET",
    "RESEND_API_KEY",
    "AWS_SECRET_ACCESS_KEY",
)

required_names = {
    "BILLWATCH_DATABASE_PASSWORD",
    "BILLWATCH_DATABASE_RUNTIME_PASSWORD",
    "BILLWATCH_PARSER_AUTH_TOKEN",
    "BILLWATCH_WEB_SESSION_REDIS_PASSWORD",
    "PLAID_SECRET",
    "RESTIC_PASSWORD",
}

configured = {}
for raw_line in environment_path.read_text(encoding="utf-8").splitlines():
    line = raw_line.strip()
    if not line or line.startswith("#") or "=" not in line:
        continue

    name, value = line.split("=", 1)
    name = name.strip()
    value = value.strip()

    if len(value) >= 2 and value[0] == value[-1] and value[0] in {"'", '"'}:
        value = value[1:-1]

    if name in secret_names:
        configured[name] = value

for name in secret_names:
    environment_value = os.environ.get(name)
    if environment_value and not configured.get(name):
        configured[name] = environment_value

missing = sorted(
    name
    for name in required_names
    if not configured.get(name)
)
if missing:
    raise SystemExit(
        "required protected values were unavailable: " + ", ".join(missing)
    )

too_short = sorted(
    name
    for name, value in configured.items()
    if value and len(value.encode("utf-8")) < 8
)
if too_short:
    raise SystemExit(
        "protected values are too short for reliable disclosure detection: "
        + ", ".join(too_short)
    )

observed = observed_path.read_bytes()
disclosed = []

for name, value in configured.items():
    if not value:
        continue

    raw = value.encode("utf-8")
    variants = {
        raw,
        urllib.parse.quote(value, safe="").encode("ascii"),
        urllib.parse.quote_plus(value, safe="").encode("ascii"),
        base64.b64encode(raw),
        base64.urlsafe_b64encode(raw).rstrip(b"="),
    }

    if any(candidate and candidate in observed for candidate in variants):
        disclosed.append(name)

if disclosed:
    raise SystemExit(
        "protected values appeared in observable output: "
        + ", ".join(sorted(disclosed))
    )
PY
then
    fail "a configured secret was disclosed through an application response or service log." 77
fi

printf '%s\n' \
    "Secret non-disclosure verification passed for public health/error responses and production service logs."
