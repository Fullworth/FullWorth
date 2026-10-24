# FullWorth

**Know when your bills change — and why.**

FullWorth is a **transaction-first bill-intelligence product** for recognizing recurring charges, spotting supported changes, and helping people understand what the available evidence says about those changes. It is **not** a bill-payment service or a general budgeting dashboard.

**Live website:** [fullworth.org](https://fullworth.org)  
**API origin:** [api.fullworth.org](https://api.fullworth.org)

> **Release status:** FullWorth is in pre-release/private-beta readiness work, not a broadly approved public launch. Working code, passing CI, and a healthy deployment are not substitutes for real-provider, financial-accuracy, recovery, device, and legal acceptance. See [Current state](#current-state) and [Release gates](#release-gates).

## How FullWorth works

1. **Transactions discover bills.** Owner-scoped bank transaction history is the starting point for recurring-bill recognition; a transaction is not automatically a bill just because money was spent.
2. **Statements add context.** Securely uploaded provider documents may explain plan changes, expired promotions, taxes, and fees after extraction, validation, and bill matching.
3. **Deterministic comparisons detect changes.** Amounts, currency, billing period, comparable history, and evidence quality must support a claim before it is shown as a change.
4. **Evidence supports explanations and alerts.** The product aims to answer **what changed**, **why it changed**, and **how much it may cost** without inventing a cause.

For a supported monthly increase, the intended presentation is a monthly difference and annualized impact, with the reason attributed only when supported by evidence. Unknown amounts, incomplete history, different currencies, and unavailable comparisons must remain visibly unknown—not an invented zero or an “all clear.”

See [Product strategy](FULLWORTH_PRODUCT_STRATEGY.md) and the [engineering roadmap](FULLWORTH_ROADMAP.md).

## Current state

**Reference date: October 23, 2026 (America/Denver).** This section is a dated evidence snapshot, not an automatic live status monitor.

| Area | Verified position / boundary |
| --- | --- |
| **Primary client** | **ASP.NET Core / Blazor Web + BFF**, with a mobile-focused browser/PWA experience. The website is the primary product surface. |
| **Native clients** | A .NET MAUI project and hosted-Web shells remain for compatibility and device testing. MAUI is **not** the primary product direction. |
| **Bank connections** | Plaid connection, transaction sync, recurring discovery, and connection-recovery logic exist. Sandbox and human/provider acceptance are still separate gates. |
| **Bill intelligence** | Deterministic discovery, bill comparison, statement upload/storage, text/OCR extraction, matching, and alert paths exist. Missing known bills and representative statement accuracy still require private evidence-based acceptance. |
| **Identity and security** | ASP.NET Core Identity, owner-scoped data access, server-side financial/provider credentials, Web/BFF sessions, and protected document workflows. Passing automated tests does not prove every real-world security scenario. |
| **Passkeys / Face ID** | Opt-in code exists, but production enablement remains **off by default** pending platform and physical-device verification. |
| **Payments and AI** | Subscription enforcement, paid rollout, and AI-derived persistence are **not enabled for general production use**. AI evaluation work is distinct from deterministic production persistence. |

### Branches and deployments are different

- **`master`** is the production/release branch. The most recently **documented guarded deployment and same-release authenticated smoke** verified commit [`dd318367`](https://github.com/Fullworth/FullWorth/commit/dd31836703bd110a30f303ed3ce9fd13c0f61d82) on **October 21, 2026** ([deploy evidence](https://github.com/Fullworth/FullWorth/actions/runs/39665707749), [authentication smoke](https://github.com/Fullworth/FullWorth/actions/runs/39666163983)). This is the last **recorded** production verification, not a fresh inspection of the host.
- **`development`** is the integration branch for features, fixes, and release hardening. Development-only work **is not deployed** merely because it merged. See the [current development branch](https://github.com/Fullworth/FullWorth/tree/development), [open pull requests](https://github.com/Fullworth/FullWorth/pulls), and [handoff context](FULLWORTH_CONTEXT.md).
- **Promotion** requires its own reviewed release PR, exact-commit checks, the guarded deployment workflow, and a separately verified runtime. Do not infer production state from GitHub branch heads.

## Technology and repository layout

| Location | Responsibility |
| --- | --- |
| [`FullWorth.Web/`](FullWorth.Web/) | Blazor interactive Web UI, browser-facing BFF, cookie authentication, PWA assets |
| [`FullWorth.API/`](FullWorth.API/) | ASP.NET Core API, Identity, EF Core/PostgreSQL, Plaid, bills, statements, background services |
| [`FullWorth.Core/`](FullWorth.Core/) | Shared models, contracts, deterministic domain logic |
| [`FullWorth.ParserWorker/`](FullWorth.ParserWorker/) | Isolated document-parsing/OCR worker |
| [`FullWorth.Tests/`](FullWorth.Tests/) | .NET integration/unit/regression tests and Node browser-logic suites |
| [`FullWorth.VisualTests/`](FullWorth.VisualTests/) | Visual/browser acceptance tooling |
| [`FullWorth.AiEvaluation/`](FullWorth.AiEvaluation/) | Offline/shadow AI evaluation tooling, not an enabled production bill-authority path |
| [`FullWorth.csproj`](FullWorth.csproj) | Transitional .NET MAUI client |
| [`compose.production.yml`](compose.production.yml), [`deploy/`](deploy/) | Hardened production topology, release guards, backup/recovery and operations scripts |

Stack: **.NET 10**, ASP.NET Core, Blazor, PostgreSQL/Entity Framework Core, ASP.NET Core Identity, Plaid, Docker Compose/Caddy, Redis-backed production Web sessions, encrypted Restic backups, xUnit, and native text/OCR extraction.

See [Architecture](ARCHITECTURE.md), [parser isolation](PARSER_WORKER_BOUNDARY.md), and [HTTP request security](HTTP_REQUEST_SECURITY.md). The modular-monolith design enforces data ownership rather than letting every module read every finance table.

## Run locally

**Prerequisites:** .NET 10 SDK, PostgreSQL, and appropriate **Plaid Sandbox** access for provider-backed flows. Use local .NET user secrets or another approved secret store; never check credentials, connection strings with passwords, bank data, or statement files into Git.

1. From a repository checkout, configure the API's local PostgreSQL connection and relevant provider settings through **user secrets for `FullWorth.API`**. The connection-string key is `ConnectionStrings:BillWatchDatabase`; Plaid configuration uses the `Plaid` section. Apply the committed EF migrations to your **local** database using the normal development tooling.
2. Trust the local ASP.NET Core HTTPS development certificate if your machine has not done so:
   ```powershell
   dotnet dev-certs https --trust
   ```
3. In one terminal, start the **API**:
   ```powershell
   dotnet run --launch-profile https --project .\FullWorth.API\FullWorth.API.csproj
   ```
4. In another terminal, start the **Web/BFF**:
   ```powershell
   dotnet run --launch-profile https --project .\FullWorth.Web\FullWorth.Web.csproj
   ```

| Local endpoint | URL |
| --- | --- |
| Web application | `https://localhost:7094` |
| API | `https://localhost:7243` |
| API liveness | `https://localhost:7243/health/live` |
| API readiness | `https://localhost:7243/health/ready` |

In Development, the Web/BFF defaults to `https://localhost:7243` as its API origin; an explicit `FullWorthApi:BaseUrl` overrides it. The Web session store uses an in-memory development fallback when Redis session settings are absent; **production requires its configured protected Redis/session boundary**.

The API needs a reachable database and valid configuration. Successful process startup, liveness, readiness, and usable provider-backed data are **different checks**. Real bank connections should only be exercised with authorized test accounts in the intended Plaid environment.

## Build and test

From the repository root:

```powershell
dotnet build .\FullWorth.Tests\FullWorth.Tests.csproj --configuration Release
dotnet test .\FullWorth.Tests\FullWorth.Tests.csproj --configuration Release --no-build
node --test (Get-ChildItem .\FullWorth.Tests\*.test.mjs | ForEach-Object { $_.FullName })
```

The last command uses PowerShell to expand every standalone JavaScript regression suite before passing its path to Node.js. In Bash, use `node --test FullWorth.Tests/*.test.mjs`. Use a recent Node.js installation. The required GitHub checks include:

- **Backend build and tests**: Release build, EF migration/model consistency, .NET tests, and **all** Node regression suites when applicable.
- **MAUI Android build**: retained while native compatibility remains in the solution.
- **Linux production container**: release images, runtime boundaries, native OCR/worker containment, and disposable encrypted backup/recovery checks.
- **Dependency review**: supply-chain change checks.

Additional browser, WebKit, iOS/Android device, governance and security workflows run where applicable. Docs-only changes may take the CI's documented lightweight path. A green disposable CI recovery test is **not** a real-host recovery certificate.

See [`.github/workflows/ci.yml`](.github/workflows/ci.yml) and [GitHub Actions](https://github.com/Fullworth/FullWorth/actions).

## Security principles

FullWorth handles sensitive financial information. Important implementation requirements include:

- Scope user-owned records, connections, transactions, statements, and alerts by the authenticated owner. Cross-owner IDs must not reveal someone else's data.
- Keep Plaid access tokens, Web bearer/refresh material, external-provider secrets, and protected statement storage **server-side**. Browser JavaScript must not receive raw financial credentials.
- Preserve no-store financial/auth responses, Web antiforgery/session boundaries, protected upload/download paths, rate limiting, and private logs.
- Treat statement text and extracted data as **untrusted evidence**, not instructions; validate before persistence or explanation.
- Never publish customer financial evidence, credentials, raw statement text, recovery secrets, or database dumps.

For sensitive findings, use [private security reporting](SECURITY.md), **not** a public issue.

## Release gates

FullWorth uses a **guarded, manually approved** production-release path. A passing PR does not authorize deployment. The [production deployment workflow](.github/workflows/production-deploy.yml) requires an exact approved `master` SHA and confirmation of the intended production target. Production database migrations are performed by a **separate, controlled migration process**—normal production API startup does **not** perform automatic migrations.

The following remain separate evidence requirements before a broader beta/launch:

- Controlled two-user ownership, Plaid connect/update/reconnect, one-time-token failure recovery, and representative known-truth transaction/statement checks.
- Physical installed-device checks, including relevant iPhone/Safari and Android paths.
- Real off-host, clean-host recovery (database, statements, **both** Data Protection key rings), provider-enforced immutable backup protection, alert-delivery observations, and controlled deletion/reboot proof.
- Qualified review of the deployed Terms/Privacy and separate commercial/payment lifecycle approval.

**Do not treat synthetic fixtures, passing CI, a green health endpoint, or an old smoke run as closure for those real-world gates.** Keep payment enforcement and AI-derived financial persistence disabled until their own release requirements are satisfied.

### Operational documentation

The root README is an orientation document, **not a shortcut for deploying a new production release**. Start with the maintained runbooks:

- [Production operations and verified release checks](deploy/README-OPERATIONS.md)
- [Backup and recovery trust](deploy/README-BACKUP-TRUST.md)
- [Private-beta acceptance checklist](deploy/README-BETA-CHECKLIST.md)
- [Current human/operator tasks](HUMAN-TODO.md)
- [Passkeys and device verification](PASSKEYS.md)
- [Revenue readiness](REVENUE_READINESS.md)
- [Current development handoff](FULLWORTH_CONTEXT.md) and [roadmap](FULLWORTH_ROADMAP.md)

Never run `docker compose down --volumes` on production, deploy a feature branch, bypass release checks, or move production secrets into a local or public issue/command transcript.

---

FullWorth is being built incrementally. **A smaller product that reliably reports what its evidence supports is preferable to a larger product that guesses.**
