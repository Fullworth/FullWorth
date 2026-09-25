#!/bin/sh

set -eu

deployment_directory="${1:-}"

fail()
{
    echo "$1" >&2
    exit "${2:-1}"
}

if [ -z "$deployment_directory" ] ||
   [ ! -f "$deployment_directory/compose.production.yml" ]; then
    fail "A FullWorth deployment directory is required." 64
fi

deployment_directory="$(cd "$deployment_directory" && pwd -P)"
environment_file="$deployment_directory/.env.production"

if [ ! -f "$environment_file" ]; then
    fail ".env.production was not found." 66
fi

compose()
{
    docker compose \
        --env-file "$environment_file" \
        --file "$deployment_directory/compose.production.yml" \
        "$@"
}

container_id()
{
    service=$1
    id="$(compose ps -q "$service")"

    if [ -z "$id" ]; then
        fail "Could not resolve production container: $service" 69
    fi

    printf '%s\n' "$id"
}

assert_networks()
{
    service=$1
    shift

    id="$(container_id "$service")"

    actual="$(
        docker inspect \
            --format '{{range $name, $_ := .NetworkSettings.Networks}}{{println $name}}{{end}}' \
            "$id" |
            sort
    )"

    expected="$(
        for network in "$@"
        do
            printf '%s\n' "billwatch_$network"
        done |
            sort
    )"

    if [ "$actual" != "$expected" ]; then
        fail "$service is attached to an unexpected production network set." 77
    fi
}

assert_read_only_runtime()
{
    service=$1
    expected_pids=$2
    id="$(container_id "$service")"

    runtime_security="$(
        docker inspect \
            --format '{{.HostConfig.ReadonlyRootfs}} {{.HostConfig.PidsLimit}}' \
            "$id"
    )"

    if [ "$runtime_security" != "true $expected_pids" ]; then
        fail "$service is not running with the required read-only root/PID boundary." 77
    fi
}

for service in api web database web-session-cache
do
    id="$(container_id "$service")"

    published_bindings="$(
        docker inspect \
            --format '{{range $port, $bindings := .NetworkSettings.Ports}}{{range $bindings}}{{printf "%s:%s->%s " .HostIp .HostPort $port}}{{end}}{{end}}' \
            "$id"
    )"

    if [ -n "$published_bindings" ]; then
        fail "$service unexpectedly publishes a host port: $published_bindings" 77
    fi
done

edge_container_id="$(container_id edge)"

edge_ports="$(
    docker inspect \
        --format '{{range $port, $bindings := .NetworkSettings.Ports}}{{range $bindings}}{{printf "%s:%s->%s\n" .HostIp .HostPort $port}}{{end}}{{end}}' \
        "$edge_container_id"
)"

if ! printf '%s\n' "$edge_ports" | grep -Eq ':(80)->80/tcp$'; then
    fail "Caddy is not publishing TCP port 80." 69
fi

if ! printf '%s\n' "$edge_ports" | grep -Eq ':(443)->443/tcp$'; then
    fail "Caddy is not publishing TCP port 443." 69
fi

if ! printf '%s\n' "$edge_ports" | grep -Eq ':(443)->443/udp$'; then
    fail "Caddy is not publishing UDP port 443." 69
fi

assert_networks api \
    data \
    api_edge \
    web_api \
    api_egress

assert_networks web \
    web_edge \
    web_api \
    web_session \
    web_egress

assert_networks web-session-cache \
    web_session

assert_networks database \
    data

assert_networks edge \
    edge_egress \
    api_edge \
    web_edge

assert_read_only_runtime api 256
assert_read_only_runtime web 256
assert_read_only_runtime web-session-cache 128
assert_read_only_runtime edge 128

echo "FullWorth production exposure verification passed."
