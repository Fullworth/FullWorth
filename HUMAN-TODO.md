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

After the isolated OCR worker change reaches `master`, an operator with the existing guarded production-host access must:

1. Deploy the exact approved `master` SHA through the guarded deployment workflow.
2. Record the deployed release SHA and verify API/Web/parser-worker readiness.
3. Run a synthetic image OCR request and a bounded memory-pressure fixture while recording the parser-worker cgroup's `memory.max`, `memory.swap.max`, `memory.events`, `memory.peak`, `cpu.max`, and surviving API/Web readiness.
4. Confirm no statement bytes, OCR text, credentials, or native diagnostics appear in API, worker, Docker, or systemd logs.
5. Do not mark distinct per-document cgroups complete until the host runtime safely delegates a writable cgroup-v2 subtree to the unprivileged worker and a test proves every child enters a unique finite subgroup that is removed after exit.

GitHub CI proves the production image and Compose boundary only; it cannot substitute for this deployed-host evidence.
