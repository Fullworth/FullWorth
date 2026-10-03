# Stripe webhook security

FullWorth accepts Stripe events only at
`POST /api/subscription/webhooks/stripe`. The endpoint is anonymous because
Stripe cannot use a FullWorth session, but it is not unauthenticated in the
provider sense: the raw request body must pass Stripe-signature verification
before event data is parsed or used.

## Authentication and input boundary

The endpoint:

- returns 404 when Stripe billing is not fully configured;
- requires an `application/json` content type;
- rejects declared and chunked bodies above 256 KiB;
- requires a `Stripe-Signature` timestamp and at least one v1 signature;
- accepts timestamps only within five minutes of the application clock; and
- compares the HMAC-SHA256 signature in constant time against the protected
  webhook signing secret.

The raw body is signed exactly as received. Do not deserialize and reserialize it
before verification. The webhook signing secret is a production secret and must
remain outside source control and logs.

## Replay and duplicate-delivery boundary

Every accepted payload must contain a bounded Stripe event ID matching
`evt_` plus ASCII letters or digits, with a maximum length of 255 characters.
After successful handling, FullWorth stores only that provider event ID and the
completion timestamp in `StripeWebhookEvents`; it does not persist the webhook
payload in the receipt.

A receipt lookup short-circuits ordinary retries. The event ID is also the table
primary key, so concurrent deliveries have a database-enforced final authority.
The receipt and any paid-entitlement changes are committed by the same
`SaveChanges` transaction. A handler or database failure therefore leaves no
receipt that could suppress Stripe's retry.

Customer-subscription events reconcile current provider state instead of trusting
delivery order. A delayed active event cannot restore an entitlement after Stripe
reports the current subscription as canceled. Completed receipts older than 32
days are pruned in bounded batches of 500; this exceeds Stripe's normal retry and
manual resend windows while preventing indefinite receipt retention.

## Verification and operations

Repository tests cover:

- invalid, missing, and out-of-range signatures;
- payload limits with and without `Content-Length`;
- non-JSON content;
- replay of both handled and ignored events;
- skipped provider work on replay;
- atomic receipt persistence with entitlement changes; and
- expired-receipt pruning.

These tests establish the repository boundary. They do not prove live Stripe
delivery, production clock health, edge-network source filtering, or deployed
database migration state. Production acceptance still requires a guarded release
and provider-observed delivery against that exact deployed release.
