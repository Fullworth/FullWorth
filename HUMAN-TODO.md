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

**Verify:** GitHub's branch API should report `protected: true` for both `master` and `development`, and the repository protection detector added by PR #443 should pass.

Do not put credentials, tokens, recovery codes, financial data, private statements, or other secrets in this file.
