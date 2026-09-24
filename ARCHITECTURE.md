# FullWorth architecture boundaries

FullWorth is built as a modular monolith first, with boundaries strong enough that a module can later move to its own process or service without forcing a whole-system rewrite.

## Core rule

Modules depend on explicit contracts, not another module's implementation details.

Treat every cross-module connection as an airlock:

- deny by default;
- expose the minimum contract needed;
- authenticate and authorize user-data entry points;
- validate all inputs crossing a boundary;
- keep secrets and financial evidence out of logs and telemetry;
- make retries idempotent where duplicate delivery is possible;
- use bounded timeouts and cancellation for external calls;
- preserve ownership isolation in persistence and queries.

## Current coarse project graph

Allowed production project references:

- `FullWorth.Core` -> no FullWorth project references.
- `FullWorth.API` -> `FullWorth.Core` only.
- `FullWorth.Web` -> `FullWorth.Core` only.
- transitional MAUI `FullWorth.csproj` -> `FullWorth.Core` only.

Test-only references:

- `FullWorth.Tests` may reference `FullWorth.API`, `FullWorth.Core`, and `FullWorth.Web`.

Forbidden examples:

- Core referencing API, Web, MAUI, or Tests.
- API referencing Web or MAUI.
- Web referencing API or MAUI.
- MAUI referencing API or Web directly.
- production projects referencing Tests.

CI enforces this graph through `deploy/tests/project-boundary-tests.sh`.

## Service-module coupling ratchet

CI also scans the named API service domains through `deploy/tests/service-module-boundary-tests.sh`.

Direct sibling-service dependencies are no longer allowlisted. New sibling-service references fail CI and should use an explicit contract instead.

The first extracted airlock is bank-data synchronization:

- Bills depends on `IBankDataSyncGateway` in the neutral Contracts area.
- Plaid owns `PlaidBankDataSyncGateway`, which implements that contract by delegating to the existing Plaid synchronization coordinator.
- Bills does not know Plaid implementation types and does not construct the Plaid coordinator.
- The composition root in `Program.cs` selects the active implementation.

This is the pattern for future extraction work: consumer modules depend on narrow contracts; provider modules own implementations; the composition root connects them.

## Domain modules inside the API

The API currently contains domains such as Accounts, Bills, Identity, Plaid, Statements, and Subscriptions. Folder boundaries alone are not considered isolation.

The migration direction is:

1. inventory cross-domain calls and table ownership;
2. define public contracts for a bounded domain;
3. hide implementation behind that contract;
4. add negative architecture/security tests;
5. move direct cross-domain database access behind the owning module;
6. repeat one domain at a time.

Do not perform a big-bang rewrite.

## Data ownership

The first explicit data ownership map covers the highest-risk finance domains:

- Plaid owns `BankConnections`, `BankAccounts`, `BankTransactions`, and `PlaidLinkSessions`.
- Bills owns `BillStreams` and `BillAlerts`.
- Statements owns `BillStatements`, `BillLineItems`, `BillChanges`, `BillStatementUploads`, and `BillStatementAiEvaluations`.

CI enforces service-module persistence ownership through `deploy/tests/data-ownership-boundary-tests.sh`. The Bills, Plaid, and Statements service modules now have zero direct cross-owner DbSet exceptions. New cross-owner service-table access fails CI.

Controller/application orchestration is separately ratcheted by `deploy/tests/controller-data-ownership-boundary-tests.sh`. Existing controller cross-owner access is frozen as exact module/table/file migration debt. New controller cross-owner DbSet access fails CI, and stale exceptions must be removed in the same change that removes the dependency.

Statements no longer reads or mutates Bills-owned `BillStreams` or `BillAlerts` directly. Bill Stream context crosses `IBillStreamReadGateway`, and statement-driven alert desired state crosses `IBillAlertReconciliationGateway`; Bills owns alert persistence and stages those changes inside the shared modular-monolith unit of work.

Bills no longer reads Statements-owned statement/change tables directly from its controller. Bill-detail history crosses `IBillStatementHistoryReadGateway`; Statements owns the user-scoped statement/change queries and returns immutable read projections.

Bills no longer reads Plaid-owned bank tables directly:

- connection-health and refresh-scheduling queries cross `IBankConnectionReadGateway`;
- recurring-bill discovery reads immutable transaction projections through `IBankTransactionDiscoveryGateway`;
- bill-link changes cross the same transaction gateway with ownership checks and expected-current-link concurrency checks;
- the Plaid gateway stages link changes without saving so the current modular-monolith unit of work can still commit Bill Streams, alerts, and transaction links atomically.

The staged-write behavior is an explicit same-process atomicity contract. If the bank-data module is later moved behind a network boundary, replace that mechanism with an outbox/coordinator or a Bills-owned association model rather than silently giving up atomicity.

Those exceptions are migration debt, not approved architecture. The ratchet also fails when an exception disappears until its stale allowance is removed, so the baseline can only tighten.

Every user-owned resource remains ownership-scoped. Cross-module convenience is not permission to bypass ownership checks.

A module that owns data is responsible for enforcing the rules around that data. Other modules should request behavior through the owning module's contract rather than query private tables as an implementation shortcut.

## Scale direction

The modular monolith must remain compatible with future scale-out:

- stateless request processing where practical;
- explicit background-job boundaries;
- queues for long-running or bursty work when justified by load;
- bounded database connection pools;
- indexed ownership-scoped queries;
- cache keys and caches that preserve tenant/user isolation;
- rate limits at externally reachable and expensive boundaries;
- health/readiness checks per independently movable workload;
- safe, forward-compatible schema migrations;
- structured telemetry without secrets or raw financial evidence.

Splitting a module into a service is a capacity or isolation decision, not a default design goal.

## Security rule

Modularization must never weaken authentication, BFF isolation, antiforgery, ownership checks, token protection, statement protection, provider-secret handling, backup protections, or compatibility boundaries.

Assume a module can fail or be compromised. Give it only the access needed for its responsibility and design so failure is contained rather than inherited by the entire system.
