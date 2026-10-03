#!/bin/sh

set -eu

umask 077

fail()
{
    printf '%s\n' "File-backed secret preparation failed: $1" >&2
    exit 64
}

env_file=${1:-.env.production}
secret_directory=${2:-.fullworth-secrets}

[ -f "$env_file" ] || fail "the protected environment file is missing."
[ ! -L "$env_file" ] || fail "the protected environment file must not be a symbolic link."

case "$secret_directory" in
    /*) ;;
    *) secret_directory="$(pwd -P)/$secret_directory" ;;
esac

[ ! -L "$secret_directory" ] ||
    fail "the secret directory must not be a symbolic link."

mkdir -p "$secret_directory"
[ -d "$secret_directory" ] && [ ! -L "$secret_directory" ] ||
    fail "the secret path must resolve to a real directory."

[ "$(stat -c '%u' "$secret_directory")" = "$(id -u)" ] ||
    fail "the secret directory must be owned by the deployment account."

chmod 0700 "$secret_directory"

unexpected_entry="$(
    find "$secret_directory" -mindepth 1 -maxdepth 1 ! -type f -print -quit
)"
[ -z "$unexpected_entry" ] ||
    fail "the secret directory contains a linked, nested, or special entry."

read_value()
{
    key=$1
    required=$2
    count=$(awk -F= -v key="$key" '$1 == key { count++ } END { print count + 0 }' "$env_file")

    [ "$count" -le 1 ] ||
        fail "$key must not appear more than once."

    if [ "$count" -eq 0 ]; then
        [ "$required" = optional ] ||
            fail "$key is missing."
        printf '%s' ""
        return
    fi

    value=$(awk -v prefix="$key=" 'index($0, prefix) == 1 { print substr($0, length(prefix) + 1); exit }' "$env_file")

    if [ "$required" = required ] && [ -z "$value" ]; then
        fail "$key is empty."
    fi

    printf '%s' "$value"
}

write_secret()
{
    name=$1
    value=$2
    temporary="$secret_directory/.$name.tmp.$$"

    rm -f "$temporary"
    printf '%s' "$value" > "$temporary"
    chmod 0644 "$temporary"
    mv -f "$temporary" "$secret_directory/$name"
}

database_password=$(read_value BILLWATCH_DATABASE_PASSWORD required)
parser_auth_token=$(read_value BILLWATCH_PARSER_AUTH_TOKEN required)
redis_password=$(read_value BILLWATCH_WEB_SESSION_REDIS_PASSWORD required)
plaid_secret=$(read_value PLAID_SECRET required)
restic_password=$(read_value RESTIC_PASSWORD required)
stripe_secret_key=$(read_value STRIPE_SECRET_KEY optional)
stripe_webhook_secret=$(read_value STRIPE_WEBHOOK_SECRET optional)
resend_api_key=$(read_value RESEND_API_KEY optional)
google_client_secret=$(read_value FULLWORTH_GOOGLE_CLIENT_SECRET optional)
apple_client_secret=$(read_value FULLWORTH_APPLE_CLIENT_SECRET optional)
aws_access_key_id=$(read_value AWS_ACCESS_KEY_ID optional)
aws_secret_access_key=$(read_value AWS_SECRET_ACCESS_KEY optional)

if { [ -n "$aws_access_key_id" ] && [ -z "$aws_secret_access_key" ]; } ||
   { [ -z "$aws_access_key_id" ] && [ -n "$aws_secret_access_key" ]; }; then
    fail "AWS backup credentials must be configured as a complete pair."
fi

write_secret api-database-connection     "Host=database;Port=5432;Database=billwatch;Username=billwatch;Password=$database_password"
write_secret database-password "$database_password"
write_secret parser-auth-token "$parser_auth_token"
write_secret plaid-secret "$plaid_secret"
write_secret redis-password "$redis_password"
write_secret restic-password "$restic_password"
write_secret stripe-secret-key "$stripe_secret_key"
write_secret stripe-webhook-secret "$stripe_webhook_secret"
write_secret resend-api-key "$resend_api_key"
write_secret google-client-secret "$google_client_secret"
write_secret apple-client-secret "$apple_client_secret"
write_secret aws-credentials "[default]
aws_access_key_id=$aws_access_key_id
aws_secret_access_key=$aws_secret_access_key"

# Remove files from older layouts instead of allowing stale credentials to linger.
find "$secret_directory" -mindepth 1 -maxdepth 1 -type f \
    ! -name api-database-connection \
    ! -name database-password \
    ! -name parser-auth-token \
    ! -name plaid-secret \
    ! -name redis-password \
    ! -name restic-password \
    ! -name stripe-secret-key \
    ! -name stripe-webhook-secret \
    ! -name resend-api-key \
    ! -name google-client-secret \
    ! -name apple-client-secret \
    ! -name aws-credentials \
    -delete

printf '%s\n' "File-backed container secrets prepared."
