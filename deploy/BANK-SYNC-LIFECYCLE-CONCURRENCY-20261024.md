# Bank synchronization lifecycle concurrency — 2026-10-23 America/Denver

Finding #940; source PR #941.
Development base: `6f22df46d9851199350587ca379a143be8b8a1bb`.
This is proposed backend work, not deployment or production incident evidence.

## Supported failure

PlaidAccountSyncService and PlaidTransactionSyncService load a connection, await provider data, and later save Active status plus metadata/transactions/freshness/cursors. PlaidConnectionSyncCoordinator also saves RequiresAttention after provider errors. The original BankConnection mapping has no optimistic concurrency predicate on status or protected credential.

A competing request can commit disconnection or replace credentials while a sync is in flight. A stale save could then overwrite winning lifecycle/financial state. Recent exchange lifecycle fixes #917/#920 address a different write path and are retained.

## Fix

- Connection Status and ProtectedPlaidAccessToken are EF concurrency tokens. Stale writes fail at the actual UPDATE predicate; account and transaction batches remain inside EF's atomic relational SaveChanges transaction.
- An owner-scoped no-tracking read checks durable lifecycle and credential identity before provider access, including reuse of a stale tracked context.
- Direct retries from RequiresAttention still work; disconnected/missing/foreign/stale-credential connections fail closed.
- Existing per-attempt snapshots detach failed candidates and restore caller tracking. A subsequent caller save must not persist failed sync state.
- Update the EF model snapshot for concurrency semantics only. No columns, data conversion, migration DDL, classifications, financial arithmetic or production settings are changed.

No Web, VisualTests, shared workflow or context/roadmap/handoff edits. The new workflow is dedicated to this backend relational fixture.

## Regression evidence

The executable entry point is separate from the scenario helper linked into the ordinary test assembly, avoiding test-host entry-point warnings. The ordinary suite adds 12 cases: direct/stale disconnected account/transaction preflight; stale protected credentials; foreign owners; normal RequiresAttention retries; both model concurrency predicates.

The dedicated executable runs **26 scenarios against disposable real PostgreSQL 17**, using the existing production PostgreSQL image digest and pinned existing action SHAs:
- 16 races: account/transaction sync × provider-response/pre-save interleaving × disconnect/credential replacement × initial Active/RequiresAttention.
- 4 direct/stale disconnected preflight cases.
- 2 stale-credential preflight cases.
- 2 cross-owner negatives.
- 2 ordinary successful sync cases.

Race cases verify the winning lifecycle, credential, cursor and freshness; rollback of added/modified/removed financial rows; preservation of caller work; a later save; no provider access on a stale tracked retry; unchanged foreign bank/account/transaction history.
Synthetic provider responses exercise actual EF/Npgsql concurrency predicates and transaction rollback. They are not live Plaid acceptance.

The executable requires an explicit fixture opt-in, loopback host, fixed disposable database and fixture username. It never connects to production, exports data or deletes live contents. The GitHub job owns its ephemeral service; no hosted credentials or secrets are changed.

The dedicated PostgreSQL workflow checks out the exact PR head explicitly and runs both the 26 relational scenarios and all 12 new standard lifecycle regressions. Shared CI validates GitHub's merge candidate against the current development branch; its tested merge commit and backend blob parity are recorded separately in the PR. Intermediate results are not final-head verification.

## Remaining release blockers

#912 remains the provider-consumed one-time public-token/ambiguous-commit recovery problem; #913's atomic initial save is already merged, and this concurrency fix does not solve that external provider outcome.
#762 and #293 need private known-truth bill/import/payroll/provider acceptance.
#251/#258 need physical Android installed PWA and iPhone Home Screen/Face ID acceptance on the actually served release.
#291/#669 require immutable off-host protection, real clean/compromised-host data/statements/both-keyring recovery, exact-release controlled two-user/provider/document/security acceptance, release provenance and qualified legal/licensing review.
Current master/last recorded deployment remains `dd31836703bd110a30f303ed3ce9fd13c0f61d82`; no new host inspection is claimed.
Available dependency checks are separate from security-alert inventories not exposed by the connector.

No merge, deploy, secret change, branch deletion, issue closure, production database mutation or acceptance attestation. Real PostgreSQL fixture rollback is not proof of unknown-commit recovery, complete security or physical/provider acceptance.
