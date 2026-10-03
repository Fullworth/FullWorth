#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "File-backed secret regression failed: $1" >&2
    exit 1
}

expect_failure()
{
    if "$@" >"$temp_dir/failure.out" 2>&1; then
        fail "a negative case unexpectedly passed."
    fi
}

write_env()
{
    output=$1
    cat > "$output" <<'ENV'
BILLWATCH_DATABASE_PASSWORD=database:password-with-more-than-32-characters
BILLWATCH_PARSER_AUTH_TOKEN=parser-worker-token-with-more-than-32-characters
BILLWATCH_WEB_SESSION_REDIS_PASSWORD=web-session-password-with-more-than-32-characters
PLAID_SECRET=test-plaid-secret
RESTIC_PASSWORD=restic-password-with-more-than-24-characters
STRIPE_SECRET_KEY=stripe-secret
STRIPE_WEBHOOK_SECRET=stripe-webhook-secret
RESEND_API_KEY=resend-secret
FULLWORTH_GOOGLE_CLIENT_SECRET=google-secret
FULLWORTH_APPLE_CLIENT_SECRET=apple-secret
AWS_ACCESS_KEY_ID=backup-access-key
AWS_SECRET_ACCESS_KEY=backup-secret-key
ENV
    chmod 600 "$output"
}

env_file="$temp_dir/production.env"
secret_directory="$temp_dir/private-secrets"
write_env "$env_file"

output="$(
    sh "$root_dir/deploy/materialize-container-secrets.sh" \
        "$env_file" \
        "$secret_directory"
)"
[ "$output" = "File-backed container secrets prepared." ] ||
    fail "materializer did not return its fixed success message."

[ "$(stat -c '%a' "$secret_directory")" = 700 ] ||
    fail "secret directory mode is not 0700."
[ "$(stat -c '%u' "$secret_directory")" = "$(id -u)" ] ||
    fail "secret directory owner is not the caller."

for secret_file in \
    api-database-connection \
    database-password \
    database-pgpass \
    parser-auth-token \
    plaid-secret \
    redis-password \
    restic-password \
    stripe-secret-key \
    stripe-webhook-secret \
    resend-api-key \
    google-client-secret \
    apple-client-secret \
    aws-credentials
do
    [ -f "$secret_directory/$secret_file" ] ||
        fail "$secret_file was not materialized."
    [ "$(stat -c '%a' "$secret_directory/$secret_file")" = 644 ] ||
        fail "$secret_file does not use the documented bind-mount mode."
done

[ "$(cat "$secret_directory/database-password")" = "database:password-with-more-than-32-characters" ] ||
    fail "database password changed during materialization."
[ "$(cat "$secret_directory/api-database-connection")" = "Host=database;Port=5432;Database=billwatch;Username=billwatch;Password=database:password-with-more-than-32-characters" ] ||
    fail "API connection string was not assembled correctly."
grep -Fqx 'database:5432:*:billwatch:database\:password-with-more-than-32-characters' \
    "$secret_directory/database-pgpass" ||
    fail "database passfile did not escape the password field."
grep -Fqx 'restore-database:5432:*:billwatch:database\:password-with-more-than-32-characters' \
    "$secret_directory/database-pgpass" ||
    fail "restore passfile entry is missing."
grep -Fqx 'aws_access_key_id=backup-access-key' "$secret_directory/aws-credentials" ||
    fail "AWS access key was not placed in the credentials file."
grep -Fqx 'aws_secret_access_key=backup-secret-key' "$secret_directory/aws-credentials" ||
    fail "AWS secret key was not placed in the credentials file."

printf '%s' stale > "$secret_directory/obsolete-secret"
sh "$root_dir/deploy/materialize-container-secrets.sh" "$env_file" "$secret_directory" >/dev/null
[ ! -e "$secret_directory/obsolete-secret" ] ||
    fail "stale secret files were not removed."

duplicate_env="$temp_dir/duplicate.env"
cp "$env_file" "$duplicate_env"
printf '%s\n' 'PLAID_SECRET=second-value' >> "$duplicate_env"
expect_failure sh "$root_dir/deploy/materialize-container-secrets.sh" "$duplicate_env" "$secret_directory"
if grep -F 'second-value' "$temp_dir/failure.out" >/dev/null; then
    fail "a failed materialization printed a secret value."
fi

missing_env="$temp_dir/missing.env"
sed '/^RESTIC_PASSWORD=/d' "$env_file" > "$missing_env"
expect_failure sh "$root_dir/deploy/materialize-container-secrets.sh" "$missing_env" "$secret_directory"

symlink_target="$temp_dir/symlink-target"
mkdir "$symlink_target"
ln -s "$symlink_target" "$temp_dir/symlink-secrets"
expect_failure sh "$root_dir/deploy/materialize-container-secrets.sh" "$env_file" "$temp_dir/symlink-secrets"

for program in \
    "$root_dir/FullWorth.API/Program.cs" \
    "$root_dir/FullWorth.Web/Program.cs" \
    "$root_dir/FullWorth.ParserWorker/Program.cs"
do
    grep -Fq 'builder.Configuration.AddKeyPerFile(' "$program" ||
        fail "$program does not load mounted secret files."
done

compose_file="$root_dir/compose.production.yml"
for forbidden in \
    'ParserWorker__AuthenticationToken: ${BILLWATCH_PARSER_AUTH_TOKEN' \
    'ConnectionStrings__BillWatchDatabase: Host=' \
    'Plaid__Secret: ${PLAID_SECRET' \
    'StripeBilling__SecretKey: ${STRIPE_SECRET_KEY' \
    'WebSession__RedisPassword: ${BILLWATCH_WEB_SESSION_REDIS_PASSWORD' \
    'POSTGRES_PASSWORD: ${BILLWATCH_DATABASE_PASSWORD' \
    'REDIS_PASSWORD: ${BILLWATCH_WEB_SESSION_REDIS_PASSWORD' \
    'PGPASSWORD: ${BILLWATCH_DATABASE_PASSWORD' \
    'RESTIC_PASSWORD: ${RESTIC_PASSWORD' \
    'AWS_SECRET_ACCESS_KEY: ${AWS_SECRET_ACCESS_KEY'
do
    if grep -F "$forbidden" "$compose_file" >/dev/null; then
        fail "Compose still exposes a protected value through an ordinary environment entry."
    fi
done

for required in \
    'target: ConnectionStrings__BillWatchDatabase' \
    'target: ParserWorker__AuthenticationToken' \
    'target: Plaid__Secret' \
    'target: WebSession__RedisPassword' \
    'POSTGRES_PASSWORD_FILE: /run/secrets/database_password' \
    'RESTIC_PASSWORD_FILE: /run/secrets/restic_password' \
    'AWS_SHARED_CREDENTIALS_FILE: /run/secrets/aws_credentials' \
    'REDIS_PASSWORD="$(cat /run/secrets/redis_password)";'
do
    grep -Fq "$required" "$compose_file" ||
        fail "Compose is missing a required file-backed secret boundary."
done

grep -Fq 'materialize-container-secrets.sh' "$root_dir/deploy/deploy-production.sh" ||
    fail "deployment does not prepare secret files."
grep -Fq 'verify-file-backed-secrets.sh' "$root_dir/deploy/deploy-production.sh" ||
    fail "deployment does not verify live secret scoping."
grep -Fq 'file-backed-secret-tests.sh' "$root_dir/.github/workflows/ci.yml" ||
    fail "CI does not run the file-backed secret regression suite."
grep -Fq 'Verify file-backed container secret boundary' "$root_dir/.github/workflows/ci.yml" ||
    fail "CI does not inspect the live container boundary."
grep -Fq 'grep -q "^NOAUTH"' "$root_dir/deploy/verify-file-backed-secrets.sh" ||
    fail "live secret verification does not prove unauthenticated Redis access is denied."
grep -Fq 'redis-cli --no-auth-warning --raw ping' "$root_dir/deploy/verify-file-backed-secrets.sh" ||
    fail "live secret verification does not prove mounted Redis authentication succeeds."
grep -Fq '.fullworth-secrets/' "$root_dir/.gitignore" ||
    fail "materialized secrets are not ignored by Git."
grep -Fxq '.fullworth-secrets' "$root_dir/.dockerignore" ||
    fail "materialized secrets are not excluded from Docker build contexts."

printf '%s\n' "File-backed secret regression tests passed."
