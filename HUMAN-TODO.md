## Current release state

**Status:** Deployment and recovery are blocked. Do not start public services or deploy the candidate.

Current release candidate on `master`:

`f401a591a8abdade557827c09412dc3166fb9de2`

- [x] PR #704 promoted the candidate to `master`.
- [x] Exact PR FullWorth CI #1679 and Dependency Security #771 passed; master-push CI #1680, Repository Governance #18, and CodeQL #58 passed.
- [x] Exact attested artifact: `fullworth-production-image-artifacts-f401a591a8abdade557827c09412dc3166fb9de2` (artifact ID `11374998766`, digest `sha256:0f24594dc289350487574f757956c7cd325c68b3a07e2d58bd4ce5f411a7052b`; expires 2026-10-12).
- [x] PR #708 merged the recovery-verifier passfile wiring into `development` at `762ff73456a88cf39c37b6fedbdd2c59402e5833`; exact-head FullWorth CI #1694 and Dependency Security #785 passed.
- [ ] Run #15 failed closed before candidate startup. Its partial API/Web/edge report conflicts with subsequent host inspection: OVH 40.160.137.55 read-only inventory found zero Docker containers, zero Docker volumes, and a missing release marker; this conflicts with run #15's partial-runtime report.
- [ ] Remote R2 recovery returned AccessDenied with the host's configured backup credential; a separate read-only recovery credential is required. The development fix does not supply this credential; do not broaden the production backup key.
- [ ] The copied object tree on the VPS has not passed encrypted Restic integrity or clean-host restore verification.
- [ ] Reconcile the guarded workflow SSH destination with the inspected OVH host and verify the actual host/project read-only before any service repair.
- [ ] Restore and verify the encrypted backup in isolation before any data-bearing production startup. The inspected OVH host has no Docker volumes; a normal first startup would be empty.
- [ ] Only after all recovery and target-identity gates pass, run the guarded workflow for the current exact master SHA and verify auth smoke, public readiness, and the release marker all identify that SHA.

Current verified live production release marker remains:

`7e8571a26447538db249c862ad009487cce119bc`. No release marker or successful deployment of the candidate has been observed. Keep credentials out of chat, source control, command history, and logs.

## Guarded production deployment and same-release acceptance — issue #669

**Next concrete sequence:**

1. Use the approved secret-handling path to install the separate least-privilege R2 Object Read credential on the verified recovery host. Do not paste its value into chat or write it into source control or shell history.
2. Re-run the isolated recovery drill against the remote repository and require encrypted-repository integrity plus successful database, statement, and Data Protection key restoration.
3. Reconcile the production workflow's SSH destination with the host inspected at `40.160.137.55`; preserve the current live release and do not guess at service restarts.
4. Only after recovery and target identity are verified, check the exact master artifact and all guarded deployment preflights, then dispatch the guarded workflow for that exact SHA.
5. Confirm automatic auth smoke, public API/Web readiness, running release, and verified marker all agree on the deployed SHA.

A workflow checkout, CI result, artifact, or copied object tree is not proof of production deployment or a successful restore.

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
