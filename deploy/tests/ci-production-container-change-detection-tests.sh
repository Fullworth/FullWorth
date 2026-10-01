#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
workflow="$root_dir/.github/workflows/ci.yml"

fail()
{
    printf '%s\n' "Production-container change-detection regression failed: $1" >&2
    exit 1
}

[ -f "$workflow" ] ||
    fail "CI workflow is missing."

container_detector="$(
    awk '
        /- name: Detect production-container-relevant changes/ {
            capture = 1
        }
        capture {
            print
        }
        capture && /- name: Detect visual-acceptance changes/ {
            exit
        }
    ' "$workflow"
)"

printf '%s\n' "$container_detector" |
    grep -Fq '"FullWorth.ParserWorker/"' ||
    fail "parser-worker source changes must trigger production-container validation."

printf '%s\n' "Production-container change detection regression passed."
