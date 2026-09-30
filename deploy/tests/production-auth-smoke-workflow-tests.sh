#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
workflow="$root_dir/.github/workflows/production-auth-smoke.yml"

fail()
{
    printf '%s\n' "Production auth smoke workflow regression failed: $1" >&2
    exit 1
}

[ -f "$workflow" ] || fail "workflow is missing."

grep -Fq 'workflow_dispatch:' "$workflow" ||
    fail "workflow is not manual-dispatch only."
grep -Fq 'workflow_run:' "$workflow" ||
    fail "workflow is not connected to completed production deployments."
grep -Fq 'FullWorth Production Deploy' "$workflow" ||
    fail "workflow is not connected to the guarded production deploy workflow."
grep -Fq "github.event.workflow_run.conclusion == 'success'" "$workflow" ||
    fail "workflow does not require a successful deployment conclusion."
grep -Fq "github.event.workflow_run.head_branch == 'master'" "$workflow" ||
    fail "workflow does not restrict automatic runs to master deployments."
grep -Fq 'github.event.workflow_run.head_sha' "$workflow" ||
    fail "workflow does not check out the deployed commit."
grep -Fq 'environment: production' "$workflow" ||
    fail "workflow does not use the production environment gate."
grep -Fq 'contents: read' "$workflow" ||
    fail "workflow permissions are not read-only."
grep -Fq 'FULLWORTH_PRODUCTION_SMOKE_EMAIL' "$workflow" ||
    fail "workflow is missing the protected smoke email secret."
grep -Fq 'FULLWORTH_PRODUCTION_SMOKE_PASSWORD' "$workflow" ||
    fail "workflow is missing the protected smoke password secret."
grep -Fq 'umask 077' "$workflow" ||
    fail "workflow does not materialize the password under a private umask."
grep -Fq 'chmod 600' "$workflow" ||
    fail "workflow does not restrict the password file."
grep -Fq 'sh deploy/smoke-private-beta.sh' "$workflow" ||
    fail "workflow omits the bearer-token smoke harness."
grep -Fq 'sh deploy/smoke-web-bff.sh' "$workflow" ||
    fail "workflow omits the browser/BFF smoke harness."
grep -Fq 'https://api.fullworth.org' "$workflow" ||
    fail "workflow omits the canonical API origin."
grep -Fq 'https://fullworth.org' "$workflow" ||
    fail "workflow omits the canonical Web origin."
grep -Fq 'BILLWATCH_SMOKE_ADMIN_EXPECTATION: deny' "$workflow" ||
    fail "workflow does not require a non-admin smoke account."
grep -Fq 'BILLWATCH_SMOKE_ALLOW_MUTATIONS: false' "$workflow" ||
    fail "workflow does not disable API mutations."
grep -Fq 'rm -f --' "$workflow" ||
    fail "workflow does not remove the temporary password."

if grep -Eq '^[[:space:]]+(push|pull_request|schedule):' "$workflow"; then
    fail "workflow can run automatically against production."
fi

if grep -Fq 'set -x' "$workflow"; then
    fail "workflow enables shell tracing around protected credentials."
fi

printf '%s\n' "Production auth smoke workflow regression passed."
