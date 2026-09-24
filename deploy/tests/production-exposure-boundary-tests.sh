#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Production exposure boundary test failed: $1" >&2
    exit 1
}

expect_failure()
{
    if "$@" >/dev/null 2>&1; then
        fail "Expected command to fail: $*"
    fi
}

deployment="$temp_dir/deployment"
fake_bin="$temp_dir/bin"

mkdir -p "$deployment/deploy" "$fake_bin"
cp "$root_dir/compose.production.yml" "$deployment/compose.production.yml"
cp "$root_dir/deploy/verify-production-exposure.sh"    "$deployment/deploy/verify-production-exposure.sh"
: > "$deployment/.env.production"

cat > "$fake_bin/docker" <<'SCRIPT'
#!/bin/sh
set -eu

case "$*" in
    *"compose "*" ps -q api")
        printf '%s\n' api-id
        ;;
    *"compose "*" ps -q web")
        printf '%s\n' web-id
        ;;
    *"compose "*" ps -q database")
        printf '%s\n' database-id
        ;;
    *"compose "*" ps -q edge")
        printf '%s\n' edge-id
        ;;
    *".NetworkSettings.Ports"*api-id|*".NetworkSettings.Ports"*web-id|*".NetworkSettings.Ports"*database-id)
        :
        ;;
    *".NetworkSettings.Ports"*edge-id)
        printf '%s\n' \
            '0.0.0.0:80->80/tcp' \
            '0.0.0.0:443->443/tcp' \
            '0.0.0.0:443->443/udp'
        ;;
    *".NetworkSettings.Networks"*api-id)
        if [ "${BILLWATCH_TEST_BAD_API_NETWORKS:-false}" = true ]; then
            printf '%s\n' \
                billwatch_api_edge \
                billwatch_data \
                billwatch_public_edge \
                billwatch_web_api
        else
            printf '%s\n' \
                billwatch_api_edge \
                billwatch_api_egress \
                billwatch_data \
                billwatch_web_api
        fi
        ;;
    *".NetworkSettings.Networks"*web-id)
        printf '%s\n' \
            billwatch_web_api \
            billwatch_web_edge \
            billwatch_web_egress
        ;;
    *".NetworkSettings.Networks"*database-id)
        printf '%s\n' billwatch_data
        ;;
    *".NetworkSettings.Networks"*edge-id)
        printf '%s\n' \
            billwatch_api_edge \
            billwatch_edge_egress \
            billwatch_web_edge
        ;;
    *".HostConfig.ReadonlyRootfs"*api-id)
        printf '%s\n' 'true 256'
        ;;
    *".HostConfig.ReadonlyRootfs"*web-id)
        if [ "${BILLWATCH_TEST_WRITABLE_WEB:-false}" = true ]; then
            printf '%s\n' 'false 256'
        else
            printf '%s\n' 'true 256'
        fi
        ;;
    *)
        printf '%s\n' "Unexpected fake docker invocation: $*" >&2
        exit 3
        ;;
esac
SCRIPT

chmod 755     "$fake_bin/docker"     "$deployment/deploy/verify-production-exposure.sh"

PATH="$fake_bin:$PATH"     "$deployment/deploy/verify-production-exposure.sh"     "$deployment" >/dev/null

expect_failure env     PATH="$fake_bin:$PATH"     BILLWATCH_TEST_BAD_API_NETWORKS=true     "$deployment/deploy/verify-production-exposure.sh"     "$deployment"

expect_failure env     PATH="$fake_bin:$PATH"     BILLWATCH_TEST_WRITABLE_WEB=true     "$deployment/deploy/verify-production-exposure.sh"     "$deployment"

printf '%s\n' 'Production exposure boundary tests passed.'
