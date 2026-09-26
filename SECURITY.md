# Security Policy

## Supported versions

FullWorth is a pre-1.0 project developed as a continuously updated service. Security fixes target the currently deployed FullWorth service and the latest code on the default branch (`master`). Older releases, tags, commits, and third-party deployments do not receive guaranteed backports; self-hosted users should update to the latest fixed revision. Reports affecting older versions are welcome when the issue may still affect supported code.

## Report a vulnerability privately

Use [GitHub Security Advisories: Report a vulnerability](https://github.com/Fullworth/FullWorth/security/advisories/new) (sign-in required), or select **Security → Report a vulnerability** in this repository. **Do not report vulnerabilities through public issues, pull requests, or discussions.**

Please include:

- A summary, affected component or endpoint, and version, commit, or observation date.
- Reproduction steps, prerequisites, and a minimal proof of concept using synthetic data.
- Expected and actual behavior, likely impact, and any suggested mitigation.
- Redacted logs or screenshots that help us reproduce the issue.

Never publish secrets, credentials, tokens, personal information, or financial data. Redact sensitive values even in private reports; describe an exposure without including the exposed data.

## What to expect

We aim to acknowledge reports within **5 business days** and provide an initial triage assessment within **10 business days** of receipt. These are targets, not guarantees; complexity and maintainer availability may affect timing. If you have not heard back, follow up in the same private report.

We will use the private advisory to discuss findings, next steps, and coordinated disclosure. Remediation timing depends on severity and complexity; no fixed resolution deadline is promised. Please give us a reasonable opportunity to investigate and address the issue before public disclosure, and coordinate publication with us.

## Responsible research

- Test only in isolated local or explicitly authorized test environments, using accounts you control and synthetic data.
- Do not test against other users, their accounts, or production data. Do not access, alter, download, or delete anyone else's information.
- Do not disrupt service, perform denial-of-service attacks, use social engineering, or test third-party systems without their authorization.
- Collect only the minimum evidence needed. If you unexpectedly encounter sensitive data, stop testing immediately and report the exposure privately without copying or sharing that data.

## Safe harbor

We consider good-faith research conducted within this policy to be authorized by FullWorth, and we will not pursue or support legal action against you for that research. If you make an inadvertent mistake, stop the activity and promptly disclose it so we can work together to resolve it. This commitment applies only to systems and rights FullWorth controls; it cannot authorize activity against third parties or bind them. Contact us through a private advisory before proceeding if the scope is unclear.
