#!/bin/sh

set -eu

deployment_directory=${1:-}

if [ -z "$deployment_directory" ] ||
   [ ! -f "$deployment_directory/compose.production.yml" ]; then
    echo "A FullWorth deployment directory is required." >&2
    exit 64
fi

deployment_directory="$(
    cd "$deployment_directory"
    pwd -P
)"

environment_file="$deployment_directory/.env.production"
FULLWORTH_SECRET_DIRECTORY="$deployment_directory/.fullworth-secrets"
export FULLWORTH_SECRET_DIRECTORY

[ -d "$FULLWORTH_SECRET_DIRECTORY" ] ||
    {
        echo "File-backed container secrets are missing." >&2
        exit 66
    }

compose()
{
    if [ -f "$environment_file" ]; then
        docker compose \
            --env-file "$environment_file" \
            --file "$deployment_directory/compose.production.yml" \
            "$@"
    else
        docker compose \
            --file "$deployment_directory/compose.production.yml" \
            "$@"
    fi
}

running_services="$(compose ps --status running --services)"

if printf '%s\n' "$running_services" |
    grep --quiet --line-regexp api
then
    echo "The API must be stopped before database role preparation or schema migration." >&2
    exit 75
fi

compose up \
    --detach \
    --wait \
    --wait-timeout 120 \
    --no-build \
    database

compose --profile operations run \
    --rm \
    database-role-provisioner \
    provision

compose --profile operations run \
    --rm \
    database-migrator

compose --profile operations run \
    --rm \
    database-role-provisioner \
    verify

printf '%s\n' "FullWorth database runtime/migration separation prepared and verified."
