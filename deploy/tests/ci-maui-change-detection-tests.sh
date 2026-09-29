#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
workflow="$root_dir/.github/workflows/ci.yml"

fail()
{
    printf '%s\n' "MAUI change-detection regression failed: $1" >&2
    exit 1
}

[ -f "$workflow" ] ||
    fail "CI workflow is missing."

maui_detector="$(
    awk '
        /- name: Detect MAUI-relevant changes/ {
            capture = 1
        }
        capture {
            print
        }
        capture && /- name: Install \.NET 10/ {
            exit
        }
    ' "$workflow"
)"

printf '%s\n' "$maui_detector" |
    grep -Fq '".github/workflows/branch-cleanup.yml"' ||
    fail "branch cleanup workflow still triggers a MAUI build."

printf '%s\n' "$maui_detector" |
    grep -Fq '".github/workflows/repository-governance.yml"' ||
    fail "repository governance workflow still triggers a MAUI build."

if printf '%s\n' "$maui_detector" |
    grep -Fq '".github/workflows/ci.yml"'
then
    fail "changes to the CI workflow itself must continue to exercise the MAUI gate."
fi

printf '%s\n' "MAUI repository-maintenance change detection regression passed."
