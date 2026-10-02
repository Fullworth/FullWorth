# Security incident response and credential rotation

This runbook covers suspected account takeover, leaked credentials, malicious or
unexpected authorization activity, provider compromise, and production-host or
release-integrity incidents. It complements the recovery and operations
procedures in `deploy/README-OPERATIONS.md`; it does not replace provider-specific
incident procedures.

Never paste secrets, tokens, raw statements, full account numbers, private
customer data, production environment files, or unredacted logs into chat,
issues, pull requests, alerts, or incident evidence.

## Authority and severity

The incident commander owns containment decisions and the evidence timeline.
Use the highest applicable severity:

- **SEV-1:** confirmed production-host, signing-key, database, backup-maintenance,
  Owner account, or broad customer-data compromise; active destructive behavior;
  or release-integrity failure.
- **SEV-2:** confirmed single-user account takeover, exposed limited-scope
  provider credential, repeated ownership denials, or material service abuse
  without evidence of broad compromise.
- **SEV-3:** suspicious security alerts or configuration drift that has not been
  confirmed as compromise.

If scope is unclear, start at the higher severity. Record UTC times, the incident
commander, affected systems, and decisions. Do not record secret values.

## First 15 minutes

1. Open a private incident record and assign an incident commander.
2. Record the current verified release SHA, affected host/service names, alert
   IDs, first/last observed UTC times, and sanitized request IDs where they help
   correlate existing logs.
3. Preserve relevant metadata before restarting or rotating anything. Do not
   export raw statements, request bodies, credentials, or customer financial
   values into the incident record.
4. If release integrity or the production host is suspect, stop all deployment
   activity. Do not promote, rebuild on the host, or change the verified release
   marker.
5. If destructive activity is active, restrict public ingress at the provider
   or edge layer while keeping PostgreSQL, Redis, backups, and evidence intact.
   Do not delete containers, volumes, logs, or backup snapshots.
6. Identify the smallest containment action that stops the threat: revoke an
   affected session, disable a provider integration, revoke a provider key, or
   isolate the host. Do not rotate unrelated credentials blindly.

## Evidence handling

Collect metadata only:

- exact verified release SHA and running image IDs;
- GitHub workflow/run URLs and attestation verification result;
- container/service health, restart counts, and sanitized security event IDs;
- firewall rule metadata and provider audit-event identifiers;
- credential name, issuer, creation/revocation UTC times, and responsible
  operator—never the credential value;
- backup snapshot ID and verification result, without repository passwords.

Use the existing bounded `FullWorth.SecurityEvents` and
`FullWorth.SecurityAlerts` streams. Preserve their fixed event IDs and safe
dimensions. Do not enable request-body, token, raw URL/query, statement, or
financial-value logging during an incident.

## Session containment

### One affected user

Have the user open Settings and use **Sign out everywhere** after confirming the
account is under their control. The same guarded path is
`POST /api/account/security/sessions/revoke-all`; do not invoke it with
credentials or bearer tokens in shell arguments, tickets, or incident notes.

Successful revocation rotates the ASP.NET Core Identity `SecurityStamp`.
Existing refresh tokens across sessions fail immediately. The current Web
session signs out immediately. Already-issued bearer access tokens are
self-contained and can remain usable for no more than their existing 15-minute
lifetime; FullWorth does not claim instant bearer-token revocation.

If the user cannot safely authenticate, contain at the identity/email provider
and restrict the account's privileged roles or entitlements through the
existing defensive removal paths. Do not invent a database update or edit
Identity tables directly.

### Broad identity compromise

1. Disable the affected external provider or email integration by setting its
   enablement/configuration to the fail-closed state in the protected production
   environment.
2. Rotate the exposed provider credential using the sequence below.
3. Deploy only an exact, verified `master` release through
   `.github/workflows/production-deploy.yml`.
4. Verify authentication, authorization, security alerts, and public readiness.
5. Treat the existing 15-minute bearer lifetime as the maximum residual window
   unless evidence proves the signing/Data Protection boundary was compromised.
6. If signing or Data Protection keys may be compromised, keep the service
   isolated and move to clean-host recovery. Do not destroy the old key material
   until required evidence and encrypted recovery access are preserved.

## Credential rotation sequence

For every rotatable credential:

1. Identify the exact credential, consumers, privilege scope, and issuer.
2. Create a replacement at the provider with equal or narrower privileges.
3. Store it only in the protected mode-`600` production environment or the
   provider's approved secret store. Do not place it in GitHub, command-line
   arguments, shell history, or logs.
4. Run `sh deploy/validate-production-env.sh .env.production`.
5. Deploy the exact approved `master` SHA through the protected
   `production` environment and `.github/workflows/production-deploy.yml`.
   Do not deploy a feature branch or bypass the guarded release path.
6. Run `sh deploy/verify-production.sh /opt/billwatch` and
   `sh deploy/verify-beta-readiness.sh /opt/billwatch`.
7. Exercise the affected integration using its bounded smoke/verification path.
8. Revoke the old credential at the issuer only after the replacement is proven.
9. Record only credential metadata and verification results.

### Rotation matrix

| Credential | Protected setting | Required follow-up |
| --- | --- | --- |
| PostgreSQL application password | `BILLWATCH_DATABASE_PASSWORD` | Coordinate the database role change and protected environment update so no old password is left active; verify migrations/readiness without publishing PostgreSQL. |
| Web-session Redis password | `BILLWATCH_WEB_SESSION_REDIS_PASSWORD` | Expect existing Web sessions to be disrupted; verify Redis remains internal-only and new sessions work. |
| Parser-worker shared token | `BILLWATCH_PARSER_AUTH_TOKEN` | Deploy API and parser worker together; verify the isolated parser/OCR path and containment proof. |
| Plaid application secret | `PLAID_SECRET` | Verify Link/token exchange and connection health using sandbox or a controlled account; never log access tokens. |
| Stripe API key | `STRIPE_SECRET_KEY` | Prefer a least-privilege restricted key; verify controlled Checkout/Portal calls before revoking the old key. |
| Stripe webhook secret | `STRIPE_WEBHOOK_SECRET` | Support provider overlap when available, verify signed delivery, then remove the old signing secret. |
| Resend API key | `RESEND_API_KEY` | Verify a controlled account-security email and ensure public responses remain enumeration-safe. |
| Google client secret | `FULLWORTH_GOOGLE_CLIENT_SECRET` | Follow `EXTERNAL_AUTH_SETUP.md` and verify controlled sign-in/linking before revocation. |
| Apple client-secret JWT | `FULLWORTH_APPLE_CLIENT_SECRET` | Generate a new bounded-lifetime JWT from the protected key and complete the Apple verification sequence before expiry/revocation. |
| Operations webhook | `BILLWATCH_OPERATIONS_ALERT_WEBHOOK_URL` | Run the metadata-only test event and confirm external receipt before removing the old endpoint. |
| Backup storage access keys | backend-specific protected settings | Rotate from the provider without granting delete/overwrite rights to the production append-only principal; verify a new snapshot. |
| Restic repository password/key | `RESTIC_PASSWORD` | Do not replace it as a plain environment edit. Use Restic key-management from the trusted maintenance/recovery host and prove clean-host restore before removing an old key. |
| GitHub production SSH key | protected GitHub `production` environment | Install and test a new dedicated non-root deploy key, then remove the old authorized key; do not copy application secrets into GitHub. |

Database, Redis, parser, and provider credential changes can cause downtime or
lost sessions if issuer and consumer changes are ordered incorrectly. Prepare a
rollback using the still-valid old credential, but revoke it promptly after the
new path is proven.

## Host or release-integrity compromise

1. Freeze deployments and preserve the last verified release marker.
2. Revoke the production deploy SSH key and restrict SSH/provider firewall
   access.
3. Rotate provider credentials from a separate trusted device; assume secrets
   present on the host are exposed.
4. Do not trust host-built application images or host-local source.
5. Recover on a separate clean host from an exact reviewed release and verified
   GitHub attestations. Follow the clean-host recovery drill in
   `deploy/README-OPERATIONS.md`.
6. Restore only from a verified encrypted off-host snapshot. Prove database,
   statement-file, migration, and Data Protection reconciliation before public
   traffic is enabled.
7. Run production exposure, parser containment, readiness, security-boundary,
   authentication, and alert-delivery verification before acceptance.
8. Production acceptance requires evidence from the recovered deployed release;
   CI evidence alone is insufficient.

## Closure gate

Do not close the incident until all applicable items are recorded:

- containment and eradication actions with UTC times;
- exact deployed release and image identity;
- affected credentials revoked and replacements verified;
- session-revocation action and residual 15-minute bearer window accounted for;
- backup snapshot and clean-host restore evidence when host integrity was in
  doubt;
- public readiness, exposure, security-boundary, parser-containment, and alert
  delivery results;
- scope of affected users/data, required notifications, and owner;
- follow-up issues with owners and deadlines;
- a sanitized timeline containing no secrets or customer financial data.

A passed CI run or a written runbook is not production evidence. Production
claims require direct evidence from the deployed release and relevant external
providers.
