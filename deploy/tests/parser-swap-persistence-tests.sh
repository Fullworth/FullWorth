#!/bin/sh

set -eu

fail()
{
    printf '%s\n' "Parser swap persistence test failed: $1" >&2
    exit 1
}

# This test deliberately reloads the service manager. Never run it on a
# production host, a self-hosted runner, or a developer's machine.
[ "${GITHUB_ACTIONS:-}" = true ] &&
[ "${RUNNER_ENVIRONMENT:-}" = github-hosted ] &&
[ -n "${GITHUB_RUN_ID:-}" ] ||
    fail "requires a disposable GitHub-hosted Actions runner."

root_dir=${1:-}
[ -n "$root_dir" ] && [ -f "$root_dir/compose.production.yml" ] ||
    fail "a production Compose fixture directory is required."

driver="$(docker info --format '{{.CgroupDriver}}')"
version="$(docker info --format '{{.CgroupVersion}}')"
[ "$driver" = systemd ] && [ "$version" = 2 ] ||
    fail "the reload regression requires systemd with cgroup v2."

container_id="$(docker compose --file "$root_dir/compose.production.yml" ps -q parser-worker)"
[ -n "$container_id" ] || fail "parser fixture is not running."
container_id="$(docker inspect --format '{{.Id}}' "$container_id")"
parser_pid="$(docker inspect --format '{{.State.Pid}}' "$container_id")"
started_at="$(docker inspect --format '{{.State.StartedAt}}' "$container_id")"
[ "$parser_pid" -gt 0 ] || fail "parser fixture has no live process."

relative="$(awk -F: '$1 == "0" && $2 == "" { print $3; exit }' "/proc/$parser_pid/cgroup")"
case "$relative" in
    *..*) fail "unexpected parser cgroup path." ;;
    /system.slice/docker-*/fullworth-supervisor) ;;
    *) fail "parser fixture is not in its dedicated delegated systemd scope." ;;
esac

parent_relative=${relative%/fullworth-supervisor}
scope=${parent_relative##*/}
[ "$scope" = "docker-$container_id.scope" ] ||
    fail "parser scope does not match the inspected container."
parent="/sys/fs/cgroup$parent_relative"

assert_no_swap()
{
    [ "$(cat "$parent/memory.swap.max")" = 0 ] ||
        fail "kernel swap limit is not zero."
    [ "$(systemctl show "$scope" --property=MemorySwapMax --value)" = 0 ] ||
        fail "systemd does not own the explicit zero swap boundary."
}

assert_no_swap
sudo -n systemctl daemon-reload
assert_no_swap

[ "$(docker inspect --format '{{.State.Pid}}' "$container_id")" = "$parser_pid" ] &&
[ "$(docker inspect --format '{{.State.StartedAt}}' "$container_id")" = "$started_at" ] ||
    fail "the parser restarted instead of preserving its boundary."

docker compose --file "$root_dir/compose.production.yml" exec -T api \
    curl --fail --silent --show-error \
        --retry 10 --retry-all-errors --retry-delay 1 \
        --cacert /var/run/fullworth-parser-tls/parser-worker.cer.pem \
        https://parser-worker:8081/health/ready >/dev/null

printf '%s\n' 'Parser no-swap boundary survived systemd reload without restart; readiness passed.'
