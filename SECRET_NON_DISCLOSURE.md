# Secret non-disclosure release gate

FullWorth treats configured credentials as data that must not cross the
application's observable-output boundary. A release candidate is rejected when
an exact configured secret, or a common transport encoding of it, appears in a
public response or retained service log.

## Covered values

The verifier requires and checks the database password, parser authentication
token, Web-session Redis password, Plaid secret, and Restic password. It also
checks configured Stripe keys, external-identity client secrets, the Resend API
key, and the AWS secret access key when those optional integrations are enabled.

The verifier never prints the values. Failure output names only the affected
configuration variables.

## Observable surfaces

`deploy/verify-secret-non-disclosure.sh` collects:

- API and Web liveness and readiness responses;
- a malformed authentication request, which exercises the public validation
  and Problem Details error path without creating persistent data;
- an unknown API route, which exercises public diagnostic/not-found output;
- API, parser-worker, Web, session-cache, edge, and database container logs
  after the probes.

The API and Web production pipelines must retain generic
`UseExceptionHandler()` boundaries. API errors use Problem Details, and
development exception detail must not be enabled in either production program.
The regression test enforces those structural requirements in addition to
testing positive and negative runtime-verifier behavior.

For every non-empty protected value of at least eight bytes, the verifier scans
for the literal value, percent-encoded and form-encoded values, and standard
and URL-safe Base64 encodings. A match fails closed without echoing the secret.

## Release integration

Linux production-container CI runs the verifier against deterministic sentinel
credentials after the candidate stack is healthy. The guarded production
deployment runs it against the host-local protected environment after public
readiness and HTTP boundary checks, and before advancing
`.billwatch-release`.

A failure leaves the candidate unverified. The deployment cleanup path stops
the public candidate services and preserves the previous verified release
marker.

## Scope boundary

This gate proves non-disclosure through application responses and retained
service logs for the exact configured values. It does not claim that ordinary
container environment variables are stronger than Docker-daemon access:
moving credentials to file- or orchestrator-backed secret injection remains a
separate hardening item. Operators with Docker or root access can inspect
container configuration and must already be treated as privileged.

The gate also cannot prove that every possible derived representation is
absent. Reviews must continue to reject exception-message logging, credential
logging, and user-visible propagation of arbitrary upstream failures.
