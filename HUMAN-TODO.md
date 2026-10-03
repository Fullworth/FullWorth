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
