#!/bin/sh

set -eu

fail()
{
    printf '%s\n' "Parser worker cgroup bootstrap failed: $1" >&2
    exit 78
}

[ "$(id -u)" = "0" ] ||
    fail "bootstrap must start as root before dropping to the parser identity."

[ -r /proc/self/cgroup ] ||
    fail "the current cgroup cannot be inspected."

[ -d /sys/fs/cgroup ] ||
    fail "cgroup v2 is unavailable."

[ -f /sys/fs/cgroup/cgroup.controllers ] ||
    fail "the host is not using a writable cgroup v2 hierarchy."

current_relative="$(
    awk -F: '$1 == "0" && $2 == "" { print $3; exit }' /proc/self/cgroup
)"

case "$current_relative" in
    ""|"/")
        fail "the parser container is not inside a dedicated host cgroup."
        ;;
    *".."*)
        fail "the parser cgroup path is invalid."
        ;;
esac

container_cgroup="/sys/fs/cgroup$current_relative"

[ -d "$container_cgroup" ] ||
    fail "the parser container cgroup is unavailable."

for required_file in     cgroup.controllers     cgroup.procs     cgroup.subtree_control     cpu.max     memory.max     memory.swap.max     pids.max
do
    [ -f "$container_cgroup/$required_file" ] ||
        fail "required cgroup control is unavailable: $required_file"
done

cpu_max="$(cat "$container_cgroup/cpu.max")"
memory_max="$(cat "$container_cgroup/memory.max")"
swap_max="$(cat "$container_cgroup/memory.swap.max")"
pids_max="$(cat "$container_cgroup/pids.max")"

case "$cpu_max" in
    max\ *|"")
        fail "the parser container CPU limit is not finite."
        ;;
esac

case "$memory_max" in
    max|"")
        fail "the parser container memory limit is not finite."
        ;;
esac

case "$swap_max" in
    max|"")
        fail "the parser container swap limit is not finite."
        ;;
esac

case "$pids_max" in
    max|"")
        fail "the parser container PID limit is not finite."
        ;;
esac

controllers="$(cat "$container_cgroup/cgroup.controllers")"
for controller in cpu memory pids
do
    printf '%s\n' "$controllers" |
        tr ' ' '\n' |
        grep -qx "$controller" ||
        fail "required delegated controller is unavailable: $controller"
done

supervisor_cgroup="$container_cgroup/fullworth-supervisor"

mkdir "$supervisor_cgroup"

# The bootstrap shell is the only parser process at this point. Move it out of
# the container cgroup root so cgroup v2 can delegate domain controllers to
# per-document child cgroups without violating the no-internal-process rule.
printf '%s\n' "$$" > "$supervisor_cgroup/cgroup.procs"

printf '%s\n' '+cpu +memory +pids' > "$container_cgroup/cgroup.subtree_control"

for controller in cpu memory pids
do
    cat "$container_cgroup/cgroup.subtree_control" |
        tr ' ' '\n' |
        grep -qx "$controller" ||
        fail "failed to enable delegated controller: $controller"
done

# Delegate only this finite Docker cgroup to the unprivileged runtime identity.
# The worker can create/remove descendants here, but it receives no capability
# or ownership over sibling/ancestor host cgroups.
chown 1654:1654     "$container_cgroup"     "$container_cgroup/cgroup.procs"     "$container_cgroup/cgroup.subtree_control"     "$supervisor_cgroup"     "$supervisor_cgroup/cgroup.procs"

exec setpriv     --reuid=1654     --regid=1654     --clear-groups     --inh-caps=-all     --ambient-caps=-all     --bounding-set=-all     -- "$@"
