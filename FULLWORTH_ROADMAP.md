# FullWorth Product & Engineering Roadmap

Last updated: 2026-09-22

Status: Active planning document

Completion notation: `~~strikethrough~~` means the roadmap item is completed to the level of evidence the item requires. Unstruck items remain open, partial, or awaiting real-environment acceptance.

Repository: `RealizmModz/FullWorth`

Integration branch: `development`

Release branch: `master`

Primary product direction: **PWA-first**

Primary product promise: **Your entire financial life. One app.**

Core bill-intelligence promise: **Know when your bills change — and why.**

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

This section is a point-in-time snapshot and should be updated periodically.

## 4.1 Branch position

As of 2026-09-22:

- `master` baseline: `81a74f11941f6ed67ba5de61b9ef186ef09bae3c`
- `development`: `e6dd9e063d33d3699bc5663d4e44a675548b0020`
- Production is operator-reported live on `81a74f11941f6ed67ba5de61b9ef186ef09bae3c`.
- Development includes the adaptive device-execution work, canonical FullWorth URL cleanup, Google/Apple production-auth configuration plumbing, public-site readability improvements, and the persistent first-run experience-preference foundation.
- ~~The first-run setup wizard passed exact-head CI #787 and merged through PR #229. New email/password registrations now enter the persisted personalization flow before the main app.~~

## 4.2 Active work

### ~~Adaptive device performance profile~~

Completed through the adaptive performance/device-execution merge path, including PR #203 and promotion through PR #204.

Intended user choices:

- Auto;
- Efficiency;
- Balanced;
- High.

This feature must improve actual workload characteristics rather than intentionally slow devices.

Examples of legitimate behavior changes:

- lower animation/transition cost;
- reduced expensive blur;
- smaller initial transaction windows;
- richer presentation on capable devices;
- hardware/network-hint-based Auto selection;
- OS/browser reduced-motion preference still respected.

Financial functionality and security behavior must remain identical across performance levels.

### ~~Cleanup PR #155~~

`cleanup/remove-stale-slnlaunch`

Purpose:

- remove the unreferenced Visual Studio `FullWorth.slnLaunch` profile;
- keep the transitional MAUI project itself intact.

~~Exact-head CI passed and PR #155 merged without removing the transitional MAUI project.~~

## 4.3 Recently completed development work

Recent merged development work includes:

- ~~adaptive device-side execution and bounded transaction filtering;~~
- ~~canonical FullWorth production URL migration and monitoring cleanup;~~
- ~~mobile logout restoration;~~
- ~~public-site readability/contrast improvements;~~
- ~~Google and Apple production external-auth configuration plumbing;~~
- ~~persistent first-run experience preference storage/API foundation;~~
- ~~first-run personalization wizard with language, theme, readability, motion, and financial-focus choices;~~
- recurring merchant-normalization improvements;
- database-side Bill Stream aggregation;
- reduced authenticated Web navigation flicker;
- installed-PWA layout rerender reduction;
- reduced Activity render work;
- reduced Transactions render work;
- reduced Bill Detail database round trips;
- reduced Account page Plaid JS wiring churn;
- removal of duplicate authenticated stylesheet work;
- removal of stale `BILLWATCH_TODO.md`;
- removal of an unused Web FullWorth logo asset.

## 4.4 Production state

The current operator-reported live production release is:

`81a74f11941f6ed67ba5de61b9ef186ef09bae3c`

The detailed guarded-deployment transcript for that release is not stored in this roadmap. The strongest preserved per-step deployment evidence remains the earlier guarded deployment of:

`cbcf261e13636f0330cb9d7be2ce413871e413aa`

Verified on 2026-09-21:

- exact promotion head `e8a512f62b188c24158abaec581e45217d3e9e58` passed FullWorth CI #644;
- guarded production deployment passed;
- encrypted pre-replacement recovery snapshot beginning `7449ac243947...` was created;
- API, Web, database, and edge are healthy;
- release marker and running image revisions match the deployed master release;
- public readiness and HTTP security-boundary checks passed;
- private-beta host readiness passed;
- backup timer and runtime watchdog are active;
- subscription enforcement remains disabled;
- non-destructive direct-API smoke passed;
- non-destructive authenticated Web/BFF smoke passed.

Still not proven by that deployment:

- objective cross-user isolation with a second controlled identity and real controlled foreign-owned fixture;
- controlled Plaid lifecycle plus human Hosted Link completion;
- representative statement semantic/OCR acceptance;
- account-deletion proof;
- controlled reboot proof;
- external alert receipt;
- clean-host restore against the real off-host repository;
- provider-enforced immutable storage;
- qualified legal review.

A local-only VPS commit reported as `f9000be` contains a Web-smoke newline-handling fix, but it is not pushed, merged, or deployed and therefore is not repository or production authority.

Production truth must always distinguish:

- merged to `master`;
- exact-head CI passed;
- guarded deployment completed;
- production verification completed;
- human/provider/operator acceptance completed.

Do not collapse these into one status.

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

This milestone contains proposed product direction and should be validated with user feedback before every subfeature becomes a commitment.

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

# 19. Milestone 13 — AI shadow evaluation

Priority: P2/P3

Goal: Evaluate AI usefulness safely before any runtime authority increases.

## 19.1 Private corpus

Keep outside Git.

Target representative corpus:

- multiple providers;
- clean PDFs;
- scanned PDFs;
- image statements;
- utilities;
- telecom;
- insurance;
- subscriptions;
- promotions;
- fees;
- ambiguous line items.

## 19.2 Ground truth

Reviewer-approved fields.

Measure:

- precision;
- recall;
- candidate coverage;
- provider-specific failures;
- false explanation candidates;
- cost;
- latency.

## 19.3 Deterministic baseline

Always compare AI against deterministic parser baseline.

AI is justified only where it provides measurable incremental value.

## 19.4 Runtime policy

Even if shadow metrics pass:

- do not automatically enable AI-derived persistence;
- require separate architecture/security/product review;
- validate cost controls;
- preserve evidence requirement;
- preserve deterministic final validation.

---

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

Metrics should answer whether FullWorth is actually delivering value.

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
- support contacts caused by incorrect financial facts.

Do not optimize vanity metrics at the expense of accuracy.

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

The current release is already deployed and healthy. The next sequence is acceptance-first rather than feature-first.

## ~~Step 1~~

~~Review and safely upstream the local-only Web-smoke newline fix reported as `f9000be`.~~

Requirements:

- ~~obtain the exact diff from the VPS or reproduce the change deliberately in a repository branch;~~
- ~~do not treat the local commit as authoritative until reviewed;~~
- ~~merge through `development` only after the full exact-head CI gate;~~
- ~~do not redeploy merely for a smoke-harness-only fix unless production runtime behavior actually depends on it.~~

Completed through the reviewed repository implementation in PR #197; the old VPS-only commit remains historical and non-authoritative.

## Step 2

Run objective cross-user ownership proof against release `cbcf261e13636f0330cb9d7be2ce413871e413aa`.

Use:

- a second controlled identity;
- real controlled foreign-owned Bill Stream/statement/resource identifiers;
- expected 404/ownership-denial behavior.

Do not substitute guessed IDs.

## Step 3

Run the controlled Plaid lifecycle.

Verify:

- Hosted Link opens;
- a suitable controlled connection completes;
- connection becomes Active where expected;
- transaction sync occurs;
- update/reconnect behavior works;
- human Hosted Link completion is observed.

## Step 4

With explicit operator approval, run the controlled statement lifecycle.

Use representative operator-known fixtures.

Verify:

- upload;
- classification/extraction;
- OCR where applicable;
- Bill Stream matching;
- comparison;
- explanation;
- alert/state outcome;
- ownership.

## Step 5

Run disposable account-deletion proof against the same release.

## Step 6

Run controlled reboot proof.

Confirm:

- services recover;
- readiness returns;
- release marker remains correct;
- backup/watchdog timers remain healthy.

## Step 7

Run independent external alert receipt proof.

## Step 8

Run clean-host recovery against the actual off-host encrypted repository.

## Step 9

Configure and prove provider-enforced immutable/WORM/Object-Lock-equivalent protection.

## Step 10

Combine same-release evidence and run Internal Beta 0 on real controlled bills.

## Step 11

Only after trust metrics and remaining external gates are acceptable:

- invite trusted external beta users;
- continue PWA parity toward MAUI retirement;
- resume recurrence-quality and statement-explanation expansion;
- proceed toward revenue rollout.

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
