# Bill confirmation reliability — 2026-10-24

Base: `67cff66012ed79ca1b7b7541664d7763fc876904`. Backend only, independent of #941/#948/#952 and UI work.

## Findings and fixes

Manual creation selected the first matching provider-name row even when several owned obligations shared that name. A name is not a unique obligation identifier. Return 409 without choosing or mutating a duplicate group; the existing exact-ID confirmation endpoint remains available.

Tracked stale lifecycle/category/provider state could hide durable changes and produce an apparent successful confirmation. Read durable owner-scoped candidates before consulting tracking. Reject conflicting pending/stale current or original values without discarding caller intent. Already-manual active no-ops remain no-save operations.

Confirmation conflicts cleared the entire shared EF tracker. Restore only this attempt's bill current/original values, state and modified flags for conflict, cancellation or other errors. A failed new manual insert is detached so a later unrelated save cannot persist it accidentally. Existing optimistic Source/IsActive database predicates remain the write-time boundary.

No financial arithmetic, classification rules, auth routes/rate limiting, ownership boundaries, schema or production settings changed. The existing category update policy is preserved. No Web, VisualTests, shared existing workflow, context, roadmap or handoff files changed.

## Regression evidence and limits

One serialized regression class spans fixture, failure, tracking and ambiguity files. The same scenarios execute in ordinary .NET InMemory tests and an exact-source disposable PostgreSQL 17 workflow. Cases cover matching/ambiguous provider groups, explicit identity confirmation, stale/pending states, failed new/existing saves, retained caller work and metadata, retry, actual conditional-update races, no-op behavior, validation, cancellation and foreign/missing identities. Owned/foreign bank credentials, history cursors and synthetic transaction history are checked.

The PostgreSQL race lets a separate context commit before the original UPDATE, exercising real concurrency predicates and relational rollback. InMemory injects the equivalent concurrency exception because its store cannot establish relational rollback of a mixed insert/update batch. Other failures are injected before save. Claims about real database behavior rely on PostgreSQL, not InMemory.

Explicit fixture opt-in and fixed loopback host/port/database/username are required. The new dedicated workflow uses existing pinned action SHAs/PostgreSQL digest, read-only repository permission and an ephemeral service. No production credentials, data or database deletion. Final source/check counts and shared-CI merge commit are recorded in the PR.

Reads are not locks: a new duplicate inserted after the name preflight, or a concurrent category/provider edit after the final read, is not serialized by this change. Source/IsActive predicates still protect the existing lifecycle update boundary. There is no provider rollback or ambiguous-commit recovery claim.

## Release blockers

#912 unknown/failed-commit token recovery; #940/#941 in-flight bank lifecycle concurrency; #948 durable Hosted Link replay; #951/#952 monitoring composition remain independent open work. #762/#293 need private known-truth bill/import/payroll/provider acceptance. #251/#258 require physical Android/iPhone Home Screen/Face ID evidence on the served release.

#291/#669 still require immutable off-host protection, real clean/compromised-host database/statements/both-keyring recovery, controlled exact-release two-owner/provider/document/security acceptance, provenance and legal/licensing review. Direct security alert inventories are unavailable through connected tools; dependency checks are not complete security. CI fixtures do not establish hard isolation, physical-device behavior or production recovery.

No merge, deployment, secrets, branch deletion, issue closure, production mutation or acceptance attestation.
