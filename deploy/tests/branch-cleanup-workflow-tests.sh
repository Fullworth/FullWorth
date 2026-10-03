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
grep -Fq "github.repository == 'Fullworth/FullWorth'" "$workflow" ||
    fail "cleanup is not restricted to the FullWorth repository."
grep -Fq "github.actor == 'RealizmModz'" "$workflow" ||
    fail "cleanup is not restricted to the authorized repository maintainer."
grep -Fq 'contents: write' "$workflow" ||
    fail "workflow lacks the ref-deletion permission."
grep -Fq 'pull-requests: read' "$workflow" ||
    fail "workflow cannot verify PR state."
grep -Fq 'group: fullworth-branch-cleanup' "$workflow" ||
    fail "cleanup runs are not serialized."
grep -Fq 'cancel-in-progress: false' "$workflow" ||
    fail "cleanup serialization may cancel an in-flight deletion run."

grep -Fq 'master|development' "$workflow" ||
    fail "long-lived branches are not explicitly preserved."
grep -Fq 'if [[ "$protected" == "true" ]]' "$workflow" ||
    fail "protected branches are not preserved."
grep -Fq 'state=open' "$workflow" ||
    fail "open pull requests are not checked."
grep -Fq 'compare/$sha...development' "$workflow" ||
    fail "cleanup does not prove whether a branch is fully contained in development."
grep -Fq 'containment_behind" == "0"' "$workflow" ||
    fail "contained-branch cleanup does not require zero commits missing from development."
grep -Fq 'containment_status" == "ahead"' "$workflow" ||
    fail "contained-branch cleanup does not accept an ancestor of development."
grep -Fq 'containment_status" == "identical"' "$workflow" ||
    fail "contained-branch cleanup does not accept a ref identical to development."
grep -Fq 'Deleted branch fully contained in development' "$workflow" ||
    fail "contained-branch cleanup is not surfaced in workflow output."

grep -Fq 'state=closed' "$workflow" ||
    fail "closed pull requests are not checked."
grep -Fq 'select(.head.sha ==' "$workflow" ||
    fail "branch head is not required to match a PR head exactly."
grep -Fq 'merged_at != null' "$workflow" ||
    fail "cleanup does not require the matching PR to have been merged."
grep -Fq 'Deleted merged-PR source branch' "$workflow" ||
    fail "merged-PR branch deletion is not surfaced in workflow output."

if grep -Fq -- '--arg' "$workflow"; then
    fail "workflow uses jq flags that gh api does not support."
fi
grep -Fq 'delete_branch_if_present()' "$workflow" ||
    fail "cleanup does not centralize race-safe ref deletion."
grep -Fq 'git/refs/heads/$branch' "$workflow" ||
    fail "workflow does not delete branch refs."
grep -Fq 'Branch already absent after concurrent cleanup' "$workflow" ||
    fail "cleanup does not tolerate a ref removed by another cleanup run."
grep -Fq 'Failed to delete branch that still exists' "$workflow" ||
    fail "cleanup does not fail closed when deletion fails and the ref remains."
grep -Fq 'repos/$REPOSITORY/branches?per_page=100' "$workflow" ||
    fail "cleanup does not re-read branch state after a failed delete."

if grep -Fq 'git push' "$workflow"; then
    fail "workflow should use the GitHub API instead of a repository push."
fi

printf '%s\n' "Guarded branch cleanup workflow regression passed."
