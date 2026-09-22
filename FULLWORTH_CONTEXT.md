# FullWorth Current Context

Last updated: 2026-09-21

## FullWorth brand transition

- Product brand: **FullWorth**.
- Tagline/positioning: **Your entire financial life. One app.**
- The authoritative continuation file is `FULLWORTH_CONTEXT.md`. `BILLWATCH_CONTEXT.md` is retained only as a temporary compatibility pointer for older project instructions.
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

## FullWorth v2 UI position

The consumer-facing v2 overhaul, installed-PWA polish, schema-drift repair, refreshed public landing experience, GitHub-rendered visual-acceptance fixes, browser PWA/offline proof, responsive-browser Back navigation proof, browser session-expiry hardening, browser security-dialog acceptance, and the user-visible PWA update path are current on `development` at `6db99dda0bc02fbb0440579da4924a82257ba786`.

Covered consumer surfaces:
- App shell/navigation and installed-PWA/mobile navigation.
- Overview / Financial Pulse.
- Bills and Bill Detail.
- Activity.
- Account and connected-bank surfaces.
- Transactions.
- Settings / account security.
- Data & Privacy.
- Subscription / plan & access.
- Profile.
- Public/auth brand consistency using the approved FullWorth mark.
- Installed-PWA launch/theme chrome polish and FullWorth user-visible export/statement filenames from PR #173.
- Forward-only repair migration for known `TimestampDisplayMode` / subscription-key label schema drift from PR #174.
- Broader FullWorth financial-hub public landing experience from PR #175.

`/app/admin` remains intentionally internal/staff-oriented and was not part of the consumer-fintech visual overhaul.

GitHub-rendered visual acceptance is now established for the public/auth experience plus the authenticated desktop and responsive-mobile browser surfaces.

- PR #182 added the production-like Playwright screenshot harness.
- PR #183 fixed broken public landing-page navigation anchors found in the first screenshot review.
- PR #184 expanded visual coverage and added public-anchor assertions.
- PR #185 fixed the duplicate mobile active-navigation state when the Menu sheet is open; exact head `8669734e21153836fd9f9681382fbaaf65fba805` passed CI #678 before merge.
- PR #186 made Menu represent secondary mobile routes such as Profile and Subscription; exact head `fd1f2c891cd3d376d51d2943ce4a550df6b9a6d8` passed CI #679 before merge. The resulting screenshots were visually checked and confirmed the intended active-state behavior.
- PR #188 added real Chromium service-worker/offline acceptance. Exact head `feda0a755c1f813ece70c07e554cf89873e217d8` proved that the FullWorth service worker registers with `updateViaCache=none` and that authenticated `/app` navigation falls back to the generic public offline page rather than cached financial UI when Chromium is forced offline. The Linux production-container/PWA/recovery job passed on the original successful attempt and then passed two additional same-commit reruns to check for flakiness.
- PR #189 added responsive-mobile browser history acceptance. Exact head `2607ab8d21b2166cb29caaf5d707b13aa8d04376` passed CI #687; Chromium navigates Overview → Bills → Activity through the real bottom nav, then browser Back twice must restore the prior URLs and active route states. This is browser-history proof, not Android/iOS hardware-back proof.
- PR #191 exposed and fixed a real browser session-expiry defect: unauthenticated cookie challenges for `/bff` could redirect `fetch()` to login HTML, preventing the BFF JavaScript from observing the expected 401. `/bff` cookie challenges now return 401 and access-denied responses return 403, while ordinary page requests keep login redirects. Exact head `703d3e474827f557550a45b9735c15ef4d5ab43c` passed full CI #691 and an additional same-head Linux production-container/PWA/recovery rerun; Chromium clears the authenticated cookie, invokes the real BFF module, and requires fail-closed navigation to `/login` with no authenticated app shell remaining.
- PR #193 added responsive-mobile Chromium acceptance for the real Change password and Change email security dialogs. Exact head `77b22b34ca757b8f92f7ba08f89c2edec2bed9f0` passed CI run `35687185230` across backend/tests, MAUI Android, and Linux production-container/PWA/recovery gates, then merged to `development` as `d11f528`. The acceptance verifies dialog titles, accessible descriptions, close behavior, and Escape dismissal; it does not replace installed-device validation.
- PR #196 added a user-visible installed-PWA update path. Review caught and fixed an activation-lifecycle defect before merge: the service worker still called `skipWaiting()` during install, which bypassed the waiting-worker state required by the Refresh-to-update flow. Exact final head `fc188d5bf4098aab7abf6693bd855a9c59655113` passed full CI #699 across backend/tests, MAUI gating, Chromium/production-container security checks, and recovery, then squash-merged to `development` as `6db99dda0bc02fbb0440579da4924a82257ba786`. The regression test now enforces the controlled `SKIP_WAITING` activation path. Real installed-device update acceptance remains open.

The remaining UI acceptance gate is real installed-device / interaction acceptance rather than another browser screenshot redesign:
- installed Android PWA;
- installed/added-to-home-screen iOS PWA where available;
- keyboard resize and Android/iOS hardware-back behavior;
- installed-PWA install/update behavior on real devices (the visible waiting-worker update path is code/CI verified by PR #196, but installed-device behavior is not yet proven);
- Plaid Hosted Link return;
- statement file picker;
- security dialogs in installed/mobile contexts (browser-dialog behavior is covered by PR #193; installed-device behavior remains open).

Do not claim those installed-device or human-interaction gates complete from Playwright screenshots alone.

## Cost-aware validation cadence

FullWorth is pre-revenue, so validation is risk-based rather than continuously exhaustive:
- ordinary documentation and low-risk changes use the cheapest relevant local or CI checks;
- UI-only changes use targeted browser acceptance when the affected Web surface changes, without automatically requiring unrelated backend or MAUI validation;
- authentication, ownership, database, payment, deployment, security, or workflow changes retain the full required CI gate;
- installed-device and full end-to-end acceptance are release-candidate or materially affected-change gates, not hourly activity;
- external production readiness monitoring is manual-only until beta/revenue operations justify recurring monitoring cost.

Existing browser and offline proofs should not be rerun without relevant source changes or new evidence.

## Repository / stack

Repository: `RealizmModz/FullWorth`

Default/release branch: `master`
Active integration branch: `development`

### Current GitHub baseline

- `master`: `19f83716a475c9ab5060a6681e06eb86dad62394`
- Latest code-bearing `development` merge: `d86c7ac09650a6605f9262ede22a339a3f25d757` (PR #197 Web/BFF protected-secret newline normalization; exact head `7e47db358c0fea0c6b7b4a1b5c5347ef99b68770`, full CI #700). Later handoff-only commits may advance the branch without changing product/runtime behavior.
- PR #163 promoted the frozen development release to `master` as `cbcf261e13636f0330cb9d7be2ce413871e413aa`. Its exact promotion head `e8a512f62b188c24158abaec581e45217d3e9e58` passed FullWorth CI #644 across backend/tests, MAUI Android, and the Linux production-container/security/recovery gate before merge.
- `development` was then fast-forwarded to the verified master merge so both long-lived branches are synchronized at the same release baseline.
- Since that release baseline, the FullWorth v2 consumer UI overhaul was completed on `development`. PRs #168–#171 finished Account/Settings, Bill Detail/Transactions, Privacy/Subscription, Profile, and final brand consistency. PR #173 added installed-PWA theme/chrome and remaining user-visible FullWorth filename polish and merged as `623ed33fc50f09b42e72b85daf34c049ca1dd2b7` after CI #655 passed. PR #174 added the forward-only idempotent repair migration for `AspNetUsers.TimestampDisplayMode` / `SubscriptionAccessKeys.Label`; exact head `b49b1b4392b5ee4fa840df2039d0cada85f1d46c` passed CI #656 before squash merge `91d4e2028504648c2f3f7be8489f0460f99c7c00`. PR #175 redesigned the public landing page; exact head `ae8d693b356e310ea5cd0604e6e0d5af716cde5c` passed CI #657 before merge `f16dcf6f5ae1ec6ca35e4aec8ecada1b12693d80`. PR #178 then promoted that verified development state to `master` as merge commit `19f83716a475c9ab5060a6681e06eb86dad62394`; that GitHub promotion is **not** evidence that production was redeployed. PRs #181–#186 subsequently added efficient docs-only CI detection, GitHub-rendered visual acceptance, and the concrete browser/mobile navigation fixes described above. PR #176 updated `Microsoft.NET.Test.Sdk` to 18.10.1 after a fresh rebase/exact-head CI pass; PR #177 updated `actions/cache` from v4 to v6 after a fresh rebase and full three-job CI pass. PR #188 then added the real-browser PWA/offline boundary proof described above. PR #189 added responsive-browser Back navigation acceptance. PR #191 fixed the BFF cookie-challenge/session-expiry defect found by Chromium acceptance and added production-cookie plus real-browser regression coverage. PR #193 added responsive-mobile security-dialog acceptance. PR #195 adopted the cost-aware validation cadence and disabled recurring production-readiness scheduling while preserving manual dispatch. PR #196 added the controlled user-visible PWA update flow described above. PR #197 independently closed the repository-visible Web/BFF smoke newline-handling defect: protected password/authenticator/recovery files are normalized into protected one-line temporary copies, ordinary LF/CRLF endings are not submitted as part of the secret, empty or multi-line files fail closed, and regression coverage now detects the behavior. Exact head `7e47db358c0fea0c6b7b4a1b5c5347ef99b68770` passed full CI #700 before squash merge `d86c7ac09650a6605f9262ede22a339a3f25d757`. Current `development` is `d86c7ac09650a6605f9262ede22a339a3f25d757`.
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
- Cookie-auth challenges under `/bff` return status codes (401/403) rather than login-page redirects so browser BFF clients can fail closed on expired/invalid sessions; normal page authentication redirects remain intact.
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
- The deployed release does **not** contain the repository Web/BFF protected-secret newline normalization later merged through PR #197. An earlier VPS-only commit reported as `f9000be` remains unreviewed historical local state and is not source authority; PR #197 is the reviewed repository implementation. Do not infer that either the PR #197 merge or that old local commit is deployed.

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

1. The latest code-bearing `development` merge is `024b95cdd9d2da7deb66cd835fa5099679d9f976` (PR #205; full CI #715). It contains the completed consumer v2 work, PRs #182–#186 for GitHub-rendered visual acceptance/navigation fixes, dependency maintenance from PRs #176/#177, PR #188's Chromium PWA/offline proof, PR #189's responsive-browser Back navigation proof, PR #191's BFF session-expiry fix/acceptance, PR #193's browser security-dialog acceptance, PR #195's cost-aware validation cadence, PR #196's controlled user-visible PWA update flow, PR #197's protected Web/BFF smoke secret-file newline normalization, and PR #205's recurring-bill merchant normalization, broader supported recurring categories, and aggregate discovery diagnostics.
2. Current GitHub `master` is `101eecbef215b912705a6926fd8234f4f42ec908` from PR #204. Production is still explicitly verified at `cbcf261e13636f0330cb9d7be2ce413871e413aa`; do not infer that the newer master or development state is deployed.
3. Desktop and responsive-mobile browser rendering has been visually reviewed through the CI screenshot artifacts. The broken public anchors, duplicate Menu/route highlight, and missing Menu current-state on secondary mobile routes were fixed through focused PRs with exact-head CI.
4. Chromium now also proves the service-worker/offline security boundary in the production-like CI stack: the worker registers, bypasses HTTP cache for updates, and an offline authenticated `/app` reload renders only the generic offline fallback. This exact-head Linux gate passed three times total on PR #188 (initial successful attempt plus two same-commit reruns).
5. Chromium also proves responsive-browser Back navigation and fail-closed session-expiry behavior. PR #191's final exact head passed full CI plus one extra same-head Linux production/PWA/recovery rerun.
6. Do **not** repeat the browser screenshot, Chromium offline-boundary, responsive-browser Back, or browser session-expiry audits unless relevant source changes or new evidence warrants it.
7. The next UI acceptance work is real installed-device / interaction validation: installed Android PWA, iOS home-screen PWA where available, keyboard resize, Android/iOS hardware-back behavior, real-device install/update behavior, Plaid Hosted Link return, and statement file picker. PR #196 provides the controlled waiting-worker Refresh-to-update path and passed full CI, but that is not a substitute for installed-device update acceptance. Browser security-dialog behavior is covered by PR #193; installed-device dialog behavior remains part of the device gate.
8. If that real-device acceptance exposes a concrete defect, stop promotion, create a focused branch from current `development`, fix it, and require exact-head CI before merge.
9. Do not promote the latest `development` state to `master` merely from GitHub browser evidence; complete the remaining real-device gates that materially require installed/browser interaction first.
10. The Web/BFF smoke newline-handling loose end is closed in repository authority by PR #197. The old VPS-only `f9000be` commit is not needed as implementation authority and must not be treated as deployed evidence.
11. The remaining real-environment private-beta gates in this document still apply; do not manufacture acceptance evidence.
12. Preserve every security invariant above and every user-owned data ownership boundary.
13. PR #205 is merged into `development` as `024b95c`. Its exact CI #715 passed Backend build/tests, MAUI Android build, and Linux production container. The merged code is deployment-ready for the guarded release path; after deployment, run one account refresh and inspect `RecurringCandidatesDetected`, `RecurringCandidatesRejected`, and `BillsDiscovered` before promoting further.
14. PR #204 promoted current `development` to `master` as `101eecb` with all three promotion checks passed. This is a release candidate, not production evidence. Use the guarded production deployment script only after explicit operator approval, then capture post-deploy refresh diagnostics and the required non-destructive smoke evidence.
