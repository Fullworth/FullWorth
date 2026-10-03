#!/bin/sh

set -eu

repository_root="$(CDPATH= cd -- "$(dirname "$0")/../.." && pwd -P)"
verifier="$repository_root/deploy/verify-secret-non-disclosure.sh"
deployment_script="$repository_root/deploy/deploy-production.sh"
ci_workflow="$repository_root/.github/workflows/ci.yml"
api_program="$repository_root/FullWorth.API/Program.cs"
web_program="$repository_root/FullWorth.Web/Program.cs"
temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Secret non-disclosure regression failed: $1" >&2
    exit 1
}

expect_failure()
{
    output_file="$temporary_directory/failure-output"

    if "$@" >"$output_file" 2>&1; then
        fail "expected the disclosure verifier to reject observable output"
    fi

    if grep -Fq 'database-password-sentinel' "$output_file"; then
        fail "the verifier printed a protected value while reporting failure"
    fi
}

for required_file in \
    "$verifier" \
    "$deployment_script" \
    "$ci_workflow" \
    "$api_program" \
    "$web_program"
do
    [ -f "$required_file" ] ||
        fail "required release boundary file is missing"
done

fake_bin="$temporary_directory/bin"
deployment_directory="$temporary_directory/deployment"
mkdir -p "$fake_bin" "$deployment_directory"

cat > "$deployment_directory/.env.production" <<'EOF_ENV'
BILLWATCH_DATABASE_PASSWORD=database-password-sentinel
BILLWATCH_PARSER_AUTH_TOKEN=parser-token-sentinel-more-than-32-characters
BILLWATCH_WEB_SESSION_REDIS_PASSWORD=redis-password-sentinel-more-than-32-characters
PLAID_SECRET=plaid-secret-sentinel
RESTIC_PASSWORD=restic-password-sentinel
STRIPE_SECRET_KEY=
STRIPE_WEBHOOK_SECRET=
FULLWORTH_GOOGLE_CLIENT_SECRET=
FULLWORTH_APPLE_CLIENT_SECRET=
RESEND_API_KEY=
AWS_SECRET_ACCESS_KEY=
EOF_ENV
chmod 600 "$deployment_directory/.env.production"
: > "$deployment_directory/compose.production.yml"

cat > "$fake_bin/curl" <<'EOF_CURL'
#!/bin/sh
set -eu

headers=
body=
url=

while [ "$#" -gt 0 ]
do
    case "$1" in
        --dump-header)
            shift
            headers=$1
            ;;
        --output)
            shift
            body=$1
            ;;
        https://*)
            url=$1
            ;;
    esac
    shift
done

[ -n "$headers" ] &&
[ -n "$body" ] &&
[ -n "$url" ] ||
    exit 2

case "$url" in
    */health/live|*/health/ready)
        status=200
        payload='{"status":"healthy"}'
        ;;
    */api/auth/login*)
        status=400
        payload='{"title":"One or more validation errors occurred.","status":400}'
        ;;
    */api/fullworth-secret-non-disclosure-probe)
        status=404
        payload='{"title":"Not Found","status":404}'
        ;;
    *)
        exit 3
        ;;
esac

printf 'HTTP/2 %s\nContent-Type: application/problem+json\n' "$status" > "$headers"

case "${FULLWORTH_TEST_DISCLOSURE_CHANNEL:-}" in
    response)
        payload='database-password-sentinel'
        ;;
    encoded-response)
        payload='database-password-sentinel'
        payload="$(python3 -c 'import sys, urllib.parse; print(urllib.parse.quote(sys.stdin.read(), safe=""))' <<EOF_VALUE
$payload
EOF_VALUE
)"
        ;;
esac

printf '%s' "$payload" > "$body"
printf '%s' "$status"
EOF_CURL
chmod 755 "$fake_bin/curl"

cat > "$fake_bin/docker" <<'EOF_DOCKER'
#!/bin/sh
set -eu

case " $* " in
    *' compose '*' logs '*)
        if [ "${FULLWORTH_TEST_DISCLOSURE_CHANNEL:-}" = logs ]; then
            printf '%s\n' 'api | database-password-sentinel'
        else
            printf '%s\n' 'api | Request completed without protected configuration values.'
        fi
        ;;
    *)
        exit 4
        ;;
esac
EOF_DOCKER
chmod 755 "$fake_bin/docker"

PATH="$fake_bin:$PATH" \
    sh "$verifier" \
    "$deployment_directory" \
    'https://api.fullworth.test' \
    'https://app.fullworth.test' \
    >/dev/null

expect_failure env \
    PATH="$fake_bin:$PATH" \
    FULLWORTH_TEST_DISCLOSURE_CHANNEL=response \
    sh "$verifier" \
    "$deployment_directory" \
    'https://api.fullworth.test' \
    'https://app.fullworth.test'

expect_failure env \
    PATH="$fake_bin:$PATH" \
    FULLWORTH_TEST_DISCLOSURE_CHANNEL=logs \
    sh "$verifier" \
    "$deployment_directory" \
    'https://api.fullworth.test' \
    'https://app.fullworth.test'

grep -Fq 'app.UseExceptionHandler();' "$api_program" ||
    fail "the API production exception boundary is missing"

grep -Fq 'builder.Services.AddProblemDetails();' "$api_program" ||
    fail "the API generic Problem Details service is missing"

grep -Fq 'app.UseExceptionHandler();' "$web_program" ||
    fail "the Web production exception boundary is missing"

if grep -Eq 'UseDeveloperExceptionPage|IncludeExceptionDetails[[:space:]]*=[[:space:]]*true' \
    "$api_program" "$web_program"
then
    fail "a detailed production exception response path is enabled"
fi

grep -Fq 'verify-secret-non-disclosure.sh' "$deployment_script" ||
    fail "guarded deployment does not run the disclosure verifier"

grep -Fq 'verify-secret-non-disclosure.sh' "$ci_workflow" ||
    fail "container CI does not run the disclosure verifier"

printf '%s\n' "Secret non-disclosure regression passed."
