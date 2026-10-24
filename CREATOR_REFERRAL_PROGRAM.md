# Creator referral and commission implementation plan

Status: proposed, default-off future acquisition capability; 2026-10-24 UTC.

Owner-selected intended pricing is USD 7.99/month and USD 59.99/year, subject to live currency/Price verification and existing launch gates. A 20% rate, first-12-month service window, 30-day referral window, 30-day minimum hold, monthly payout review and USD 25 threshold are **proposed pilot defaults**, not an executed agreement or authorization to move money.


## Economics and boundaries

At the proposed 20% rate, an eligible USD 7.99 monthly collection yields USD 1.60 commission and USD 6.39 retained before processor, refund and service costs. An eligible USD 59.99 annual collection yields USD 12.00 commission and USD 47.99 retained before those costs. The annual offer is USD 35.89 (about 37.43%) below twelve monthly payments. Twelve separately rounded monthly commissions total USD 19.20.

Actual processor/provider/support costs must be measured before setting an allowable CAC or scaling. Treat unknown costs as unknown. Keep cash, annual service-period revenue, variable contribution, acquisition expense and fixed overhead distinct. Annual cash is not immediate profit; reserve future service/refund costs and unpaid commissions. Contribution after acquisition subtracts commissions once. CAC reporting must not cause the same cost to be subtracted twice. No profitability, conversion or retention forecast is supported by this arithmetic.

## Confirmed inputs and proposed decisions

Recommended pilot defaults: 20% commission, eligible service limited to the first 12 calendar months from the referred customer's first qualifying paid service start, new paying customers only, no automatic customer discount, 30-day referral window, 30-day minimum commission hold, monthly manual-reviewed payouts and USD 25 payout threshold. These are design proposals, not accepted creator contracts. Confirm them before inviting creators or enabling payments. Prefer a small initial group of 3–5 approved creators; this is an effort limit, not a forecast.

Changing the business price does not authorize changing Stripe configuration or existing customer contracts. Preserve actual Price IDs, live/test isolation, renewal disclosures and all revenue-readiness gates. Do not promise guaranteed savings, complete bill coverage, automatic cancellations or unsupported financial outcomes.


## Referral journey and customer experience

Each creator receives a unique non-sensitive code and a first-party link such as https://fullworth.org/?ref=EXAMPLE (illustrative, not a live supported route). A validated link records a provisional referral candidate. At registration, bind the candidate to the authenticated customer through a server-controlled flow and strip referral parameters before entering protected financial screens. A manual creator-code field before the first paid checkout supports spoken recommendations, blocked storage and cross-device purchases.

Show the selected creator, explain that the creator may receive commission, and allow correction before the first qualifying payment. A creator code is an attribution identifier, not a coupon, access key, authentication credential or entitlement. Invalid or expired codes get a clear message and must not block ordinary registration, checkout, cancellation, export or account deletion. No automatic discount or stacking of discounts is included in the pilot.

Browser-only tracking cannot measure every visit or reliably follow a person across devices. Persist only permitted first-party referral data, respect applicable consent and storage choices, and offer manual code entry without fingerprinting. Report observed visits and attributed purchases honestly; do not claim complete influencer traffic coverage.


## Attribution policy and conflict resolution

Proposed deterministic precedence: a valid creator code explicitly confirmed by the customer before first payment overrides a stored link candidate; otherwise use the last valid creator-link candidate captured within the preceding 30 x 24 hours. With neither, retain unattributed/organic. Store reason, candidate time, source, policy version and final attribution time. Do not match by email guesses, IP addresses or financial history.

At signup, a valid unexpired link candidate may be bound provisionally to the account and survives a later trial or purchase delay. The anonymous candidate expires after 30 days if no signup occurs. At first qualifying payment, freeze the creator, commission rate and policy snapshot. Later links cannot switch a paying customer or start a new earning window. A valid manual code can be selected before that first payment even if no browser candidate exists.

One customer has at most one commission-bearing acquisition attribution. Existing paying customers, self-referrals, staff/test/free accounts, duplicate subscriptions and re-created accounts used to restart rewards are ineligible. Returning former payers do not qualify as new customers. Detect suspected abuse for review without covert tracking; acknowledge that account recreation cannot be perfectly prevented. Legitimate household members must not be rejected solely for sharing a network.

After first payment, only an authorized operator may correct attribution using an audit reason and compensating ledger adjustments. Never overwrite paid history. Creator suspension blocks new attribution immediately; whether previously earned eligible commissions remain payable follows the recorded agreement and review outcome. Renaming or retiring a public code must not reassign historical sales; do not recycle codes to another creator.


## Commission base, window and rounding

Commissionable base is successfully collected subscription consideration allocated to eligible FullWorth service, after customer discounts and credits, excluding tax and any refunded or lost-dispute portion. Processing fees are a FullWorth cost, not deducted from the proposed creator base. Unpaid invoices, free trials, complimentary access, zero-value invoices, out-of-band paid markings and unrelated products do not create commission. Confirm actual cash settlement/payment evidence; subscription status or a Checkout return URL alone is insufficient.

Use integer minor units and an integer basis-point rate, initially 2,000 basis points. Allocate money to eligible invoice lines deterministically, calculate commission once per settled eligible invoice allocation, and round half up to the nearest cent. On later refunds, recompute the target commission on the remaining eligible collected amount and append only the delta. Full reversal cannot exceed the original credited commission; repeated events cannot create another credit.

The earning interval is [first qualifying paid service start, start plus 12 calendar months), recorded in UTC with a documented calendar rule. Include only the portion of paid service overlapping that interval. The first full-year invoice is eligible; the next annual renewal is not. Monthly renewal after the boundary is not eligible. A monthly-to-annual switch near month 12 earns only on the portion inside the original interval, rather than a full additional year's annual payment.

Proration credits, upgrades, downgrades, refunds and plan changes must be allocated to their original service periods and payments so service is never commissioned twice. Cancellation stops future collection but does not itself reverse valid unrefunded earnings. A resumed subscription retains its original attribution and expiry. Unknown allocation, multiple currencies, mixed products, manual invoices or ambiguous credits place the affected commission in review; ordinary customer access continues through existing billing rules.


## Ledger and payment reconciliation

Keep acquisition attribution and the commission ledger separate from subscription entitlements. Current development source includes StripeBillingService, signed webhook handling, entitlement reconciliation and short-lived webhook receipts. It has no inspected creator implementation. Extend those boundaries with dedicated services; do not treat an active subscription or an existing webhook receipt as proof of cash or creator earnings.

Persist immutable money entries and explicit transitions: pending, approved/available, reserved in payout, paid; plus on-hold, rejected and reversed/adjusted entries. A creator's balance is derived from ledger entries and payout allocations, not a manually edited total. Keep original and compensating records, their currency, source payment/invoice/refund/dispute references, eligibility dates, policy version and operator reason.

Validate webhook signatures against the raw body. Persist event receipt and ledger mutations atomically. Enforce durable uniqueness on the underlying commission source allocation as well as provider event IDs; provider retries and distinct events describing the same payment must converge. Current short webhook-receipt retention is not a durable financial deduplication guarantee. Maintain financial uniqueness for the applicable ledger retention period.

Handle out-of-order and missed events through authoritative provider reconciliation. A refund received before the payment event must not later produce positive commission on refunded money. Dispute opening holds affected earnings; resolution releases or reverses the appropriate amount. Failed provider reads and ambiguous commits retry safely; do not mark work complete before the durable transaction. Do not acknowledge a failed ledger write as processed.

A daily bounded reconciliation compares payment/refund/dispute source objects with ledger allocations, detects gaps and records a last-success watermark. A separate monthly close reconciles the statement, paid cash, approved balance and carryover. Display stale or failed reconciliation explicitly; do not label a dashboard live when its money data is delayed. No scheduler or production job is created by this chapter.


## Payout policy and operating procedure

Proposed payout availability: no earlier than 30 days after successful collection and only after pending refunds/disputes, reconciliation and eligibility review clear. Pay approved available balances of at least USD 25 on the 15th of the following monthly payout cycle. Carry smaller balances forward visibly. The agreement must define termination settlement, late adjustments and any legally required payment timing; do not silently expire earned balances.

For a small pilot, maintain an operator-reviewed payout batch rather than building automatic split payments immediately. Freeze the included ledger entries and terms, review creator identity/payment destination through a secure approved process, check available cash and required tax/payee documentation, then obtain authorization for the actual money movement. Creating this plan or a payout preview does not authorize sending funds or establishing provider accounts.

Payout execution uses a stable batch idempotency key and a unique external reference. Prevent one approved entry from being allocated to multiple batches. An ambiguous payment result remains reserved/unknown until independently reconciled; do not blindly resend. Mark paid only from confirmed external evidence, not from clicking a button. Record failures, retry decisions, settlement time, payout fee and statement reference.

A refund after payout creates a transparent negative adjustment against later earnings under the accepted terms; never silently debit a creator's bank account. Review a final negative balance manually. For scale, reassess a compliant payout provider and automation only when volume, cost and reconciliation evidence justify it; no provider selection or new subscription is approved here.


## Founder dashboard

The first reporting release is an authenticated admin view with date range, creator, monthly/annual plan, acquisition cohort, currency and live/test filters. Show both activity during the selected period and first-payment cohorts clearly. Default commercial views exclude test, staff, bots and duplicate records, with exclusion counts available. Limit bulk export to authorized operators and sanitize spreadsheet-formula prefixes in CSV text.

Acquisition view: observed link visits, attributed signups, verified first-value activation, first-time payers and signup-to-paid conversion. Unique visits are an estimate with a stated session definition, not known people. Paid conversion denominator is attributed eligible signups in a defined matured cohort; report the numerator, denominator and incomplete observation window. Code-only purchases have no observed link visit, so do not divide all code purchases by link clicks as one conversion funnel.

Revenue view: gross collected subscription cash, discounts, tax excluded, refunds/disputes, eligible net collections and annual service-period revenue shown separately. Liability view: pending, held, available, reserved and paid commissions, negative adjustments and next payout date. Business view: realized contribution before/after acquisition, cost completeness and cash reserved for annual service. Never label receipts or retained pre-cost cash as profit.

Quality view: signup-to-first-value, mature second-month payment retention, cancellations, refunds, support time, complaints, suspected duplicate/self-referrals and attribution disputes by creator. Second-month retention denominator includes every monthly first payer whose renewal opportunity matured, including pre-renewal cancellations; annual customers are separate. Show payment failure separately from voluntary cancellation. Link aggregate totals to an operator-only, auditable payment-to-commission explanation.

Operational health: webhook processing lag, failed/retried events, unmatched payments/refunds, last reconciled time and failed payout batches. Provisional figures must be labeled. All commercial money totals reconcile to source allocations within a single currency, and dashboard balances reconcile to the ledger and payout history.


## Creator view and privacy

A later creator portal exposes only that creator's code/link, accepted terms/rate, aggregate visits/signups/payers, eligible revenue, pending/available/paid balances, adjustments with non-sensitive reasons, and payout statements. Build the founder ledger/reporting first. Creators cannot browse referred users, email addresses, bank names, balances, bill documents, transaction histories, account IDs or another creator's performance.

Every API, BFF route, report and export enforces the authenticated role and creator scope server-side. Apply CSRF protection to mutations, rate limits to code validation and audited operator actions to attribution/rate/payment changes. Anonymous code validation must not disclose balances, private creator details or whether a particular person subscribed. Use privacy-reviewed aggregates and suppress unnecessary small-cohort detail.

Keep finance and payment identifiers in access-controlled records; use opaque internal IDs in any necessary Stripe metadata. Do not put customer banking or bill content in metadata, referral URLs, analytics, logs, public issues or screenshots. Define separate retention schedules for short-lived click data and legally required payment records. Customer deletion removes/anonymizes referral analytics and unnecessary personal links while retaining only records required under the reviewed financial/legal policy.


## Proposed data model and service boundaries

Creator: stable ID, unique public code, status, accepted agreement/version, effective commission policy and secured payout-profile reference. ReferralCandidate: opaque token, creator ID, source, capture/expiry times and consent state. CustomerAttribution: owned customer ID, creator ID, explicit-code/link reason, provisional/frozen state, first-paid time, earning-window dates and immutable policy snapshot.

PaymentAllocation: unique provider account/mode/payment/invoice-line identity, currency, collected/discount/tax/refund amounts, service dates and eligible overlap. CommissionEntry: signed minor-unit amount, rate, allocation reference, adjustment reason, hold/availability state and event idempotency reference. PayoutBatch/PayoutAllocation: frozen entries, totals/currency, stable payment key, status, external confirmation and audit metadata. Receipt/ReconciliationRun/AuditEvent: processing outcome, retries, watermark and actor/reason.

Reuse the existing database, authentication, billing and BFF architecture. Add additive migrations, foreign keys, unique constraints, status-transition rules and transactional balance allocation. Keep affiliate logic in dedicated gateways/services and do not overload subscription access keys, paid entitlements or the customer financial ledger. Existing payment authorization, deletion exemptions and bank ownership rules remain authoritative. Names above are design concepts, not existing classes or endpoints.


## Disclosure, creator terms and abuse controls

Before launch, have the relevant reviewer check the actual creator agreement, customer disclosures, privacy notice, tax/payee process and payout policy for the business jurisdiction. The agreement needs eligible revenue, rate/window, attribution precedence, hold/threshold, refunds/disputes, termination, fraud review, correction/appeal path, payout costs and change notice. Freeze accepted terms per referral; apply changes prospectively.

Creators must clearly disclose the paid relationship near their recommendation and link, using language readers understand, such as 'I earn a commission if you subscribe through my link or code.' Train creators, review the initial promotional material and monitor disclosures and claims. Prohibit invented outcomes, paid-positive reviews, spam, misleading savings promises and unauthorized coupon-site distribution. Pause new referrals on supported abuse concerns and preserve an appeal/audit trail.


## Pilot design and decision rules

Start only after existing same-release product, security/recovery, provider/device, legal and subscription lifecycle gates pass, and actual per-customer costs have been measured. Recruit 3–5 relevant creators through separately authorized outreach, each with accepted terms and one clearly disclosed campaign. Begin with commission-only terms; no signing bonus, guaranteed payment, automatic discount or extra software purchase is assumed.

Review weekly for attribution failures, false payment credits, privacy exposure, unexpected support costs, poor activation and deceptive claims. Review mature monthly cohorts at 30/60/90 days, reporting sample counts and uncertainty. Annual renewal cannot be proven in 90 days. Compare with an available non-creator cohort descriptively; do not claim creator causation from an uncontrolled tiny sample or attributed revenue that may have occurred organically.

Continue a creator only when collected/refund-adjusted revenue reconciles, the cohort receives reliable first value, realized contribution after acquisition is positive, support fits the available margin and no critical trust problem remains. Choose a concrete cash/payback ceiling from verified costs before launch; no numerical CAC benchmark is invented here. Pause new promotion for negative measured contribution, broken attribution, unpaid balances that cannot be funded, critical money/privacy defects or deceptive claims. Pay valid accrued obligations under the accepted terms.


## Implementation sequence and acceptance

CR-0 — Decision and cost record. Record intended pricing, proposed terms, actual processor/provider/service costs, currency/refund choices and launch gates in the appropriate access-controlled business records. Deliver this specification and its linked roadmap queue. Planning completion is not launch readiness.

CR-1 — Attribution foundation. Implement creator administration, validated link/manual-code capture, account binding, frozen attribution and audit corrections behind a default-off feature flag. Acceptance: link/code conflicts, expiry, trial delay, consent-denied storage, cross-device code entry, invalid/retired codes, existing payers and cross-owner attempts. Normal onboarding must remain usable when attribution is unavailable.

CR-2 — Financial ledger. Add payment allocations, immutable commissions, refund/dispute adjustments, durable uniqueness and reconciliation using the existing subscription integration. Acceptance: real relational transactions and independent webhook deliveries/retries, ambiguous commit recovery, replay after receipt cleanup, out-of-order refund/payment, unpaid invoices, zero invoices, unrelated prices, prorations and window boundaries. Verify both monthly and annual calculations without changing customer entitlements.

CR-3 — Founder reporting and payout preview. Build scoped dashboards, drilldowns, exports, balance reconciliation and immutable proposed payout batches. Acceptance: aggregate-to-source reconciliation, unknown/stale states, multiple currencies kept separate, role boundaries, concurrent batch reservation, refunded already-paid entries and uncertain payment outcome. No automated fund transfer.

CR-4 — Creator visibility and operating acceptance. Add the limited creator view and disclosure/terms workflow, validate keyboard/mobile/loading/error behavior, and exercise secure payout reconciliation in an isolated non-live environment. Creator and admin permissions must be negatively tested. Keep platform-native heading/help text clear; do not show implementation internals to customers.

CR-5 — Controlled activation. Complete current REVENUE_READINESS.md and exact-release acceptance, verify real configured prices and payment/refund events, obtain explicit commercial/outreach/payout authorization, then enable for the limited approved cohort. Require exact-head CI/security gates and guarded deployment. Add monitoring, rollback and a support owner. No unit test or synthetic payout alone establishes live collection or settlement.

CR-6 — Evaluate and expand conditionally. Review mature contribution/retention and reconciliation evidence, honor existing creator obligations and decide whether to continue, change prospective terms or pause new acquisition. Defer tiers, lifetime rewards, broad self-service applications, global currencies, automated payouts and discount stacking until the pilot supports them.


## Roadmap continuation and source basis

Continue financial truth, #934 vNext and release acceptance before creator launch. Reuse active R4/R5 and backend work; inspect fresh PR/CI state before changing overlapping files. Advance the commercial specification without calling existing release gates complete.

Planning basis: current development source at `d9d6c0d884d8e8e87b6a92708afe845b186fd9e4`, including FULLWORTH_ROADMAP.md, REVENUE_READINESS.md and the subscription options/service. Existing Checkout, signed webhook and entitlement infrastructure is present; no creator-specific implementation was found in the inspected tree. This plan neither deploys nor enables billing, attribution or payouts.

Official technical references: Stripe webhook guidance https://docs.stripe.com/webhooks ; subscription events https://docs.stripe.com/billing/subscriptions/webhooks ; metadata privacy https://docs.stripe.com/metadata . These support signature, retry/order and data-minimization requirements; the business-specific attribution/rate/payout rules above are proposals.

Disclosure reference: FTC Endorsement Guides FAQ https://www.ftc.gov/business-guidance/resources/ftcs-endorsement-guides-what-people-are-asking . Its guidance supports clear relationship disclosure and a reasonable creator training/monitoring program. It does not approve FullWorth's agreement or settle jurisdiction-specific legal/tax requirements.

Open commercial decisions before activation: accept or revise the proposed rate/window/hold/threshold; verify USD currency and actual Price configuration; set refund/service and payout-fee policy; establish actual variable costs and cash reserve; approve agreement/disclosure/payee handling and a payout method; choose pilot creators and authorize outreach. These do not block documenting or implementing the default-off foundation within the normal repository workflow.

