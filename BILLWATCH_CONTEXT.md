# FullWorth Current Context (legacy filename retained)

Last updated: 2026-09-21

## FullWorth brand transition

- Product brand: **FullWorth**.
- Tagline/positioning: **Your entire financial life. One app.**
- The repository is now named `RealizmModz/FullWorth`. This file remains `BILLWATCH_CONTEXT.md` temporarily because current continuation tooling references that filename.
- Production compatibility identifiers such as `BILLWATCH_*` environment variables, `/opt/billwatch`, Data Protection application/purpose strings, persisted secure-storage keys, Stripe metadata keys, and current public domains remain unchanged until a dedicated guarded operations migration is completed.
- PR #113 promoted the recurring-discovery coverage fix to `master` as `1b2b52cd77f45143d4b741d77f1cb79f292f86c2`; that merge is **not** claimed as deployed production.
- PR #114 merged the approved FullWorth brand assets into `development` as `b51a49705d0cf062abba0ca9c3522f1d3c6994fe` after exact-head CI passed all three required jobs.
- The FullWorth product and repository identity migration is merged. Remaining `BILLWATCH_*` and `billwatch` infrastructure identifiers stay behind the guarded compatibility boundary described above.

## Authority / continuation rules

This is the durable FullWorth development handoff. Current source, exact-head CI, and verified production state win over this file for implementation/runtime truth.

- Stop immediately for compile/runtime/test/CI/deployment failures caused by current work, destructive migration risk, genuine security problems, or unresolved architecture uncertainty.
- Never weaken authentication, BFF isolation, antiforgery, HTTPS, ownership checks, trusted-proxy rules, token protection, statement protections, backup protections, migration safety, or financial-data boundaries to pass a check.
- Work in coherent slices. Do not merge a feature branch until its exact final head has passed the complete CI/container/recovery gate.
- Never deploy a feature branch directly to production. Use the guarded release path from a verified `master` commit.
- Prefer useful code or real acceptance work over repeated audits and synthetic acceptance artifacts.

## Product promise

**Your entire financial life. One app.**

FullWorth remains transaction-first while expanding into a broader financial-life hub. Bank transactions discover recurring bills. Provider statements/evidence explain why bills changed. AI may produce structured candidate facts, but deterministic code validates evidence, performs arithmetic, enforces ownership/security, compares history, and makes final persistence/alert decisions. AI output is never evidence by itself.

## Client platform direction

FullWorth is moving to a **PWA-first client architecture**.

- `FullWorth.Web` is the target single client for desktop browsers, mobile browsers, and installed home-screen/desktop web-app use.
- The MAUI client remains transitional until the installed PWA has verified feature parity for authentication, Plaid connection/update flows, transactions, bills, activity, statements, account security/settings, localization, and session-expiry behavior.
- Do not remove the MAUI project or its CI gate until that parity/cutover checkpoint is explicitly complete.
- PWA caching must preserve FullWorth financial-data boundaries. Authenticated HTML, BFF/API responses, financial data, statements, tokens, cookies, and account-specific content must never be intentionally persisted into Cache Storage for offline use.
- The initial service worker may cache only a generic public offline fallback; live FullWorth navigation remains network-first.
- If native app-store packaging is later required, prefer a thin web-oriented native wrapper after PWA parity rather than rebuilding a second product UI.

## Repository / stack

Repository: `RealizmModz/FullWorth`

Default/release branch: `master`
Active integration branch: `development`

### Current GitHub baseline

- `master`: `cbcf261e13636f0330cb9d7be2ce413871e413aa`
- `development`: `cbcf261e13636f0330cb9d7be2ce413871e413aa`
- PR #163 promoted the frozen development release to `master` as `cbcf261e13636f0330cb9d7be2ce413871e413aa`. Its exact promotion head `e8a512f62b188c24158abaec581e45217d3e9e58` passed FullWorth CI #644 across backend/tests, MAUI Android, and the Linux production-container/security/recovery gate before merge.
- `development` was then fast-forwarded to the verified master merge so both long-lived branches are synchronized at the same release baseline.
- PR #96 synchronized the Slack-compatible readiness-alert payload into `development` as `0253f08581417f9e41293481fccbcaf5da301ede`.
- PR #98 promoted the secure private-beta acceptance hardening to `master` as `3622b57c84c035c30a63bea070f53195635a62eb`. CI #534 passed all three required jobs on exact head `0253f085...`.
- PR #99 fixed HTML-encoded ASP.NET Core Identity confirmation-link parsing and merged into `development` as `847e17a20c97352114aafb7ef407da8a40882591`.
- PR #100 promoted that focused registration hotfix to `master` as `7824cc5f6ddb0231c986a793f15654d0314a8ad5`. Its exact PR head `847e17a...` passed backend build/tests, MAUI Android build, and Linux production container/recovery checks.
- No post-merge CI run is being claimed for merge commit `7824cc5...`; the verified automated gate is the exact PR #100 head.

Stack: .NET 10 MAUI + ASP.NET Core API + Blazor Interactive Server Web/BFF, PostgreSQL/EF Core, ASP.NET Core Identity bearer auth, encrypted HttpOnly Web/BFF auth, Plaid, xUnit, PdfPig, Tesseract, Docker Compose/Caddy/systemd, encrypted Restic recovery.

Public Web: `https://billbeacon.net`
Public API: `https://api.billbeacon.net`
Production path: `/opt/billwatch`

## Security invariants

- Plaid access tokens remain server-side/protected at rest.
- Web bearer/refresh tokens remain inside encrypted HttpOnly BFF state and are not intentionally exposed to browser JavaScript.
- External provider ID tokens/proofs must not be exposed to browser JavaScript.
- User financial resources and statements remain ownership-scoped; cross-user IDs normally return 404 where appropriate.
- Staff roles do not grant access to another user's financial evidence.
- Statement storage paths never leave the API; signature/type/size validation remains enforced.
- Financial/auth API and BFF responses remain no-store.
- Production requires persistent Data Protection keys, explicit statement storage, Plaid credentials, AllowedHosts, and trusted reverse-proxy configuration.
- Never log raw statements, full account numbers, auth/Plaid/provider tokens, passwords, recovery codes, provider/database/Restic secrets, or private operations webhooks.
- AI-derived persistence remains disabled; deterministic extraction remains production persistence.

## Current verified production state

The current explicitly verified guarded production deployment is:

`cbcf261e13636f0330cb9d7be2ce413871e413aa`

On 2026-09-21, the production host was updated to that exact verified `master` release and deployed only through `deploy/deploy-production.sh .env.production`.

- The guarded deployment completed successfully.
- The deployment created and verified encrypted Restic recovery snapshot `7449ac243947...` before replacing services. The full snapshot identifier remains an operator-side production artifact; do not infer or invent the omitted suffix in repository documentation.
- API, Web, database, and edge are healthy.
- The release marker and running image revisions match `cbcf261e13636f0330cb9d7be2ce413871e413aa`.
- Public API/Web readiness and HTTP security-boundary verification passed.
- Private-beta host readiness passed.
- Subscription enforcement remains safely disabled.
- The backup timer and runtime watchdog are active.
- Non-destructive direct-API smoke passed against the deployed release.
- Non-destructive authenticated Web/BFF smoke passed using a protected newline-normalized temporary credential.
- The deployed release does **not** contain the later Web-smoke newline-handling fix. A local-only VPS commit was reported as `f9000be`, but it is not pushed to GitHub, is not part of `master`, is not deployed, and must not be treated as source authority until its exact diff is reviewed and merged through the normal repository/CI path.

This verifies the guarded release, host prerequisites, non-destructive direct-API smoke, and non-destructive authenticated Web/BFF smoke. It does **not** yet prove objective cross-user isolation, the controlled Plaid lifecycle, representative statement semantics/OCR, hosted-link human completion, reboot behavior, external alert receipt, clean-host recovery against the real off-host repository, provider-enforced immutable storage, account-deletion evidence, or qualified legal review.

## Verified P0/private-beta code position

The repository contains regression-tested P0 work covering identity/BFF isolation, ownership, Plaid state handling, statement validation/processing, subscription rollout gating, backup/recovery, operations alerts, release integrity, controlled reboot evidence, and private-beta evidence correlation.

Important verified slices include:

- centralized Web antiforgery, security headers, no-store boundaries, sensitive endpoint authorization, and user-partitioned authenticated rate limits;
- Owner/Admin and access-key privilege boundaries with role-claim freshness;
- versioned private-beta Terms/Privacy acceptance across Web, MAUI, and direct API registration;
- account deletion reauthentication/2FA/staff-role protections, Plaid revoke-first behavior, crash-safe statement quarantine/reconciliation, owned-data erasure, and deployed-release disposable-account deletion proof support;
- server-side BFF access-token refresh with rotated-token persistence and fail-closed sign-out on refresh failure;
- objective cross-user Web/BFF ownership smoke using a second controlled identity and real foreign-owned resources;
- external sign-in support and BillWatch 2FA/recovery-code flows;
- Plaid `RequiresAttention` classification/persistence and ownership isolation;
- PDF/JPG/JPEG/PNG statement signature validation, upload/status/download ownership, terminal-state semantics, OCR coverage, and storage-path secrecy;
- guarded direct API, authenticated Web/BFF, Owner/Admin, access-key, Plaid, statement lifecycle, statement semantic-review, subscription lifecycle, and account-deletion smoke/proof harnesses;
- Internal Beta 0 release-pinned acceptance runner;
- release-pinned Plaid Hosted Link observation proof;
- encrypted Restic backup/restore verification and guarded clean-host recovery support;
- append-only routine backup boundary, runtime watchdog, release-integrity checks, metadata-only operations alerts, independent external readiness alerts, and controlled reboot pre/postflight proof.

## Private-beta smoke hardening

PR #86 added secure second-factor support to `deploy/smoke-private-beta.sh` without weakening its existing ownership/admin/mutation safeguards.

The harness supports either:

- `BILLWATCH_SMOKE_TWO_FACTOR_CODE_FILE`, or
- `BILLWATCH_SMOKE_RECOVERY_CODE_FILE`

Secret files must be regular, non-symlink files with mode `600`; both cannot be supplied simultaneously. Authentication failures are handled without printing tokens or secret values.

The deployed Web/BFF smoke harness on current `development` likewise supports TOTP/recovery-code files, encrypted cookie-session verification, authenticated BFF probes, export secret/storage-boundary checks, antiforgery issuance, and protected logout. The cross-user Web/BFF harness supports a separate foreign identity with its own protected second factor and proves foreign bill-stream/statement-upload access returns 404.

These capabilities are code/CI verified. They are **not** proof that the deployed production environment has been exercised with controlled accounts.

## Current machine-verifiable P0 position

The exact PR #163 promotion head `e8a512f62b188c24158abaec581e45217d3e9e58` passed the complete CI #644 gate, and guarded production deployment of master release `cbcf261e13636f0330cb9d7be2ce413871e413aa` completed successfully.

Non-destructive direct-API smoke and authenticated Web/BFF smoke have now also passed against that deployed release.

Most remaining private-beta P0 items are real-environment acceptance gates, not missing generic application code. Do not manufacture synthetic evidence.

In particular:

- objective cross-user Web/BFF ownership proof requires a second controlled identity plus a controlled foreign-owned statement/resource fixture;
- the Plaid lifecycle requires a controlled account with a suitable real/sandbox connection, and Hosted Link completion remains a human interaction gate;
- representative statement lifecycle/semantic/OCR acceptance requires explicit approval to upload a controlled fixture with operator-known facts;
- disposable account-deletion evidence must still be produced and matched to the deployed release;
- controlled reboot proof remains an explicit operator action;
- independent external alert receipt must be observed;
- clean-host restore must use the actual off-host repository;
- provider-enforced immutable/Object-Lock/WORM/equivalent protection must be configured and proven;
- qualified legal review of the exact Terms/Privacy version remains external evidence.

## Production/rollout rules

- Global subscription enforcement remains OFF until its separate rollout gate is deliberately approved.
- Staff roles do not grant access to another user's financial evidence.
- AI-derived persistence remains disabled.
- Startup EF migrations mean production remains one API instance until migration ownership is redesigned.
- Never run `docker compose down --volumes` against production.
- Beta Terms/Privacy are operational drafts, not qualified legal review.
- Guarded-deploy only a fully green `master` release through the normal release path.

## Remaining real-environment private-beta gates

Before trusted external beta invitations:

1. Run objective cross-user Web/BFF ownership proof with a second controlled identity and controlled foreign-owned resource/statement fixture against release `cbcf261e...`.
2. Run disposable account-deletion proof and feed same-release evidence into Internal Beta 0.
3. Exercise the controlled Plaid connect/update lifecycle with a suitable account and complete the Hosted Link human-interaction observation.
4. With explicit approval, upload controlled representative PDF/scanned-PDF/JPG/PNG fixtures and review extraction/OCR fields plus bill-change explanations against operator-known facts.
5. Run controlled reboot proof.
6. Run clean-host restore against the actual off-host repository.
7. Configure and prove provider-enforced immutable/protected backup recovery.
8. Run independent alert-observation proof and personally confirm intended destinations.
9. Combine same-release technical, alert, Plaid, recovery, and acceptance evidence.
10. Complete Internal Beta 0 on real controlled bills with explicit expected subscription state where known.
11. Obtain qualified review of the exact deployed Terms/Privacy version.
12. Run the trusted-beta launch evidence verifier only after every underlying real-world fact is genuinely complete.

## Immediate resume point

1. Guarded production deployment of `master` release `cbcf261e13636f0330cb9d7be2ce413871e413aa` completed successfully on 2026-09-21; API, Web, database, and edge are healthy and the release marker/image revisions match.
2. A verified encrypted pre-replacement recovery snapshot beginning `7449ac243947...` was created. Backup timer and runtime watchdog remain active.
3. Non-destructive direct-API smoke and authenticated Web/BFF smoke both passed against the deployed release.
4. The next highest-value acceptance gate is objective cross-user ownership proof using a second controlled identity and a controlled foreign-owned fixture, followed by controlled Plaid and statement-lifecycle observation.
5. The locally committed Web-smoke newline fix `f9000be` is not pushed, merged, or deployed. Review its exact diff and put it through the normal development PR + full CI path before relying on it.
6. Do not manufacture acceptance evidence. Human/provider/operator gates remain as listed above.
7. If acceptance exposes a concrete defect, stop progression, create a focused branch from current `development`, fix it, and require the full three-job CI gate before merge.
8. Preserve every security invariant above and all user-owned data ownership boundaries.
