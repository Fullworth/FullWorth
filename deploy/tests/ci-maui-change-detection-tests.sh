#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
workflow="$root_dir/.github/workflows/ci.yml"

fail()
{
    printf '%s\n' "CI change-detection regression failed: $1" >&2
    exit 1
}

[ -f "$workflow" ] ||
    fail "CI workflow is missing."

backend_detector="$(
    awk '
        /- name: Detect backend-relevant changes/ {
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

assert_root_markdown_docs_only()
{
    detector="$1"
    label="$2"

    printf '%s\n' "$detector" |
        grep -Fq '$rootMarkdown =' ||
        fail "$label detector no longer classifies root Markdown generically."

    printf '%s\n' "$detector" |
        grep -Fq -- '-not $file.Contains("/")' ||
        fail "$label detector must restrict the generic Markdown exemption to repository-root files."

    printf '%s\n' "$detector" |
        grep -Fq '".md"' ||
        fail "$label detector no longer recognizes Markdown files."

    printf '%s\n' "$detector" |
        grep -Fq '[System.StringComparison]::OrdinalIgnoreCase' ||
        fail "$label detector should classify the Markdown extension case-insensitively."
}

assert_root_markdown_docs_only "$backend_detector" "backend"
assert_root_markdown_docs_only "$maui_detector" "MAUI"

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

printf '%s\n' "CI repository-maintenance change detection regression passed."
