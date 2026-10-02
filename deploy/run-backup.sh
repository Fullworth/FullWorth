#!/bin/sh

set -eu

deployment_directory="${1:-}"

if [ -z "$deployment_directory" ] ||
   [ ! -f "$deployment_directory/compose.production.yml" ]; then
    echo "A FullWorth deployment directory is required." >&2
    exit 64
fi

deployment_directory="$(cd "$deployment_directory" && pwd -P)"

environment_file="$deployment_directory/.env.production"

if [ -f "$environment_file" ]; then
    environment_owner="$(stat -c '%u' "$environment_file")"
    environment_permissions="$(stat -c '%a' "$environment_file")"

    if [ "$environment_owner" -ne "$(id -u)" ] ||
       [ "$((environment_permissions % 100))" -ne 0 ]; then
        echo ".env.production must be owned by the deployment account and inaccessible to group/other users." >&2
        exit 77
    fi
fi

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

wait_for_api_health()
{
    attempt=1

    while [ "$attempt" -le 60 ]
    do
        api_container="$(compose ps -q api 2>/dev/null || true)"

        if [ -n "$api_container" ]; then
            health_status="$(
                docker inspect \
                    --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' \
                    "$api_container" \
                    2>/dev/null || true
            )"

            if [ "$health_status" = "healthy" ]; then
                return 0
            fi
        fi

        sleep 2
        attempt=$((attempt + 1))
    done

    return 1
}

wait_for_parser_readiness()
{
    attempt=1

    while [ "$attempt" -le 30 ]
    do
        if compose exec -T api \
            curl --fail --silent \
                --cacert /var/run/fullworth-parser-tls/parser-worker.cer.pem \
                https://parser-worker:8081/health/ready \
                >/dev/null 2>&1; then
            return 0
        fi

        sleep 2
        attempt=$((attempt + 1))
    done

    return 1
}

api_was_running=false
edge_was_running=false

if compose ps \
        --status running \
        --services \
        | grep --quiet --line-regexp api
then
    api_was_running=true
fi

if compose ps \
        --status running \
        --services \
        | grep --quiet --line-regexp edge
then
    edge_was_running=true
fi

lock_directory="$deployment_directory/.billwatch-backup.lock"

if ! mkdir "$lock_directory" 2>/dev/null; then
    echo "Another backup is running, or a stale backup lock requires operator review." >&2
    exit 75
fi

restore_services()
{
    restore_result=0

    if [ "$api_was_running" = true ]; then
        if compose up --detach --no-build api &&
           wait_for_api_health &&
           wait_for_parser_readiness; then
            api_was_running=false
        else
            restore_result=1
        fi
    fi

    if [ "$edge_was_running" = true ]; then
        if compose start edge; then
            edge_was_running=false
        else
            restore_result=1
        fi
    fi

    return "$restore_result"
}

finish_backup()
{
    exit_code="$?"

    trap - EXIT HUP INT TERM

    if ! restore_services; then
        exit_code=1
    fi

    rmdir "$lock_directory" 2>/dev/null || exit_code=1

    exit "$exit_code"
}

interrupt_backup()
{
    exit 130
}

trap finish_backup EXIT
trap interrupt_backup HUP INT TERM

if [ "$edge_was_running" = true ]; then
    compose stop \
        --timeout 30 \
        edge
fi

if [ "$api_was_running" = true ]; then
    compose stop \
        --timeout 30 \
        api
fi

# Reconcile the database service onto the current Compose data network before
# starting the one-shot backup container. This restores the service-level
# "database" DNS alias after a guarded network-topology transition.
compose up \
    --detach \
    --wait \
    --wait-timeout 120 \
    --no-build \
    database

compose --profile operations run \
    --rm \
    backup \
    backup

restore_services
rmdir "$lock_directory"
trap - EXIT HUP INT TERM
