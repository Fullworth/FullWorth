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

The current known direct sibling-service dependency is Bills to Plaid in `BillMonitoringRefreshService.cs`. That edge is temporarily allowlisted so the guard can land without a large rewrite. The allowance is a ceiling, not permission to add similar dependencies elsewhere.

New sibling-service references fail CI and should use an explicit contract instead. If the existing allowlisted edge is removed, the test also fails until the stale allowance is deleted in the same change.

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
