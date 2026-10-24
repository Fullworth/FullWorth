# FullWorth revenue launch gate

FullWorth has a hosted Stripe Checkout and Customer Portal integration for monthly and yearly subscriptions. The existence of that code does not mean the deployed service is ready to charge customers. Keep `BILLWATCH_STRIPE_ENABLED=false` and `BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED=false` until the relevant gates below have been proven on the exact release.

## 1. Prove the product with controlled users

- Complete the same-release private-beta acceptance in [deploy/README-INTERNAL-BETA0.md](deploy/README-INTERNAL-BETA0.md), including authenticated Web/API use, a second user's ownership boundary, Plaid update mode, statement accuracy against known facts, account deletion, off-host recovery, and independent alert receipt.
- Confirm that a user can discover a real recurring bill and understand a real change with available evidence. Record missed bills and false alerts before charging for monitoring.
- Obtain qualified review of the exact Terms and Privacy Policy presented to paying users. Publish a working customer-support and billing-contact path.

## 2. Configure the commercial offer

- Owner-selected intended pricing (2026-10-24): USD 7.99/month or USD 59.99/year. Verify the actual billing currency, live/test environment, Product/Price IDs, interval and customer-facing amount before activation; this planning choice is not proof of live Stripe configuration. Confirm renewal, refund and support terms. Annual pricing is USD 35.89 below twelve monthly payments; do not infer savings from billing interval alone.
- Complete the applicable tax registration and Stripe Tax setup before collecting tax. Merely enabling automatic tax without a registration does not establish tax collection.
- Set up Stripe Customer Portal cancellation and payment-method management. Use a least-privilege restricted API key where its permissions cover this integration, and protect that key and the webhook signing secret outside source control.
- Configure a signed Stripe webhook for `POST /api/subscription/webhooks/stripe` and verify delivery for checkout, subscription update, cancellation, and payment-state changes.

## 3. Prove the payment lifecycle

- Run the [subscription rollout preflight](deploy/README-SUBSCRIPTION-ROLLOUT.md) with enforcement still off.
- Complete a controlled hosted Checkout purchase, then prove that FullWorth grants paid access only after the configured FullWorth Price is active. Verify that an unrelated Stripe Price never grants access.
- Open Customer Portal, cancel the test subscription, and prove that the signed webhook and provider reconciliation update local access correctly. Repeat with a delayed/retried event and a failed or past-due payment case.
- Run the read-only and opt-in lifecycle probes in [deploy/README-SUBSCRIPTION-ROLLOUT.md](deploy/README-SUBSCRIPTION-ROLLOUT.md). Keep receipt, provider state, local entitlement, and release evidence aligned without recording secrets or financial data in the repository.

## 4. Roll out safely

- Require the full backend, Android, and Linux container/recovery CI gate on the exact final PR head. Promote through `development` to a verified `master` release, then use the guarded production deployment.
- Enable Stripe billing first while subscription enforcement remains off. Verify live-mode Price display, hosted Checkout, Portal, webhook delivery, and entitlement reconciliation with a controlled account.
- Enable enforcement for `InternalTester` only after those proofs pass. Check paid, complimentary, unpaid, canceled, and expired accounts, including the permanent data-export, bank-disconnect, and deletion exemptions. Broaden the cohort only after a separate reviewed result.

Do not use a successful Checkout redirect, passing unit tests, or a healthy public endpoint as proof that money was collected or that the product is ready for external paid users.

## Optional creator acquisition gate

The [creator referral program](CREATOR_REFERRAL_PROGRAM.md) is planned, not implemented or enabled. It is not a prerequisite for a non-affiliate release. Before enabling commission-bearing referrals, additionally prove server-bound attribution, first-12-month eligible service allocation, durable collected-payment/commission uniqueness, refund/dispute reversals, ledger reconciliation, creator isolation and reviewed payout previews. Accept actual creator terms and verify costs before a limited pilot. Keep subscription access keys and entitlements separate from creator codes. Program activation, creator outreach and money movement require their relevant authorization; neither this checklist nor its proposed 20% rate authorizes them.
