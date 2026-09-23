# FullWorth Android internal testing

FullWorth can produce an installable Android APK for internal testing without creating or storing a production Android signing identity.

## What the workflow produces

`.github/workflows/android-internal-apk.yml` publishes the MAUI Android app in Release configuration against:

`https://api.fullworth.org/`

The workflow creates a short-lived signing identity inside the GitHub-hosted runner, signs the APK, calculates a SHA-256 digest, uploads the APK plus digest as a GitHub Actions artifact, and then removes the temporary keystore.

The artifact is retained for 14 days.

## Security boundary

The internal-test signing identity is intentionally ephemeral.

- No Android signing key or password is committed to the repository.
- No persistent signing secret is required in GitHub.
- The temporary signing password is masked in workflow output.
- The temporary keystore is removed at the end of the job.
- The generated APK is **not** a production or Google Play release artifact.
- A future production signing identity must be created once, protected outside source control, backed up securely, and reused for all production updates.

Because every internal workflow run has a different signing identity, Android can reject an update over an APK from an earlier run. Uninstall the older FullWorth internal-test APK before installing a newer artifact if Android reports a signature mismatch.

## Build triggers

The workflow runs automatically when Android/MAUI-relevant source reaches `master`. It can also be launched manually from GitHub Actions after the workflow exists on the default branch.

## Install test

Download the `fullworth-android-internal-<commit>` artifact from the successful workflow run. Verify its included SHA-256 file before sideloading the APK onto a test Android device.

Android may require the tester to explicitly allow APK installation from the browser or file manager used to open the artifact. Disable that permission again after testing if it is no longer needed.

Use only controlled FullWorth test accounts. Do not place real financial credentials, Plaid secrets, database credentials, production tokens, or another person's financial information into an internal test.

## What this does not prove

A successful APK build does not prove:

- Google Play readiness;
- production signing-key custody;
- Play App Signing enrollment;
- store review or policy compliance;
- real-device behavior;
- installed-PWA acceptance;
- Plaid Hosted Link provider behavior;
- production deployment state.

Those remain separate release gates.
