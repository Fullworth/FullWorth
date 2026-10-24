# Disconnect failure recovery — 2026-10-22

## Observed defect and fix

PlaidConnectionDisconnectService previously staged credential/cursor removal and
Disconnected status before querying the owned accounts. Account-query failure
could therefore leave a partial disconnect in EF tracking. A failed/canceled
SaveChanges likewise left credential removal and account deactivation staged.
A later unrelated save on the same context could persist that failed operation.

The service now loads all owner-scoped accounts before staging local changes and
snapshots the affected entries. Pre-commit save failure/cancellation restores
current values, originals, states and dirty-property flags while preserving
caller edits. The original failure still propagates. A retry retains the
encrypted credential, attempts provider revocation again, accepts ITEM_NOT_FOUND
and commits local cleanup through the existing path. No provider request is
automatically retried and no ownership predicate is relaxed.

Eight dedicated scripted/in-memory regressions cover failed and canceled saves
from Active, RequiresAttention and credential-retaining Disconnected states;
failed and canceled account materialization; a later unrelated save; preservation
of pre-existing caller changes; provider-already-revoked retry; foreign-owner
state; and retained historical transactions. No real bank is disconnected.

## Evidence boundaries

Restoring local tracked state does not undo Plaid revocation or establish that a
bank remains usable. The API still reports failure until a retry commits cleanup.
These tests inject failures before database commit; they do not establish
PostgreSQL rollback after ambiguous network/commit outcomes. A fresh persisted
state check and real relational fault-injection evidence remain necessary for
that claim. Concurrent sync/disconnect coordination is also a separate concern.

The manual bulk account/transaction endpoints still use coordinator loops that
stop at the first bank failure. Background combined refresh has per-bank scope
isolation from PR #803. The remaining manual bulk path should be handled in its
own change; this PR does not claim to fix it.

Production remains the verified dd31836703bd110a30f303ed3ce9fd13c0f61d82 release
unless a newer guarded deployment is independently verified. This change touches
no UI, shared workflows, schema, secrets, financial calculations/classifications,
production database contents or feature flags.

Broader launch still needs physical iPhone/Face ID and signed-domain association,
controlled two-user/provider/statement acceptance, provider-enforced immutable
off-host backups, separate clean/compromised-host recovery of real database,
statements and both key rings, reboot/external-alert evidence and qualified
legal/license review. Scripted failure recovery and CI fixture restore do not
close those gates.
