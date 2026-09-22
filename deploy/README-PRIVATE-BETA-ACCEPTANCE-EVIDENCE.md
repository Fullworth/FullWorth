# Private-beta acceptance evidence

FullWorth keeps machine proofs and human observations distinct, then allows them to be correlated to one exact deployed release without overstating what they prove.

## Plaid Hosted Link observation

Use a controlled private-beta account and an existing owned Plaid connection. Store the password in a non-symlink mode-600 file outside the checkout, and choose protected pending/completed evidence paths outside the checkout.

Set `BILLWATCH_PLAID_OBSERVATION_EMAIL`, `BILLWATCH_PLAID_OBSERVATION_PASSWORD_FILE`, `BILLWATCH_PLAID_OBSERVATION_CONNECTION_ID`, `BILLWATCH_PLAID_OBSERVATION_PENDING_FILE`, and `BILLWATCH_PLAID_OBSERVATION_EVIDENCE_FILE`. Then explicitly opt in with `BILLWATCH_PLAID_OBSERVATION_ALLOW_PREPARE=true` and run:

```sh
sh /opt/billwatch/deploy/run-plaid-observation-proof.sh prepare /opt/billwatch https://api.fullworth.org
```

The command authenticates the controlled tester, verifies ownership of the configured connection, creates a real update-mode Hosted Link session, rejects non-HTTPS/non-Plaid destinations, and writes a protected release-pinned pending record. The Hosted Link URL is printed for the operator but is never stored in completed evidence.

Complete the flow in Plaid Hosted Link. Then set the exact confirmation phrase shown by the script contract:

```sh
export BILLWATCH_PLAID_OBSERVATION_CONFIRMATION='I completed the BillWatch Plaid update flow in Plaid Hosted Link'
sh /opt/billwatch/deploy/run-plaid-observation-proof.sh confirm /opt/billwatch https://api.fullworth.org
```

Confirmation calls the real server-side Hosted Link completion endpoint, runs connection-scoped account and transaction syncs, and requires the owned connection to be Active with a successful sync timestamp before publishing metadata-only evidence. Completed evidence contains no password, bearer token, Hosted Link URL, session ID, connection ID, institution name, or provider response body.

## Installed-device observation

Installed-device acceptance is a human interaction gate, not a browser-emulation result. Test the exact deployed release on an installed Android PWA. iOS added-to-home-screen acceptance should also be recorded when an iOS device is available.

For each platform, verify all of the following before recording evidence:

- installed PWA launch;
- keyboard resize behavior on editable fields;
- Back/navigation behavior;
- the controlled waiting-worker PWA update flow;
- statement file picker behavior;
- Change password / Change email security dialogs in the installed/mobile context.

Plaid Hosted Link return remains separately proven by the Plaid observation flow above.

Choose a protected evidence path outside the checkout, set `BILLWATCH_INSTALLED_DEVICE_PLATFORM` to `android` or `ios`, explicitly opt in with `BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_ALLOW_RECORD=true`, and set the exact confirmation phrase for that platform:

```sh
export BILLWATCH_INSTALLED_DEVICE_PLATFORM=android
export BILLWATCH_INSTALLED_DEVICE_EVIDENCE_FILE=/secure/billwatch/android-device-acceptance.state
export BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_ALLOW_RECORD=true
export BILLWATCH_INSTALLED_DEVICE_ACCEPTANCE_CONFIRMATION='I completed the FullWorth installed-device acceptance checks on Android'

sh /opt/billwatch/deploy/record-installed-device-acceptance.sh /opt/billwatch
```

For iOS, use a separate evidence file, set the platform to `ios`, and use the confirmation phrase `I completed the FullWorth installed-device acceptance checks on iOS`.

The recorder pins the attestation to the verified release and writes only metadata: release SHA, completion time, platform, and the fixed passed-phase list. It does not record device identifiers, account data, screenshots, statement contents, credentials, tokens, or provider responses.

## Same-release acceptance bundle

After the existing machine technical proof, alert-observation proof, Plaid-observation proof, and Android installed-device evidence are complete for the same release, set their evidence paths plus a new output path and run. Set `BILLWATCH_ANDROID_DEVICE_EVIDENCE_FILE` to the Android evidence above. If iOS acceptance was performed, also set `BILLWATCH_IOS_DEVICE_EVIDENCE_FILE` to that separate evidence file:

```sh
sh /opt/billwatch/deploy/verify-private-beta-acceptance-evidence.sh /opt/billwatch
```

The verifier refuses cross-release, incomplete, symlinked, weakly permissioned, or in-checkout evidence and can atomically publish a mode-600 metadata-only acceptance record. Android installed-device evidence is required. iOS evidence is verified and included in the combined phases when supplied.

This bundle still does **not** claim provider-enforced immutable/Object-Lock/WORM backup protection or qualified Terms/Privacy review. Those remain separate launch gates and must not be inferred from a successful acceptance evidence file.
