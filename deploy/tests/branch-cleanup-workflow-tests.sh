#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
workflow="$root_dir/.github/workflows/branch-cleanup.yml"

fail()
{
    printf '%s\n' "Branch cleanup workflow regression failed: $1" >&2
    exit 1
}

[ -f "$workflow" ] || fail "workflow is missing."

grep -Fq 'issue_comment:' "$workflow" ||
    fail "cleanup is not owner-comment triggered."
grep -Fq "github.event.issue.number == 245" "$workflow" ||
    fail "cleanup is not restricted to the control issue."
grep -Fq "github.event.comment.body == '/cleanup-merged-branches'" "$workflow" ||
    fail "cleanup does not require the exact command."
grep -Fq 'github.actor == github.repository_owner' "$workflow" ||
    fail "cleanup is not restricted to the repository owner."
grep -Fq 'contents: write' "$workflow" ||
    fail "workflow lacks the ref-deletion permission."
grep -Fq 'pull-requests: read' "$workflow" ||
    fail "workflow cannot verify PR state."

grep -Fq 'master|development' "$workflow" ||
    fail "long-lived branches are not explicitly preserved."
grep -Fq 'if [[ "$protected" == "true" ]]' "$workflow" ||
    fail "protected branches are not preserved."
grep -Fq 'state=open' "$workflow" ||
    fail "open pull requests are not checked."
grep -Fq 'merged_at != null and .head.sha ==' "$workflow" ||
    fail "branch head is not required to match a merged PR head exactly."

if grep -Fq -- '--arg' "$workflow"; then
    fail "workflow uses jq flags that gh api does not support."
fi
grep -Fq 'git/refs/heads/$branch' "$workflow" ||
    fail "workflow does not delete branch refs."

grep -Fq 'current_sha" != "$expected_sha' "$workflow" ||
    fail "reviewed stale branches are not pinned to their reviewed SHA."
grep -Fq 'Keeping reviewed stale branch with an open PR' "$workflow" ||
    fail "reviewed stale cleanup does not preserve newly active branches."
grep -Fq 'feat/ui-foundation-primitives' "$workflow" &&
    fail "unmerged reusable UI foundation work must remain preserved."

if grep -Fq 'git push' "$workflow"; then
    fail "workflow should use the GitHub API instead of a repository push."
fi

printf '%s\n' "Guarded branch cleanup workflow regression passed."
