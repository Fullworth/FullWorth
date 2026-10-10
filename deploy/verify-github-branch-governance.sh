#!/usr/bin/env bash
set -euo pipefail

repository="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"
token="${GITHUB_TOKEN:?GITHUB_TOKEN is required}"
api_url="${GITHUB_API_URL:-https://api.github.com}"
required_checks_json='["FullWorth CI","FullWorth Dependency Security"]'

request_json() {
    local url="$1"

    curl \
        --fail-with-body \
        --silent \
        --show-error \
        --location \
        --header "Authorization: Bearer ${token}" \
        --header "Accept: application/vnd.github+json" \
        --header "X-GitHub-Api-Version: 2022-11-28" \
        "$url"
}

request_public_json() {
    local url="$1"

    curl \
        --fail-with-body \
        --silent \
        --show-error \
        --location \
        --header "Accept: application/vnd.github+json" \
        --header "X-GitHub-Api-Version: 2022-11-28" \
        "$url"
}

verify_branch() {
    local branch="$1"
    local response
    local protected
    local rulesets_response
    local ruleset_id
    local ruleset_response
    local approval_count
    local missing_checks
    local strict_checks

    if ! response="$(request_json "${api_url}/repos/${repository}/branches/${branch}")"; then
        echo "::error title=Branch governance unavailable::Could not read GitHub protection state for ${branch}."
        return 1
    fi

    protected="$(
        jq -r 'if has("protected") then (.protected | tostring) else "missing" end' \
            <<<"$response"
    )"

    if [[ "$protected" != "true" ]]; then
        echo "::error title=Unprotected FullWorth branch::${branch} reports protected=${protected}. Configure GitHub branch protection or a repository ruleset before relying on merge governance."
        return 1
    fi

    if ! rulesets_response="$(request_public_json "${api_url}/repos/${repository}/rulesets?includes_parents=true")"; then
        echo "::error title=Branch governance unavailable::Could not read applicable public GitHub rulesets for ${branch}."
        return 1
    fi

    ruleset_id="$(
        jq -r --arg ref "refs/heads/${branch}" '
            [
                .[] |
                select(
                    .name == "FullWorth protected branches" and
                    .enforcement == "active" and
                    ((.conditions.ref_name.include // []) | index($ref) != null)
                )
            ] |
            if length == 1 then .[0].id else "" end
        ' <<<"$rulesets_response"
    )"

    if [[ -z "$ruleset_id" || "$ruleset_id" == "null" ]]; then
        echo "::error title=Missing FullWorth branch ruleset::${branch} is not covered by exactly one active FullWorth protected branches ruleset."
        return 1
    fi

    if ! ruleset_response="$(request_public_json "${api_url}/repos/${repository}/rulesets/${ruleset_id}")"; then
        echo "::error title=Branch governance unavailable::Could not read the active public ruleset for ${branch}."
        return 1
    fi

    approval_count="$(
        jq -r '
            [.rules[]? | select(.type == "pull_request") |
                (.parameters.required_approving_review_count // 0)] |
            if length == 0 then 0 else max end
        ' <<<"$ruleset_response"
    )"

    if ! [[ "$approval_count" =~ ^[0-9]+$ ]] || (( approval_count < 1 )); then
        echo "::error title=Review approval required::The active ruleset for ${branch} must require at least one approving review."
        return 1
    fi

    missing_checks="$(
        jq -r --argjson required "$required_checks_json" '
            [
                .rules[]? |
                select(.type == "required_status_checks") |
                .parameters.required_status_checks[]?.context
            ] |
            unique as $configured |
            $required - $configured |
            join(", ")
        ' <<<"$ruleset_response"
    )"

    if [[ -n "$missing_checks" ]]; then
        echo "::error title=Required checks missing::The active ruleset for ${branch} is missing required checks: ${missing_checks}."
        return 1
    fi

    strict_checks="$(
        jq -r '
            [
                .rules[]? |
                select(.type == "required_status_checks") |
                .parameters.strict_required_status_checks_policy
            ] |
            length > 0 and all(.[]; . == true)
        ' <<<"$ruleset_response"
    )"

    if [[ "$strict_checks" != "true" ]]; then
        echo "::error title=Strict required checks missing::The active ruleset for ${branch} must require current-branch status checks."
        return 1
    fi

    echo "${branch}: protected; reviewed PR and current CI/security checks required"
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
