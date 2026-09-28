#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
workflow="$root_dir/.github/workflows/repository-governance.yml"
verifier="$root_dir/deploy/verify-github-branch-governance.sh"

fail()
{
    printf '%s\n' "Repository governance regression failed: $1" >&2
    exit 1
}

[ -f "$workflow" ] || fail "workflow is missing."
[ -f "$verifier" ] || fail "branch protection verifier is missing."

grep -Fq 'branches:' "$workflow" ||
    fail "workflow has no branch trigger."
grep -Fq -- '- master' "$workflow" ||
    fail "workflow does not run after master updates."
grep -Fq 'schedule:' "$workflow" ||
    fail "workflow has no recurring governance check."
grep -Fq 'workflow_dispatch:' "$workflow" ||
    fail "workflow cannot be run manually."
grep -Fq 'contents: read' "$workflow" ||
    fail "workflow permissions are broader than the documented read boundary."
grep -Fq 'bash deploy/verify-github-branch-governance.sh' "$workflow" ||
    fail "workflow does not execute the guarded verifier."

grep -Fq 'for branch in master development' "$verifier" ||
    fail "both long-lived branches are not verified."
grep -Fq '/branches/${branch}' "$verifier" ||
    fail "verifier does not read the GitHub branch resource."
grep -Fq 'if has("protected") then (.protected | tostring) else "missing" end' "$verifier" ||
    fail "verifier does not fail closed on missing protection state."
grep -Fq 'if [[ "$protected" != "true" ]]' "$verifier" ||
    fail "unprotected branches are not rejected."
grep -Fq 'exit 1' "$verifier" ||
    fail "governance failure does not produce a failing result."

if grep -Eq 'echo .*GITHUB_TOKEN|printf .*GITHUB_TOKEN' "$verifier"; then
    fail "verifier must never print the GitHub token."
fi

printf '%s\n' "Repository governance workflow regression passed."
