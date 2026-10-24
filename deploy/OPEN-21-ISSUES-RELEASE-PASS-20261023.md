# Disposition of all 21 open issues — 2026-10-23 America/Denver

This pass reconciles the 21 open issues observed on 2026-10-24 UTC. Development is pinned to
`3da853da5d4d58e27a30c335e60673dcc5aa781e`. The existing integration candidate is
[PR #894](https://github.com/Fullworth/FullWorth/pull/894); its exact final SHA and fresh combined check results belong in the PR.
No issue is closed, PR merged, branch deleted, provider mutated or production deployed by this document.

## Code defects consolidated for combined verification

These eight defect implementations already exist in focused source PRs. Reuse their exact file blobs;
do not write competing implementations. The first four were already in #894; this pass adds the last four.

| Issue | Behavior corrected | Source PR | Exact source head |
| --- | --- | --- | --- |
| [#884](https://github.com/Fullworth/FullWorth/issues/884) | Failed RequiresAttention writes restore bank state and preserve caller tracking | #885 | `d258fd178cb2443098bc07fe14bce9d1515a2fbe` |
| [#886](https://github.com/Fullworth/FullWorth/issues/886) | Failed Hosted Link terminal saves restore pending session/token and update-bank state | #887 | `2d90d7434295d2ce466c7802f2d3f59a299889d8` |
| [#888](https://github.com/Fullworth/FullWorth/issues/888) | Failed exchange candidates are detached and existing/caller state restored | #889 | `02afbe039170f0b2be7a446527a92122ed135ac8` |
| [#892](https://github.com/Fullworth/FullWorth/issues/892) | Discovery failure restores the prior unit of work without clearing caller work | #893 | `4076709f0291cb8309c760fc09e525b5608a7520` |
| [#896](https://github.com/Fullworth/FullWorth/issues/896) | Failed immutable-plan candidates are detached without losing unrelated tracking | #897 | `59b5a2a68254b05e47bacf5325bd4d48b3671310` |
| [#898](https://github.com/Fullworth/FullWorth/issues/898) | Stable user-ID sweep continuation cannot skip later users when earlier failures leave the due set | #899 | `de6c0099d6c28141235defb30a08dd20a7820fa3` |
| [#900](https://github.com/Fullworth/FullWorth/issues/900) | Failed health-alert mutations are restored; no-op reconciliation does not save caller work | #901 | `8493f04d1c5d1e81b172389df207910a20fc3f7b` |
| [#902](https://github.com/Fullworth/FullWorth/issues/902) | Transaction identity stabilizes same-date/same-batch bounded history windows | #903 | `397492618f068d69d88202078133f60f495c73fc` |

Each source PR has successful required check contexts on its pinned head. Those records alone do not certify
this combined candidate. Keep source PRs and issues open until normal integration and issue-specific acceptance.

Four new `MonitoringRecoveryCompositionTests` exercise the real monitoring orchestrator, discovery persistence,
planning store and health reconciliation on one DbContext. Inject database/unexpected pre-commit failures after
financial rows are staged. The subsequent real health save must preserve caller additions/edits/deletions,
persist only the intended health warning, retain the foreign owner's state, and leave failed discovery/plan rows
absent. Retry must persist the intended result; immutable plan replay must retain one run/allocation.
These complement the individual save/cancellation/race tests, rather than substituting a direct caller SaveChanges
for the actual secondary reconciliation path.

Important #886 limit: initial public-token exchange and session completion remain separate commits.
Restoring Pending does not establish replay of a consumed token or rollback of a committed connection.
Real-provider partial-completion recovery remains unverified. Do not persist public tokens to manufacture retry.

## Other repository and review issues

| Issue | Evidence/disposition | Remaining completion requirement |
| --- | --- | --- |
| [#800](https://github.com/Fullworth/FullWorth/issues/800) | #881's MAUI 10.0.120 toolchain correction is already retained in #894; no second package patch | Verify actual Android SVG rasterization/APK/emulator and iOS simulator/device packaging on the new combined head; integrate normally |
| [#875](https://github.com/Fullworth/FullWorth/issues/875) | Existing 11-branch patch/blob review and explicit retention reasons are retained from #882 | No missing implementation was established. Keep refs for lineage; any later deletion requires new explicit authorization and fresh checks |
| [#876](https://github.com/Fullworth/FullWorth/issues/876) | Existing security/recovery branch review is retained; do not resurrect superseded governance/deployment controls | Real recovery gates remain #291/#669. Preserve unresolved evidence and refs |
| [#877](https://github.com/Fullworth/FullWorth/issues/877) | Existing 30-branch review and five recovered statement-worker isolation regressions are retained | All named branches remain explicitly retained or previously absent. No deletion authorized |
| [#245](https://github.com/Fullworth/FullWorth/issues/245) | Cleanup is an owner control, not a product defect; active head/base and current-SHA checks already exist | A future owner-authorized cleanup must recheck protection, active PR dependency, containment, heads and resulting inventory. Do not dispatch from this snapshot |
| [#819](https://github.com/Fullworth/FullWorth/issues/819) | Ongoing audit index; original 23 scoped defects were integrated previously; this pass consolidates eight subsequent backend defects | Keep open for future findings; index status does not establish all workflows correct |

Fresh inventory: **72 branch refs**. All **52** entries in the retained review were rechecked:
**50 present with identical recorded heads, two still absent**. No mismatch or new disappearance was observed.
The existing `branch-retention-review-20261023.json` is a retention record, never a deletion allowlist.

## Acceptance and future-program issues requiring evidence beyond repository tests

| Issue | Concrete remaining work | Required evidence boundary |
| --- | --- | --- |
| [#762](https://github.com/Fullworth/FullWorth/issues/762) | Privately match expected payments to imported posted history, sync freshness and deterministic rejection reasons; decide additional cadence/account-duplicate semantics from actual negative/positive evidence | Owner-scoped private examples and known truth. Do not equate ledger spend with recurring obligations, broaden classifications or invent amounts to improve totals |
| [#293](https://github.com/Fullworth/FullWorth/issues/293) | Controlled posted-payroll -> owner plan/notification acceptance with expected contributions and foreign-user rejection | Approved provider/sandbox trial on the confirmed exact runtime; synthetic payroll fixtures are insufficient |
| [#291](https://github.com/Fullworth/FullWorth/issues/291) | Provider-enforced immutable/off-host protection; separate clean/compromised-host restore of actual database, statements and both keyrings; exact-release controlled security acceptance | Operator/private provider evidence. CI restore or one-user auth smoke does not prove these three gates |
| [#669](https://github.com/Fullworth/FullWorth/issues/669) | Integrate verified candidate, obtain fresh exact-master checks/artifacts, then separately authorized guarded release and same-release provider/document/two-user/alert/reboot/legal acceptance | Do not deploy from an unmerged integration head or reuse another SHA's evidence; launch remains gated |
| [#251](https://github.com/Fullworth/FullWorth/issues/251) | Complete all ten installed-PWA checks on real Android against the confirmed served release | Existing protected installed-device recorder only after actual human checks pass; emulator/browser success is insufficient |
| [#258](https://github.com/Fullworth/FullWorth/issues/258) | Real iPhone Safari Add to Home Screen retest; separately test physical Face ID/passkey behavior | Confirm served release and record non-sensitive OS/browser/result metadata. No physical result or production passkey enablement is supplied by this pass |
| [#296](https://github.com/Fullworth/FullWorth/issues/296) | Future sponsor/partner, legal/compliance, authoritative ledger/reconciliation and operational program decisions | Explicit future program, outside current non-custodial guidance. No account opening, KYC/SSN collection, transfers, card issuance or insurance claims are enabled |

These seven are not unimplemented copies of the eight backend defects. Their remaining work cannot be honestly
marked complete from a GitHub-only source/CI pass. Existing operator entry points include
`smoke-plaid-lifecycle.sh`, `smoke-statement-lifecycle.sh`, `smoke-web-bff-cross-user.sh`,
`record-installed-device-acceptance.sh`, `run-clean-host-recovery-drill.sh`, and
`verify-private-beta-acceptance-evidence.sh`. Their presence is not a completed operator run.
Do not run mutation/attestation scripts merely to reduce the issue count.

## Release identity, preservation and validation

Last recorded verified deployment/master target remains `dd31836703bd110a30f303ed3ce9fd13c0f61d82`
([guarded deploy](https://github.com/Fullworth/FullWorth/actions/runs/39665707749),
[same-release auth smoke](https://github.com/Fullworth/FullWorth/actions/runs/39666163983)).
This is a reread of recorded evidence, not a new host inspection. Confirm actual served/host release before
collecting acceptance. Passkeys remain disabled pending their applicable prerequisites and physical acceptance.

The added source files do not collide with #894's current modifications: before composition each modified path
matched development and each new test path was absent. The existing #894 tree, including its prior UI/native
work and independent CSS correction, is preserved exactly outside the ten adopted backend/test paths.
This pass edits no FullWorth.Web, FullWorth.VisualTests, workflow, roadmap or context/handoff source.
The separate UI-agent PR #895 is not copied or modified.

No authentication policy, ownership filter, financial arithmetic/classification, schema, secret, production
setting or database content is changed by this integration pass. Successful service writes retain their existing
shared-context SaveChanges contract; the recovery fixtures inject failures before commit and do not prove
rollback after an ambiguous committed exception, real PostgreSQL contention, hard resource isolation,
complete security or production recovery.

Fresh combined CI must include backend/offline builds, EF pending-model verification, all tests, production-container
HTTP/parser and disposable backup/restore checks, dependency review, and the existing PR-triggered native/browser jobs.
Record actual executed steps versus gated skips in #894. Direct Dependabot/CodeQL/secret-alert inventories are
unavailable through this connector; report available check results without claiming an alert-free audit.
