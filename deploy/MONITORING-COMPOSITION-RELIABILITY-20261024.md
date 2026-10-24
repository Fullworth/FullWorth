# Monitoring composition reliability batch — 2026-10-24

Development base: `f031ab2a120bc526f8f5dee3836e36e402f31302`. Independent of open PRs #941/#948 and the UI work.
Repository-only changes; no deployment or production incident is claimed.

## Findings and fixes

1. Payday refresh stops on the first failing payroll fact/plan/alert. Since it processes older facts first, one repeatedly failing paycheck can block later healthy candidates. Attempt every independent candidate once, keep request cancellation immediate, stop on missing user-level schedule, and propagate the original first exception after partial work instead of reporting success. Persisted plan/alert replay remains the retry boundary.
2. Payday alert idempotency reads through EF's identity map. Stale or unsaved immutable content can be mistaken for a durable matching alert. Read persisted owner-scoped immutable content without tracking; reject matching pending insert/delete/identity/content changes without discarding them. Read/dismiss/timestamp acknowledgements remain caller work. If their durable alert row was removed elsewhere, reject the stale acknowledgement before writing a replacement; preserve it for explicit reload/resolution. Strict immutable-content comparison and the existing unique owner/type/source-event index remain.
3. Connection-health reconciliation ignores pending additions and treats pending deletions as live slots. This can create duplicate managed aggregate alerts, or remove one despite continuing attention. Merge persisted and tracked live identities, exclude deleted/currently unrelated rows, preserve the existing deterministic primary slot and acknowledgement policy, and restore the entire touched pending state after failure. Canceling an Added-only health alert is a no-save operation for unrelated work.

No financial arithmetic, classification, new cadence, schema, authentication, ownership or deployment-policy change. The 500-bill/coverage/allocation limits remain intact; investigation did not justify expanding them.

## Evidence

New regression files form one serialized xUnit class to avoid concurrent schema creation in the disposable PostgreSQL service. The same cases run in ordinary .NET InMemory fixtures and a dedicated exact-source PostgreSQL 17 workflow.

The batch covers independent fact/run/alert failures at every position; non-request cancellation versus request cancellation; sibling persistence and retry; first-error preservation; missing schedules and unsupported/deduplicated payroll facts; durable/unsaved immutable-alert conflicts; read/dismiss preservation; strict identical/conflicting insert races; pending connection-health additions/deletions/duplicates; save-failure restoration; caller tracking and subsequent saves; owner/foreign bank credentials, cursors and transaction-history preservation.

The PostgreSQL insert race uses the real unique index. InMemory injects a corresponding DbUpdateException because it does not enforce relational uniqueness. Other deterministic save failures are injected before save; this is transaction/cleanup evidence, not ambiguous-commit recovery or real-provider acceptance. Planning failure-isolation fixtures use empty bill sets with synthetic payroll facts; full existing tests cover monetary recommendations. No provider calls or production financial data are involved.

The dedicated workflow requires explicit opt-in and fixed loopback host/port/database/username, uses pinned existing actions/PostgreSQL digest and read-only repository permission, and owns an ephemeral service. It never uses production credentials or deletes existing databases. Final executed counts, source SHA, tested merge candidate and results are maintained in the PR.

## Remaining release blockers

#912 consumed-token/failed-or-unknown-commit recovery still requires work; #948 only reconciles durable completion. #940/#941 remains independent in-flight bank lifecycle concurrency work. #762/#293 need private known-truth bills/import/payroll/provider acceptance. #251/#258 need physical Android/iPhone Home Screen/Face ID acceptance on the actually served release.

#291/#669 still require provider-enforced immutable off-host protection, actual clean/compromised-host recovery of database/statements/both keyrings, controlled exact-release two-owner/provider/document/security acceptance, release provenance and qualified legal/licensing review. CI fixtures do not establish complete security, hard resource isolation or successful real production recovery. Direct security-alert inventories are unavailable through the connected tools.

Master/last independently recorded deployment remains `dd31836703bd110a30f303ed3ce9fd13c0f61d82` unless fresh release/host evidence establishes otherwise. No fresh host inspection, acceptance attestation, merge, deployment, secrets, branch deletion, issue closure or production data operation.
