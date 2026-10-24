# Backend evidence reliability batch — 2026-10-24 UTC

Development base: `3da853da5d4d58e27a30c335e60673dcc5aa781e`.
Source PR: #910. Related audit/release controls: #819, #669, #291.
This is proposed development work. No production acceptance is recorded.

## Evidence-backed gaps and resulting behavior

| Gap | Evidence in the pre-change source | Fix and regression evidence |
| --- | --- | --- |
| Overlapping scoped Planning reads | PlanningBillChangeWatchService starts three tasks before awaiting them; all production adapters receive the same scoped FullWorthDbContext. Real asynchronous database reads can overlap although synchronously completed InMemory tests hid it. | Sequential awaits; explicitly gated and yielding adapter regressions reject overlap and verify failure stops later work. This does not claim a PostgreSQL runtime stress test. |
| Change watch fails beyond 500 active bills | ListOwnedActiveAsync is unbounded; statement-fact, change-fact and allocation adapters reject a request exceeding 500 IDs. | Read all IDs in sequential batches of at most 500 without dropping bills; 0/1/499/500/501/1001 boundaries and actual scoped adapters with foreign-owner evidence. Monetary formulas and evidence qualification remain unchanged. |
| Failed upload metadata can outlive deleted file | UploadStatement deletes the file in its SaveChanges catch, but leaves the Added upload entity tracked. | Detach only the failed metadata candidate before existing file cleanup. Database failure, cancellation and unexpected pre-commit exceptions preserve caller Added/Modified/Deleted work; subsequent save writes no orphan, explicit retry creates readable file metadata. |
| Failed statement graph can be flushed by later save | PersistAsync stages statements, line items, changes, alerts and upload completion without restoring them on any exception. A later caller/status save can flush a failed attempt. | Snapshot scalar current/original values, states, modified flags and navigation references/collection membership; detach only newly tracked attempt entities and restore baseline on failure. New statement, duplicate no-enrichment, duplicate enrichment and same-cycle correction tests inject database, cancellation, unexpected save and after-staging failures; preserve foreign evidence and caller work; retry and replay are idempotent. |
| Association staging ignores pending assignments/removals | StageAssignmentsAsync queries only persisted rows. Repeated Added assignments are invisible; pending Deleted instances cannot be replaced under the composite key; persisted fallback accepts bill targets pending deletion. | Combine tracked current assignments with queried rows, exclude deleted targets before mutation, and reuse a removed composite-key entity when reassigning. Entire batch remains validated before mutation; stale expectations and original concurrency values are retained. Opaque transaction IDs remain at the existing Bills boundary; no cross-module BankTransactions reads are added. |
| Statement change staging ignores current unit of work | ReconcileAsync reads persisted history/line items/change rows, ignoring Added records and pending deletions. Repeated staging can create duplicate changes and alerts before one save. | Combine owner/bill-scoped tracked history, prefer tracked line-item values, exclude pending deletions, reuse staged change rows deterministically, restore a failed staging attempt. Regression cases cover pending/modified/deleted history and fee evidence, repeated reconciliation with acknowledgments, foreign history and failures after alert staging. The comparison/classification algorithms are unchanged. |
| Processor mistakes failed writes for invalid documents | Its document-error catch includes status and financial graph saves. InvalidOperationException from persistence can mark a valid document Failed; canceled/failed status writes leave terminal state tracked. | Perform persistence outside the document-validation catch and restore failed state writes. 33 cases span Uploaded/Processing, initial transition, OCR, incomplete/review evidence, genuine document failure, complete persistence, database errors, cancellation and unexpected writes. Genuine invalid documents still reach Failed only after a successful status save. |

## Regression inventory

- ChangeWatchReadReliabilityTests: 14 cases.
- AssociationUnitOfWorkReplayTests: 24 cases.
- StatementUploadWriteRecoveryTests: 6 cases.
- StatementGraphFailureRecoveryTests: 32 cases.
- StatementChangeStagingReplayTests: 12 cases.
- StatementProcessingWriteRecoveryTests: 33 cases.
- Total: **121 new regression cases**, six dedicated new test classes plus a dedicated test support file.

These are scenarios across seven gaps, not 121 distinct defects.
The first checkpoint exposed an incorrect root-provider resolution in the new upload fixture; the fixture now resolves UserManager from a proper scope. No production checks were weakened to make the tests pass.
Checkpoint tests exposed incomplete graph restoration, including required-relationship severing when a statement was detached before its line items. Recovery now restores navigation baselines with change detection suspended and detaches dependents before principals; the full restoration assertions remain and also check references and collection membership. Final exact-commit GitHub verification is recorded in the PR.

## Preserved behavior and boundaries

No edits to FullWorth.Web, FullWorth.VisualTests, shared workflows, roadmap/context/handoff files, schemas or migrations.
No financial arithmetic, classification, cadence policy, saved plan history, provider endpoints, credentials or authentication controls are changed.
Successful SaveChanges retains the existing shared-context unit-of-work contract; no hidden staging save or global ChangeTracker.Clear is added.
The processor's per-upload background scopes, scheduling and existing bounded backoff remain unchanged.
Tracker restoration is in-memory pre-commit recovery, not a remote transaction rollback or proof of recovery from an unknown/acknowledgment-lost commit.
PDF/OCR/extraction fixtures test orchestration; they do not establish native parser correctness or real statement/provider acceptance.

## Remaining release blockers

1. Review/integrate proposed source and existing #894 work without duplicating source PRs; then rerun checks and release artifact/SBOM/attestation verification on the exact authorized release commit. PR-gated artifact skips are not release provenance.
2. #886 still requires direct proof/design for the consumed Link token and separate exchange/terminal-session commits. This batch does not invent replay or store public tokens.
3. #762 requires private known-truth expected bills, import/freshness/rejection evidence and account/cadence decisions; #293 requires controlled real-provider posted payroll, owner plan and notification acceptance.
4. #251 and #258 require physical Android installed-PWA and iPhone Home Screen/Face ID checks against the actually served release. Simulator/emulator builds and unsigned IPA packaging are not physical acceptance; production passkeys remain disabled.
5. #291 requires provider-enforced immutable/off-host protection, clean/compromised-host real-data recovery including statements and both key rings, and exact-release controlled security acceptance. Synthetic CI restore is not that evidence.
6. Qualified legal/licensing and the separate regulated-partner #296 program decisions remain human/provider gates. No funds movement, KYC, SSN collection or insurance claims are enabled.
7. Dependabot/CodeQL/secret-alert inventories are not available through the connected GitHub endpoint whitelist. A passing dependency job is not a complete security audit.

No merge, deploy, issue closure, branch deletion, production database mutation, secret change or acceptance attestation performed.
