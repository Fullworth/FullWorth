# FullWorth Product & Engineering Roadmap

Last updated: 2026-10-06

Status: Active planning document

Completion notation: `~~strikethrough~~` means the roadmap item is completed to the level of evidence the item requires. Unstruck items remain open, partial, or awaiting real-environment acceptance.

Repository: `Fullworth/FullWorth`

Integration branch: `development`

Release branch: `master`

Primary product direction: **PWA-first**

Primary product promise: **Your entire financial life. One app.**

Strategic category: **Financial change intelligence**

Core bill-intelligence promise: **Know when your bills change — and why.**

Commercial objective: build toward a durable multi-million-dollar recurring-revenue business by earning trust, retention, and paid monitoring value rather than by maximizing feature count.

Durable product strategy: `FULLWORTH_PRODUCT_STRATEGY.md`

---

## 1. Purpose of this roadmap

This document is the durable, detailed development roadmap for FullWorth.

It is intentionally broader than a short TODO list. It defines:

- what FullWorth is trying to become;
- the order in which major product and engineering milestones should be completed;
- the security and data-integrity rules that must remain true while the product evolves;
- the concrete deliverables expected in each milestone;
- the tests and acceptance evidence required before a milestone is considered complete;
- the difference between repository-complete work and real-environment acceptance;
- the gates that must be satisfied before MAUI retirement, private beta, revenue activation, broader public release, and later scale work;
- which product ideas are committed near-term work versus later product-direction candidates.

This roadmap must not be treated as implementation truth when it disagrees with current source code, current exact-head CI, or verified production state.

### Authority order

When sources disagree, use this order:

1. Current source code.
2. Exact-head CI results for the branch/commit being considered.
3. Verified production evidence.
4. `FULLWORTH_CONTEXT.md` while that legacy filename remains required by continuation tooling.
5. This roadmap.
6. Older planning notes, issue text, stale PR descriptions, and historical chat summaries.

The roadmap should be updated when major milestones are completed, when product direction changes, or when a new technical constraint materially changes sequencing.

---

# 2. Product north star

FullWorth is becoming a trusted financial-life hub centered on automatic financial awareness rather than manual bookkeeping.

The long-term customer experience should answer, with as little manual work as possible:

- What changed in my financial life?
- Why did it change?
- How much does it cost me now?
- What will it cost me over time?
- What recurring commitments do I have?
- Which accounts or providers need my attention?
- What important financial events happened recently?
- What should I know before the next charge, renewal, due date, or account problem?
- Where did the supporting evidence come from?
- How confident is FullWorth in the explanation?

The product should feel like premium consumer fintech, not enterprise administration software.

## 2.1 Strategic category and commercial wedge

FullWorth's category is **financial change intelligence**.

The product should continuously:

1. detect meaningful financial changes;
2. explain why they happened;
3. quantify current and longer-term impact;
4. prioritize what deserves attention;
5. show the evidence supporting the conclusion.

Recurring bills remain the initial wedge because FullWorth can combine posted transaction facts, statement/provider evidence, deterministic comparison, and validated explanations into a concrete customer promise.

Expansion into planning, cash-flow context, subscriptions, liabilities, net worth, and other financial domains should strengthen this monitoring relationship rather than turn FullWorth into a generic feature-parity dashboard.

Competitive rule:

- do not chase every feature offered by all-in-one finance products;
- compete on earlier detection, stronger evidence, clearer explanation, lower false-alert rates, less manual work, and better trust;
- require a new module to improve trust, time to value, retention, monetization, acquisition efficiency, or operational leverage before treating it as strategic work.

The compounding product moat should come from:

- normalized recurring financial history;
- provider-specific bill/statement understanding;
- evidence-linked change history;
- deterministic financial validation;
- user-specific corrections and outcomes;
- high-quality evaluation and security regression coverage;
- reliable alerting, ingestion, recovery, and provenance.

The scale path is:

trustworthy monitoring
→ retained users
→ paid recurring value
→ repeatable acquisition
→ scalable recurring revenue.

Detailed positioning, monetization, growth, moat, and business-stage guidance lives in `FULLWORTH_PRODUCT_STRATEGY.md`.

### Experience principles

Prefer:

- immediate access to the most important financial change;
- large, readable amounts;
- clear monthly and annualized impact;
- concise explanation before detail;
- premium rounded surfaces;
- strong visual hierarchy;
- polished loading, empty, offline, and error states;
- mobile-first ergonomics;
- fast installed-PWA startup;
- accessible contrast and keyboard/touch behavior;
- dark/light theme support;
- evidence-backed explanations;
- truthful uncertainty.

Avoid:

- cluttered dashboards;
- admin-like forms as the main user experience;
- forcing users to manually categorize every transaction;
- presenting AI guesses as facts;
- hiding the evidence behind financial conclusions;
- fake controls or placeholder actions;
- financial arithmetic performed by AI when deterministic code can do it exactly;
- unnecessary duplication between Web/PWA and MAUI.

---

# 3. Non-negotiable technical and security rules

Every roadmap milestone inherits these rules.

## 3.1 Authentication and token isolation

- Plaid access tokens remain server-side and protected at rest.
- Web bearer and refresh tokens remain inside encrypted HttpOnly BFF state.
- Browser JavaScript must not intentionally receive bearer tokens, refresh tokens, Plaid access tokens, provider proof tokens, or external identity ID tokens.
- Protected client services must continue to obtain valid access through the existing authentication/token-refresh architecture rather than bypassing it.
- Authentication shortcuts must never be introduced merely to simplify development or testing.

## 3.2 Ownership isolation

Every user-owned database resource must remain ownership-scoped.

Preferred application check:

`UserId + resource ID`

For important database relationships, prefer ownership-enforcing composite relationships such as:

`(Id, UserId)`

Cross-user identifier manipulation should normally return 404 where appropriate rather than revealing that another user's object exists.

Staff/Admin/Owner roles must never imply permission to read another user's financial evidence.

## 3.3 Statement and document protection

- Physical statement paths must never be returned to clients.
- Uploads must continue to validate type, signature, size, and ownership.
- Raw statements and extracted text must not be logged.
- User-controlled paths must never be trusted.
- Storage changes must preserve deletion, quarantine, reconciliation, and recovery behavior.

## 3.4 PWA cache boundary

The service worker must not intentionally cache:

- authenticated HTML;
- BFF responses;
- API responses;
- transaction data;
- bill data;
- statements;
- tokens;
- cookies;
- account-specific content.

The safe baseline remains:

- generic public offline fallback may be cached;
- authenticated navigation remains network-first;
- financial data lives in server-backed application state, not browser offline caches.

## 3.5 AI boundary

AI may help with:

- candidate extraction;
- explanation drafting;
- evidence structuring;
- provider-document interpretation;
- ambiguity classification.

AI must not be the final authority for:

- ownership;
- money arithmetic;
- security decisions;
- persistence permission;
- historical comparison math;
- alert thresholds;
- factual evidence;
- access control.

A confident “we do not know yet” is preferable to an invented explanation.

The current AI direction is to evaluate existing, externally trained models through FullWorth's bounded server-side candidate-extraction contract. The first local evaluation candidate is Qwen3-4B Q4_K_M served by the pinned llama.cpp runtime. This is an evaluation choice, not production approval and not a claim of extraction quality. Existing deterministic code remains responsible for evidence validation, financial arithmetic, ownership, persistence, historical comparison, thresholds, and alerts. The earlier from-scratch model-training plan is superseded.

## 3.6 Production safety

Never:

- weaken HTTPS/proxy trust to pass a smoke test;
- bypass antiforgery;
- disable ownership checks;
- expose secrets in logs;
- deploy a feature branch directly to production;
- run destructive volume-removal commands against production;
- claim a security/recovery feature exists without proof;
- treat passing unit tests as proof of real provider behavior.

---

# 4. Current development snapshot

This section is the current release-readiness snapshot. It must be refreshed when a major security/release milestone lands, when the live production release changes, or when acceptance evidence materially changes.

## 4.1 Branch and release position

As of 2026-10-06:

- The `master` release branch at candidate `f401a591a8abdade557827c09412dc3166fb9de2` was promoted by PR #704; it is not a deployed release.
- The candidate's exact PR FullWorth CI #1679, Dependency Security #771, master FullWorth CI #1680, Repository Governance #18, CodeQL #58, and attested artifact checks passed. Artifact `fullworth-production-image-artifacts-f401a591a8abdade557827c09412dc3166fb9de2` (ID `11374998766`, digest `sha256:0f24594dc289350487574f757956c7cd325c68b3a07e2d58bd4ce5f411a7052b`) expires 2026-10-12.
- Current `development` head is `94ed452e721a871645b0b1beca34403e6ec75e08`, the merge commit for PR #709. PR #709 added an explicit production-host confirmation gate; exact-head FullWorth CI #1701 and Dependency Security #792 passed before merge. PR #707 remains open with the recovery-verifier passfile change; its earlier exact-head checks passed, but it does not establish remote R2 access or production recovery. No deployment occurred.
- Guarded production deploy run #15 for the candidate failed closed before candidate startup. Its partial API/Web/edge report conflicts with follow-up read-only inventory of the OVH target: OVH 40.160.137.55 read-only inventory found zero Docker containers, zero Docker volumes, and a missing release marker; this conflicts with run #15's partial-runtime report.
- Remote R2 recovery returned AccessDenied with the host's configured backup credential; a separate read-only recovery credential is required. PR #708 fixes the verifier secret wiring but does not grant R2 access. Do not broaden the production backup credential or put secret values in chat, source control, command history, or logs.
- The copied backup tree on the OVH host has object-level parity only. It is not an off-host copy, verified encrypted Restic repository, or successful clean-host recovery.
- The candidate is not deployed. The currently verified live production release remains `7e8571a26447538db249c862ad009487cce119bc`. Do not start the public stack against an empty database: the OVH inspection found no Docker volumes and no release marker.
- Before deployment, reconcile which SSH target the guarded workflow addresses, install the separate read-only recovery credential through the approved secret-handling path, and complete isolated encrypted-repository integrity and restore checks. Keep the conflicting host observations unresolved until verified against the actual workflow target.
- Issue #291 remains 61/65, with four real-evidence/governance gates: provider-enforced immutable/off-host storage, compromised-host clean restore, independent-review policy, and exact deployed-release evidence.
### Release-readiness estimate

The candidate has repository/CI release evidence and an attested artifact, but guarded deployment failed before candidate startup. Production deployment and same-release acceptance are incomplete. Public-release readiness remains blocked by the live host state and the remaining provider, recovery, device, legal, and governance evidence—not by the candidate's CI status.

### Security hardening program — issue #291

Issue #291 currently records **61 completed checks and 4 open checks**.

Major completed areas include:

- server-side BFF session/token isolation;
- bounded refresh-token families, logout/account-wide revocation, and security-stamp invalidation;
- strong reauthentication for destructive/security-sensitive operations;
- OIDC state/nonce/PKCE/correlation-cookie invariants;
- response-enumeration hardening;
- ownership and module-boundary ratchets;
- route-level and model-wide negative cross-user ownership coverage;
- model-wide same-user composite foreign-key enforcement for `UserId`-scoped relationships;
- endpoint inventory and cost-aware rate limits;
- body/header/request-line/content-type boundaries;
- Fetch Metadata / Origin CSRF defense in depth;
- CSP nonce/static-style hardening;
- statement download/export cache and disposition boundaries;
- parser/OCR pre-admission bounds;
- parser-worker TLS, authentication, replay resistance, and request integrity;
- per-document and per-image cgroup CPU/memory/PID containment;
- malicious/corrupt PDF/PNG/JPEG regression corpus;
- Data Protection key permission/rotation/recovery hardening;
- least-scoped file-backed production secret injection and live secret non-disclosure verification;
- future non-local PostgreSQL certificate-verified TLS requirements;
- dedicated least-privilege `fullworth_runtime` PostgreSQL role and one-shot migration/runtime credential separation;
- SSH/operator hardening and host/kernel/container patch-cadence verifier/runbook;
- immutable/pinned Actions and container-image references;
- signed build provenance and release SBOMs;
- bounded security-event logging, aggregation, and incident-response runbooks;
- encrypted backup/restore verification and restored-asset permission checks.

The four remaining #291 items are now evidence/governance gates rather than ordinary feature-code work:

- complete immutable/off-host storage proof before relying on backups for destructive incidents;
- exercise compromised-host recovery, not only ordinary failure recovery;
- finish the branch-governance decision for independent approving reviews where GitHub permissions allow;
- make no production-security claim without direct evidence from the exact deployed release.

Repository ruleset `FullWorth protected branches` is active for both `master` and `development`, requires pull requests, blocks deletion/non-fast-forward updates, requires review-thread resolution, has no bypass actors, and reports this connection cannot bypass it. Its current required approving review count is **0**, so the branch-governance checklist item remains open until that policy decision is deliberately completed through an admin-capable GitHub path.

### Document parser and OCR containment

The repository implementation now has hard OS-enforced document-processing containment:

- isolated parser worker;
- authenticated/TLS-protected worker boundary;
- parser request integrity and replay protection;
- bounded PDF/OCR input and work budgets;
- finite container resources;
- per-document and per-image cgroups;
- 384 MiB child memory ceilings;
- zero child swap;
- bounded PIDs;
- OOM-kill and cleanup/readiness regression proof;
- native/parser attack-surface documentation;
- generated malicious/corrupt document regression coverage.

This code is repository/CI evidence. **Production containment acceptance remains open until an exact-`master` guarded deployment runs the host containment verifier on the real production host and preserves sanitized deployed-host evidence.**

### Installed PWA acceptance

Issue #251 remains open. The required Android installed-PWA checklist currently has **10 unrecorded checks**.

Browser, Playwright, emulator, and CI evidence do not replace:

- real Android installed-PWA launch;
- persisted personalization/accessibility behavior;
- on-screen keyboard/viewport behavior;
- mobile/back navigation;
- installed update lifecycle;
- statement picker return;
- security dialogs in the installed context.

iOS remains optional for the current #251 bundle, while issue #258 separately tracks re-testing Safari Add to Home Screen on the current production release.

### External provider and real-world acceptance

Still open where applicable:

- real Google/Apple provider callback/registration/sign-in/linking/account-isolation acceptance;
- controlled Plaid connect/update/reconnect plus Hosted Link human-return observation;
- real-world Plaid payroll/sandbox acceptance for Paycheck Bill Plan issue #293;
- representative controlled PDF/scanned-PDF/JPG/PNG statement lifecycle and semantic/OCR review;
- external alert receipt;
- account-deletion same-release proof;
- controlled reboot proof;
- clean-host recovery against the actual off-host encrypted repository;
- provider-enforced immutable/WORM/Object-Lock-equivalent backup protection.

### Human-only launch gates

`HUMAN-TODO.md` currently identifies launch work that repository automation cannot safely complete:

- required CI/security status checks in the existing `master`/`development` protected-branch ruleset;
- qualified commercial legal/license review;
- the intentional production-release decision needed to collect deployed-host containment evidence.

## 4.3 Recently completed engineering position

The recent development cycle materially advanced release safety rather than adding speculative product surface.

Important completed slices include:

- FullWorth v2 consumer Web/PWA experience, responsive/mobile navigation, public landing experience, accessibility/readability preferences, offline/public fallback behavior, controlled PWA update flow, and browser-session expiry handling;
- protected Web/BFF sessions with server-side Redis-backed tickets and opaque HttpOnly browser references;
- authentication/session revocation, MFA/recovery, OIDC, strong-reauthentication, and anti-enumeration hardening;
- ownership/module boundary enforcement with zero direct controller cross-owner persistence shortcuts in enforced domains;
- Paycheck Bill Plan implementation through posted-payroll-triggered alerts and Web UX; only real-world Plaid payroll acceptance remains open in issue #293;
- request-cost classification, API/BFF rate limits, request-size/content-type/framing controls, CSP/CSRF defense-in-depth, and sensitive-response no-store protections;
- Stripe webhook replay protection and minimal event-receipt persistence;
- parser/OCR process isolation and OS-enforced resource containment;
- statement/document malicious-input regression corpus;
- Data Protection key lifecycle and restore-permission hardening;
- security-event telemetry, bounded alert aggregation, and incident-response/credential-rotation runbooks;
- pinned GitHub Actions and production/recovery container inputs;
- release SBOM generation and signed build provenance;
- encrypted recovery and restored-asset ownership/permission verification.

AI-derived persistence remains disabled. Deterministic extraction remains the production persistence authority until AI runtime, held-out quality, fallback, resource, rollback, and product-approval gates are all explicitly satisfied.

## 4.4 Production state

The current verified live production release is:

`7e8571a26447538db249c862ad009487cce119bc`

It was proven by successful guarded production deploy run #7 (`36221860082`).

That evidence establishes the release actually deployed through the guarded path. It does **not** silently extend to newer code on `master` or `development`.

The current release/acceptance model must continue to distinguish:

- repository implementation complete;
- exact-head CI/security complete;
- promotion to `master` complete;
- guarded deployment complete;
- deployed-host verification complete;
- provider/device/human acceptance complete;
- private/internal beta complete;
- external beta complete;
- commercial/public launch complete.

The next production deployment should be a deliberately frozen release candidate containing the intended security/document/recovery improvements, not an arbitrary moving `development` head.

## 4.5 Milestone status at a glance

This table summarizes where the roadmap stands without pretending that feature count proves readiness.

| Milestone | Current status | Release interpretation |
| --- | --- | --- |
| 0 — Stabilize integration branch | **Active** | Branch cleanup run #120 completed and no PRs are open; `development` differs from `master` only in the three handoff documents. Three branches lacking merged-PR proof remain preserved, and required-check enforcement plus release-freeze reconciliation remain open. |
| 1 — PWA performance baseline | **Substantially implemented** | Major browser/render/performance work exists; installed-device acceptance still matters before calling the client release-complete. |
| 2 — Installed PWA parity / MAUI retirement | **Partially complete** | PWA parity is advanced; Android physical acceptance is open and MAUI has not yet been deliberately retired. |
| 3 — Recurring bill discovery quality | **Core implemented; quality work ongoing** | Recurring discovery is production-capable, but broader real-world recurrence/accuracy evidence remains ongoing product-quality work. |
| 4 — Statement intelligence / “why” engine | **Core implemented; acceptance incomplete** | Deterministic ingestion/extraction/matching/change logic exists and parser/OCR containment is strong in repo; representative same-release semantics/OCR and AI-quality gates remain. |
| 5 — Alerts and proactive monitoring | **Core implemented; operational proof incomplete** | Financial/security/provider alerting exists; real destination receipt/noise acceptance remains part of release proof. |
| 6 — Financial Home expansion | **Substantial product surface exists** | Accounts, transactions, bills, cash-flow/planning and related views are present; additional expansion is not the current release priority. |
| 7 — Account, privacy, and trust | **Technically advanced; external gates remain** | Strong auth, export, deletion, privacy/security surfaces are implemented; legal/provider/human proof remains. |
| 8 — Real production/private-beta acceptance | **Current critical milestone** | This is the main release bottleneck: same-release production, device, provider, recovery, statement and isolation evidence must be completed. |
| 9 — Internal Beta 0 | **Blocked on Milestone 8 evidence** | Do not broaden testing until the release candidate can prove its own critical trust boundaries. |
| 10 — Revenue readiness | **Infrastructure partially complete** | Stripe/entitlement infrastructure exists, but enforcement remains off and lifecycle/legal/economic gates remain. |
| 11 — Trusted external beta | **Not yet entered as a release stage** | Begins only after same-release trust evidence and Internal Beta 0 are acceptable. |
| 12 — Broader launch preparation | **Not complete** | Requires prior beta, operational, legal, recovery, security, provider and product-quality gates. |

## 4.6 Current release philosophy

The project is no longer primarily blocked on “building the app.”

The release-critical problem is now converting a large amount of implemented, CI-verified functionality into **same-release real-world proof**.

Therefore:

- stop adding unrelated product scope while release blockers remain;
- finish release-critical security work;
- freeze a release candidate;
- reconcile and promote deliberately;
- deploy only the exact verified `master` SHA;
- prove critical behavior on that exact deployed release;
- fix failures through the normal development → PR → exact-head CI → promotion → guarded-deploy path;
- only then advance through Internal Beta 0, trusted external beta, revenue rollout, and broader launch.

---

# 5. Roadmap sequencing rules

FullWorth should not attempt every product category at once.

The critical sequence is:

1. Stabilize the current integration branch.
2. Finish Web/PWA performance and parity.
3. Prove the installed PWA can replace MAUI.
4. Improve recurring-bill intelligence quality.
5. Complete statement-to-change explanation reliability.
6. Complete real production/private-beta acceptance.
7. Run trusted internal/beta use on real controlled bills.
8. Only then expand the broader “financial life” surface aggressively.
9. Turn on billing only after product trust and payment lifecycle evidence are real.
10. Scale infrastructure only after actual traffic or operational need justifies it.

A lower-priority expansion must not distract from a current P0 security, data-integrity, CI, deployment, or acceptance defect.

---

# 6. Milestone 0 — Stabilize the active integration branch

Priority: P0

Goal: Create a clean, known-good `development` baseline before another large feature wave.

## 6.1 Finish active branches

### Adaptive performance profile

Required:

- ~~ensure branch is still based on current `development`;~~
- ~~require exact-head CI;~~
- ~~fix any localization/resource omissions;~~
- ~~verify settings persistence is device-local by design;~~
- ~~verify Auto does not break when browser hardware/network APIs are missing;~~
- ~~verify lower modes do not remove financial functionality;~~
- ~~verify reduced-motion preference takes precedence where appropriate;~~
- ~~verify transaction-window changes do not alter transaction ordering or security;~~
- ~~verify startup applies the selected/effective profile early enough to avoid visual mode flicker.~~

Exit criteria:

- ~~exact final head green;~~
- ~~no security regression;~~
- ~~no browser console errors;~~
- ~~clear Settings explanation;~~
- ~~merge to `development`.~~

### Cleanup PR #155

Required:

- ~~wait for exact-head CI;~~
- ~~merge only if green;~~
- ~~do not remove MAUI itself as part of this cleanup.~~

## 6.2 Branch hygiene

Target long-term steady-state branch list:

- `master`;
- `development`;
- a small number of active feature/fix branches;
- temporary release branch only when justified.

Delete stale merged source branches after they are no longer needed for an open PR.

Recommended repository setting:

- enable automatic deletion of head branches after PR merge if GitHub repository policy allows it.

Do not delete:

- `master`;
- `development`;
- branch used by an open PR;
- branch containing unmerged work;
- branch needed for an active release incident until recovery is complete.

## 6.3 Documentation hygiene

Keep:

- `FULLWORTH_CONTEXT.md` until continuation tooling no longer depends on the legacy filename;
- this roadmap;
- operational/recovery documentation that is actively referenced;
- revenue-readiness documentation;
- AI corpus safety documentation.

Remove or update:

- ~~stale TODO files;~~
- ~~obsolete launch profiles;~~
- ~~unused assets;~~
- old instructions that point to nonexistent branches or completed milestones.

### Exit gate for Milestone 0

Do not call this milestone complete until:

- `development` has no known failing CI;
- active branches are understood;
- no stale planning document contradicts current work;
- performance-profile work is either merged or intentionally deferred with a documented reason.

---

# 7. Milestone 1 — PWA performance baseline

Priority: P0

Goal: Make the installed FullWorth PWA feel fast enough that MAUI startup/performance is no longer a reason to keep a separate native UI.

## 7.1 Startup performance

Measure and improve:

- first navigation to `/app`;
- authenticated app-shell render time;
- time until primary content is readable;
- layout shift during initial load;
- JS module-loading overhead;
- CSS processing;
- interactive-server connection/reconnection behavior;
- standalone-installed-PWA launch;
- normal-browser launch;
- slow-network behavior.

Avoid “performance improvements” that merely hide latency with longer splash/loading screens.

## 7.2 CSS and visual rendering

Continue auditing:

- duplicate stylesheet loads;
- route-level stylesheet injection;
- expensive backdrop filters;
- large shadows;
- unnecessary animation;
- layout invalidation;
- mobile navigation transitions;
- skeleton dimensions that shift when content loads.

Performance modes may tune presentation cost, but the default design must remain polished without requiring High mode.

## 7.3 Blazor render control

Audit every authenticated primary page for:

- unnecessary `StateHasChanged()`;
- data sorting/filtering repeated during render;
- repeated JS interop;
- repeated module imports;
- repeated expensive computed properties;
- event handlers that trigger extra renders;
- data reloads triggered by navigation when existing circuit state could safely be reused.

Financial data must not be moved into unsafe browser persistence merely to avoid server fetches.

## 7.4 Data-window strategy

Large list pages should have explicit bounded windows.

Transactions:

- ~~Efficiency: small window;~~
- ~~Balanced: medium window;~~
- ~~High: full current supported window;~~
- ~~Auto: selected based on available device/network hints.~~

Long-term improvement:

- add server-side pagination or cursor-based continuation so a user can access deeper history without loading all rows at once.

## 7.5 Performance metrics

Create a lightweight performance-budget document or tests covering:

- initial CSS/JS asset count;
- route-level extra stylesheet count;
- maximum initial transaction rows;
- maximum initial alert rows;
- repeated JS interop count for key routes;
- service-worker cache boundary.

Do not introduce invasive analytics solely for internal performance measurement if local/browser measurement can answer the question.

### Exit gate for Milestone 1

- installed PWA launch is subjectively and measurably responsive on a mid-range Android device;
- no visible route-level CSS flash on primary authenticated navigation;
- no known repeated render/interop hot path on Overview, Bills, Bill Detail, Activity, Account, Transactions, Settings;
- ~~lower performance mode materially reduces presentation/list workload;~~
- ~~no financial cache boundary weakened.~~

---

# 8. Milestone 2 — Installed PWA parity and MAUI retirement

Priority: P0/P1

Goal: Prove `FullWorth.Web` can become the single primary customer client.

## 8.1 Required parity matrix

The installed PWA must verify all of the following before MAUI removal.

### Authentication

- registration;
- email confirmation;
- login;
- logout;
- token refresh through BFF;
- expired-session behavior;
- password reset;
- two-factor authentication;
- recovery codes;
- external identity linking/sign-in where configured;
- safe error messages.

### Plaid

- new bank connection;
- hosted-link popup behavior;
- successful return to app;
- account/transaction synchronization;
- RequiresAttention handling;
- update-mode reconnect;
- disconnected-bank handling;
- no provider/access token exposure.

### Transactions

- current transaction list;
- account filter;
- search;
- pending/posted display;
- refresh;
- performance-profile load size;
- empty/error states;
- mobile scrolling.

### Bills

- recurring Bill Stream list;
- current amount;
- prior average;
- monthly delta;
- annualized impact;
- change state;
- provider logo/fallback;
- bill detail navigation.

### Bill detail and statements

- statement upload;
- PDF/JPG/JPEG/PNG picker behavior on mobile;
- upload progress/status;
- terminal state handling;
- extracted/history display;
- change explanation;
- evidence visibility;
- retry/error behavior.

### Activity

- alerts load;
- mark read;
- dismiss;
- attention filter;
- unread filter;
- empty states;
- mobile navigation.

### Account

- bank accounts;
- connections;
- reconnect;
- disconnect;
- export;
- privacy route;
- deletion flow;
- action confirmation and errors.

### Settings/security

- timestamp mode;
- device performance profile;
- password change;
- email change;
- 2FA setup;
- recovery-code regeneration;
- authenticator replacement;
- 2FA disable;
- provider linking;
- safe dialogs;
- no raw technical exception display.

### Platform behavior

- install on Android;
- install on supported desktop browsers;
- iOS Add to Home Screen flow;
- standalone detection;
- icon quality;
- safe-area layout;
- back navigation;
- hamburger/menu close behavior;
- bottom navigation;
- keyboard resize behavior;
- file picker;
- theme behavior;
- localization;
- reconnection after temporary network loss;
- update/service-worker behavior.

## 8.2 PWA update strategy

Requirements:

- ~~safe service-worker update;~~
- ~~no stale authenticated document cache;~~
- current deployment picked up predictably on the real installed-device acceptance pass;
- ~~generic offline page remains truthful;~~
- ~~user is not trapped in an obsolete app shell.~~

## 8.3 MAUI cutover decision

MAUI removal must be a deliberate milestone, not cleanup-by-accident.

Before removal:

- parity matrix complete;
- Android installed-PWA experience accepted;
- no native-only required feature identified;
- production/support plan updated;
- CI changed intentionally;
- README updated;
- solution/project references updated;
- native-only secure-storage compatibility evaluated before deleting legacy key logic;
- any app-store requirement explicitly considered.

After approval:

1. create dedicated `platform/remove-maui` branch;
2. remove MAUI project from solution;
3. remove MAUI-only source/resources/platform folders;
4. remove MAUI Android CI job;
5. remove native-only tests/configuration that no longer protect a real surface;
6. update README and roadmap;
7. require full remaining CI/container/recovery gate;
8. do not rename persistent compatibility identifiers in the same PR.

### Exit gate for Milestone 2

- FullWorth Web/PWA is the supported customer client;
- MAUI is either removed or intentionally retained only for a documented remaining requirement;
- no feature exists solely in MAUI;
- CI no longer spends time building unused native UI after formal retirement.

---

# 9. Milestone 3 — Recurring bill discovery quality

Priority: P1

Goal: Make recurring-bill detection trustworthy enough that users do not need to manually correct FullWorth constantly.

## 9.1 Merchant normalization

Continue deterministic normalization for:

- bank prefixes/suffixes;
- debit/check-card noise;
- recurring/autopay markers;
- transaction reference identifiers;
- location suffixes;
- provider-specific payment descriptors.

Must preserve meaningful merchant identity:

- digit-containing brands;
- legitimate short tokens;
- provider distinctions that matter.

Testing:

- positive normalization fixtures;
- false-merge fixtures;
- merchant names with digits;
- transaction reference noise;
- provider aliases.

## 9.2 Cadence model

Current monthly-only assumptions should evolve into explicit recurrence classes.

Target classes:

- weekly;
- biweekly;
- monthly;
- approximately monthly;
- every 2 months where support is justified;
- quarterly;
- semiannual;
- annual;
- irregular but predictably recurring;
- unknown.

Do not force a transaction series into a recurrence type when evidence is weak.

## 9.3 Missed-period tolerance

Handle:

- skipped billing month;
- statement timing differences;
- weekend/holiday shifts;
- pending-to-posted date movement;
- provider rebilling;
- temporary pause.

## 9.4 Amount behavior

Model:

- fixed amount;
- narrow-variable amount;
- usage-driven variable amount;
- promotional period;
- one-time spike;
- fee addition;
- discount removal.

Use deterministic statistics and explicit thresholds.

## 9.5 Duplicate Bill Stream prevention

Prevent duplicate recurring streams created by:

- merchant descriptor drift;
- account sync replay;
- provider rename;
- statement attachment before merchant normalization converges;
- multiple accounts paying the same provider.

Duplicate prevention must never merge genuinely separate obligations simply because provider text is similar.

## 9.6 Discovery confidence

Each candidate Bill Stream should have an explainable confidence basis.

Example evidence:

- number of matching posted transactions;
- interval consistency;
- amount consistency;
- normalized merchant consistency;
- provider/statement evidence.

Avoid presenting a single opaque AI confidence number as truth.

### Exit gate for Milestone 3

- common monthly subscriptions detect reliably;
- annual/quarterly cadence no longer gets incorrectly forced into monthly;
- duplicate stream creation has regression tests;
- low-confidence recurrence is labeled or withheld rather than asserted.

---

# 10. Milestone 4 — Statement intelligence and “why” engine

Priority: P1

Goal: Turn provider statements into reliable evidence explaining bill changes.

## 10.1 Ingestion pipeline

Maintain the pipeline:

Upload
→ secure storage
→ classification
→ text/OCR extraction
→ structured deterministic extraction
→ candidate AI extraction where enabled
→ validation
→ Bill Stream matching
→ historical comparison
→ change detection
→ explanation
→ alert.

Every stage must have:

- explicit state;
- bounded failure behavior;
- retry rules where appropriate;
- ownership;
- safe user-facing status.

## 10.2 Extraction quality

Target fields:

- provider;
- account-safe identifier/mask where permitted;
- statement period;
- statement date;
- due date;
- total amount;
- previous balance;
- payments/credits where supported;
- taxes;
- fees;
- discounts;
- promotions;
- service/package line items;
- usage charges;
- one-time adjustments.

Do not pretend unsupported providers have provider-specific precision.

## 10.3 OCR

Validate representative:

- clean digital PDF;
- scanned PDF;
- low-contrast scan;
- JPG;
- PNG;
- rotated image if supported;
- multi-page statement;
- statement with dense tables.

OCR extraction must remain bounded and must not leak raw statement text into logs.

## 10.4 Bill Stream matching

Matching hierarchy should prefer deterministic signals.

Potential signals:

- normalized provider;
- transaction history;
- amount range;
- account-safe identifier;
- billing dates;
- known connection/provider metadata.

Require adequate evidence before automatic attachment.

Ambiguous match:

- do not attach automatically;
- preserve statement securely;
- expose truthful review state if user action is supported.

## 10.5 Historical comparison

Compare structured statements deterministically.

Examples:

`$79.99 → $104.99`

`+$25/month`

`+$300/year`

Potential component explanation:

- promotion expired: +$20;
- new fee: +$5.

Arithmetic must come from deterministic code.

## 10.6 Change taxonomy

Target reason types:

- promotion expired;
- discount removed;
- fee added;
- fee increased;
- plan/service price increased;
- tax/regulatory charge changed;
- usage increased;
- one-time charge;
- credit/refund applied;
- service/package changed;
- unknown/insufficient evidence.

A statement may contain multiple simultaneous reasons.

## 10.7 Explanation quality

Explanation order:

1. What changed?
2. How much?
3. Annualized impact where meaningful.
4. Why FullWorth believes it changed.
5. Supporting statement evidence.
6. Confidence/uncertainty.

Never invent a cause simply because the amount changed.

### Exit gate for Milestone 4

- representative provider documents parse with known accuracy;
- explanations can trace back to structured evidence;
- multi-factor changes are supported;
- unknown explanations stay unknown;
- cross-user statement access remains impossible.

---

# 11. Milestone 5 — Alerts and proactive monitoring

Priority: P1/P2

Goal: FullWorth should proactively surface material events without becoming noisy.

## 11.1 Alert types

Core:

- meaningful bill increase;
- new recurring bill discovered;
- upcoming due date where evidence exists;
- connection needs attention;
- statement processing failure requiring action;
- major fee/new charge;
- subscription/recurrence anomaly.

Later:

- recurring bill disappeared;
- unusually high variable bill;
- renewal approaching;
- duplicate charge candidate.

## 11.2 Alert quality rules

Each alert should answer:

- what happened;
- amount;
- monthly/annual impact where appropriate;
- provider;
- date;
- why;
- confidence/evidence;
- action if needed.

## 11.3 Noise control

Prevent:

- repeated alerts for same event;
- alerts based only on pending transactions unless designed explicitly;
- tiny changes below configured materiality threshold;
- multiple alerts from the same statement comparison.

## 11.4 Notification channels

Start with a low-maintenance channel.

Candidate sequence:

1. in-app Activity;
2. email;
3. optional push once PWA push/support requirements are justified.

Notification preference requirements:

- category-level opt-in/out;
- unsubscribe;
- security notifications remain separate from marketing;
- no sensitive financial content in lock-screen-visible push unless explicitly designed and reviewed.

### Exit gate for Milestone 5

- meaningful change produces one understandable alert;
- alert state is idempotent;
- read/dismiss works;
- no unsafe sensitive notification content;
- notification failure does not corrupt financial state.

---

# 12. Milestone 6 — FullWorth Financial Home expansion

Priority: P2, after bill-intelligence trust

Goal: Expand from bill monitoring into a broader financial-life overview without turning FullWorth into a generic cluttered budget dashboard.

The Financial Home must remain change-first. Its primary job is to answer "What changed, what needs attention, and why?" before showing secondary balances or analytics.

This milestone contains proposed product direction and should be validated with user feedback before every subfeature becomes a commitment. A proposed subfeature is not strategic merely because a competitor offers it; it should strengthen monitoring, understanding, retention, or paid value.

## 12.1 Financial Home

Potential top-level summary:

- connected cash/bank accounts;
- recurring monthly commitments;
- recent important changes;
- upcoming known charges;
- connection health;
- recent income/outflow context;
- annualized cost changes.

The home screen should remain action-oriented.

Avoid dozens of small KPI cards.

## 12.2 Cash-flow awareness

Potential deterministic summaries:

- posted inflow;
- posted outflow;
- recurring obligations;
- known upcoming bill load;
- month-to-date comparison.

Do not market estimates as guaranteed future cash flow.

## 12.3 Net-worth direction

Only pursue if reliable asset/liability data is available.

Requirements before launch:

- explicit account-type support;
- stale-balance handling;
- timestamp visibility;
- liability sign conventions;
- no double counting;
- manual asset support only if maintenance burden is acceptable.

## 12.4 Subscription center

Build on Bill Streams rather than creating a second recurring-charge model.

Potential features:

- active subscriptions;
- annual vs monthly billing;
- next expected charge;
- price-change history;
- estimated yearly cost;
- cancellation/help link only when verified.

Do not claim FullWorth can cancel something unless the product actually performs the cancellation.

## 12.5 Spending insights

Only add insights that can be made accurately.

Examples:

- recurring vs non-recurring outflow;
- merchant trend;
- major month-over-month movement.

Avoid forcing FullWorth into manual envelope budgeting unless customer evidence justifies it.

## 12.6 Debt/liability view

Possible later surface:

- linked liability balance;
- APR where source supports it;
- payment due;
- minimum payment;
- interest cost.

Do not provide fabricated APR/payment data.

### Exit gate for Milestone 6

- expansion strengthens the “one app” promise;
- bill-change intelligence remains prominent;
- no duplicate financial truth models;
- each feature has a reliable data source and freshness model.

---

# 13. Milestone 7 — Account, privacy, and trust experience

Priority: P1/P2

Goal: Security should feel premium and understandable rather than like an admin portal.

## 13.1 Settings structure

Target sections:

- Account;
- Security;
- Appearance;
- Performance;
- Notifications;
- Privacy;
- Data/export;
- Connected sign-in methods.

Avoid giant permanent credential forms.

Sensitive inputs appear only during the action that needs them.

## 13.2 ~~Performance preference~~

Document clearly:

- ~~Auto is recommended;~~
- ~~Efficiency is for older devices/slower networks;~~
- ~~Balanced is moderate;~~
- ~~High enables the richest presentation and larger initial data windows.~~

~~Preference is per device unless product requirements deliberately change.~~

## 13.3 Privacy center

Explain:

- what transaction data is used for;
- what statement data is used for;
- what is stored;
- how deletion works;
- export;
- bank disconnect;
- AI boundaries;
- retention where applicable.

## 13.4 Account deletion

Maintain:

- reauthentication;
- 2FA where required;
- staff-role safeguards;
- Plaid revoke-first behavior;
- owned-data erasure;
- crash-safe statement cleanup;
- no cross-user impact.

### Exit gate for Milestone 7

- security actions are clear and safe;
- technical exceptions do not surface raw;
- user can export, disconnect, and delete;
- privacy language matches implementation.

---

# 14. Milestone 8 — Real production/private-beta acceptance

Priority: P0

Goal: Convert code-level confidence into real environment evidence.

This is one of the most important milestones in the roadmap.

Passing CI is not enough.

## 14.1 Authenticated browser/BFF smoke

Against the exact deployed release:

- login;
- 2FA if enabled;
- Overview;
- Bills;
- Bill Detail;
- Activity;
- Account;
- Transactions;
- Settings;
- export;
- logout.

Confirm:

- tokens not exposed;
- no-store behavior;
- session refresh;
- expired session failure is safe.

## 14.2 Direct API smoke

Using controlled credentials and secret-file handling:

- authenticated reads;
- authorized mutations;
- ownership;
- rate limits;
- invalid auth.

## 14.3 Objective cross-user isolation proof

Use two controlled users.

Create real owned resources for user A.

Verify user B receives 404/appropriate denial for foreign:

- Bill Stream;
- statement upload/status/download;
- alerts if identifier-accessible;
- account/connection resource where applicable.

Do not use guessed fake IDs as the only isolation evidence.

## 14.4 Plaid observation

Human/provider evidence:

- Hosted Link opens;
- successful institution connection;
- return flow;
- Active status;
- sync;
- update-mode reconnect;
- attention handling;
- disconnect/revoke where safe in controlled test.

## 14.5 Statement observation

Use controlled representative files with known facts.

Verify:

- field extraction;
- OCR;
- provider match;
- comparison;
- explanation;
- alert;
- ownership.

## 14.6 Account deletion proof

Use a disposable account.

Verify:

- required reauthentication;
- provider disconnect/revoke;
- deletion;
- credentials no longer authenticate;
- data no longer accessible.

## 14.7 Recovery proof

Run clean-host restore using actual off-host repository.

Verify together:

- database;
- statements;
- Data Protection keys;
- correct release;
- protected Plaid data decryptability;
- statement file consistency.

## 14.8 Immutable backup proof

Configure provider-enforced protection:

- Object Lock;
- WORM;
- immutable snapshot;
- append-only equivalent.

Prove an application-host compromise cannot trivially erase all recovery points.

## 14.9 External alerts

Observe real receipt at both intended destinations where applicable.

Do not treat script success as proof of receipt.

## 14.10 Legal review

Obtain qualified review of the exact Terms/Privacy version that will be shown to external testers/users.

### Exit gate for Milestone 8

Every required artifact must correspond to the same intended release or have an explicitly documented relationship.

No synthetic “proof” may substitute for a human/provider/real-host gate.

---

# 15. Milestone 9 — Internal Beta 0

Priority: P0/P1

Goal: Use FullWorth for real controlled bills before inviting external beta users.

## 15.1 Tester population

Start with a very small controlled group.

Expected:

- owner/internal account;
- optionally a second controlled tester for isolation/real-world diversity.

## 15.2 Beta scenarios

Track:

- time to first bank connection;
- time to first recurring Bill Stream;
- missed bills;
- false recurring bills;
- duplicate Bill Streams;
- wrong provider names;
- wrong amounts;
- wrong cadence;
- incorrect change reasons;
- statement parsing failure;
- connection-attention accuracy;
- useful vs noisy alerts.

## 15.3 Trust metrics

Create explicit quality measures:

- recurring detection precision;
- recurring detection recall;
- duplicate-stream rate;
- statement field accuracy;
- explanation support rate;
- unknown explanation rate;
- false alert rate;
- missed material change rate;
- time from sync to surfaced change.

### Exit gate for Milestone 9

Do not invite broader testers until the product can reliably explain its own mistakes and uncertainty.

---

# 16. Milestone 10 — Revenue readiness

Priority: P2

Goal: Charge only after FullWorth demonstrates trustworthy core value and the payment lifecycle is verified.

## 16.1 Keep enforcement off initially

Maintain:

`BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED=false`

until rollout gates pass.

## 16.2 Offer design

Decide:

- monthly price;
- annual price;
- billing currency;
- trial policy if any;
- refund/support policy;
- features included;
- beta grandfathering if applicable.

Do not build excessive pricing complexity before demand exists.

## 16.3 Stripe configuration

Verify:

- ~~Product;~~
- ~~monthly Price;~~
- ~~annual Price;~~
- ~~webhook;~~
- ~~Customer Portal;~~
- cancellation;
- payment-method management;
- tax configuration;
- restricted key feasibility.

## 16.4 Lifecycle proof

Controlled live/test-mode flow as appropriate:

- ~~checkout created;~~
- ~~payment succeeds in the configured sandbox flow;~~
- correct configured Price grants entitlement;
- unrelated Price does not;
- portal loads;
- cancellation updates;
- webhook retry is idempotent;
- failed/past-due state handled;
- local reconciliation matches provider state.

## 16.5 Enforcement rollout

Suggested stages:

1. billing integration enabled, enforcement off;
2. InternalTester cohort;
3. BetaTester cohort;
4. broader cohort;
5. All only after review.

Permanent safety exemptions should remain for essential user rights such as:

- account deletion;
- bank disconnect;
- data export;
- subscription recovery.

## 16.6 Business economics and scale proof

Revenue readiness is not complete merely because checkout works.

Once paid rollout begins, measure:

- free → paid conversion;
- trial → paid conversion if trials are used;
- monthly and annual paid retention;
- logo and revenue churn;
- realized ARPU;
- gross margin;
- payment-processing cost;
- Plaid/data-provider cost per active user;
- compute/storage cost per active user;
- support cost per active user;
- customer acquisition cost by channel;
- payback period;
- lifetime-value assumptions using real cohort evidence.

Translate the long-term business goal into subscriber economics:

`gross ARR = paying subscribers × realized average monthly subscription revenue × 12`

Illustrative scale only:

- 10,000 payers at an $8 realized monthly average ≈ $960,000 gross ARR;
- 25,000 payers at an $8 realized monthly average ≈ $2.4 million gross ARR;
- 50,000 payers at an $8 realized monthly average ≈ $4.8 million gross ARR.

These examples are not price targets or forecasts. They make clear that retention and repeatable acquisition matter more than raw registrations.

Do not scale paid acquisition until FullWorth has evidence of trustworthy core value, meaningful retention, and acceptable unit economics.

### Exit gate for Milestone 10

- product value proven;
- payment lifecycle proven;
- support/refund path exists;
- legal/tax requirements addressed;
- enforcement fail-safe verified.

---

# 17. Milestone 11 — Trusted external beta

Priority: P2

Goal: Invite a small outside group without creating a support/security crisis.

## 17.1 Initial cohort

Start approximately 3–5 trusted testers.

Collect:

- device/browser;
- institution/provider mix;
- bill types;
- setup friction;
- detection misses;
- explanation errors;
- perceived value;
- notification noise.

## 17.2 Support tooling

Need:

- safe user identifier;
- connection-health diagnostics without secrets;
- statement processing status;
- release identifier;
- correlation IDs;
- ability to distinguish data issue from UI issue.

Never build “support access” that lets staff browse arbitrary customer financial evidence.

## 17.3 Feedback taxonomy

Classify:

- onboarding;
- performance;
- bank connection;
- transaction sync;
- recurring detection;
- statements;
- explanation;
- alerts;
- account/security;
- billing;
- visual polish.

### Exit gate for Milestone 11

- no open P0 security/data-loss issue;
- recurring-detection quality acceptable;
- statement explanations useful;
- support burden understood;
- recovery and monitoring proven.

---

# 18. Milestone 12 — Broader launch preparation

Priority: P2/P3

Goal: Prepare for growth without prematurely over-engineering.

## 18.1 Reliability

Define targets for:

- API availability;
- Web availability;
- readiness monitoring;
- sync success;
- statement-processing success;
- alert generation;
- backup freshness.

## 18.2 Observability

Add safe structured telemetry:

- correlation ID;
- release ID;
- operation type;
- duration;
- safe error category;
- provider/institution identifier only where privacy policy permits and risk is reviewed.

Never include:

- raw statement text;
- tokens;
- full account numbers;
- passwords;
- recovery codes;
- private webhook URLs.

## 18.3 Capacity

Measure before scaling:

- API CPU/memory;
- PostgreSQL connections;
- DB query latency;
- transaction count;
- Bill Stream count;
- statement processing time;
- OCR resource use;
- storage growth.

## 18.4 Migration architecture

Current startup migrations imply one API instance.

Before multi-instance scale:

- move migrations to explicit release job;
- ensure only one migration runner;
- validate rollback/recovery strategy;
- keep deployment fail-closed.

## 18.5 Background processing

As volume grows, evaluate:

- job queue;
- retry policy;
- dead-letter behavior;
- idempotency;
- concurrency limits;
- OCR isolation;
- Plaid sync scheduling.

Do not introduce distributed complexity before metrics justify it.

---

# 19. AI-assisted bill intelligence and model evaluation

Priority: P2 for isolated evaluation; production use remains gated.

Status: Qwen3-4B is the first pinned local evaluation candidate. Runtime and quality acceptance are incomplete. No AI-derived persistence or production activation is approved.

Goal: Measure whether a locally served, externally trained model improves statement candidate extraction over FullWorth's deterministic parser while keeping all financial and security decisions deterministic.

The active direction replaces the earlier plan to train a FullWorth model from scratch. The superseded first-party tokenizer/decoder/initializer/checkpoint/training implementation and its dedicated tests have been removed from the active product tree rather than carried into release. Do not reintroduce that path as product work without a new, evidence-backed architecture decision.

The first evaluation artifact is recorded in `deploy/ai-models/qwen3-4b-q4_k_m.manifest`:

- Repository: `Qwen/Qwen3-4B-GGUF`
- Revision: `a9a60d009fa7ff9606305047c2bf77ac25dbec49`
- File: `Qwen3-4B-Q4_K_M.gguf`
- Size: `2,497,280,256` bytes
- SHA-256: `7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5`
- License identifier recorded by the manifest: `Apache-2.0`
- Runtime alias: `fullworth-local`

The model file is not committed to Git. Its manifest, exact hash/size verifier, and example configuration are repository-controlled. Artifact provenance and license notices must remain tied to the exact revision and file actually used.

## 19.1 Deterministic and security boundaries

- Treat model output and all document text as untrusted candidate data. Source excerpts must match the supplied extracted/OCR text.
- The model cannot choose a user, select resources, authorize access, perform arithmetic, write financial records, compare historical bills, set thresholds, or decide alerts.
- Preserve module ownership contracts, `UserId + resource ID` checks, secure storage, no-store boundaries, and the absence of account/provider credentials and physical storage paths from model inputs and outputs.
- Keep the API/BFF as the only product-facing path. Browser and MAUI clients must not call the model runtime directly.
- Preserve explicit uncertainty and deterministic-only fallback. Never silently send a failed local inference request to another AI provider.

Exit gate: contract and regression tests prove model output cannot cross ownership, persistence, arithmetic, or alert-decision boundaries.

## 19.2 Isolated local runtime

- Run the pinned llama.cpp server only through the development/evaluation Compose profile, bound to host loopback and an internal container network.
- Require the separate local API key, exact approved model SHA-256 and size, and the pinned runtime image digest before startup.
- Keep model weights outside Git, mount them read-only, and retain the current container confinement and bounded-resource settings.
- The runtime health/authentication smoke proves service availability and request protection only. It does not establish extraction accuracy.
- Do not change production Compose or production AI flags as part of local evaluation setup.

Exit gate: the exact model and runtime artifacts pass their manifests, and an authenticated local structured-output smoke succeeds without exposing output or secrets in logs.

## 19.3 Private evaluation corpus and ground truth

- Keep statement files, extracted text, labels, model responses, and case-level results outside Git.
- Use synthetic or explicitly authorized representative documents. Product processing permission does not itself authorize model evaluation or training use.
- Separate evaluation and held-out cases by document and provider to reduce leakage.
- Preserve reviewer-approved expected fields, line items, supported evidence excerpts, ambiguity, and unknown labels.
- Minimize identifiers before inference. Never include full account numbers, credentials, or unnecessary personal data.

Measure provider coverage, field precision/recall, unsupported candidate rate, evidence support, abstention/unknown behavior, deterministic-baseline comparison, failures, latency, and resource use. Do not publish case-level data in CI logs or repository artifacts.

Exit gate: the corpus has documented authorization/provenance and reviewed ground truth; secret/private inputs remain outside GitHub.

## 19.4 Evaluation thresholds

Set numerical acceptance thresholds before running a held-out benchmark. Include at least:

- correctness for amount, dates, currency, and supported line items;
- precision/recall and candidate coverage;
- source-excerpt fidelity and unsupported-claim rate;
- uncertainty/abstention on absent or ambiguous facts;
- false explanation/false alert behavior;
- latency, memory, and CPU/GPU use.

Compare the local model to the deterministic extraction baseline. A result is useful only if it improves a defined task without weakening evidence or safety. Do not claim model quality from a health check, a single hand-picked statement, a schema-valid response, or synthetic CI fixtures.

## 19.5 Actual-model benchmark

- First prove the pinned model starts and completes a synthetic structured-output smoke through the same schema-constrained API shape used by the extractor.
- Then run the authorized private corpus offline with the pinned model/runtime and the committed deterministic scorer. Record only aggregate metrics, model/runtime manifest identifiers, and safe run metadata in repository-visible results.
- Inspect representative field-level errors privately before changing prompts, schema, model size, or extraction behavior.
- Consider a larger model only when the measured 4B errors justify its added storage, latency, and resource cost.

Exit gate: reproducible held-out measurements meet the thresholds from 19.4 and show a useful improvement over deterministic extraction without unsupported evidence claims.

## 19.6 Product integration and rollout

- Keep local and external provider paths disabled by default unless explicitly configured for an isolated evaluation.
- Shadow output remains non-persistent and cannot change the statement or bill state.
- Any later user-facing explanation must be composed from validated evidence and deterministic amounts, with clear uncertainty and authorized evidence references.
- Production serving requires a separately reviewed runtime, artifact integrity and provenance, least-privilege network boundaries, quotas/deadlines, overload behavior, monitoring, rollback, and kill switch.
- Provider outages return deterministic results or a clear unavailable/unknown state. No provider fallback is allowed unless separately reviewed and deliberately implemented.

Exit gate: exact-head CI, security review, held-out quality evidence, resource/load tests, failure/rollback drills, and separate product approval pass before production activation. Passing evaluation does not turn on AI-derived persistence.

## 19.7 Privacy, retention, and observability

- Define separate access and retention rules for raw documents, extracted text, candidates, explanations, evaluation results, caches, and backups.
- Keep all model prompts/responses containing financial data out of logs. Emit metadata-only diagnostics such as model/runtime versions, safe correlation ID, latency, resource use, and rejection category.
- Make account deletion, corpus withdrawal, retention expiry, backup reconciliation, and incident response cover AI evaluation artifacts.
- Do not claim a deleted source document can be removed from an already-trained model; the active plan does not authorize training on customer financial data.

Exit gate: reviewed privacy/retention controls and tests cover the full evaluation and inference lifecycle.
# 20. Cross-cutting testing roadmap

## 20.1 Unit tests

Required for deterministic:

- normalization;
- cadence;
- amount math;
- annualization;
- comparison;
- reason classification;
- ownership helpers;
- path validation;
- provider URL validation;
- subscription state.

## 20.2 Integration tests

Focus on:

- EF query translation;
- ownership;
- composite relationships;
- API authorization;
- BFF refresh;
- antiforgery;
- headers;
- upload lifecycle;
- delete lifecycle;
- Stripe webhook idempotency.

## 20.3 Browser tests

Highest-value future browser automation:

- login;
- app navigation;
- menu close;
- reconnect;
- transaction filtering;
- statement upload;
- settings dialogs;
- performance mode;
- mobile viewport.

Do not rely exclusively on browser automation for provider behaviors like real Plaid Hosted Link.

## 20.4 Regression policy

Every production defect should receive:

- focused regression test where practical;
- smallest safe fix;
- exact-head CI;
- no unrelated refactor in emergency fix.

---

# 21. Database roadmap

## 21.1 Query performance

Continue reviewing:

- transaction list indexes;
- Bill Stream metrics;
- detail-page round trips;
- connection-health queries;
- discovery hot path;
- statement history queries.

Index additions must be justified by real query shape.

Avoid index bloat.

## 21.2 Ownership constraints

Audit every user-owned relationship.

Priority entities:

- BankConnection;
- BankAccount;
- BankTransaction;
- BillStream;
- BillStatement;
- StatementUpload;
- BillAlert;
- subscription/entitlement state where user-owned.

## 21.3 Data lifecycle

Define:

- transaction retention;
- removed transaction behavior;
- statement deletion;
- account deletion;
- restored-backup deletion reconciliation.

---

# 22. UX roadmap

## 22.1 Navigation

Primary mobile navigation should expose only the most-used sections.

Suggested core:

- Overview/Home;
- Bills;
- Activity;
- Account.

Everything else belongs in contextual settings or hamburger/secondary navigation.

## 22.2 Financial hierarchy

On a bill change:

Primary:
`$79.99 → $104.99`

Secondary:
`+$25/month`

`+$300/year`

Then:
`Promotion expired +$20`

`New fee +$5`

Then evidence.

## 22.3 Loading states

Skeletons must reserve realistic space.

Avoid:

- content jumps;
- spinner-only blank screens;
- full-page reload appearance during simple filter changes.

## 22.4 Empty states

Explain:

- what is missing;
- why;
- what the user can do;
- whether waiting for sync is expected.

## 22.5 Errors

User errors should be human-readable.

Do not expose raw:

- `HttpRequestException`;
- stack trace;
- internal endpoint;
- provider payload;
- path;
- secret.

---

# 23. Accessibility roadmap

Required:

- keyboard navigation;
- visible focus;
- semantic buttons/links;
- dialog focus handling;
- labels;
- sufficient contrast;
- text zoom;
- reduced motion;
- touch target sizing;
- screen-reader-compatible status/error messages.

Performance mode must not be used as an excuse to remove accessibility behavior.

---

# 24. Localization roadmap

Maintain current localization architecture.

Before broader launch:

- audit strings added during recent FullWorth rename;
- performance-profile strings;
- security settings;
- statement states;
- alert reasons;
- bill-change reasons;
- subscription/billing copy.

Do not hardcode customer-visible English strings into otherwise localized surfaces unless intentionally temporary and tracked.

---

# 25. Release engineering roadmap

## 25.1 Normal development

Flow:

feature/fix branch
→ PR to `development`
→ exact-head CI
→ merge only green.

## 25.2 Release

`development`
→ release/promotion PR to `master`
→ exact-head CI
→ merge
→ verify `master` release
→ guarded production deploy
→ production verification
→ real acceptance where needed.

## 25.3 Do not

- deploy feature branch;
- merge failing head;
- reuse old CI result after head changes;
- claim release is live because PR merged;
- modify production manually to “catch it up.”

---

# 26. Definition of Done by work type

## UI feature

Done only when:

- responsive;
- accessible;
- loading/empty/error complete;
- localization considered;
- mobile tested;
- no fake action;
- relevant tests pass;
- CI green.

## API endpoint

Done only when:

- auth correct;
- ownership correct;
- rate-limit category considered;
- no-store/security headers through API/BFF where applicable;
- DTO does not leak secrets/paths;
- tests;
- CI green.

## Database change

Done only when:

- migration committed;
- pending-model check clean;
- ownership constraints considered;
- query impact considered;
- rollback/recovery risk reviewed;
- CI green.

## Statement feature

Done only when:

- ownership;
- storage safety;
- signature/type/size;
- status transitions;
- no raw text logging;
- failure behavior;
- tests;
- CI green.

## Production feature

Done only when:

- code complete;
- exact-head CI;
- guarded deploy;
- runtime verification;
- provider/human acceptance where required.

---

# 27. Product metrics roadmap

Metrics should answer whether FullWorth is actually delivering value and whether that value can become a durable business.

## Primary business scorecard

Use a small set of outcomes rather than a wall of vanity metrics:

1. **Activation to monitored state** — percentage of eligible new users who connect data and reach at least one trustworthy monitored recurring stream.
2. **Trusted change coverage** — ability to detect and correctly explain meaningful financial changes using controlled ground truth, evidence support, and user-confirmed outcomes where available.
3. **Monitored-user retention** — percentage of activated users who remain connected and meaningfully monitored over 30/90/180-day periods.

For paid cohorts, also track retained subscription revenue.

Cross-user disclosure incidents remain a zero-tolerance security guardrail and must never be traded for growth.

## Acquisition/setup

- registration completion;
- first successful bank connection;
- time to first transaction sync.

## Activation

- time to first recurring bill;
- percentage of connected users with at least one detected recurring bill;
- time to first useful change explanation.

## Quality

- recurring detection precision;
- recurring detection recall;
- duplicate Bill Stream rate;
- false alert rate;
- missed meaningful-change rate;
- statement parse success;
- explanation support rate.

## Engagement

- weekly active users;
- Activity review;
- Bills review;
- statement upload use;
- reconnect completion.

## Trust

- account disconnect rate after false detection;
- corrected/ignored alerts;
- statement failure rate;
- support contacts caused by incorrect financial facts;
- unsupported explanation rate;
- user correction rate;
- unresolved evidence-gap rate.

## Monetization and growth

After paid rollout begins, measure:

- free → paid conversion;
- trial → paid conversion if applicable;
- paid retention;
- logo churn;
- revenue churn;
- realized ARPU;
- gross margin;
- CAC by channel;
- payback period;
- provider/infrastructure/support cost per active user.

Measure acquisition quality by downstream activation, retention, and paid conversion rather than by cheap registrations alone.

Do not optimize vanity metrics at the expense of accuracy, trust, privacy, or cancellation fairness.

---

# 28. Technical debt register

Track deliberately rather than fixing everything immediately.

Known categories:

- legacy `BILLWATCH_*` infrastructure identifiers;
- `/opt/billwatch`;
- legacy secure-storage keys;
- legacy Stripe metadata keys;
- legacy cookie/purpose identifiers;
- legacy BillBeacon compatibility domains/aliases;
- MAUI transitional code;
- startup EF migrations;
- historical branding in operational test domains/fixtures;
- stale branch refs until deleted.

These are not all safe search-and-replace tasks.

Each compatibility rename needs:

- migration plan;
- rollback plan;
- dual-read/dual-write where required;
- production coordination;
- tests.

---

# 29. Proposed legacy-identifier migration milestone

Priority: Later, after PWA/private-beta stability

Do not combine this with normal product work.

Potential migration groups:

1. customer-visible filenames/text;
2. internal DOM IDs;
3. test-only names;
4. environment variables;
5. cookie names;
6. Data Protection application/purpose names;
7. native secure-storage keys;
8. Stripe metadata keys;
9. production filesystem paths;
10. public domains.

Easy visible rename candidates can happen earlier only when compatibility is irrelevant.

Persistent identifiers require explicit migration.

---

# 30. Critical path from today

The shortest credible path to production release is now **guarded deployment → same-release acceptance → external/device/governance closure**, not another feature sprint.

## ~~Step 1 — Complete the exact-head secret non-disclosure milestone~~

~~PR #649 exact head `1735c7512416a3fbf284ba2087f7ffb175a56961` passed FullWorth CI #1582 and Dependency Security #679, then merged to `development` as `998cd63c654be7a0fb012544865173a95e61e7e7`. PR #652 recorded the milestone after its own exact-head checks passed.~~

## Step 2 — Finish the evidence/governance remainder of #291

The ordinary repository-side security-hardening slices are complete through PR #662. Issue #291 is now **61/65 complete**.

The remaining four items require real evidence or repository-owner policy, not another generic application-code sprint:

- configure and prove immutable/off-host backup protection suitable for destructive-incident recovery;
- run a compromised-host/clean-host recovery exercise against the actual off-host recovery repository;
- decide whether `master`/`development` must require at least one independent approving review and apply that through an admin-capable GitHub path if required;
- preserve the rule that no production-security claim is valid without direct evidence from the exact deployed release.

Do not manufacture CI substitutes for provider-enforced immutability, clean-host recovery, or deployed-host evidence. Do not add unrelated security code merely to increase the checklist count.

## ~~Step 3 — Freeze the current release candidate~~

~~The security-hardened candidate `c092a9c76c5f4e811941400606c32d75a0a50a29` was superseded by deployment-path fixes discovered through guarded deploy attempts. PR #670 fixed runner-side release-SHA validation. PR #673 added secret-safe repair for identical duplicate runtime-database-password entries. PR #674 promoted the repaired candidate to `master` as `bc9c73954e3443f98ea56f003eac28df49122034`. AI-derived persistence remains disabled and deterministic extraction remains the production persistence authority.~~

## ~~Step 4 — Reconcile `development` and `master` deliberately~~

~~PR #675 synchronized the `bc9c7395...` master promotion ancestry back into `development` with zero source-file changes. Subsequent `development` changes are non-runtime repository maintenance only: release/handoff documentation plus CI change-detection/regression coverage. Application/runtime code remains aligned with the current master release tree. No force-push or destructive history rewrite was used.~~

## ~~Step 5 — Run exact-head release verification~~

~~PR #673 exact corrected head `f0ad457932b7b27599c840647f542b78d170685e` passed FullWorth CI #1618 and Dependency Security #713 before merge as `4d0385ec80242bc2b8839a9887d7841df1e28448`. PR #674 promoted that repair to exact master SHA `bc9c73954e3443f98ea56f003eac28df49122034`. Master-push FullWorth CI #1620 and Repository Governance #13 passed, and the exact attested production artifact is `fullworth-production-image-artifacts-bc9c73954e3443f98ea56f003eac28df49122034` (artifact ID `11292127567`, digest `sha256:d5ba31fb2d7222718bfd74c9518fcde5bbd45af59ca752a350f41db466bb2d1a`).~~

## Step 6 — Guarded-deploy the exact `master` release

**Current state:** Candidate `f401a591a8abdade557827c09412dc3166fb9de2` is not deployed. Run #15 failed closed before candidate startup. Its report of a partially running public runtime conflicts with a later read-only inventory of the OVH host:

OVH 40.160.137.55 read-only inventory found zero Docker containers, zero Docker volumes, and a missing release marker; this conflicts with run #15's partial-runtime report.

Do not assume the workflow and interactive SSH inspection reached the same machine or Compose project. Do not start the application against an empty database. The copied backup objects on this host have not passed encrypted Restic integrity or clean-host recovery verification.

**Recovery blocker:** Remote R2 recovery returned AccessDenied with the host's configured backup credential; a separate read-only recovery credential is required. PR #708 merged the verifier's passfile/secret wiring, and PR #709 added a production-host confirmation gate; neither supplies the R2 read credential or completes the R2 restore. Never expose the credential or broaden the production backup key to work around access denial.

**Next actions, in order:**

1. Through the approved secret-handling path, install the dedicated least-privilege R2 Object Read credential on the verified recovery host. Keep values out of chat, source control, shell history, and logs.
2. Re-run the isolated recovery drill against the remote encrypted repository. Require Restic integrity verification and restoration of the database, statements, and both Data Protection key rings; record sanitized evidence only.
3. In GitHub Environment `production`, make `FULLWORTH_PRODUCTION_SSH_HOST` an inspectable non-secret variable set to `40.160.137.55` only after verifying the pinned host-key entry belongs to that OVH server. The workflow now requires typing the same target into `confirm_production_host` and refuses a mismatch before artifact download or SSH. Verify Compose project, containers, volumes, and release marker read-only; do not restart or stop services based on the conflicting run report.
4. Preserve the existing live release and data. Do not deploy until the actual target is identified, recovery succeeds, the exact-master artifact remains valid, and every guarded preflight passes.
5. Only then use the guarded production workflow for the current exact `master` SHA. Require its automatic auth smoke, public API/Web readiness, and release marker to confirm the same deployed SHA.

The current verified live production release remains `7e8571a26447538db249c862ad009487cce119bc`. CI, an attested image, a source checkout, or the same-host object copy does not prove deployment or recovery.
## Step 7 — Build one same-release private-beta evidence bundle

Against the exact deployed candidate, complete and correlate:

1. objective cross-user Web/BFF/API ownership proof with two controlled identities and controlled foreign-owned resources;
2. controlled Plaid connect/update/reconnect lifecycle plus Hosted Link human-return observation;
3. controlled representative PDF/scanned-PDF/JPG/PNG statement lifecycle;
4. semantic/OCR review against operator-known facts;
5. bill matching/change/explanation/alert outcome review;
6. disposable account-deletion proof;
7. controlled reboot proof;
8. independent external alert-receipt proof;
9. clean-host restore against the actual off-host encrypted repository;
10. provider-enforced immutable/WORM/Object-Lock-equivalent backup proof;
11. production release/integrity/containment evidence.

Do not mix evidence from unrelated deployed releases without an explicit documented relationship.

## Step 8 — Complete physical Android installed-PWA acceptance

Close issue #251 only after all required Android checks pass on the exact live release and the metadata-only acceptance record is created.

Browser/Chromium/emulator evidence is not a substitute.

Re-test iOS issue #258 separately on the then-current production release; do not make speculative manifest/service-worker changes if the failure no longer reproduces.

## Step 9 — Complete external provider/product acceptance

Where those capabilities are included in the release:

- prove Google/Apple external-auth callback, account creation/sign-in/linking and isolation behavior;
- complete real-world Plaid payroll/sandbox acceptance for Paycheck Bill Plan issue #293;
- verify provider failure/reconnect behavior;
- preserve only non-sensitive acceptance metadata.

## Step 10 — Make the AI release scope explicit

For the initial public release, choose one evidence-backed state:

- keep deterministic extraction as the production authority and explicitly leave AI-derived persistence disabled; or
- complete the local/open-weight runtime, held-out accuracy, unsupported-claim, fallback, resource/load, rollback and approval gates before activation.

Do not ship an ambiguous half-enabled AI persistence path.

## Step 11 — Complete governance and legal gates

Before commercial/public launch:

- add required CI/security checks to the active protected-branch ruleset for `master` and `development`;
- decide and enforce the independent-approval policy through an admin-capable path;
- complete qualified commercial license review;
- complete qualified review of the exact customer-facing Terms/Privacy version;
- resolve third-party/model/runtime notice obligations;
- preserve the approved legal text through the normal PR/CI path.

## Step 12 — Run Internal Beta 0

Use the same deployed release and real controlled bills.

Track:

- false positive/negative recurring detection;
- statement extraction errors;
- unsupported explanations;
- alert usefulness/noise;
- account/provider failures;
- recovery/support friction;
- user-visible uncertainty behavior.

Fix release-blocking defects before expanding the tester population.

## Step 13 — Enter trusted external beta

Only after the Internal Beta 0 trust metrics and remaining external gates are acceptable:

- invite a deliberately small cohort;
- keep support and incident paths ready;
- continue observing financial-data correctness, ownership, provider behavior, latency, and alert quality;
- avoid turning subscription enforcement on simply because Stripe checkout exists.

## Step 14 — Revenue and broader launch

Revenue enforcement and broader launch require:

- proven subscription lifecycle;
- approved legal/commercial posture;
- acceptable unit economics;
- stable provider/infrastructure costs;
- production/recovery maturity;
- trustworthy bill/statement/change intelligence;
- acceptable beta retention and support burden;
- no unresolved P0/P1 security or financial-correctness defect.

---

# 31. What should not happen next

Avoid these traps:

- building many new financial modules before recurring-bill accuracy is trustworthy;
- optimizing MAUI startup instead of completing PWA cutover;
- turning the service worker into an offline financial-data cache;
- enabling AI persistence because a demo looks impressive;
- enabling subscription enforcement before lifecycle proof;
- rewriting all legacy production identifiers at once;
- introducing microservices or distributed queues before load requires them;
- shipping “Upload statement” or “Cancel subscription” buttons that do not perform real actions;
- treating an updated `master` branch as proof production has been deployed;
- manufacturing acceptance evidence from synthetic IDs.

---

# 32. Roadmap maintenance rules

Update this roadmap when:

- a milestone is completed;
- product direction materially changes;
- MAUI is retired;
- production release architecture changes;
- billing enforcement is enabled;
- a major new financial domain becomes committed;
- a security boundary changes;
- an external acceptance gate is completed;
- a roadmap assumption proves wrong.

When updating:

- preserve historical milestone intent where useful;
- mark completed work instead of rewriting history;
- remove stale exact SHAs when they no longer help;
- keep the “current snapshot” current;
- avoid duplicating detailed production procedures already documented elsewhere.

This roadmap is a sequencing and decision document, not a substitute for tests, source code, production evidence, or security review.


---

## Backup migration runbook (Cloudflare R2 to a replacement VPS)

When moving hosts, treat backup migration as a copy-and-verify operation. Do not delete or repoint the existing R2 repository until the replacement host has been independently verified.

1. Create a short-lived Cloudflare R2 credential with **Object Read only** permission scoped only to the source bucket `billwatch-production-backups-01`. Never commit or paste the credential into chat, source control, shell history, or logs.
2. Copy the complete Restic-compatible object tree to a private destination on the replacement host, using a mounted destination path and a pinned transfer tool.
3. Compare the source object count and aggregate byte count with the destination. A matching object count/size proves a complete object copy, not that encrypted Restic contents are readable.
4. Run the repository's Restic integrity and restore verification with the separately escrowed `RESTIC_PASSWORD`; verify the database, statements, Data Protection keys, release relationship, and permissions before treating the migration as a recovery proof.
5. Keep the original R2 repository unchanged until the restore proof and a second independent recovery path are complete. Revoke the temporary migration credential after verification.

The 2026-10-05 host-migration checkpoint copied 426 objects from `billwatch-production-backups-01` to `/home/debian/fullworth-backup-r2-migration` on the replacement VPS, totaling 6,479,351 bytes at the destination. Object-level parity is recorded; encrypted repository integrity and clean-host restore remain open acceptance gates. This migration does not establish production security or hard CPU/RAM isolation.
