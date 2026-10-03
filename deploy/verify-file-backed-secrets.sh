#!/bin/sh

set -eu

fail()
{
    printf '%s\n' "File-backed secret verification failed: $1" >&2
    exit 1
}

deployment_directory=${1:-}
[ -n "$deployment_directory" ] ||
    fail "a deployment directory is required."

deployment_directory="$(cd "$deployment_directory" && pwd -P)"
env_file="$deployment_directory/.env.production"
secret_directory="$deployment_directory/.fullworth-secrets"

[ -d "$secret_directory" ] && [ ! -L "$secret_directory" ] ||
    fail "the private secret directory is missing or unsafe."
[ "$(stat -c '%a' "$secret_directory")" = 700 ] ||
    fail "the private secret directory must have mode 0700."
[ "$(stat -c '%u' "$secret_directory")" = "$(id -u)" ] ||
    fail "the private secret directory has the wrong owner."

compose()
{
    docker compose --env-file "$env_file" \
        --file "$deployment_directory/compose.production.yml" "$@"
}

sensitive_environment_pattern='^(ConnectionStrings__BillWatchDatabase|ParserWorker__AuthenticationToken|Plaid__Secret|StripeBilling__SecretKey|StripeBilling__WebhookSecret|IdentityEmail__ApiKey|ExternalIdentity__Google__ClientSecret|ExternalIdentity__Apple__ClientSecret|WebSession__RedisPassword|POSTGRES_PASSWORD|REDIS_PASSWORD|PGPASSWORD|RESTIC_PASSWORD|AWS_ACCESS_KEY_ID|AWS_SECRET_ACCESS_KEY)='

for service in api parser-worker web web-session-cache database
do
    container_id="$(compose ps -q "$service")"
    [ -n "$container_id" ] ||
        fail "$service is not running."

    environment="$(
        docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$container_id"
    )"

    if printf '%s\n' "$environment" | grep -E "$sensitive_environment_pattern" >/dev/null; then
        fail "$service exposes a protected value through its ordinary container environment."
    fi
done

compose exec -T api sh -ec '
    test -s /run/secrets/ConnectionStrings__BillWatchDatabase
    test -s /run/secrets/ParserWorker__AuthenticationToken
    test -s /run/secrets/Plaid__Secret
    test ! -e /run/secrets/WebSession__RedisPassword
    test ! -e /run/secrets/ExternalIdentity__Google__ClientSecret
    test ! -e /run/secrets/RESTIC_PASSWORD
'
compose exec -T parser-worker sh -ec '
    test -s /run/secrets/ParserWorker__AuthenticationToken
    test ! -e /run/secrets/ConnectionStrings__BillWatchDatabase
    test ! -e /run/secrets/Plaid__Secret
    test ! -e /run/secrets/WebSession__RedisPassword
'
compose exec -T web sh -ec '
    test -s /run/secrets/WebSession__RedisPassword
    test -e /run/secrets/ExternalIdentity__Google__ClientSecret
    test -e /run/secrets/ExternalIdentity__Apple__ClientSecret
    test ! -e /run/secrets/ConnectionStrings__BillWatchDatabase
    test ! -e /run/secrets/Plaid__Secret
'
compose exec -T database sh -ec '
    test -s /run/secrets/database_password
    test "$POSTGRES_PASSWORD_FILE" = /run/secrets/database_password
'
compose exec -T web-session-cache sh -ec '
    test -s /run/secrets/redis_password
'

printf '%s\n' "File-backed container secret verification passed."
