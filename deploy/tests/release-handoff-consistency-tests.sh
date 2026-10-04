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
    printf '%s\n' "$1" | grep -Eq '^[0-9a-f]{40}$'
}

extract_first_sha_after()
{
    file="$1"
    marker="$2"

    awk -v marker="$marker" '
        index($0, marker) {
            capture = 1
            next
        }
        capture && match($0, /[0-9a-f]{40}/) {
            print substr($0, RSTART, RLENGTH)
            exit
        }
    ' "$file"
}

extract_first_sha_from_line()
{
    file="$1"
    marker="$2"

    awk -v marker="$marker" '
        index($0, marker) && match($0, /[0-9a-f]{40}/) {
            print substr($0, RSTART, RLENGTH)
            exit
        }
    ' "$file"
}

todo_master="$(extract_first_sha_after "$todo" 'Current release candidate on `master`:')"
context_master="$(extract_first_sha_from_line "$context" '- `master`:')"
roadmap_master="$(extract_first_sha_from_line "$roadmap" '- `master` is the frozen release branch at candidate')"

for candidate in "$todo_master" "$context_master" "$roadmap_master"
do
    is_sha "$candidate" || fail "could not extract a valid current master release SHA from handoff docs."
    [ "$candidate" = "$master_sha" ] ||
        fail "handoff master release SHA does not match origin/master."
done

todo_live="$(extract_first_sha_after "$todo" 'Current verified live production release marker remains:')"
context_live="$(extract_first_sha_from_line "$context" 'Verified live production remains')"
roadmap_live="$(extract_first_sha_from_line "$roadmap" 'currently verified **live production** release remains')"

for candidate in "$todo_live" "$context_live" "$roadmap_live"
do
    is_sha "$candidate" || fail "could not extract a valid verified-live production SHA from handoff docs."
done

[ "$todo_live" = "$context_live" ] ||
    fail "HUMAN-TODO and FULLWORTH_CONTEXT disagree on verified-live production release."

[ "$todo_live" = "$roadmap_live" ] ||
    fail "HUMAN-TODO and FULLWORTH_ROADMAP disagree on verified-live production release."

printf '%s\n' 'Release handoff consistency tests passed.'
