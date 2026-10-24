# Passkey production rollout preparation — 2026-10-21

## Verified gap

Release dd31836703bd110a30f303ed3ce9fd13c0f61d82 contains the
passkey endpoints and first-step Web sign-in from PR #801. Production Compose
did not pass Passkeys:Enabled to API or Web, so setting a host-local enablement
variable alone could not expose enrollment or sign-in.

This change adds one optional BILLWATCH_PASSKEYS_ENABLED flag, default false,
to both services. API Passkeys:Origin comes from the existing public Web hostname:
https://${BILLWATCH_WEB_HOST}. There is no separately configurable origin that
can silently diverge from the Web host. Existing exact-origin, user-verification,
ownership, MFA, rate-limit and challenge-consumption checks remain in place.

The production validator rejects duplicate flags and values other than true/false
(empty/missing retains false, consistent with Compose's default). Tests invoke
the real validator and render the actual Compose model without starting containers,
using synthetic credentials. They verify missing, empty, false and true states,
both service flags, the public origin and rejection cases.

## Controlled enablement

No production environment values are changed by this PR. Do not enable solely
because these configuration regressions or Chromium's virtual authenticator pass.

Before activation, verify the normal release applied AddPasskeys, use one API
process (the ceremony store is process-local), and complete physical iPhone
Safari / Home Screen enrollment, repeat sign-in, cancellation and password
fallback on a controlled HTTPS environment with the same release and reviewed
origin. Include MFA-enabled accounts and persistent-session opt-in. Record source
SHA, iOS version and sanitized results; never record biometric data or keys.

After acceptance, the operator can set BILLWATCH_PASSKEYS_ENABLED=true in the
protected host-local .env.production through the approved operational path and
use a reviewed guarded release. Verify both service settings and the live login
offer. Rollback sets the flag false in both services and recreates them through
the guarded path; restarting the API consumes no old challenges and clears
pending ceremonies. Disabling does not revoke existing sessions, delete enrolled
credentials, or substitute for session revocation after compromise. Password,
authenticator and recovery-code paths remain available.

## Installed iPhone application

The current unsigned personal IPA is not Face ID acceptance. WKWebView additionally
requires a signed webcredentials:fullworth.org associated-domains entitlement and
a matching live apple-app-site-association containing the actual Apple Team ID
and final signed bundle identifier. Neither identifier has been supplied or
verified. Do not publish invented identifiers or treat the temporary personal ID
as the final signed identity. See PASSKEYS.md and IOS_PERSONAL_TESTING.md.

## Remaining release evidence

Physical iPhone Safari/Home Screen and signed-app acceptance remain open. A real
USB/NFC key also needs hardware testing when advertised. CI fixtures, virtual
WebAuthn, deployment readiness and password-based authentication smoke do not
establish these outcomes. Issue #291's off-host immutable protection,
clean/compromised-host recovery of real statements/database/both key rings and
controlled two-user/provider/statement acceptance remain separate blockers.
