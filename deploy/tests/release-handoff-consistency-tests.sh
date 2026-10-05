#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)

fail()
{
    printf '%s\n' "Release handoff consistency test failed: $1" >&2
    exit 1
}

context="$root_dir/FULLWORTH_CONTEXT.md"
roadmap="$root_dir/FULLWORTH_ROADMAP.md"
todo="$root_dir/HUMAN-TODO.md"

for file in "$context" "$roadmap" "$todo"
do
    [ -f "$file" ] || fail "required handoff file is missing: $file"
done

master_sha="$(
    git -C "$root_dir" rev-parse --verify refs/remotes/origin/master^{commit} 2>/dev/null
)" || fail "origin/master is unavailable; checkout must fetch full branch history."

is_sha()
{
    awk 'length($0) == 40 && $0 !~ /[^0-9a-f]/ { valid = 1 } END { exit !valid }'
}

extract_first_sha_after()
{
    file="$1"
    marker="$2"

    awk -v marker="$marker" 'index($0, marker) { capture = 1; next } capture { print }' "$file" | grep -Eo '[0-9a-f]{40}' | sed -n '1p'
}

extract_first_sha_from_line()
{
    file="$1"
    marker="$2"

    awk -v marker="$marker" 'index($0, marker) { print; exit }' "$file" | grep -Eo '[0-9a-f]{40}' | sed -n '1p'
}

extract_first_sha_after_or_from_line()
{
    file="$1"
    marker="$2"

    candidate="$(extract_first_sha_from_line "$file" "$marker")"
    if is_sha "$candidate"; then
        printf '%s\n' "$candidate"
        return
    fi

    extract_first_sha_after "$file" "$marker"
}

todo_master="$(extract_first_sha_after "$todo" 'Current release candidate on')"
context_master="$(extract_first_sha_from_line "$context" 'current release candidate')"
roadmap_master="$(extract_first_sha_from_line "$roadmap" 'release branch at candidate')"

for candidate in "$todo_master" "$context_master" "$roadmap_master"
do
    is_sha "$candidate" || fail "could not extract a valid current master release SHA from handoff docs (todo=${todo_master:-empty}, context=${context_master:-empty}, roadmap=${roadmap_master:-empty})."
done

[ "$todo_master" = "$context_master" ] ||
    fail "HUMAN-TODO and FULLWORTH_CONTEXT disagree on the current master release."
[ "$todo_master" = "$roadmap_master" ] ||
    fail "HUMAN-TODO and FULLWORTH_ROADMAP disagree on the current master release."

documented_master="$todo_master"

if [ "${GITHUB_EVENT_NAME:-}" = push ] &&
   [ "${GITHUB_REF:-}" = refs/heads/master ]; then
    previous_master_sha="$(
        git -C "$root_dir" rev-parse --verify "$master_sha^1" 2>/dev/null
    )" || fail "previous master commit is unavailable during master-push validation."

    if [ "$documented_master" != "$master_sha" ] &&
       [ "$documented_master" != "$previous_master_sha" ]; then
        fail "master-push handoff must name the new master SHA or its immediate pre-promotion parent."
    fi
else
    [ "$documented_master" = "$master_sha" ] ||
        fail "handoff master release SHA does not match origin/master."
fi

todo_live="$(extract_first_sha_after "$todo" 'Current verified live production release marker remains:')"
context_live="$(extract_first_sha_after_or_from_line "$context" 'Verified live production remains')"
roadmap_live="$(extract_first_sha_after_or_from_line "$roadmap" 'currently verified')"

for candidate in "$todo_live" "$context_live" "$roadmap_live"
do
    is_sha "$candidate" || fail "could not extract a valid verified-live production SHA from handoff docs."
done

[ "$todo_live" = "$context_live" ] ||
    fail "HUMAN-TODO and FULLWORTH_CONTEXT disagree on verified-live production release."

[ "$todo_live" = "$roadmap_live" ] ||
    fail "HUMAN-TODO and FULLWORTH_ROADMAP disagree on verified-live production release."

printf '%s\n' 'Release handoff consistency tests passed.'
