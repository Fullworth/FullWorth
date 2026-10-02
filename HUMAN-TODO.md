# Human TODO

This file contains only work that cannot be completed safely through the current automated FullWorth development tooling.

## GitHub branch protection — required

**Status:** Human action required.

**Why a human is needed:** GitHub currently reports both `master` and `development` as unprotected, and the connected GitHub tooling can detect that condition but does not expose a repository-administration action for creating branch protection/rulesets.

**Action:**
1. Open the FullWorth repository on GitHub.
2. Go to **Settings → Rules → Rulesets** (or the equivalent branch-protection page).
3. Create protection that targets both `master` and `development`.
4. Require pull requests before changes are merged.
5. Require the repository's existing CI/security checks to pass before merge.
6. Block force pushes and branch deletion for both branches.
7. Save/enable the ruleset.

**Verify:** GitHub's branch API should report `protected: true` for both `master` and `development`, and the repository protection detector refreshed by PR #513 should pass.

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


## OCR containment production proof

**Status:** Awaiting an exact-`master` guarded production deployment after PR #601 is merged.

PR #601 makes `deploy/deploy-production.sh` run `deploy/verify-parser-containment.sh` before a candidate release can receive the verified release marker. That host verifier checks the running parser supervisor identity/capabilities, finite delegated cgroup controls, a real synthetic OCR child in its own finite subgroup, subgroup cleanup, a kernel-observed per-document OOM kill under the 384 MiB ceiling, parser survival/readiness, and absence of the synthetic raw-document marker, parser credential, and native OCR diagnostic markers from API/parser container logs.

The remaining human action is the intentionally manual production release decision:

1. Merge only after the exact PR #601 head passes full CI and dependency security.
2. Promote the verified `development` state to `master` through the normal reviewed release path.
3. Dispatch the guarded production workflow for that exact current `master` SHA.
4. Preserve the workflow log containing the sanitized containment verifier output and release SHA as deployed-host evidence.
5. Review the production systemd journal for the proof window if systemd-level log evidence is required; the automated verifier intentionally does not grant itself broader journal privileges or export journal contents.

Do not mark production containment accepted merely from GitHub CI. Acceptance requires the exact deployed `master` release to pass the host verifier.
