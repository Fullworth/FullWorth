## Current release state

**Status:** Repository and CI evidence is complete for a guarded-deployment decision; production deployment still requires explicit human approval.

Current release candidate on `master`:

`97516073e8c02c34805e4f3526411f563c0b9710`

`development` is three commits ahead of `master` after PR #691's ancestry sync and documentation PRs #692 and #693. The only file differences are `FULLWORTH_CONTEXT.md`, `FULLWORTH_ROADMAP.md`, and `HUMAN-TODO.md`; runtime/application files remain aligned. PR #693's exact head passed all four checks before merge as `3173b8cc57428f19438911724d59093249baf8fb`. Branch cleanup run #120 succeeded on `master` at `97516073e8c02c34805e4f3526411f563c0b9710`: it removed three proven stale refs and preserved three branches whose current heads had no matching merged PR, along with protected `master` and `development`.

Current verified live production release marker remains:

`7e8571a26447538db249c862ad009487cce119bc`

Repository evidence for the current candidate:

- [x] PR #687 repaired migration of missing runtime-only secrets and safe handling of identical duplicates.
- [x] PR #688 promoted that repair to `master` as `97516073e8c02c34805e4f3526411f563c0b9710`.
- [x] Master-push FullWorth CI #1640 (run `37192001483`), Repository Governance #15 (run `37192001532`), and Push on master #56 (run `37192001559`) passed on the exact SHA.
- [x] Exact production artifact: `fullworth-production-image-artifacts-97516073e8c02c34805e4f3526411f563c0b9710` (artifact ID `11299287390`, digest `sha256:b6d70eba9f867961dc19210adfe2ecb0d6e32ba134c93de5fe7a102bccb30279`; expires 2026-10-11).
- [x] PR #691 synchronized master ancestry back into `development`; exact-head FullWorth CI #1641 and Dependency Security #733 passed with zero file changes.
- [x] PR #687 repairs the failure seen when candidate `87be14e5407ed475f45d459e7674fc6600888119` was attempted in deploy run `37190632437`; candidate startup was blocked and the verified release marker did not advance.

Do not dispatch production deployment without the separate explicit release-owner approval recorded for issue #669. The current candidate is not deployed or production-accepted.

Historical failed deploys:

- Run #10 failed before production-host access because the workflow rejected a valid SHA; PR #670 fixed that validator.
- Run #11 reached the production host and fast-forwarded the checkout to `86d95c03c76792445913665ffdf353a45504133d`, then failed closed because protected `.env.production` contained duplicate runtime-database-password entries. Candidate containers were not started and the verified release marker did not advance. PR #673 fixed this path so future deploys repair identical duplicates before mutating the production checkout and fail closed if duplicate values conflict.

The source checkout on the production host may therefore be newer than the verified live release marker. Treat `.billwatch-release`, the running verified services, and guarded-deploy evidence as the production truth until the next deployment succeeds.

## Guarded production deployment and same-release acceptance — issue #669

**Status:** Human release approval and production evidence required.

**Why a human is needed:** The workflow requires explicit production approval, and the remaining acceptance work depends on the real host, controlled user accounts, physical devices, provider behavior, and external systems.

**Action:**

1. Obtain the separate release-owner decision to deploy; if approved, confirm `master` points exactly to the current candidate SHA recorded in issue #669.
2. In GitHub, open **Actions → FullWorth Production Deploy → Run workflow**.
3. Select `master`.
4. Set:
   - `release_sha=<exact current master SHA from issue #669>`
   - `confirm_guarded_deploy=true`
5. Do not substitute a different branch, shortened SHA, stale release, or manually bypass the guarded script.
6. If the deploy fails, stop acceptance work and fix the exact failure first.
7. If the deploy succeeds, confirm the automatic production auth smoke also succeeds and that the public API/Web readiness checks pass.
8. Record only sanitized, release-correlated evidence in issue #669.

**Verify:** The successful guarded-deploy evidence, production release marker, running release, public readiness, and production auth smoke all identify the same exact master SHA.

A successful merge or CI run alone is not production acceptance.

## Deployed-host security and ownership proof — issue #669 / #291

**Status:** Human production evidence required after the guarded deploy succeeds.

Repository and CI coverage already exercise these controls. The remaining work is to prove the same controls on the exact deployed release.

**Action:**

1. Run the deployed parser/OCR containment verification and retain only sanitized metadata.
2. Run deployed secret non-disclosure verification without printing protected values.
3. Run the production host/operator hardening verifier and record only pass/fail metadata.
4. Prove the steady-state API uses the dedicated runtime database role and does not retain migration/bootstrap credentials.
5. With two controlled test identities, perform objective Web/BFF/API ownership checks.
6. Confirm manipulated cross-user identifiers return the intended non-disclosing 404 behavior.
7. Process controlled representative text-PDF, scanned-PDF, and JPG/PNG statements.
8. Verify extraction, Bill Stream matching, historical comparison, deterministic monthly/annualized math, evidence-grounded explanation, and alert behavior against operator-known facts.
9. Confirm an external alert reaches the intended destination.
10. Complete disposable-account deletion without affecting another controlled user.
11. Perform the controlled production reboot and prove the exact release returns healthy.

**Verify:** All evidence is tied to the exact deployed release. CI or a different release does not satisfy this gate.

## Off-host immutable backup and compromised-host recovery — issue #669 / #291

**Status:** Human/provider action required before relying on backups for destructive incidents.

**Action:**

1. Confirm the actual encrypted Restic repository is off-host and reachable through the documented recovery path.
2. Configure provider-enforced Object Lock, WORM, immutable snapshots, or an equivalent control.
3. Verify from the provider control plane that production-host credentials cannot remove or weaken the protected recovery points.
4. Run a compromised-host / clean-host recovery exercise against the real off-host repository.
5. Restore the database, statement files, API Data Protection ring, and Web Data Protection ring into an isolated clean host.
6. Confirm restored ownership/modes, migration consistency, application readiness, and release identity.
7. Record only sanitized metadata in issue #669.

**Verify:** Both provider-enforced immutability and a real clean-host restore are demonstrated. Local CI restore tests are not substitutes.

## Installed-device and external-provider acceptance — issues #251, #258, #293

**Status:** Human/device/provider acceptance required after the current candidate is successfully deployed.

**Action:**

1. After the new release is verified live, re-pin installed-device acceptance issue #251 from the old release to the exact new production SHA before recording evidence.
2. Complete all required Android installed-PWA checks on a physical Android device.
3. Record Android acceptance only after every required check passes.
4. Re-test iOS Safari Add to Home Screen on a real iPhone for issue #258 if iOS remains in launch scope.
5. Complete controlled Plaid connect/update/reconnect lifecycle and Hosted Link return observation.
6. Complete issue #293's remaining real-world sandbox acceptance with Plaid payroll transactions.
7. Confirm provider failure/reconnect behavior without exposing provider tokens or raw provider payloads.

**Verify:** Physical-device and provider evidence is tied to the exact live release. Browser emulation, simulator-only evidence, repository tests, and a previous production SHA do not satisfy these gates.

## GitHub protected-branch governance

**Status:** Human repository-administrator action required.

The active repository ruleset **FullWorth protected branches** targets both `master` and `development`. Current verified configuration:

- active enforcement;
- pull requests required;
- deletion blocked;
- non-fast-forward updates blocked;
- review-thread resolution required;
- extra approval required for unattributed changes;
- no bypass actors;
- current connection cannot bypass;
- required approving review count: **0**;
- required status-check rule: **not configured**.

**Why a human is needed:** Current connected GitHub tooling can inspect the ruleset but cannot mutate the repository-administration settings.

**Action:**

1. Open **GitHub → FullWorth repository → Settings → Rules → Rulesets → FullWorth protected branches**.
2. Preserve the existing protections for both `master` and `development`.
3. Add the actual CI/security check contexts that must block merge when failing.
4. Decide whether at least one independent approving review is required and set the approval count accordingly.
5. Keep bypass actors empty unless a deliberate, documented emergency policy is later approved.

**Verify:** GitHub ruleset readback shows the intended approval policy and required status checks for both protected branches.

## Commercial legal/license review

**Status:** Human action required before commercial launch.

**Why a human is needed:** Legal entity selection, commercial rights, customer-facing terms, third-party obligations, model/runtime licenses, and proprietary licensing require qualified legal judgment.

**Action:**

1. Review closed draft PR #368 only as historical draft material; do not treat it as current approved legal text.
2. Confirm the correct FullWorth copyright holder/legal entity.
3. Have qualified counsel review the exact customer-facing Terms of Service and Privacy Policy intended for launch.
4. Have qualified counsel review the proprietary source-license approach and third-party/model/runtime notice obligations.
5. After approval, recreate the approved text from the then-current `development` head and send it through normal PR/CI review.

**Verify:** The legally approved versions are merged from current source before commercial launch, and repository/product documentation does not claim legal approval before that review is complete.
