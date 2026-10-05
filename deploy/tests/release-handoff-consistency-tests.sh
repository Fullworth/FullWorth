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
        capture {
            token_count = split($0, tokens, /[^0-9a-f]+/)
            for (i = 1; i <= token_count; i++) {
                if (length(tokens[i]) == 40) {
                    print tokens[i]
                    exit
                }
            }
        }
    ' "$file"
}


extract_first_sha_from_line()
{
    file="$1"
    marker="$2"


    awk -v marker="$marker" '
        index($0, marker) {
            token_count = split($0, tokens, /[^0-9a-f]+/)
            for (i = 1; i <= token_count; i++) {
                if (length(tokens[i]) == 40) {
                    print tokens[i]
                    exit
                }
            }
        }
    ' "$file"
}


extract_first_sha_after_or_from_line()
{
    file="$1"
    marker="$2"


    candidate="$(extract_first_sha_from_line "$file" "$marker")"
