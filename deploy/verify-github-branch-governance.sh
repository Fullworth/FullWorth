#!/usr/bin/env bash
set -euo pipefail

repository="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"
token="${GITHUB_TOKEN:?GITHUB_TOKEN is required}"
api_url="${GITHUB_API_URL:-https://api.github.com}"

request_json() {
    local url="$1"

    curl         --fail-with-body         --silent         --show-error         --location         --header "Authorization: Bearer ${token}"         --header "Accept: application/vnd.github+json"         --header "X-GitHub-Api-Version: 2022-11-28"         "$url"
}

verify_branch() {
    local branch="$1"
    local response
    local protected

    response="$(
        request_json             "${api_url}/repos/${repository}/branches/${branch}"
    )"

    protected="$(
        jq -r             'if has("protected") then (.protected | tostring) else "missing" end'             <<<"$response"
    )"

    if [[ "$protected" != "true" ]]; then
        echo "::error title=Unprotected FullWorth branch::${branch} reports protected=${protected}. Configure GitHub branch protection or a repository ruleset before relying on merge governance."
        return 1
    fi

    echo "${branch}: protected"
}

failure=0

for branch in master development; do
    if ! verify_branch "$branch"; then
        failure=1
    fi
done

if (( failure != 0 )); then
    exit 1
fi

echo "FullWorth branch-governance verification passed."
