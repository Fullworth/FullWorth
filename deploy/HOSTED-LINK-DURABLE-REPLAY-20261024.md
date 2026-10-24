# Hosted Link durable replay — 2026-10-24

Related issue #912. Development base: `89ed6ca7fff246f297e5a6538ed2b0b25d0b642b`.
This is a focused partial recovery fix; no deployment or production incident is claimed.

## Finding and behavior

The completion service previously resolved a tracked session through EF identity resolution. A previously tracked Pending instance could conceal a durable Completed/Exited/Expired/Failed row from another request, or a successful commit whose acknowledgement was lost. That stale instance could trigger another provider poll/public-token exchange or overwrite terminal state.

Read owner-scoped durable session state with AsNoTracking before using tracked state. Return terminal evidence without provider access or rewriting the caller's tracked entity. For pending work, reject unsaved or stale lifecycle/credential/bank/expiry changes rather than discarding them. Recheck durable state after the awaited provider status request and before acting on its response. Completed-session bank evidence retains the existing fresh independent owner/status predicate; a disconnected or foreign bank does not become a connected result.

Pending expiry comparisons account for PostgreSQL microsecond timestamp precision. Ordinary caller timestamp edits and unrelated work are preserved. No monetary calculation, classification, schema migration, UI, shared existing workflow, context, roadmap or handoff changes.

## Regression evidence and boundaries

One new ordinary xUnit regression file also runs against a disposable real PostgreSQL 17 service through a new dedicated workflow. It covers tracked stale terminal state before/during provider polling, initial/update flows, fresh owned/attention/disconnected/foreign bank evidence, successful-commit followed by synthetic acknowledgement loss for new/existing/update connections, stale/unsaved pending changes, missing/deleted/foreign sessions, cancellation, ordinary completion/replay and unfinished polls. Foreign-bank references are rejected by PostgreSQL's existing composite owner foreign key; InMemory additionally verifies the independent application defense against forged persisted links. No constraint is bypassed. The cases verify retained owned/foreign financial history/cursors, caller work and no duplicate token exchange.

The PostgreSQL fixture requires explicit opt-in and a fixed loopback host/port/database/username, uses synthetic provider data and an ephemeral GitHub service, and never uses production credentials or deletes existing databases. Exact source checkout is pinned in the dedicated workflow. Final commit, test counts, checks and tested shared-CI merge candidate are recorded in the PR after execution.

A SavedChanges interceptor deliberately throws AFTER a successful real database save to simulate a lost client acknowledgement. This proves reconciliation of a known committed outcome; it is not network-level commit fault injection, provider rollback or proof that every ambiguous outcome is recoverable.

## Remaining release blockers

#912 remains open: provider-success before a FAILED/unknown database commit can leave a Pending session after a consumed public token. The read/check/provider/write boundaries here are not an atomic lease or lock. A concurrent change after the last read, simultaneous provider exchanges, safe fresh-Link UX/reconciliation without durable success, and controlled Plaid Sandbox acceptance require separate work. This pass does not claim them solved.

#940/PR #941 remains independent lifecycle concurrency work, not duplicated here. #762/#293 still need private known-truth recurring-bill/import/payroll/provider acceptance. #251/#258 need physical Android/iPhone Home Screen/Face ID evidence on the actually served release. #291/#669 still require provider-enforced immutable off-host protection; real clean/compromised-host database, statement and both-keyring recovery; controlled exact-release two-owner/provider/document/security acceptance; release provenance and qualified legal/licensing review.

Passing dependency review is separate from a complete security-alert inventory, which this connector does not expose. The last recorded verified production SHA remains `dd31836703bd110a30f303ed3ce9fd13c0f61d82`; no fresh host inspection or changed deployment is claimed.

No merge, deployment, secrets, branch deletion, issue closure or production data operation.
