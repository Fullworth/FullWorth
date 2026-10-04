# Human TODO

This file contains only work that cannot be completed safely through the current automated FullWorth development tooling.

## GitHub protected-branch required checks — remaining

**Status:** Partial human action required.

GitHub now has an active repository ruleset named `FullWorth protected branches` targeting both `master` and `development`. It blocks branch deletion and non-fast-forward updates and requires pull requests, with no bypass actor configured.

The remaining gap is that the ruleset does not currently contain a required-status-check rule, so repository policy does not itself require the existing CI/security checks before merge.

**Why a human is needed:** The connected GitHub tooling can verify rulesets but does not expose the repository-administration mutation needed to change this ruleset.

**Action:**
1. Open the FullWorth repository on GitHub.
2. Go to **Settings → Rules → Rulesets**.
3. Edit **FullWorth protected branches**.
4. Preserve the existing pull-request, deletion, and non-fast-forward protections.
5. Add the repository's required CI/security status checks so a failing required check blocks merge.
6. Keep the ruleset active for both `master` and `development`.

**Verify:** GitHub should continue to report both branches as protected, and the active ruleset should include required status checks in addition to the existing pull-request/deletion/non-fast-forward rules.

## Commercial legal/license review — required before commercial launch

**Status:** Human action required before commercial launch.

**Why a human is needed:** Draft PR #368 contains proposed proprietary FullWorth license language and a third-party license policy. Selecting the legal entity/copyright holder, confirming commercial rights and obligations, and approving customer-facing legal terms require qualified legal judgment and cannot be established by CI or repository automation.

**Action:**
1. Review the closed draft PR #368 and the current dependency/model/runtime license inventory.
2. Confirm the correct copyright holder/legal entity for FullWorth.
3. Have qualified counsel review the proprietary source license, third-party notice obligations, and alignment with the Terms of Service and Privacy Policy.
4. After approval, recreate the approved legal text from the then-current `development` head and send it through normal PR/CI review.

**Verify:** The legally approved license/notice text is merged from a current-development PR before commercial launch, and no repository documentation claims legal approval before that review is complete.

Do not put credentials, tokens, recovery codes, financial data, private statements, or other secrets in this file.


## Current guarded production candidate acceptance — issue #669

**Status:** Human release decision and same-release production evidence required. The repository/CI candidate is master SHA `c092a9c76c5f4e811941400606c32d75a0a50a29`; the verified live release remains `7e8571a26447538db249c862ad009487cce119bc`. No deployment authorization or production acceptance is recorded for the candidate.

**Why a human is needed:** Deployment requires the production environment's authorized reviewer, an explicit release decision, access to the protected production environment, and real controlled accounts/devices/provider integrations. CI cannot establish these facts.

**Action:**
1. Review the current checklist in [issue #669](https://github.com/Fullworth/FullWorth/issues/669) and confirm `master` still points exactly to `c092a9c76c5f4e811941400606c32d75a0a50a29`.
2. If the release owner explicitly approves deployment, dispatch **FullWorth Production Deploy** from `master` with `release_sha=c092a9c76c5f4e811941400606c32d75a0a50a29` and `confirm_guarded_deploy=true`. The workflow must complete its guarded checks; do not substitute a branch head or stale SHA.
3. Preserve sanitized output that proves the exact release marker, readiness/auth smoke, deployed parser containment, secret non-disclosure, runtime database-role boundary, and image provenance. Never include credential values, user data, statement contents, or raw sensitive logs.
4. Against that same deployed SHA, complete the objective two-user isolation checks, controlled statement/Plaid/provider flows, external alert receipt, account deletion and reboot checks, and physical Android acceptance listed in #669.
5. Do not mark #669 or #291's deployed-evidence items complete until evidence identifies the same release and each real-world check passed.

**Verify:** The guarded deployment identifies the exact SHA and issue #669 contains a sanitized, release-correlated evidence record. A successful merge or CI run alone is not production acceptance.

## Off-host immutable backup and compromised-host recovery — issue #669

**Status:** Human/provider action required before relying on backups for destructive incidents.

**Action:**
1. Configure provider-enforced Object Lock, WORM, immutable snapshots, or an equivalent append-only control for the actual encrypted Restic repository.
2. Verify from the provider control plane that the retention control applies to recovery points and cannot be removed by credentials available to the application host.
3. Run a compromised-host/clean-host recovery exercise against that actual off-host repository; restore database, statements, and both Data Protection rings into an isolated clean host.
4. Confirm file ownership/modes, application readiness, and release identity. Record only sanitized metadata in #669.

**Verify:** Provider enforcement and the clean-host restore are both demonstrated against the real repository. Repository tests or a local restore are not substitutes.

## Branch ruleset governance decision

**Status:** Human repository-administrator action required. The active `FullWorth protected branches` ruleset covers `master` and `development`, but currently requires zero approving reviews and no status checks.

**Action:**
1. Decide whether an independent approving review is required for both protected branches.
2. In **Settings → Rules → Rulesets → FullWorth protected branches**, preserve existing PR, review-thread, deletion, and non-fast-forward protections; add the required PR CI/security checks and the chosen approval count.
3. Confirm the ruleset still targets both branches and has no unintended bypass actor.

**Verify:** GitHub's active ruleset readback shows the selected approval policy and required CI/security checks for both branches. Repository automation can inspect but cannot make this administration change.

## Installed-device, external-provider, and legal acceptance

**Status:** Human acceptance required for the applicable launch scope.

**Action:**
1. Complete Android installed-PWA acceptance on the exact deployed release; retest iOS issue #258 separately if it remains in scope.
2. Complete controlled Plaid connect/update/reconnect and real payroll/sandbox acceptance; observe Hosted Link return behavior.
3. Have qualified counsel review the exact customer-facing Terms/Privacy, commercial license, and third-party/model/runtime notices.
4. Preserve only sanitized acceptance metadata and link it to the release in #669.

**Verify:** Physical-device, provider, and legal evidence is reviewable and tied to the exact release. Emulator, repository tests, and draft legal text do not satisfy these gates.

## Parser containment deployed-host proof

**Status:** Human release/deployment evidence is still required.

Repository and CI coverage now exercise the parser/OCR isolation boundary, including per-document and per-image cgroups, finite CPU/memory/PID limits, zero child swap, containment cleanup, kernel OOM-kill evidence, parser readiness, native-library boundary checks, and generated malicious/corrupt document regressions.

That is repository/CI evidence only. The remaining human action is the intentionally guarded production release decision:

1. Promote only a current `development` head whose exact release candidate has passed all required CI and security gates.
2. Merge that reviewed promotion to `master`.
3. Dispatch the guarded production deployment for that exact `master` SHA.
4. Preserve the sanitized deployed-host containment evidence produced by the release's required verifiers, including `deploy/verify-parser-containment.sh`.
5. If host-level journal evidence is required, review it directly on the production host without exporting credentials, document contents, financial data, or raw statement material.

Do not mark production containment accepted merely from GitHub CI. Acceptance requires the exact deployed `master` release to pass the guarded host verification.
