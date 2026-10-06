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

sh "$root_dir/deploy/tests/production-env-repair-tests.sh"

grep -Fq 'workflow_dispatch:' "$workflow" ||
    fail "workflow is not manual-dispatch only."
grep -Fq 'release_sha:' "$workflow" ||
    fail "workflow does not require an exact release SHA."
grep -Fq 'confirm_production_host:' "$workflow" ||
    fail "workflow does not require explicit production SSH target confirmation."
grep -Fq 'CONFIRMED_PRODUCTION_HOST: ${{ inputs.confirm_production_host }}' "$workflow" ||
    fail "workflow does not bind the manual host confirmation input."
grep -Fq 'PRODUCTION_SSH_HOST: ${{ vars.FULLWORTH_PRODUCTION_SSH_HOST }}' "$workflow" ||
    fail "workflow does not use an inspectable production environment host variable."
grep -Fq 'sh deploy/confirm-production-target.sh "$PRODUCTION_SSH_HOST" "$CONFIRMED_PRODUCTION_HOST"' "$workflow" ||
    fail "workflow does not run the fail-closed production target confirmation helper."
target_check_line=$(grep -n 'sh deploy/confirm-production-target.sh' "$workflow" | head -n 1 | cut -d: -f1)
artifact_download_line=$(grep -n 'Download and verify exact release image artifacts' "$workflow" | head -n 1 | cut -d: -f1)
[ -n "$target_check_line" ] && [ -n "$artifact_download_line" ] &&
    [ "$target_check_line" -lt "$artifact_download_line" ] ||
    fail "production target confirmation must run before release artifact download."
if grep -Fq 'secrets.FULLWORTH_PRODUCTION_SSH_HOST' "$workflow"; then
    fail "production SSH host remains hidden in a secret instead of an inspectable variable."
fi
grep -Fq 'confirm_guarded_deploy:' "$workflow" ||
    fail "workflow does not require explicit deployment confirmation."
grep -Fq 'environment: production' "$workflow" ||
    fail "workflow does not use the production environment gate."
grep -Fq 'actions: read' "$workflow" ||
    fail "workflow cannot read the exact CI artifact."
grep -Fq 'attestations: read' "$workflow" ||
    fail "workflow cannot verify GitHub attestations."
grep -Fq 'contents: read' "$workflow" ||
    fail "workflow cannot read the approved source release."
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
grep -Fq 'case "$release" in' "$workflow" ||
    fail "workflow does not use deterministic release SHA character validation."
grep -Fq "*[!0-9a-f]*" "$workflow" ||
    fail "workflow does not reject non-lowercase-hex release SHA characters."
grep -Fq '[[ "${#release}" -ne 40 ]]' "$workflow" ||
    fail "workflow does not enforce exact 40-character release SHA length."
if grep -Fq '[[ ! "$release" =~ ^[0-9a-f]{40}$ ]]' "$workflow"; then
    fail "workflow still uses the release SHA regex that rejected the valid production dispatch."
fi
grep -Fq 'git merge --ff-only "$release"' "$workflow" ||
    fail "production checkout update is not constrained to a fast-forward."
grep -Fq 'scp -q deploy/repair-production-env.sh' "$workflow" ||
    fail "workflow does not transfer the exact-release production environment repair helper."
grep -Fq 'sh "$env_repair" "$root/.env.production"' "$workflow" ||
    fail "workflow does not run the production environment repair helper on the protected host-local file."

repair_line=$(grep -n 'sh "$env_repair" "$root/.env.production"' "$workflow" | head -n 1 | cut -d: -f1)
merge_line=$(grep -n 'git merge --ff-only "$release"' "$workflow" | head -n 1 | cut -d: -f1)

[ -n "$repair_line" ] && [ -n "$merge_line" ] ||
    fail "workflow repair/merge ordering could not be determined."
[ "$repair_line" -lt "$merge_line" ] ||
    fail "workflow mutates the production checkout before repairing the known safe environment duplication."

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

grep -Fq 'fullworth-production-image-artifacts-$RELEASE_SHA' "$workflow" ||
    fail "workflow does not select the exact release image artifact."
grep -Fq 'actions/workflows/ci.yml/runs' "$workflow" ||
    fail "workflow does not require a successful exact-head CI run."
grep -Fq 'gh attestation verify' "$workflow" ||
    fail "workflow does not verify release image attestations."
grep -Fq -- '--predicate-type https://slsa.dev/provenance/v1' "$workflow" ||
    fail "workflow does not require build-provenance attestations."
grep -Fq -- '--predicate-type https://spdx.dev/Document' "$workflow" ||
    fail "workflow does not require SBOM attestations."
grep -Fq 'sha256sum -c SHA256SUMS' "$workflow" ||
    fail "remote release images are not checksum-verified after transfer."
grep -Fq 'docker load --input "$image_archive"' "$workflow" ||
    fail "workflow does not load the verified CI images on the production host."
grep -Fq 'FULLWORTH_USE_PREBUILT_RELEASE_IMAGES=1' "$workflow" ||
    fail "guarded deployment can rebuild and replace verified application images."

printf '%s\n' "Guarded GitHub production deploy workflow regression passed."
