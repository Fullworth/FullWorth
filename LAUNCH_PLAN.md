# FullWorth revenue launch execution plan

Started: 2026-09-24. Target: 2026-10-23 (29 calendar days).

Owner objective: a finished product that can be promoted and earn revenue, using the roadmap through section 32 as the scope. This is a target, not a promise that unverified production, provider, legal, or device gates will pass on a date.

## Scope and completion rule

`FULLWORTH_ROADMAP.md` remains the detailed requirements source. Its numbered sections are not 32 sequential implementation steps: section 30 contains the operational critical path, and section 32 defines maintenance. This plan covers all sections without interpreting later/conditional product directions as already delivered. Any proposed scope deferral requires an explicit owner decision; it is not completion.

Ship only behavior supported by source, tests, and the required real-environment evidence. A green PR is not a deployed release. An evidence recorder is not proof that its human/provider steps happened. Keep billing and subscription enforcement disabled until the documented rollout gates pass.

## Working baseline

- Repository: `Fullworth/FullWorth`. Feature branches target `development`; verified release promotions target `master`.
- Integration baseline inspected: `e3f1bb96454ca1dba08e1a5d001ca8053d077e68`. All 713 .NET tests passed locally on 2026-09-24. This does not establish container, Android, production, or provider acceptance.
- PR #284 addresses pending transactions re-entering core discovered bill histories. Its three focused regression tests passed locally; merge requires final-head CI.
- Current recurrence detection explicitly models only monthly cadence. Roadmap section 9 remains incomplete.
- Historical production release claims in context/roadmap are not current deployment verification. Select and verify a release candidate before collecting same-release evidence.
- The owner has an owner account and a test account. Controlled foreign-owned resources, safe credential access, disposable deletion identity, and server access still need verification.
- The owner has no Android device available at present. Installed Android acceptance and MAUI retirement remain open.

## Dated delivery sequence

| Window | Deliverables | Exit evidence |
| --- | --- | --- |
| Sep 24–27 | Reconcile current branches/CI, resolve release security alerts, establish controlled acceptance access, inventory every launch requirement against current code | Current commit references, reviewed dependency findings, named open gates; no invented production results |
| Sep 28–Oct 4 | Close recurring-bill cadence/confidence/duplicate gaps; validate statement explanations and alert behavior; measure PWA performance | Deterministic regression cases, realistic known-answer fixtures, final-head CI, browser measurements |
| Oct 5–11 | Promote a verified candidate; run same-release ownership, Plaid, statement, deletion, recovery, external-alert and device acceptance; run Internal Beta 0 | Private release-matched evidence and observed human/provider outcomes; defects fixed and affected evidence repeated |
| Oct 12–18 | Prove commercial offer, payment and cancellation lifecycle, entitlements, support/privacy experience; complete qualified legal review and trusted beta | Provider and application states agree, controlled beta findings resolved, approved commercial/legal decisions |
| Oct 19–23 | Verify launch candidate, accessibility/localization, capacity and operational response; reconcile all roadmap requirements and publish only supported claims | Final readiness decision with each required gate proven or explicitly unresolved; phased billing activation only after rollout gates |

Windows overlap when work is independent. Reassess the target when a prerequisite cannot be obtained in time; do not compensate by dropping protections or marking requirements complete without evidence.

## Roadmap coverage and proof required

All rows below are open unless their individual evidence proves completion. Existing implementation is a starting point, not blanket acceptance.

| Sections | Work and completion evidence |
| --- | --- |
| 1–5 | Preserve product direction, security invariants, authority order, and sequencing; reconcile current source/CI with production and planning claims |
| 6 | Review active branches, preserve useful unmerged work, keep context current; merge only required checks passing on the final head |
| 7 | Measure startup, render/interop behavior, asset and list budgets; prove responsiveness on a mid-range installed Android PWA without financial offline caching |
| 8 | Complete the per-feature PWA parity matrix and installed-device/provider interactions; retire MAUI only after the explicit cutover gate |
| 9 | Implement and verify supported recurrence classes, skipped periods, amount behavior, duplicate prevention and explainable confidence; weak evidence stays unknown/withheld |
| 10 | Known-answer statement/OCR fixtures, secure bounded ingestion, unambiguous matching, deterministic comparisons and evidence-backed multi-factor explanations |
| 11 | Meaningful, idempotent alerts; read/dismiss, noise thresholds, actionable failures and notification preferences; no sensitive lock-screen leakage |
| 12 | Validate proposed financial-home, cash-flow, net-worth, subscription, spending and liability directions with the owner; implement committed features only with reliable source/freshness semantics and no duplicate financial truth |
| 13 | Verify settings, privacy, export, disconnect and deletion paths; account access and recovery remain usable |
| 14 | Same-release authenticated Web/API, objective cross-user resources, Plaid, statements, deletion, reboot, clean-host recovery, immutable backups, independently received alerts and legal review |
| 15 | Run Internal Beta 0 with controlled users and known bills; record accuracy, false alerts and failures, then resolve acceptance defects |
| 16 | Approve offer/prices/currency/refund/support terms; verify Stripe Checkout, Portal, signed webhook/retries and access transitions; stage enforcement per `REVENUE_READINESS.md` |
| 17 | Trusted external cohort, privacy-safe support tooling and actionable feedback; resolve correctness and trust failures before broad promotion |
| 18 | Reliability, sanitized observability, bounded capacity, safe migration/recovery and background processing demonstrated for the launch workload |
| 19 | Private reviewer-approved representative corpus; compare shadow extraction with deterministic baseline for accuracy, cost and latency; AI-derived persistence remains separately gated |
| 20–21 | Meaningful unit/integration/browser coverage, query-shape-backed performance, ownership constraints and deletion/retention/recovery lifecycle |
| 22–24 | Verify navigation, loading/empty/error states, financial hierarchy, keyboard/focus/dialogs/contrast/zoom/reduced motion/touch/status announcements and localized launch copy |
| 25–26 | Feature → development → master → guarded deploy; satisfy the definition of done for each changed work type and verify the deployed release |
| 27 | Measure setup, activation, quality, engagement and trust using privacy-safe evidence; define denominators and observation windows before claiming outcomes |
| 28–29 | Maintain compatibility debt register; any committed legacy migration needs migration/rollback/compatibility tests and production coordination; never mass-rename persisted identifiers |
| 30 | Complete the operational critical path on the selected current candidate, not a stale hardcoded historical release |
| 31–32 | Avoid roadmap anti-patterns, update completion evidence and current snapshot, and keep detailed procedures in their existing operational documents |

## Owner and external dependencies

- Confirm an existing server connection/profile and a protected mechanism for controlled test credentials. Do not place credentials in chat, Git, or ordinary output files.
- Prepare two controlled accounts with known ownership-separated resources; use a separate expressly disposable identity for deletion testing.
- Arrange installed Android access; iOS checks remain required where available under the roadmap's parity rules. Emulator/browser results do not establish real-device acceptance.
- Supply representative statements with independently known facts through private fixture storage. Never commit customer statements or raw extracted financial text.
- Complete human bank/provider authorization when required and obtain independently observed alert delivery and backup-provider immutability evidence.
- Decide the commercial offer and support/refund terms, arrange qualified Terms/Privacy review, and identify controlled beta participants.

## Execution log

| Date | Evidence/change | Remaining limitation |
| --- | --- | --- |
| 2026-09-24 | `SECURITY.md` committed on master as `a4d60bc`; GitHub recognizes the policy and private reporting is available | Policy publication is not product security acceptance |
| 2026-09-24 | Integration baseline: 713/713 .NET tests; PR #284: three new focused tests passed | Final-head CI, merge, release promotion and production acceptance remain separate |

Update this log with commit/PR identifiers and evidence summaries. Keep secret-bearing and financial acceptance artifacts outside the repository. Never mark the overall product finished based only on this plan or its checklists.
