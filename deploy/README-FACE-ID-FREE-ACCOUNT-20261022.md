# Face ID path for a free Apple account — 2026-10-22

## Supported next target

The owner confirmed a free Apple account. Do not promise Face ID inside the
personally signed WKWebView app: free personal teams cannot provision Apple's
Associated Domains capability. An unsigned IPA, Face ID usage description, native
biometric unlock prompt or fabricated Team ID does not solve that requirement.

The next physical acceptance target is FullWorth in iPhone Safari, optionally
added to the Home Screen. Website passkeys do not require signing FullWorth with
an Apple Developer team. Safari and the installed shell have separate sessions.
This pass adds protocol regression evidence; it does not enable production or
prove a Safari, Home Screen, iCloud sync or biometric result.

## Evidence added

Existing virtual-key tests used USB transports and increasing counters. The new
SyncedPlatformPasskeyTests exercise actual API routes and the configured .NET
Identity verifier with ES256-signed discoverable platform credentials:

- Zero signature counters on enrollment and three successive sign-ins.
- Both initial backup states and subsequent backup-state changes.
- Platform attachment, internal transport, required resident keys and user verification.
- Same-challenge replay rejection even when the counter remains zero.
- Rejection of missing presence/verification, changed backup eligibility,
  impossible backup flags, wrong origin/RP, bad signature and stale challenge.
- Failed completion leaves stored credential metadata intact, consumes the
  challenge, and allows a valid new ceremony.
- First-step platform sign-in still requires valid authenticator MFA when enabled.
- Password-first MFA completion with the owned platform key and single-use proof.
- Successful sign-ins enroll a working refresh session.

These thirteen cases use test-generated keys and an in-memory database. They do
not claim Apple hardware, browser serialization/user-gesture compatibility,
cross-device synchronization, PostgreSQL contention, recovery or deployed acceptance.
No production verifier behavior needs relaxing for zero counters. Do not replace
cryptographic verification with a successful local biometric prompt.

## Physical Safari acceptance procedure

Use controlled test accounts on a reviewed HTTPS deployment with the exact
tested source, Passkeys:Origin matching the public Web origin, AddPasskeys applied,
and one API process. Follow the existing controlled rollout in
deploy/README-PASSKEY-ROLLOUT-20261021.md. A disabled production feature cannot
be tested by installing a new IPA. Do not change security defaults simply because
these protocol tests pass.

1. On the iPhone, open the reviewed deployment in Safari. Record source SHA,
   public origin, iOS version and Safari versus Home Screen mode.
2. Sign in with the existing password and authenticator if configured.
3. In Settings → Passkeys & security keys, use the current password and required
   authenticator code to add a key. Complete Apple's prompt. Never record codes,
   private keys, biometric data, cookies or tokens.
4. Sign out and use Sign in with Face ID or a passkey. Record the actual prompt
   and whether authentication completes. A device passcode alternative is not
   evidence that Face ID itself was exercised.
5. Repeat sign-out/sign-in three times, including a reload and a new browser
   session. Test both session-persistence choices and logout.
6. Cancel the system prompt and confirm password/authenticator fallback works.
7. Repeat with an MFA-enabled test account: first-step passkey without an
   authenticator code must not grant access; valid code must complete it.
8. Also exercise password-first continuation with the enrolled passkey.
9. Remove the key and confirm it cannot begin a new successful sign-in.
   Existing sessions are separate; use explicit session revocation when testing it.
10. If adding to the Home Screen, repeat enrollment/sign-in/cancellation/session
    checks there. Do not infer that Safari acceptance proves Home Screen behavior.

Keep a sanitized result for every step: pass/fail, timestamp, source/origin,
device/OS, expected and observed behavior. A failure stops activation until
diagnosed. No physical acceptance is recorded by this document.

## Installed app later

A paid team, the actual signing identity/app identifier prefix and final bundle ID,
a provisioned webcredentials:fullworth.org entitlement, a matching live AASA file,
and signed-device acceptance remain required for the installed-app route.
No team identifier, entitlement, association or signing credential is invented here.
The existing personal IPA remains a separate password/MFA testing artifact.

## References and remaining release gates

- Apple service association:
  https://developer.apple.com/documentation/authenticationservices/connecting-to-a-service-with-passkeys
- Microsoft platform/signing requirements, including free personal-team limits:
  https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/communication/passkeys?view=net-maui-10.0
- .NET verifier used by this release:
  https://github.com/dotnet/aspnetcore/blob/v10.0.13/src/Identity/Core/src/PasskeyHandler.cs

Production was last verified at dd31836703bd110a30f303ed3ce9fd13c0f61d82
with passkeys disabled. This test/document change does not deploy or activate them.
Issue #291's immutable off-host protection, real clean/compromised-host recovery
and controlled two-user/provider/statement acceptance remain open, along with
physical device, reboot/external-alert and legal/license evidence.
