# Google and Apple sign-in setup

FullWorth keeps provider credentials on the server. Never commit OAuth client
secrets, Apple private keys, generated Apple client-secret JWTs, or production
environment files.

The current production Web origin is:

`https://fullworth.org`

The current server-side OpenID Connect callback URLs are:

- Google: `https://fullworth.org/signin-billwatch-google`
- Apple: `https://fullworth.org/signin-billwatch-apple`

The `signin-billwatch-*` path names are compatibility identifiers. The public
product/domain remains FullWorth.

## Production environment variables

The protected `.env.production` file supports these optional credential pairs:

```text
FULLWORTH_GOOGLE_CLIENT_ID=
FULLWORTH_GOOGLE_CLIENT_SECRET=
FULLWORTH_APPLE_CLIENT_ID=
FULLWORTH_APPLE_CLIENT_SECRET=
```

A provider remains disabled unless both values in its pair are present.
`deploy/validate-production-env.sh` rejects partially configured pairs.

Do not paste the values into chat, issues, pull requests, Actions logs, or
repository files.

## Google

Create a Google OAuth client for a Web application.

Configure the authorized redirect URI exactly as:

`https://fullworth.org/signin-billwatch-google`

The URI must match exactly, including scheme, host, path, case, and trailing
slash behavior.

Use the Google OAuth client ID for:

`FULLWORTH_GOOGLE_CLIENT_ID`

Use its client secret for:

`FULLWORTH_GOOGLE_CLIENT_SECRET`

The API receives the same client ID as the accepted token audience.

After configuration, run the production environment preflight before any
deployment.

## Apple

Sign in with Apple for the Web requires Apple Developer configuration outside
the FullWorth repository.

Configure:

1. a primary App ID with Sign in with Apple enabled;
2. a Services ID associated with that primary App ID;
3. the website domain `fullworth.org`;
4. the return URL:
   `https://fullworth.org/signin-billwatch-apple`;
5. a Sign in with Apple private key.

Use the Services ID as:

`FULLWORTH_APPLE_CLIENT_ID`

Apple's OAuth client secret is not an ordinary static password. It is a signed
JWT generated with the Sign in with Apple private key. Store only the generated
client-secret JWT in:

`FULLWORTH_APPLE_CLIENT_SECRET`

Keep the Apple `.p8` private key outside the repository and outside the
application host when practical. The current FullWorth configuration consumes
the generated JWT as a protected environment value.

Apple client-secret JWTs expire. Apple does not allow an expiration more than
six months in the future, so rotation must be scheduled before expiration.
FullWorth does not currently auto-generate or auto-rotate the Apple client
secret.

## Security behavior

Provider configuration does not weaken FullWorth's account boundaries.

- Provider ID tokens remain server-side during Web sign-in and registration.
- Provider access/refresh tokens are not persisted by the Web OIDC handler.
- Existing-account provider linking requires current FullWorth credentials and
  the current second factor when 2FA is enabled.
- Provider-backed account creation requires current Terms/Privacy acceptance.
- Provider-backed account creation requires a provider-verified email.
- FullWorth does not automatically link a provider to an existing account just
  because the provider reports the same email.
- New provider-backed accounts still create a FullWorth password so existing
  account deletion, recovery, 2FA, and sensitive-setting reauthentication
  boundaries remain intact.

## Verification sequence

Do not enable both providers at once for the first proof.

For each provider:

1. configure only that provider's credential pair in the protected production
   environment;
2. run `sh deploy/validate-production-env.sh .env.production`;
3. deploy only a verified `master` release through
   `sh deploy/deploy-production.sh .env.production`;
4. confirm the provider button appears on sign-in and registration;
5. create a controlled test account through the provider;
6. confirm first-run setup opens;
7. sign out and sign back in through the provider;
8. for an existing email/password test account, verify provider linking from
   Settings and then provider sign-in;
9. verify a provider identity cannot silently attach itself to a different
   existing FullWorth account;
10. verify logout and account-security flows still work.

Only after one provider passes this sequence should the second provider be
enabled and tested.
