# Plaid response deadline readiness — 2026-10-22

## Evidence and fix

The Plaid client uses ResponseHeadersRead to enforce a 5 MiB response limit
without unbounded buffering. HttpClient.Timeout then ends at receipt of response
headers. Subsequent stream reads previously used only the caller's cancellation
token, so a bank returning headers and then stalling its body could hold a
scheduled monitoring pass until shutdown or caller cancellation.

Microsoft documents this boundary:
https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0

Each Plaid call now has one linked cancellation deadline covering outbound send,
headers and body consumption. It preserves a shorter HttpClient.Timeout and caps
longer/infinite settings at the existing default 100 seconds. Body progress does
not renew the deadline. Caller cancellation still propagates; requests are never
automatically replayed. Response streams, responses and timer sources are
disposed on success, provider error, cancellation and timeout. The existing
5 MiB response bound, origin restrictions and credential handling remain.

Deterministic tests advance a manual TimeProvider rather than sleeping. They
cover stalled success/error bodies, stalled headers, shared header/body time,
slow body progress, shorter and finite fallback budgets, caller cancellation,
successful/provider-error cleanup and a stalled transaction delta preserving its
cursor/freshness with no persisted transactions.

## Limits and remaining production gates

These are cooperative network cancellation deadlines, not hard resource isolation.
The tests use scripted HTTP streams and an in-memory database; they do not prove
real Plaid availability, maximum-history performance or ambiguous PostgreSQL
commit recovery. The full pagination sequence still has its separate page/event
bounds; per-user/per-pass load and latency need production-representative
observation before expanding cohorts.

Production remains the previously verified release
dd31836703bd110a30f303ed3ce9fd13c0f61d82 unless a later guarded deployment is
independently verified. No host configuration, secrets, bank credentials,
financial calculations/classifications or database schema/data were changed.

Broader launch still requires controlled two-user/provider/statement acceptance,
provider-enforced immutable/off-host protection, separate clean/compromised-host
recovery of real statements/database/both key rings, external alert/reboot
evidence and qualified legal/license review. Physical iPhone enrollment/sign-in,
cancellation and fallback remain unverified; the signed app's actual Apple
identity/domain association is still required. Passkeys and payments remain
disabled. CI fixture success does not close those gates.
