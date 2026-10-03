#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
runbook="$root_dir/SECURITY_INCIDENT_RESPONSE.md"
operations="$root_dir/deploy/README-OPERATIONS.md"
env_example="$root_dir/.env.production.example"
account_security="$root_dir/FullWorth.API/Controllers/AccountSecurityController.cs"

fail()
{
    printf '%s\n' "Security incident-response runbook test failed: $1" >&2
    exit 1
}

[ -f "$runbook" ] || fail "runbook is missing."
[ -f "$operations" ] || fail "operations guide is missing."
[ -f "$env_example" ] || fail "production environment example is missing."
[ -f "$account_security" ] || fail "account security controller is missing."

for heading in     '## Authority and severity'     '## First 15 minutes'     '## Evidence handling'     '## Session containment'     '## Credential rotation sequence'     '### Rotation matrix'     '## Host or release-integrity compromise'     '## Closure gate'
do
    grep -Fq "$heading" "$runbook" ||
        fail "required section is missing: $heading"
done

for setting in     BILLWATCH_DATABASE_PASSWORD     BILLWATCH_WEB_SESSION_REDIS_PASSWORD     BILLWATCH_PARSER_AUTH_TOKEN     PLAID_SECRET     STRIPE_SECRET_KEY     STRIPE_WEBHOOK_SECRET     RESEND_API_KEY     FULLWORTH_GOOGLE_CLIENT_SECRET     FULLWORTH_APPLE_CLIENT_SECRET     BILLWATCH_OPERATIONS_ALERT_WEBHOOK_URL     RESTIC_PASSWORD
do
    grep -Fq "$setting" "$env_example" ||
        fail "production environment no longer declares $setting."
    grep -Fq "$setting" "$runbook" ||
        fail "rotation matrix does not cover $setting."
done

for path in     deploy/validate-production-env.sh     deploy/verify-production.sh     deploy/verify-beta-readiness.sh     deploy/README-OPERATIONS.md     .github/workflows/production-deploy.yml     EXTERNAL_AUTH_SETUP.md
do
    [ -f "$root_dir/$path" ] ||
        fail "referenced operational path is missing: $path"
    grep -Fq "$path" "$runbook" ||
        fail "runbook does not reference $path."
done

grep -Fq 'Do not deploy a feature branch' "$runbook" ||
    fail "runbook does not prohibit feature-branch deployment."
grep -Fq 'CI evidence alone is insufficient' "$runbook" ||
    fail "runbook does not preserve the production-evidence boundary."
grep -Fq '15-minute' "$runbook" ||
    fail "runbook does not document the residual access-token window."
grep -Fq 'sessions/revoke-all' "$account_security" ||
    fail "documented account-wide session revocation route is missing."
grep -Fq 'UpdateSecurityStampAsync' "$account_security" ||
    fail "documented SecurityStamp revocation mechanism is missing."
grep -Fq 'SECURITY_INCIDENT_RESPONSE.md' "$operations" ||
    fail "operations guide does not link the incident runbook."

if grep -Eiq     '(paste|copy).*(secret|token|password).*(chat|issue|pull request|log)'     "$runbook"; then
    grep -Fq 'Never paste secrets' "$runbook" ||
        fail "runbook may instruct operators to expose credentials."
fi

printf '%s\n' 'Security incident-response runbook tests passed.'
