#!/bin/sh

set -eu

umask 077

fail()
{
    printf '%s\n' "AI evaluation startup failed: $1" >&2
    exit 1
}

env_file=${1:-.env.ai}
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(dirname "$script_dir")
compose_file="$repo_root/compose.ai-evaluation.yml"
lock_dir="${TMPDIR:-/tmp}/fullworth-ai-evaluation-start.lock"

[ -f "$compose_file" ] ||
    fail "AI evaluation Compose file is missing."

command -v docker >/dev/null 2>&1 ||
    fail "Docker is required."

docker compose version >/dev/null 2>&1 ||
    fail "Docker Compose is required."

if ! mkdir "$lock_dir" 2>/dev/null
then
    fail "another AI evaluation startup is already running."
fi

cleanup_lock()
{
    rmdir "$lock_dir" 2>/dev/null || true
}

trap cleanup_lock EXIT HUP INT TERM

sh "$script_dir/validate-ai-evaluation-env.sh" "$env_file"
sh "$script_dir/verify-approved-ai-model.sh" full "$env_file"

compose()
{
    docker compose         --env-file "$env_file"         --file "$compose_file"         "$@"
}

if ! compose pull local-ai
then
    fail "approved llama.cpp runtime image could not be pulled."
fi

if ! compose up     --detach     --no-build     --force-recreate     local-ai
then
    compose down --remove-orphans >/dev/null 2>&1 || true
    fail "llama.cpp evaluation container could not be started."
fi

if ! sh "$script_dir/check-ai-evaluation-runtime.sh" "$env_file"
then
    compose down --remove-orphans >/dev/null 2>&1 || true
    fail "llama.cpp failed the guarded runtime check."
fi

printf '%s\n' "AI evaluation runtime is ready on 127.0.0.1:8080."
