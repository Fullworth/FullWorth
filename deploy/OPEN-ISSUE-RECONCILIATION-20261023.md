# Open-issue reconciliation — 2026-10-23

Pinned development: `305ac30601323cc259d2b8824acd1076bd2ffabc`.
Recorded production/master release: `dd31836703bd110a30f303ed3ce9fd13c0f61d82`.
This is a repository review and evidence index, not a release, provider, legal or device acceptance certificate.

## All 13 open issues

| Issue | Repository result | Required completion outside this change |
| --- | --- | --- |
| [#877](https://github.com/Fullworth/FullWorth/issues/877) | Reviewed 30 named audit branches against current files/patches; 29 refs present, one absent. Per-ref retention reasons and original/current blob SHAs are in the manifest. Recover the missing worker fault-isolation acceptance with today's implementation. | No deletion is authorized/performed. Owner may retain the named refs for the documented reasons, or separately approve a revalidated cleanup. |
| [#876](https://github.com/Fullworth/FullWorth/issues/876) | Reviewed 11 security/recovery/untracked entries; 10 refs present, one absent. OCR probe is integrated/strengthened; alternate payday fields are superseded by migrated bounded coverage. Keep security/release lineage. | Real provider/host/restore evidence remains under #291/#669. No stale governance, target-confirmation removal or un-migrated coverage experiment is restored. |
| [#875](https://github.com/Fullworth/FullWorth/issues/875) | Reviewed 11 web/finance branches. All changed paths exist in current development. Exact blobs and retained patch additions identify integrated features; changed behavior maps to newer safety fixes. | Preserve original UI/source/test lineage; no wholesale resurrection or destructive cleanup. |
| [#819](https://github.com/Fullworth/FullWorth/issues/819) | Its 23 scoped audit defects are closed after protected integrations. The continuing audit index is intentionally open. | New findings and real workflow acceptance remain evidence-driven. An audit index is not a remaining single code bug. |
| [#800](https://github.com/Fullworth/FullWorth/issues/800) | Existing PR #881 is the focused MAUI 10.0.120/no-Resizetizer-downgrade integration. Exact head `4ccae80061e4999d87f870b37f7041c7a532b534` has 12 successful contexts, including actual native packaging/emulator runs. No duplicate package change here. | Review/integration of #881. Physical installation/signing is separate. |
| [#762](https://github.com/Fullworth/FullWorth/issues/762) | Diagnostics, review/retention and source-account deactivation safeguards are already implemented, including #874. | Controlled private expected-payment/import/cadence/account-duplicate evidence. No actual missing rent/internet/electric root cause is established from synthetic data. |
| [#669](https://github.com/Fullworth/FullWorth/issues/669) | Verified recorded deploy/auth-smoke links for the exact production SHA below; older issue instructions target a superseded release. | Same-release controlled users/provider/statements, host/reboot/alerts, real recovery and qualified legal/license acceptance. No deploy in this change. |
| [#296](https://github.com/Fullworth/FullWorth/issues/296) | Future regulated-partner Money program; current non-custodial planning language is intentional. | A sponsor model and qualified legal/compliance/operational decisions must precede implementation. No account opening, money movement, banking claims or new third-party service is provisioned. |
| [#293](https://github.com/Fullworth/FullWorth/issues/293) | Nine implementation checkpoints are complete. | Controlled real-provider/sandbox posted-payroll -> owner-only plan/notification acceptance; CI cannot supply that observation. |
| [#291](https://github.com/Fullworth/FullWorth/issues/291) | Repository hardening controls and synthetic checks exist; no new production control is marked passed. | Provider-enforced immutable/offhost proof, clean/compromised-host real statements/database/both keyrings restoration, and exact-release controlled security acceptance. |
| [#258](https://github.com/Fullworth/FullWorth/issues/258) | Current manifest/install metadata already exists. No speculative Safari manifest fix is introduced. | Real iPhone Safari Share -> Add to Home Screen -> Open as Web App -> Add, then launch and report actual result on the current recorded release. |
| [#251](https://github.com/Fullworth/FullWorth/issues/251) | Installed-device checklist and guarded metadata-only recorder already exist. | Real Android installed-PWA acceptance is required; iOS is optional for this bundle. CI/emulator/unsigned IPA success is not a human attestation. |
| [#245](https://github.com/Fullworth/FullWorth/issues/245) | Fresh inventory has 59 refs, replacing the old 142-ref snapshot. #880 preserves active PR head/base refs and compares expected SHAs before cleanup. | No cleanup is dispatched and no refs are deleted. Revalidate heads, active PR dependencies, protected refs and explicit owner authorization before any future deletion. |

## Recorded production evidence and usable device target

- [Guarded production deploy](https://github.com/Fullworth/FullWorth/actions/runs/39665707749) succeeded for `dd31836703bd110a30f303ed3ce9fd13c0f61d82` on 2026-10-21.
- [Authentication smoke](https://github.com/Fullworth/FullWorth/actions/runs/39666163983) succeeded for the same SHA.
- This is GitHub evidence of the recorded deployment, not a fresh production-host observation. Do not attach the newer development fixes to this production release.
- Old `422718f` device/acceptance instructions are historical. Before recording a real device result, confirm the actually served release still matches the recorded SHA. Do not sign a human attestation from CI or guessed behavior.
- Passkey production enablement and physical Face ID remain unverified; do not change security flags to manufacture acceptance.

For #251, preserve its ten real Android checks: launch, first-run preferences, applied/editable settings, keyboard resizing, navigation/back, controlled waiting-worker update, statement picker, password dialog and email dialog. For #258, use the real Safari Home Screen flow and record only non-sensitive device/browser versions and observed result. Do not post credentials, financial documents, account/device identifiers or private screenshots.

## Branch review evidence

[branch-retention-review-20261023.json](branch-retention-review-20261023.json) contains all 52 issue-named entries, their observed exact heads, original PR heads, per-file source/current blob SHAs, replacement merge evidence and reasons for retention. There are **154 identical blob comparisons** across the original PR changed-file sets; modified files were reviewed for retained behavior and intentional newer safeguards. Exact blobs establish file identity, not completeness of all user workflows. Patch-line matching was used to locate differences, not as an automatic merge/deletion decision.

Important alternatives retained:
- #861 private-method pager tests target a superseded implementation; current pager/component/browser tests exercise the integrated controls.
- #844/#845 daily-reminder and batch-runner architectures are superseded by scoped hourly maintenance and per-upload worker scopes. The new worker test exercises actual failure progression, deferral, fresh contexts and abandoned tracked writes.
- #842 browser-only statement recovery is superseded by authenticated owner-scoped server metadata, including blocked-storage independence.
- #854's `UploadId` shape is superseded by owner/bill/processed `SourceUploadId`; no duplicate read contract is added.
- #840 chronology tests target an older component API; current selection is based on statement periods.
- The old #715 one-review/workflow-name verifier is superseded by the explicitly approved solo-maintainer policy and actual job-name gates. #724's proposal to remove typed production-target confirmation is not restored.
- Historical release documents are preserved as historical evidence, not reapplied to current instructions.

## Regression scope and limits

`RetainedStatementWorkerIsolationTests` uses the real current worker and scoped processing service with synthetic text, in-memory EF and a controlled extraction fault. It covers one/four failing early rows, unexpected timeout or unrelated cancellation, later healthy progress, unique contexts, no flushed sentinel writes, retry deferral and requested cancellation propagation. It recovers useful intent from an alternate branch without reviving that branch's production architecture.

Exact final-commit CI must pass before treating this test recovery as verified. These are disposable fixtures, not native OCR, real provider, production recovery or hard resource-isolation proof. No source change is made to financial calculations/classifications, authorization, production flags, schemas, workflows or shared roadmap/context/handoff files.
