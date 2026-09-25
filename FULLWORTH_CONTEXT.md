# FullWorth Current Context

Last updated: 2026-09-24

## MFA disable/session revocation checkpoint — 2026-09-25

PR #327 fixed replayed MFA enrollment: a repeated successful `two-factor/enable` request now returns 409 before mutation, preserving the recovery codes already issued and leaving the security stamp unchanged. Exact head `0e0359305122532af224e43c90fdb3fe8b93846c` passed dependency security #70 and full CI #958 before squash merge as `eb5cf04420f599694bbd96a00f9a1a0b3f14d459`.

PR #328 fixed the next confirmed MFA gap at the product boundary: successful Web/BFF disable now removes the current protected Web session and returns the browser to sign-in, while focused regression coverage proves post-enrollment refresh tokens are invalidated and invalid proof preserves MFA/session state. The first #328 implementation also added an explicit app-level SecurityStamp rotation. A concurrent proof PR (#329) then established on exact-head CI #963 that ASP.NET Core Identity's own `SetTwoFactorEnabledAsync` transition already rotates SecurityStamp and invalidates post-enrollment refresh tokens. PR #331 therefore removed FullWorth's redundant explicit stamp write and relies on the framework-owned atomic MFA-disable + stamp transition while preserving the Web signout and regression tests. Existing remote bearer access remains bounded by the normal 15-minute bearer lifetime; FullWorth does not claim instant remote bearer invalidation.

PR #328 initially exposed one Web compile error because the Settings page did not inject `NavigationManager`; the missing injection was fixed without changing security semantics. Corrected exact head `dab33016099419ba7732c24ed1c85bfb72decc47` passed dependency security #73 and full CI #961 across backend/tests, migration verification, MAUI gating, production API/Web containers, visual acceptance, HTTP security, encrypted backup and isolated recovery before squash merge as `8be4e68967632b505a421af1f845209ff5fb3d86`. PR #331 exact head `af0426ef3e4702ff95ca9842ab0be88315348d47` then passed dependency security #77 and full CI #965 before squash merge as `42209c96391db5675945768e508b605114548df1`, locking the framework-owned MFA-disable stamp behavior without a duplicate FullWorth write.

The MFA review remains open. Continue with setup/reset failure atomicity, then broader recovery semantics and enumeration/error behavior. Do not claim globally single-use TOTP verification or a complete MFA concurrency audit.

## MFA enrollment replay checkpoint — 2026-09-25

The enrollment audit reproduced a confirmed defect: replaying a successful `two-factor/enable` request silently replaced the recovery codes just issued and rotated revocation state again. The enable endpoint now rejects an already-enabled account with 409 after password verification and before further mutation. Intentional recovery-code replacement remains on the strongly reauthenticated regeneration endpoint. The Web explains how to obtain replacement codes, with Spanish localization.

Regression coverage checks that the original ten codes remain usable after a rejected replay, each is redeemable only once, the security stamp stays unchanged, and invalid enrollment proof neither enables MFA nor issues codes. This protects the completed enrollment transition; it does not claim globally single-use TOTP verification across operations or resolve every concurrent enrollment/reset transition.

Read the associated PR and exact-head CI for merge/validation evidence. Continue #291 with setup/reset failure atomicity, disabling behavior, and recovery/enumeration boundaries. No production deployment or complete MFA-audit claim is made.

## Architecture modularization checkpoint — 2026-09-24

FullWorth is a modular monolith with deny-by-default ownership boundaries and explicit contracts between modules.

Completed architecture checkpoints:
- #261 established project dependency airlocks and CI-enforced project graph rules.
- #263 established direct sibling service-module coupling enforcement.
- #264 introduced the provider-neutral bank synchronization airlock.
- #265–#279 incrementally moved Accounts, Bills, Plaid, and Statements cross-owner reads/writes behind owner contracts and drove the controller/service ownership ratchets to zero exceptions for the enforced domains.
- #281 removed the cross-domain database foreign key/navigation from Bills-owned alerts to Statements-owned bill changes while retaining opaque correlation metadata.
- #286 introduced the Bills-owned bank-transaction association bridge/backfill.
- #287 moved recurring-discovery/runtime behavior to the Bills-owned transaction association.
- #288 removed the legacy Plaid-owned `BankTransactions.BillStreamId` column/indexes/foreign key after the bridge and runtime cutover were proven.
- #289 extended service ownership enforcement to Subscriptions and routed Statements quarantine user-existence reads through Identity.
- #290 routed Admin user/role/subscription mutations through Identity/Subscriptions owner contracts and added Admin to the zero-exception service ownership ratchet.

Active architecture state:
- Issue #260 remains the modular-architecture tracker.
- The previously documented `BillAlerts -> BillChanges` and `BankTransactions.BillStreamId -> BillStreams` schema couplings are resolved by #281 and #286–#288; do not reopen them unless new evidence shows a regression.
- Continue ownership enforcement only module-by-module after inspecting the current source. Do not introduce broad allowances or perform a big-bang rewrite.

Architecture rules:
- Modules depend on contracts, not sibling implementations.
- Controllers and services must not access another module's private persistence directly where an owner contract exists.
- Shared scoped DbContext transactions are an explicit modular-monolith coordination mechanism. A future network split requires an outbox/coordinator or equivalent rather than silently losing atomicity.
- Sensitive provider credentials, statement storage identifiers, authentication material, and cross-user financial evidence must not leak through contracts, logs, export payloads, or exceptions.

Production remains unchanged by architecture/security merges unless a separate guarded production deployment is explicitly approved.

## Current migration/security checkpoint — 2026-09-24

This checkpoint supersedes older branch/domain summaries below. Current GitHub source and exact-head CI remain authoritative.

- Repository: `Fullworth/FullWorth`; release branch: `master`; integration branch: `development`.
- Current `development`: `42209c96391db5675945768e508b605114548df1` after PR #331 aligned MFA-disable revocation with ASP.NET Core Identity's framework-owned SecurityStamp transition while preserving current-Web-session signout.
- Current `master`: `a4d60bc25d680dfc3b786fb476e4e7f42f84eba1`. A GitHub branch head is not evidence of a production deployment.
- The last operator-reported live production release remains `81a74f11941f6ed67ba5de61b9ef186ef09bae3c`. No later architecture/security merge is being claimed as deployed.
- Production remains untouched unless a guarded deployment is separately and explicitly approved.

Security program:
- Issue #291 is the active ASVS-based defense-in-depth tracker. It is a hardening/verification program, not a claim of ASVS certification.
- PR #292 hardened the Web antiforgery cookie and added a commit-pinned pull-request dependency-review gate. GitHub CodeQL default setup is already enabled, and NuGet vulnerability warnings NU1901–NU1904 remain build-blocking.
- PR #297 split production networking into least-connectivity edge/API/Web/data/egress networks, made the data path internal-only, and proved read-only API/Web roots, bounded noexec/nosuid/nodev temporary storage, PID ceilings, HTTPS, statement handling, encrypted backup, isolated restore, and recovery.
- PR #298 confined the Caddy edge with a read-only root, `no-new-privileges`, all Linux capabilities dropped except `NET_BIND_SERVICE`, a 128-PID ceiling, and bounded noexec/nosuid/nodev temporary storage.
- PR #299 moved Web authentication tickets into a Data-Protection-protected distributed server-side store backed in production by an isolated, password-protected, non-persistent Redis service. The browser auth cookie now carries an opaque protected session reference rather than API access/refresh tokens.
- PR #300 explicitly set Identity bearer access-token lifetime to 15 minutes and refresh-token lifetime to 14 days, and added regression coverage for password security-stamp rotation.
- PR #302 ends the current server-side Web session immediately after a successful password change, preserves the session on rejected password changes, and returns the browser to sign-in.
- PR #305 locks the existing external OIDC security invariants with regression coverage for authorization-code flow, PKCE, HTTPS metadata, issuer/audience validation, provider-token non-persistence, response modes, and hardened nonce/correlation cookies.
- PR #306 revokes refresh sessions when an existing account adds or removes an external sign-in method.
- PR #307 revokes refresh sessions atomically with staff role promotion/demotion.
- PR #308 revokes refresh sessions atomically with two-factor recovery-code regeneration.
- PR #309 adds bounded single-use refresh-token families, replay rejection, PostgreSQL-backed concurrency control, and distributed Web/BFF refresh-race recovery. Review after merge found that its advisory lock did not serialize against ordinary Identity security-stamp writes.
- PR #310 replaces that advisory lock with a transaction-scoped `AspNetUsers` row lock and revalidates the current security stamp after the row is locked, closing the refresh/security-change race. Exact head `c11658733ba29820237e6e8501d026741894146b` passed dependency security and full CI #934 before squash merge as `ac722b0f5f68dc3699374ccc183e8df8654f106f`.
- PR #312 revokes the current enrolled refresh family on first-party Web/MAUI logout while preserving independent sessions. Exact head `79e25b1f24189030b4b0d8c2b96c520ef66545d5` passed dependency security and full CI #937 before squash merge as `2109bb8d1950aeab3229cf79c9d959d4c23eb9fb`.
- PR #313 adds strongly reauthenticated account-wide refresh revocation using ASP.NET Core Identity `SecurityStamp` rotation. Existing refresh tokens across sessions fail immediately; already-issued bearer access is still bounded by the explicit 15-minute lifetime. Exact head `b2947dcc24404005a620d096138b3bd48bf0f3df` passed dependency security and full CI #938 before squash merge as `8a338197139a1ad63094eab589b590060aff92e4`.
- PR #314 exposes account-wide revocation through an antiforgery-protected Web/BFF `Sign out everywhere` control, signs out the current Redis-backed Web session after a successful revocation, discloses the bounded 15-minute remote access-token window, and adds Spanish localization plus antiforgery coverage. Exact head `4722c7f9bd421cb6c3c91659ba17057cb0ae6354` passed dependency security and full CI #939 before squash merge as `489568293aa0af3deea1682f9f7ce2f2f2362cf0`; the generated desktop/mobile Settings screenshots were visually inspected and the new card rendered cleanly.
- The current token/session lifecycle checkpoint is complete: refresh replay is single-use/bounded, first-party logout revokes the current refresh family, account-wide revocation rotates Identity `SecurityStamp`, and the Web sign-out-everywhere flow removes the current server-side Web session on success. Already-issued remote bearer access remains bounded by the explicit 15-minute access-token lifetime; FullWorth does not claim instant remote bearer-token invalidation.
- PR #316 closes the first confirmed strong-reauthentication gap: Admin/Owner subscription access-key creation now requires the actor's current password and, when 2FA is enabled, a current authenticator code before any privilege-granting key is issued. Defensive access-key revocation intentionally remains friction-light for incident response. Exact head `f35648e4da969ce071b44cf4ddf9272cab55f095` passed dependency security and full CI #941 across backend/tests, MAUI gating, production-container/browser/security/recovery before squash merge as `c699ea86a9f8c18d6e3fbcf476b36631efcdc006`.
- PR #318 fixes the admin Web client regression exposed by #316: the three known strong-reauthentication 401 ProblemDetails responses now remain on the current admin page and surface as credential errors, while ordinary/unrecognized 401 responses still redirect to sign-in. Exact head `60749c1cb758b6fc92baf9e63758a97b3ae3c2e7` passed dependency security and full CI #943 across backend/tests, MAUI gating, production-container/browser/security/recovery before squash merge as `0f102e70c32ba2ac032cd4b72d9a4b002c018364`.
- PR #320 requires the acting Admin/Owner's current password and, when enabled, current authenticator code before assigning Admin or Moderator roles. Rejected reauthentication fails before mutation; role-removal DELETEs deliberately retain the existing friction-light incident-response path. The Web/BFF carries the credentials only for role grants and surfaces known credential errors on-page. Exact corrected head `d5505c1948c57f2eb6ad17f52ca1cba89662e8ca` passed dependency security and full CI #946 across backend/tests, MAUI gating, production-container/browser/security/recovery before squash merge as `e2eb0cceaf1cb7fbacbaddd791f235da490ff364`.
- PR #322 extends that strong reauthentication to subscription entitlement grants and program-membership activation. Entitlement revocation and program deactivation stay friction-light for incident response, and the Web clears entered password/2FA values after every mutation attempt. Exact head `65604f56fa61a688b94c068ffc5152c83801cc38` passed dependency security and full CI #948 across backend/tests, MAUI gating, production-container/browser/security/recovery before squash merge as `8984145aded753af228a8a6783128ac22080a9bc`.
- PR #324 hardens sensitive account export and completes the current strong-reauthentication audit. The API and Web/BFF export surfaces are POST-only, the Web path is antiforgery-protected, fresh account verification is required before export construction, and the previous GET path is regression-blocked. Existing ownership, no-store, rate-limit, and forbidden-secret/storage checks remain covered. Export-bearing smoke harnesses reject recovery-only mode before network use and keep sensitive request material out of process arguments. Exact head `ba5f89c266b8f6b76a10ab977bb70f9c2de3d25a` passed dependency security and full CI #955 before squash merge as `3d8187b4f81a44f2248bbeda66914260a5d0a721`. Generated desktop/mobile privacy screenshots were inspected; the default layout remained clean and responsive, while the automated capture did not open the expanded export-confirmation form.
- External OIDC uses authorization-code flow, PKCE, HTTPS metadata, issuer/audience validation, provider-token non-persistence, explicit provider response modes, and hardened nonce/correlation cookies; PR #305 regression-locks these invariants.
- Database runtime/migration privilege separation, stronger production secret injection, Data Protection key-at-rest/rotation design, parser-worker isolation, host/SSH/firewall hardening, security-event detection, SBOM/provenance, and immutable/off-host recovery proof remain open security work.

Platform/acceptance:
- Android physical installed-PWA acceptance remains open under issue #251. CI, Chromium, and emulator evidence do not satisfy that gate.
- iOS Add to Home Screen failure remains tracked separately in issue #258.
- No real-device, provider, backup-immutability, legal-review, production, or human acceptance evidence may be fabricated or inferred from CI.

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

Repository: `Fullworth/FullWorth`

Default/release branch: `master`
Active integration branch: `development`

### Current GitHub baseline

- `master`: `a4d60bc25d680dfc3b786fb476e4e7f42f84eba1` (current GitHub release-branch head; this is not by itself production-deployment evidence).
- `development`: `2cb7a585323ca7be76360fcf1c5e7e0e80012c35` after PR #302 completed the password-change Web-session invalidation checkpoint with exact-head CI green.
- Latest code-bearing `development` merge before the later maintenance/handoff work: `d86c7ac09650a6605f9262ede22a339a3f25d757` (PR #197 Web/BFF protected-secret newline normalization; exact head `7e47db358c0fea0c6b7b4a1b5c5347ef99b68770`, full CI #700). Later handoff-only commits may advance the branch without changing product/runtime behavior.
- PR #163 promoted the frozen development release to `master` as `cbcf261e13636f0330cb9d7be2ce413871e413aa`. Its exact promotion head `e8a512f62b188c24158abaec581e45217d3e9e58` passed FullWorth CI #644 across backend/tests, MAUI Android, and the Linux production-container/security/recovery gate before merge.
- `development` was then fast-forwarded to the verified master merge so both long-lived branches are synchronized at the same release baseline.
- Since that release baseline, the FullWorth v2 consumer UI overhaul was completed on `development`. PRs #168–#171 finished Account/Settings, Bill Detail/Transactions, Privacy/Subscription, Profile, and final brand consistency. PR #173 added installed-PWA theme/chrome and remaining user-visible FullWorth filename polish and merged as `623ed33fc50f09b42e72b85daf34c049ca1dd2b7` after CI #655 passed. PR #174 added the forward-only idempotent repair migration for `AspNetUsers.TimestampDisplayMode` / `SubscriptionAccessKeys.Label`; exact head `b49b1b4392b5ee4fa840df2039d0cada85f1d46c` passed CI #656 before squash merge `91d4e2028504648c2f3f7be8489f0460f99c7c00`. PR #175 redesigned the public landing page; exact head `ae8d693b356e310ea5cd0604e6e0d5af716cde5c` passed CI #657 before merge `f16dcf6f5ae1ec6ca35e4aec8ecada1b12693d80`. PR #178 then promoted that verified development state to `master` as merge commit `19f83716a475c9ab5060a6681e06eb86dad62394`; that GitHub promotion is **not** evidence that production was redeployed. PRs #181–#186 subsequently added efficient docs-only CI detection, GitHub-rendered visual acceptance, and the concrete browser/mobile navigation fixes described above. PR #176 updated `Microsoft.NET.Test.Sdk` to 18.10.1 after a fresh rebase/exact-head CI pass; PR #177 updated `actions/cache` from v4 to v6 after a fresh rebase and full three-job CI pass. PR #188 then added the real-browser PWA/offline boundary proof described above. PR #189 added responsive-browser Back navigation acceptance. PR #191 fixed the BFF cookie-challenge/session-expiry defect found by Chromium acceptance and added production-cookie plus real-browser regression coverage. PR #193 added responsive-mobile security-dialog acceptance. PR #195 adopted the cost-aware validation cadence and disabled recurring production-readiness scheduling while preserving manual dispatch. PR #196 added the controlled user-visible PWA update flow described above. PR #197 independently closed the repository-visible Web/BFF smoke newline-handling defect: protected password/authenticator/recovery files are normalized into protected one-line temporary copies, ordinary LF/CRLF endings are not submitted as part of the secret, empty or multi-line files fail closed, and regression coverage now detects the behavior. Exact head `7e47db358c0fea0c6b7b4a1b5c5347ef99b68770` passed full CI #700 before squash merge `d86c7ac09650a6605f9262ede22a339a3f25d757`. That commit remains a historical code-bearing baseline; current branch heads are recorded above.
- PR #96 synchronized the Slack-compatible readiness-alert payload into `development` as `0253f08581417f9e41293481fccbcaf5da301ede`.
- PR #98 promoted the secure private-beta acceptance hardening to `master` as `3622b57c84c035c30a63bea070f53195635a62eb`. CI #534 passed all three required jobs on exact head `0253f085...`.
- PR #99 fixed HTML-encoded ASP.NET Core Identity confirmation-link parsing and merged into `development` as `847e17a20c97352114aafb7ef407da8a40882591`.
- PR #100 promoted that focused registration hotfix to `master` as `7824cc5f6ddb0231c986a793f15654d0314a8ad5`. Its exact PR head `847e17a...` passed backend build/tests, MAUI Android build, and Linux production container/recovery checks.
- No post-merge CI run is being claimed for merge commit `7824cc5...`; the verified automated gate is the exact PR #100 head.

Stack: .NET 10 MAUI + ASP.NET Core API + Blazor Interactive Server Web/BFF, PostgreSQL/EF Core, ASP.NET Core Identity bearer auth, opaque HttpOnly Web session references with Data-Protection-protected server-side Redis auth tickets, Plaid, xUnit, PdfPig, Tesseract, Docker Compose/Caddy/systemd, encrypted Restic recovery.

Public Web: `https://fullworth.org`
Public API: `https://api.fullworth.org`
Legacy compatibility aliases: `https://billbeacon.net` and `https://api.billbeacon.net`
Production path: `/opt/billwatch`

## Security invariants

- Plaid access tokens remain server-side/protected at rest.
- Web bearer/refresh tokens remain server-side inside Data-Protection-protected distributed authentication tickets. The browser receives only an opaque protected HttpOnly session reference; bearer/refresh material is not intentionally exposed to browser JavaScript.
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

1. Read current GitHub `development`, open PRs, issue #291, issue #260, and this checkpoint before making changes.
2. PRs #305–#314 are merged. External OIDC invariants are regression-locked; security-changing actions rotate revocation state; refresh tokens are single-use within bounded families; current-session logout revokes its refresh family; account-wide revocation invalidates every existing refresh token; and the Web exposes a strongly reauthenticated sign-out-everywhere control with accurate 15-minute bearer-token semantics.
3. Continue security issue #291 in small reviewable slices. The current strong-reauthentication audit is complete through PR #324. MFA enrollment replay is covered by PR #327; MFA-disable revocation/Web signout is covered by #328, with #329 proving the framework stamp behavior and #331 removing the redundant app-level write. Continue the remaining MFA review with setup/reset failure atomicity first, then recovery semantics and enumeration/error behavior; inspect current source and patch only confirmed gaps.
4. Do not reopen or replace the framework Identity bearer-token/bounded refresh-family design without new evidence. Preserve the completed session-revocation semantics while auditing strong reauthentication.
5. Issue #260 remains open for remaining bounded-domain ownership enforcement. Inspect current source before choosing the next domain; do not restore already-removed `BillAlerts -> BillChanges` or `BankTransactions.BillStreamId` schema coupling.
6. The last operator-reported live production release remains `81a74f11941f6ed67ba5de61b9ef186ef09bae3c`. Do not claim newer GitHub code is deployed without guarded deployment evidence.
7. Physical Android installed-PWA acceptance under #251 remains required. Browser/Chromium/emulator evidence is not a substitute. iOS issue #258 remains separate.
8. Preserve authentication, server-side BFF sessions, antiforgery, HTTPS, ownership, provider-token, statement-storage, network isolation, container confinement, backup/recovery, migration, and financial-data boundaries. Never weaken them to make a build or architecture check pass.
