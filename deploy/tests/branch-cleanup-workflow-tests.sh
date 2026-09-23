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
grep -Fq 'merged_at != null and .head.sha == $sha' "$workflow" ||
    fail "branch head is not required to match a merged PR head exactly."
grep -Fq 'git/refs/heads/$branch' "$workflow" ||
    fail "workflow does not delete branch refs."

if grep -Fq 'git push' "$workflow"; then
    fail "workflow should use the GitHub API instead of a repository push."
fi

printf '%s\n' "Guarded branch cleanup workflow regression passed."
