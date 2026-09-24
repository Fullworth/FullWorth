# FullWorth Current Context

Last updated: 2026-09-24

## Architecture modularization checkpoint — 2026-09-24

FullWorth is being evolved as a modular monolith with deny-by-default ownership boundaries and explicit contracts between modules.

Completed checkpoints:
- #261 project dependency airlocks and CI-enforced project graph.
- #262 bounded Android emulator packaging/install/launch smoke. This is repository packaging proof only; it does not satisfy physical installed-PWA acceptance #251.
- #263 direct sibling service-module coupling ratchet.
- #264 provider-neutral bank synchronization airlock.
- #265 persistence ownership ratchet for finance service modules.
- #266–#274 incremental Plaid/Bills/Statements ownership extractions.
- #275 controller data-ownership ratchet.
- #276 Statements-owned Bill detail history read airlock.
- #277 Subscriptions-owned admin read airlock.
- #278 owner-specific account deletion airlocks. Controller data-ownership baseline is now zero exceptions.

Active:
- #279 extracts account export reads behind owner projections and expands service data-ownership enforcement to Accounts at a zero-exception baseline.
- #280 removes unnecessary MAUI Android builds for architecture-only documentation.
- #260 remains the architecture roadmap issue.

Architecture rules:
- Modules depend on contracts, not sibling implementations.
- Controllers must not read another module's private persistence directly.
- Accounts, Bills, Plaid, and Statements are moving toward zero direct cross-owner finance DbSet access.
- Shared scoped DbContext transactions are an explicit modular-monolith coordination mechanism; moving a boundary across a network later requires an outbox/coordinator or equivalent rather than silently losing atomicity.
- Sensitive provider credentials, statement storage identifiers, and cross-user financial evidence must not leak through contracts, logs, export payloads, or exceptions.

Remaining schema-level coupling under review:
- `BillAlerts.BillChangeId` still has a database foreign key into Statements-owned `BillChanges`; the intended next step is to keep the ID as an opaque correlation identifier while removing the cross-domain FK/navigation.
- `BankTransactions.BillStreamId` still points from Plaid-owned persistence into Bills-owned `BillStreams`; a Bills-owned association model remains a later migration candidate.

Production remains unchanged by this architecture work unless a separate guarded production deployment is explicitly approved.

## Current migration checkpoint — 2026-09-24

This checkpoint supersedes the older branch/domain summaries below; current GitHub source and exact-head CI remain authoritative.

- Repository: `RealizmModz/FullWorth`; release branch: `master`; integration branch: `development`.
- The last operator-reported live production release remains `81a74f11941f6ed67ba5de61b9ef186ef09bae3c`. No architecture merge listed below is being claimed as deployed.
- Production remains untouched unless a guarded deployment is separately and explicitly approved.
- PR #261 established project dependency airlocks. PR #263 added direct sibling service-module coupling enforcement with zero exceptions. PR #264 introduced the provider-neutral bank synchronization airlock.
- PR #265 established finance data-ownership enforcement. PR #266 moved Bills connection reads behind a Plaid-owned contract. PR #267 moved recurring-bill transaction discovery/link updates behind a Plaid-owned contract.
- PR #270 and PR #273 removed Statements direct reads of Bills-owned Bill Streams. PR #274 moved statement-driven Bill Alert persistence behind a Bills-owned reconciliation gateway; the Bills/Plaid/Statements service ownership baseline reached zero exceptions.
- PR #275 added controller/application data-ownership enforcement and routed statement-upload Bill Stream ownership through the Bills gateway.
- PR #276 moved Bill-detail statement/change history behind a Statements-owned read gateway.
- PR #277 moved admin access-key/subscription/program reads behind a Subscriptions-owned read gateway.
- PR #278 merged to `development` as `e0a6a59cf5ab6010fd2f03dd13c9a2f239f830dd` after exact-head CI #857 passed backend/tests, MAUI Android, and Linux production-container/security/backup/recovery. Account deletion now coordinates Plaid/Bills/Statements/Subscriptions owner contracts, and the controller data-ownership ratchet has zero exceptions.
- PR #279 is the active Accounts-service checkpoint. It moves account export reads behind Plaid/Bills/Statements export gateways and expands service ownership enforcement to Accounts at a zero-exception target. Initial stacked CI exposed and fixed a missing Accounts namespace import; final integration-base CI must still pass before merge.
- PR #280 is stacked behind #279 and adds `ARCHITECTURE.md` to the MAUI-safe documentation list. It exists only to avoid unrelated Android builds for future architecture-doc-only changes; it still requires its own final exact-head CI because it changes `ci.yml`.
- Issue #260 is the modular-architecture tracker. After #279/#280, the next persistence boundary is schema coupling: first evaluate/remove the `BillAlerts -> BillChanges` FK while preserving `BillChangeId` as an opaque correlation ID, then handle `BankTransactions.BillStreamId` with a proper Bills-owned association rather than merely dropping referential integrity.
- Android physical installed-PWA acceptance remains open under issue #251. CI, browser tests, and emulator proof do not satisfy that gate. A real Android tester may become available; use the release-pinned checklist against the exact deployed release.
- iOS Add to Home Screen failure remains tracked separately in issue #258.
- No real-device, Plaid/provider, backup-immutability, legal-review, production, or human acceptance evidence may be fabricated or inferred from CI.

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

- `master`: `5c8a75af702031d14652325939e3b3c03c5df639` (PR #247 maintenance promotion; exact promotion head `142eac4b0b380c81964da0f065c325bea951d994` passed FullWorth CI #818).
- `development`: `142eac4b0b380c81964da0f065c325bea951d994` after PR #249 merged the guarded reviewed-stale cleanup extension; exact PR head `2dbb8b5ec43a96bf7719fc7e136c340e52b5e300` passed FullWorth CI #817.
- Latest code-bearing `development` merge before the later maintenance/handoff work: `d86c7ac09650a6605f9262ede22a339a3f25d757` (PR #197 Web/BFF protected-secret newline normalization; exact head `7e47db358c0fea0c6b7b4a1b5c5347ef99b68770`, full CI #700). Later handoff-only commits may advance the branch without changing product/runtime behavior.
- PR #163 promoted the frozen development release to `master` as `cbcf261e13636f0330cb9d7be2ce413871e413aa`. Its exact promotion head `e8a512f62b188c24158abaec581e45217d3e9e58` passed FullWorth CI #644 across backend/tests, MAUI Android, and the Linux production-container/security/recovery gate before merge.
- `development` was then fast-forwarded to the verified master merge so both long-lived branches are synchronized at the same release baseline.
- Since that release baseline, the FullWorth v2 consumer UI overhaul was completed on `development`. PRs #168–#171 finished Account/Settings, Bill Detail/Transactions, Privacy/Subscription, Profile, and final brand consistency. PR #173 added installed-PWA theme/chrome and remaining user-visible FullWorth filename polish and merged as `623ed33fc50f09b42e72b85daf34c049ca1dd2b7` after CI #655 passed. PR #174 added the forward-only idempotent repair migration for `AspNetUsers.TimestampDisplayMode` / `SubscriptionAccessKeys.Label`; exact head `b49b1b4392b5ee4fa840df2039d0cada85f1d46c` passed CI #656 before squash merge `91d4e2028504648c2f3f7be8489f0460f99c7c00`. PR #175 redesigned the public landing page; exact head `ae8d693b356e310ea5cd0604e6e0d5af716cde5c` passed CI #657 before merge `f16dcf6f5ae1ec6ca35e4aec8ecada1b12693d80`. PR #178 then promoted that verified development state to `master` as merge commit `19f83716a475c9ab5060a6681e06eb86dad62394`; that GitHub promotion is **not** evidence that production was redeployed. PRs #181–#186 subsequently added efficient docs-only CI detection, GitHub-rendered visual acceptance, and the concrete browser/mobile navigation fixes described above. PR #176 updated `Microsoft.NET.Test.Sdk` to 18.10.1 after a fresh rebase/exact-head CI pass; PR #177 updated `actions/cache` from v4 to v6 after a fresh rebase and full three-job CI pass. PR #188 then added the real-browser PWA/offline boundary proof described above. PR #189 added responsive-browser Back navigation acceptance. PR #191 fixed the BFF cookie-challenge/session-expiry defect found by Chromium acceptance and added production-cookie plus real-browser regression coverage. PR #193 added responsive-mobile security-dialog acceptance. PR #195 adopted the cost-aware validation cadence and disabled recurring production-readiness scheduling while preserving manual dispatch. PR #196 added the controlled user-visible PWA update flow described above. PR #197 independently closed the repository-visible Web/BFF smoke newline-handling defect: protected password/authenticator/recovery files are normalized into protected one-line temporary copies, ordinary LF/CRLF endings are not submitted as part of the secret, empty or multi-line files fail closed, and regression coverage now detects the behavior. Exact head `7e47db358c0fea0c6b7b4a1b5c5347ef99b68770` passed full CI #700 before squash merge `d86c7ac09650a6605f9262ede22a339a3f25d757`. That commit remains a historical code-bearing baseline; current branch heads are recorded above.
- PR #96 synchronized the Slack-compatible readiness-alert payload into `development` as `0253f08581417f9e41293481fccbcaf5da301ede`.
- PR #98 promoted the secure private-beta acceptance hardening to `master` as `3622b57c84c035c30a63bea070f53195635a62eb`. CI #534 passed all three required jobs on exact head `0253f085...`.
- PR #99 fixed HTML-encoded ASP.NET Core Identity confirmation-link parsing and merged into `development` as `847e17a20c97352114aafb7ef407da8a40882591`.
- PR #100 promoted that focused registration hotfix to `master` as `7824cc5f6ddb0231c986a793f15654d0314a8ad5`. Its exact PR head `847e17a...` passed backend build/tests, MAUI Android build, and Linux production container/recovery checks.
- No post-merge CI run is being claimed for merge commit `7824cc5...`; the verified automated gate is the exact PR #100 head.

Stack: .NET 10 MAUI + ASP.NET Core API + Blazor Interactive Server Web/BFF, PostgreSQL/EF Core, ASP.NET Core Identity bearer auth, encrypted HttpOnly Web/BFF auth, Plaid, xUnit, PdfPig, Tesseract, Docker Compose/Caddy/systemd, encrypted Restic recovery.

Public Web: `https://fullworth.org`
Public API: `https://api.fullworth.org`
Legacy compatibility aliases: `https://billbeacon.net` and `https://api.billbeacon.net`
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

## Current production state

The current operator-reported live production release is:

`81a74f11941f6ed67ba5de61b9ef186ef09bae3c`

On 2026-09-22, the operator reported that this exact `master` release was live after the requested guarded deployment flow. The detailed deployment transcript for this release is not stored in this context. The stronger per-step guarded-deployment evidence listed below remains historical evidence from the earlier verified deployment and must not be silently attributed to `81a74f...`.

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
- external sign-in support and FullWorth 2FA/recovery-code flows;
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

1. Run objective cross-user Web/BFF ownership proof with a second controlled identity and controlled foreign-owned resource/statement fixture against deployed release `81a74f11941f6ed67ba5de61b9ef186ef09bae3c`.
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

1. Read current GitHub `development`, open PRs, issue #260, and this checkpoint before making changes.
2. PR #278 is merged into `development` as `e0a6a59cf5ab6010fd2f03dd13c9a2f239f830dd`; controller cross-owner DbSet exceptions are zero.
3. Finish PR #279 first: keep it based on current `development`, require exact-head backend/tests + MAUI gate + Linux production-container/security/backup/recovery green, then merge only if the final head remains unchanged.
4. After #279 merges, retarget PR #280 to the resulting `development`, require fresh exact-head CI, then merge the MAUI detector efficiency fix.
5. Update issue #260 as each checkpoint merges. Do not mark schema decoupling complete until the database relationships themselves are removed or replaced.
6. Next schema checkpoint: remove the Bills-owned `BillAlerts` foreign-key/navigation dependency on Statements-owned `BillChanges` while preserving nullable/indexed `BillChangeId` as correlation metadata and preserving the public alert API.
7. The harder remaining schema boundary is `BankTransaction.BillStreamId`; prefer a Bills-owned transaction-to-bill association model rather than simply dropping the FK.
8. The last operator-reported live production release remains `81a74f11941f6ed67ba5de61b9ef186ef09bae3c`. Do not claim newer GitHub code is deployed without guarded deployment evidence.
9. Physical Android installed-PWA acceptance under #251 remains required. Browser/Chromium/emulator evidence is not a substitute. iOS issue #258 remains separate.
10. Preserve authentication, BFF, antiforgery, HTTPS, ownership, provider-token, statement-storage, backup/recovery, migration, and financial-data boundaries. Never weaken them to make modularization pass.

