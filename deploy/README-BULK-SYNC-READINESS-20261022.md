# Manual bulk refresh isolation — 2026-10-22

## Observed gap

The authenticated POST api/plaid/accounts/sync and transactions/sync endpoints
called the coordinator's single-context bulk loops. A failed first bank stopped
later healthy banks. The gateway's account-only and transaction-only methods used
those same loops, although combined background refresh already isolated banks.

The exposed bulk endpoints and all three gateway operations now share one
owner-scoped orchestration path. It reads only active, credential-bearing
connection IDs in the caller's context, creates an independent service scope for
each bank, and delegates to the existing single-bank coordinator. Archived active
banks remain eligible. Foreign, disconnected, attention-required and
credential-missing banks are excluded as before.

Other eligible banks are attempted after provider, validation, non-caller timeout
or save failures. The first failure is rethrown after the pass, so the response
does not report complete success for partial work. Caller cancellation stops the
pass. No provider request is automatically retried. Account-only refresh does not
mark transaction history fresh. Complete-pass count summaries retain their shape.

Twenty-two dedicated regressions cover both modes, provider attention/transient/
timeout/invalid responses, data-save and attention-save failures, fresh contexts,
unsaved caller work, complete/empty summaries, archived eligibility, ownership,
inactive/missing credentials, multiple failures and cancellation. Authenticated
HTTP regressions verify the actual exposed routes continue to the healthy bank
and return failure rather than partial success. Existing combined background
refresh regressions remain required.

## Remaining boundaries

The coordinator's legacy single-context bulk helpers remain for internal
compatibility/tests; production bulk HTTP and gateway entry points no longer use
them. Future exposed bulk consumers should use the scoped gateway.

The HTTP tests use scripted provider responses and an in-memory database. They
do not prove actual provider availability, PostgreSQL ambiguous-commit recovery
or physical-device behavior. Concurrent sync/disconnect coordination,
production-representative multi-bank load, per-user/per-pass duration budgets and
fairness across concurrent requests still need separate work/evidence. A failed
pass can have committed healthy-bank work; it is not globally atomic.

Production remains the last verified dd31836703bd110a30f303ed3ce9fd13c0f61d82
release unless a later guarded deployment is independently verified. No UI,
shared workflows/handoffs, schema, secrets, production data, financial
calculations/classifications or feature activation were changed.

Broader launch still needs signed Apple domain association and physical Face ID,
controlled two-user/provider/statement acceptance, immutable off-host backup
protection, clean/compromised-host recovery of real statements/database/both key
rings, reboot/external-alert evidence and qualified legal/license review.
Synthetic HTTP, in-memory failure tests and CI restore fixtures do not close those
gates.
