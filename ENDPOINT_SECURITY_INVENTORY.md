# Endpoint security inventory

This document describes the executable endpoint security inventory enforced by
`EndpointSecurityInventoryTests`. The inventory is generated from the runtime
ASP.NET Core `EndpointDataSource` for both the API and Web hosts, so it records
the method, route template, authentication metadata, and effective rate-limit
class that the application actually exposes.

## Current snapshot

| Host | Method-route exposures | Authenticated | Explicit anonymous | Implicit anonymous | Named rate limit |
| --- | ---: | ---: | ---: | ---: | ---: |
| API | 83 | 69 | 6 | 8 | 23 |
| Web | 115 | 86 | 5 | 24 | 7 |
| **Total** | **198** | **155** | **11** | **32** | **30** |

The complete, reviewable list is embedded in
`FullWorth.Tests/Security/EndpointSecurityInventoryTests.cs`. A route addition,
removal, HTTP-method change, authentication change, or rate-limit metadata change
produces an exact CI diff and requires an intentional snapshot update.

## Classification semantics

Authentication values mean:

- `authenticated`: the endpoint has authorization metadata.
- `anonymous-explicit`: the endpoint deliberately has `IAllowAnonymous`.
- `anonymous-implicit`: the endpoint has neither authorization nor explicit
  anonymous metadata. This includes public pages and framework-mapped Identity
  API routes whose access behavior is defined by the endpoint implementation.

Rate-limit values mean:

- `named:<policy>`: the endpoint selects an application policy explicitly.
- `global:300-per-minute`: the API endpoint inherits the API host's global
  partitioned limiter (authenticated user ID when available, otherwise source IP).
- `none`: the Web host has no endpoint-level or global limiter for that route.
  BFF calls still reach API endpoints where API authorization and rate limiting
  apply; this value must not be interpreted as an API bypass.
- `disabled`: rate limiting is explicitly disabled. No application endpoint is
  currently classified this way.

The OpenAPI document route appears in the test snapshot because the test host runs
in the Development environment. Production maps OpenAPI only in Development, so
the inventory does not claim that route is production-exposed.

## Security review by endpoint family

| Endpoint family | Access | Effective control | Review note |
| --- | --- | --- | --- |
| API health | Explicit anonymous | API global limiter | Low-cost liveness/readiness responses; no customer data |
| API authentication | Mixed | `authentication` (20/min/IP) | Credential and recovery flows use a dedicated abuse boundary |
| API statement upload | Authenticated | `statement-upload` (12/10 min/user) | Bounds expensive parsing/storage work |
| API statement download | Authenticated | `statement-download` (30/10 min/user) | Bounds repeated sensitive-file reads |
| API account export | Authenticated | `account-export` (5/hour/user) | Bounds expensive, privacy-sensitive export work |
| API subscription writes | Authenticated | `subscription-redemption` (5/10 min/user) | Bounds checkout, portal, sync, and access-key activity |
| Stripe webhook | Explicit anonymous | API global limiter plus Stripe signature verification | Signature behavior is tested separately; replay/idempotency remains a dedicated review item |
| Other API account, admin, financial, planning, Plaid, and alert routes | Authenticated | API global limiter | Exact route/auth state is protected by the snapshot; further cost-based policy refinement remains reviewable work |
| Web public pages | Anonymous | None at Web layer | Primarily page rendering; authentication form posts use the named policy below |
| Web authentication posts | Anonymous or authenticated | `web-authentication` (20/min/IP) | Bounds login, registration, recovery, and external-auth completion |
| Web application pages and BFF | Authenticated | None at Web layer; downstream API controls apply | Unsafe BFF requests also require antiforgery; statement upload has its own Web policy |
| Web statement upload BFF | Authenticated | `statement-upload` (12/10 min/user) | Prevents the Web proxy from becoming an unbounded upload path |

## Change procedure

When adding or changing an endpoint:

1. Run the security inventory tests and inspect the exact snapshot diff.
2. Classify data sensitivity, mutation behavior, computational cost, and abuse
   potential.
3. Require authentication unless anonymous access is deliberate and documented.
4. Add a named limiter when the host default is not an adequate abuse boundary.
5. Update the snapshot and this document in the same focused pull request.

Static assets and framework infrastructure endpoints are excluded. The inventory
contains route templates only; it never records customer identifiers, request
values, credentials, or secrets.
