# FullWorth host and operator hardening

This runbook covers the production-host controls that sit outside FullWorth application authentication. It is intentionally separate from Identity, BFF sessions, and application authorization.

A passing repository test only proves that this policy and verifier exist. A production-security claim still requires the verifier to pass on the exact deployed release host and the remaining provider/firewall facts to be inspected directly.

## Supported operator model

Use one dedicated non-root deployment account named `deploy` unless the production environment deliberately overrides `FULLWORTH_DEPLOYMENT_USER`.

The deployment account may be the sole explicit member of the `docker` group because the current release tooling requires Docker. Docker-group membership is effectively root-equivalent host privilege, so do not add ordinary interactive users, CI service accounts, or application users to that group.

Production application containers must continue to run with their existing capability, read-only-filesystem, network, and user restrictions. Do not use host root access as a workaround for an application/container permission failure.

## SSH baseline

The effective OpenSSH server configuration must enforce all of the following:

- `PermitRootLogin no`
- `PasswordAuthentication no`
- `KbdInteractiveAuthentication no`
- `PermitEmptyPasswords no`
- `PubkeyAuthentication yes`
- `MaxAuthTries 4` or lower
- `LoginGraceTime 60` seconds or lower
- `AllowTcpForwarding no`
- `X11Forwarding no`

Use unique operator keys. Remove a departed or compromised operator key immediately rather than sharing or rotating a common private key.

The production GitHub deployment workflow must continue to use a pinned `known_hosts` value obtained from a trusted administrative source. Never learn or accept the SSH host key during a deployment.

SSH source restriction is also required at the VPS/provider firewall or private administration network. The repository cannot truthfully prove those provider rules. Inspect them directly and allow SSH only from explicitly trusted operator source ranges.

## Host security-update cadence

The supported Ubuntu host must have `unattended-upgrades` installed and both `apt-daily.timer` and `apt-daily-upgrade.timer` enabled and active.

Review update/reboot state before every guarded production release and at least weekly while the service is live. A pending `/var/run/reboot-required` is a failed hardening check: schedule the controlled reboot procedure and re-run the release-pinned verification afterward.

Critical host/kernel/OpenSSH/container-runtime security advisories should be evaluated promptly rather than waiting for the next feature release. Apply emergency fixes through the normal operator change process and preserve sanitized evidence of the resulting versions/state.

Container base images remain digest-pinned in source. Refresh them through reviewed dependency/security PRs and exact-head CI; do not introduce an unattended `latest` pull on the production host.

## Verification

Run on the production VPS as an operator with root access:

```sh
cd /opt/billwatch
sudo FULLWORTH_DEPLOYMENT_USER=deploy sh deploy/verify-host-hardening.sh
```

The verifier reads effective settings and emits only pass/fail text. It does not print SSH keys, firewall source addresses, credentials, application configuration, financial data, or statement content.

A pass verifies the repository-checkable host baseline:

- dedicated non-root deployment account exists;
- the deployment account is the sole explicit Docker-group member;
- effective SSH authentication/forwarding limits match this policy;
- unattended security-update machinery is installed and scheduled;
- no reboot is currently required to finish applying host updates.

It does **not** prove the VPS/provider firewall source ranges, the security of the operator's endpoint, provider-account MFA, SSH private-key custody, or that a future security advisory has been remediated. Those are direct operator/provider evidence.

## Incident handling

If an operator key or deployment host is suspected compromised, follow `SECURITY_INCIDENT_RESPONSE.md`. Do not merely replace the SSH key and continue using a potentially compromised host. The existing clean-host/off-host recovery path remains the recovery authority for a compromised-host scenario.
