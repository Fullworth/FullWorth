# Backend lifecycle reliability batch — 2026-10-23 America/Denver

Pinned base: `3da853da5d4d58e27a30c335e60673dcc5aa781e`. Focused source PR: [#907](https://github.com/Fullworth/FullWorth/pull/907).
This pass audits configuration writes, staged alert replay, current-evidence handling and failed maintenance.
It does not duplicate #894's already implemented bank/plan/discovery recovery fixes or #895's separate UI work.

| Finding | Implementation | Regression coverage |
| --- | --- | --- |
| #904: SemiMonthly accepts a null second payday | Explicit null rejection in API/service/core input validation; existing incomplete rows require reconfiguration before new immutable plans | Four authenticated invalid/null/day-boundary rejection cases with two owners, plus direct service rejection and valid 1/31 round trips; six additional core/legacy-row/repair/historical replay cases |
| #905: Failed planning-settings writes remain staged | Restore prior row current/original values/state/flags, detach only a failed insertion | 30 cases across schedule create/update, preference create/update/delete, database/cancellation/unexpected failures, caller work present/absent; two foreign-write negatives |
| #906: Clear -> restage can lose desired alerts | Exclude tracked Deleted rows from reconciliation; ignore pending-deleted NewBill rows for duplicate checks; honor fast-path cancellation | Six live-slot replay cases across three modes/tracking states, one NewBill replacement, two canceled fast paths |
| #908: Invalid latest evidence strands obsolete reminders | Owner-checked clear path needs no fabricated money/currency/name; unusable latest statement clears the managed slot without older fallback | Eight missing/invalid/inactive evidence cases, positive USD/lowercase-EUR identity behavior, foreign/missing-owner clearing negatives |
| #909: Failed reminder maintenance remains staged | Restore the prior scoped tracker metadata and discard only entries first tracked by the failed operation, retaining module contracts | 18 create/update/remove cases across database/cancellation/unexpected failures and caller edits; later caller saves, exact original metadata and retry |

Total: **81 dedicated regression cases**. These fixtures inject pre-commit failures using EF InMemory and authenticated test HTTP.
Fresh exact-head results and any subsequent combined-candidate checks belong in the PR; a written count is not a test result.

## Preserved invariants

- Financial arithmetic/calendar generation, classification, amount, currency-conversion, recurrence, payable-date and insurance rules are unchanged. Calculator input validation rejects incomplete SemiMonthly schedules. Existing stored invalid rows are preserved for explicit user repair; already saved immutable plans still replay unchanged.
- Existing PaymentDue creation validation/formatting/severity remains unchanged. Clearing an unsupported reminder does not mark a bill paid and does not change statement facts.
- Latest-statement ordering is unchanged; unusable latest evidence never borrows a convenient older obligation.
- Module ownership remains explicit. Statements reads its statement DbSet; Bills-owned alert staging stays behind the existing reconciliation contract. Recovery captures EF unit-of-work metadata without a new cross-domain DbSet query.
- Caller Added/Modified/Deleted rows and original values/modified flags survive failed operations. Successful saves retain the existing shared-context unit-of-work contract.
- Existing bank composite owner foreign keys were confirmed. No hypothetical malformed bank graph was used to justify redundant ownership changes.
- No UI, VisualTests, shared workflow, roadmap/context/handoff, database schema, production config, authentication flag or secret edits.

## Release limits

The current master/last recorded verified deployment remains `dd31836703bd110a30f303ed3ce9fd13c0f61d82`;
no fresh host verification or deployment is performed. The current integration candidate #894 remains separate until
this batch's exact blobs and any new integrated head are independently checked. Source PR checks do not certify a later combined tree.

Remaining real evidence is unchanged: #886 initial token-exchange/session completion partial-commit recovery;
#762/#293 private expected-payment, provider/payroll acceptance and additional-cadence decisions;
#251/#258 physical Android/iPhone PWA and Face ID;
#291/#669 provider-enforced immutable/off-host protection, actual clean-host database/statements/both-keyring recovery,
and exact-release controlled two-user/provider/document/security acceptance; qualified legal review.
#296 remains a separately gated future regulated-partner program.

Fixtures, emulators and disposable restore tests do not establish real provider/device acceptance, complete security,
hard resource isolation, PostgreSQL contention or recovery from an ambiguous committed exception.
No PR merge, deployment, secrets, issue closure or branch deletion is performed.
