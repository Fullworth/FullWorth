#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
workflow="$root_dir/.github/workflows/production-deploy.yml"

fail()
{
    printf '%s\n' "Production deploy workflow regression failed: $1" >&2
    exit 1
}

[ -f "$workflow" ] || fail "workflow is missing."

grep -Fq 'workflow_dispatch:' "$workflow" ||
    fail "workflow is not manual-dispatch only."
grep -Fq 'release_sha:' "$workflow" ||
    fail "workflow does not require an exact release SHA."
grep -Fq 'confirm_guarded_deploy:' "$workflow" ||
    fail "workflow does not require explicit deployment confirmation."
grep -Fq 'environment: production' "$workflow" ||
    fail "workflow does not use the production environment gate."
grep -Fq 'contents: read' "$workflow" ||
    fail "workflow permissions are not read-only."
grep -Fq 'cancel-in-progress: false' "$workflow" ||
    fail "workflow allows an in-flight production deploy to be cancelled by another dispatch."

if grep -Eq '^[[:space:]]+(push|pull_request|schedule):' "$workflow"; then
    fail "workflow can run automatically instead of manual dispatch."
fi

grep -Fq 'refs/heads/master' "$workflow" ||
    fail "workflow does not enforce master-only dispatch."
grep -Fq 'GITHUB_SHA' "$workflow" ||
    fail "workflow does not bind approval to the checked-out GitHub commit."
grep -Fq 'git rev-parse origin/master' "$workflow" ||
    fail "workflow does not reject a stale master release."
grep -Fq 'git status --porcelain --untracked-files=normal' "$workflow" ||
    fail "workflow does not require clean source checkouts."
grep -Fq 'git merge --ff-only "$release"' "$workflow" ||
    fail "production checkout update is not constrained to a fast-forward."

grep -Fq 'FULLWORTH_PRODUCTION_SSH_PRIVATE_KEY' "$workflow" ||
    fail "workflow is missing the protected SSH private-key input."
grep -Fq 'FULLWORTH_PRODUCTION_SSH_KNOWN_HOSTS' "$workflow" ||
    fail "workflow is missing pinned SSH host-key material."
grep -Fq 'umask 077' "$workflow" ||
    fail "workflow does not create SSH material under a private umask."
grep -Fq 'StrictHostKeyChecking yes' "$workflow" ||
    fail "SSH host-key verification is not fail-closed."
grep -Fq 'PasswordAuthentication no' "$workflow" ||
    fail "SSH password authentication is not disabled."
grep -Fq 'KbdInteractiveAuthentication no' "$workflow" ||
    fail "SSH keyboard-interactive authentication is not disabled."
grep -Fq 'ForwardAgent no' "$workflow" ||
    fail "SSH agent forwarding is not disabled."
grep -Fq 'BatchMode yes' "$workflow" ||
    fail "SSH could become interactive."

if grep -Eq 'uses:[[:space:]]+[^[:space:]]*(ssh|scp)' "$workflow"; then
    fail "workflow delegates production SSH to an unreviewed third-party action."
fi

if grep -Fq 'set -x' "$workflow"; then
    fail "workflow enables shell tracing around protected deployment material."
fi

grep -Fq 'BILLWATCH_RELEASE_ID=$release' "$workflow" ||
    fail "workflow does not pin the production environment to the approved release."
grep -Fq 'sh deploy/validate-production-env.sh .env.production' "$workflow" ||
    fail "workflow bypasses the existing production preflight."
grep -Fq 'sh deploy/deploy-production.sh .env.production' "$workflow" ||
    fail "workflow bypasses the guarded production deployment script."
grep -Fq 'sh deploy/verify-production.sh /opt/billwatch' "$workflow" ||
    fail "workflow omits post-deploy production verification."
grep -Fq 'sh deploy/verify-beta-readiness.sh /opt/billwatch' "$workflow" ||
    fail "workflow omits post-deploy private-beta host verification."
grep -Fq 'cat .billwatch-release' "$workflow" ||
    fail "workflow does not verify the release marker after deployment."

grep -Fq 'sh deploy/monitor-readiness.sh https://api.fullworth.org' "$workflow" ||
    fail "workflow omits external canonical API readiness verification."
grep -Fq 'sh deploy/monitor-readiness.sh https://fullworth.org' "$workflow" ||
    fail "workflow omits external canonical Web readiness verification."

if grep -Eq '(cat|cp|scp).*[.]env[.]production' "$workflow"; then
    fail "workflow appears to copy or print the production environment file."
fi

printf '%s\n' "Guarded GitHub production deploy workflow regression passed."
