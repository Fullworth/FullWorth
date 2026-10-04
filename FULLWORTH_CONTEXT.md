## Repository-side security closure checkpoint — 2026-10-03

The October 3 repository-side security program has advanced to **61 completed checks and 4 open checks** on issue #291. The remaining items are real-environment/provider/governance gates rather than missing generic application code.

Completed exact-head slices in this closure sequence:

- PR #653 required explicit production PostgreSQL credentials and `SSL Mode=VerifyFull` for any future non-local PostgreSQL host while preserving the current internal-only Docker database path. Exact head `655c9b3e6e5d6225d66a846bec2ce6b3d314a8ca` passed FullWorth CI #1592 and Dependency Security #689 before squash merge as `fd8520b722a4f8c61596340cf8398e236905f044`.
- PR #657 moved protected production values into least-scoped file-backed mounts, fixed PostgreSQL passfile lifetime through backup/restore, and proved Redis denies unauthenticated access while accepting the mounted secret. Exact corrected head `98f4317c0bc0b7f6cba702f21d143e0151d491ae` passed FullWorth CI #1600 and Dependency Security #697 before squash merge as `04a7c37ff36f97d1a5569b6bd5e06daa832f1ed9`.
- PR #658 added the host/operator hardening runbook and fail-closed verifier for the dedicated deployment account, effective SSH policy, Docker-group restriction, unattended security updates, patch review cadence, and pending-reboot handling. Exact head `c94b6243811f131598626f7c71c4300866371e0d` passed FullWorth CI #1601 and Dependency Security #698 before squash merge as `cf94b9542bb8cd928023ecc0e6d96b04ba1a0abd`.
- PR #659 separated steady-state PostgreSQL runtime authority from migration/bootstrap authority. The API now uses dedicated non-owner `fullworth_runtime` credentials with bounded DML/sequence rights; production startup migrations are disabled; schema changes run through one-shot owner-credential containers and grants are re-verified before candidate startup. Exact head `cfc5deaefb398b1af2737440c2c26444312debce` passed FullWorth CI #1602 and Dependency Security #699 before squash merge as `ac428de0a1e91079c93ad465d0aaa2b139cf31d4`.
- PR #660 added a model-wide fail-closed regression requiring every database relationship between two `UserId`-scoped entities to carry the dependent and principal `UserId` at the same composite-key position. Exact head `8dae783fe663a0cfe6642dc2acb8013c9524b62e` passed FullWorth CI #1603 and Dependency Security #700 before squash merge as `0689383e38ec5045bb7df19f493b2cff7e98d882`.
- PR #661 added an executable route-inventory ratchet requiring every non-admin GUID-addressable API resource route to map to explicit two-user negative evidence. Exact head `98d9b053117e5b2cb68f19f155d214544dc782c8` passed FullWorth CI #1604 and Dependency Security #701 before squash merge as `5dcebcc24b4ab4531473fae06fe0e3ca439a0457`.
- PR #662 completed the persistence-side ownership sweep: subscription status and recent payday-plan history now have explicit two-user isolation coverage; subscription deletion proves another user's entitlement/redemption/program-membership graph survives; and every EF entity with `UserId` must map to executable negative-isolation evidence. Exact refreshed head `31ac6051c187f1aa6f1a228193075a52b8b23b17` passed FullWorth CI #1606 and Dependency Security #703 before squash merge as `c283463eba028570d9b1629a5740d2dcf4c64e85`.

The four open #291 items are:

1. immutable/off-host storage proof;
2. compromised-host/clean-host recovery exercise;
3. branch-governance decision for independent approving reviews;
4. direct same-release deployed evidence before production-security claims.

Branch-governance evidence currently shows active repository ruleset `FullWorth protected branches` on both `master` and `development`. It requires pull requests, blocks deletion and non-fast-forward updates, requires review-thread resolution, has no bypass actors, and reports `current_user_can_bypass: never`. Its `required_approving_review_count` remains 0. The available GitHub connector can read but not mutate this ruleset, so no approval-policy change is claimed.

Current branch/release position:

- `master`: `bc9c73954e3443f98ea56f003eac28df49122034`, the current frozen release candidate.
- `development` contains the master promotion ancestry and PR #676's documentation-only release handoff. The current GitHub comparison with `master` differs only in `FULLWORTH_CONTEXT.md` and `HUMAN-TODO.md`; application and runtime files are unchanged.
- PR #670 fixed runner-side release-SHA validation. Its exact head `defe30292d9c84d54f3b873c443574e95d74fc34` passed FullWorth CI #1615 (`37167477045`) and Dependency Security #711 (`37167477082`) before merge as `e43c725bac00a9c0e30cbb6102ae87d7c61a68a7`.
- PR #673 added secret-safe repair for identical duplicate runtime database password entries. Its exact head `f0ad457932b7b27599c840647f542b78d170685e` passed FullWorth CI #1618 (`37172820886`) and Dependency Security #713 (`37172820948`) before merge as `4d0385ec80242bc2b8839a9887d7841df1e28448`.
- PR #674 promoted that repair to `master` as `bc9c73954e3443f98ea56f003eac28df49122034`. Exact master-push FullWorth CI #1620 (`37173901198`), Repository Governance #13 (`37173901252`), and the `Push on master` workflow (`37173901126`) passed. The attested production artifact is `fullworth-production-image-artifacts-bc9c73954e3443f98ea56f003eac28df49122034` (artifact ID `11292127567`).
- PR #675 synchronized master promotion ancestry into `development` as `6d8c4f1a930fd30c5d51c93bffbff793609a3699`. At that point the branches had no file differences.
- PR #676 documented the current candidate, corrected the stale branch snapshot, and recorded the remaining human release gates. Exact head `c81eaa997cffdf1e89a058b86b2254c95cb66fd7` passed FullWorth CI #1624 (`37176760392`) and Dependency Security #718 (`37176760371`) before merge to `development` as `a3f415024c0fcf16b9628218f55cc7d45b0386b5`.
- Older candidates `c092a9c76c5f4e811941400606c32d75a0a50a29` and `86d95c03c76792445913665ffdf353a45504133d` are superseded. Deploy run #10 stopped before production-host access; run #11 reached the host but correctly failed closed on duplicate protected `.env.production` runtime-password entries before starting candidate containers or advancing the release marker.
- Active `FullWorth protected branches` ruleset targets `master` and `development`, requires PRs/review-thread resolution, blocks deletion/non-fast-forward updates, and has no bypass actors. It currently requires zero approving reviews and no status checks.
- Verified live production remains `7e8571a26447538db249c862ad009487cce119bc`. Issue #669 tracks candidate `bc9c73954e3443f98ea56f003eac28df49122034`; it explicitly does not authorize deployment.
- Guarded deployment of the current candidate, same-release production acceptance, provider-enforced immutable/off-host storage, compromised-host clean-host recovery, independent-review policy, installed-device/provider/legal acceptance, and required-check ruleset configuration remain human/operator gates. See `HUMAN-TODO.md` and issue #669 for exact actions.

The candidate is repository/CI-ready for a guarded deployment decision; it is **not deployed or production-accepted**. Do not combine evidence from other releases or infer deployed security from CI.

## Secret non-disclosure release gate — 2026-10-03

FullWorth now fails a release candidate when any configured protected value appears in public health, validation-error, not-found, or retained production service-log output. The verifier covers required database, parser, Web-session Redis, Plaid, and Restic credentials plus optional Stripe, Google, Apple, Resend, and AWS secrets when configured. It scans exact values and common percent/form/Base64 representations without printing the value; failures identify configuration names only.

Linux production-container CI runs the verifier against deterministic sentinels after the real API, parser, Web, Redis, edge, and database stack is healthy. The guarded production deployment repeats the check against the protected host-local environment and cannot advance `.billwatch-release` after a disclosure. API and Web production exception-handler boundaries remain regression-locked, and the gate exercises generic application error output without creating persistent data. `SECRET_NON_DISCLOSURE.md` documents covered surfaces, representations, failure behavior, and scope.

CI exposed three integration defects before acceptance: a deployment test fixture initially omitted the new verifier, a shell heredoc failure wrapper consumed the failure command as Python, and the first CI dotenv writer collapsed protected values after the first line. The final fixture copies/stubs and failure-tests the deployment gate, the verifier uses an explicit fail-closed wrapper, and CI writes one protected entry per line while preserving file precedence.

Exact corrected PR #649 head `1735c7512416a3fbf284ba2087f7ffb175a56961` passed FullWorth CI #1582 (run `37146574448`: backend tests, MAUI Android, production images, live secret non-disclosure, parser containment, visual acceptance, HTTP security, encrypted backup, isolated restore, and API recovery) and Dependency Security #679 (run `37146574421`) before squash merge to `development` as `998cd63c654be7a0fb012544865173a95e61e7e7`.

This completes repository and CI verification that configured secret values do not appear on the covered application response and retained-log surfaces. It does not claim stronger secret injection than ordinary container environment variables, protection from privileged Docker/root inspection, deployed production acceptance, or absence of every possible derived representation. No production deployment occurred.

## Generated malicious/corrupt document corpus — 2026-10-03

FullWorth now exercises a deterministic, source-generated document-security corpus without retaining customer statements, uploaded files, screenshots, account data, or private evaluation material. The corpus contains 21 named PDF, PNG, and JPEG cases, each bounded to 4 KiB: malformed PDF catalogs/xrefs/stream lengths/ASCIIHex/Flate data; truncated PNG/JPEG structures; zero or excessive dimensions; invalid PNG bit-depth/color, compression, filter, interlace, and IHDR CRC fields; and malformed JPEG marker/frame sequences.

Production PNG admission now validates the complete fixed IHDR payload and CRC before calculating pixels or allowing native decode. Bit depth must be legal for the declared color type, compression/filter methods must be zero, interlace must be supported, dimensions must be non-zero, and the decoded working-set estimate must remain inside the existing ceiling. Corpus tests require every PDF to fail with FullWorth's fixed sanitized extraction message and every hostile image to fail before native decoding with a zero admitted pixel count. Separate invariants require unique descriptive names, deterministic bytes, supported media/extension pairs, and the per-case size bound. `DOCUMENT_SECURITY_CORPUS.md` documents coverage, data rules, residual risk, and the procedure for adding minimized synthetic regressions.

Exact PR #647 head `576a1719c3ed7ba0ffb1753d91785dd360144889` passed FullWorth CI #1574 (run `37131915446`: backend build and tests, MAUI Android, production images, live parser containment and OCR, HTTP security, encrypted backup, isolated restore, and API recovery) and Dependency Security #671 (run `37131915489`) before squash merge to `development` as `c8c1f175624b837882146162e4389f5a1342b168`.

This completes the malicious/corrupt document regression-corpus checklist item without storing user data. It does not claim detection of every malicious document or production containment of an unknown parser/native/kernel vulnerability. No production deployment occurred.

## Parser native-library and resource boundary review — 2026-10-03

FullWorth now has a documented and executable threat model for its untrusted statement parsing surface. The dedicated parser worker disables core dumps, caps open file descriptors at 512, retains its read-only root and private noexec/nosuid/nodev temporary store, publishes no host port, attaches only to the internal parser network, disables .NET diagnostics, and keeps finite container CPU, memory, swap, and PID ceilings. Its temporary four-capability bootstrap allowlist remains exact; the running UID 1654 supervisor has empty effective and bounding capability sets.

The production-container gate now inspects those live Docker settings in addition to observing per-document and per-image cgroups, requiring 384 MiB child memory ceilings, zero child swap, finite CPU quotas, a 48-PID document ceiling, cgroup cleanup, kernel OOM-kill evidence, and parser readiness after containment. `deploy/tests/parser-native-boundary-tests.sh` regression-locks the Dockerfile, Compose, live verifier, and `PARSER_NATIVE_ATTACK_SURFACE.md` review.

CI exposed two integration assumptions before acceptance. Docker's external init remains in the delegated parent cgroup and violates the cgroup-v2 no-internal-process rule needed for child controllers, so the corrected design deliberately keeps that parent empty and has the worker own/await native children. Docker inspect also reports added capabilities with a `CAP_` prefix; CI and deployment verification now normalize only that representation before enforcing the unchanged allowlist. Exact corrected head `899e42d7e20f2e15d47f3f1e8ceb396d0c914cf2` passed FullWorth CI #1572 (run `37129188455`: backend tests, MAUI Android, exact production images, live parser boundary and OOM containment, HTTP security, encrypted backup, isolated restore, and API recovery) and Dependency Security #669 (run `37129188469`) before squash merge to `development` as `c3f90a9a9dffd809fbd9c87664ea448aa4d1096e`.

This completes the repository review of parser/native-library confinement and current OCR/import process/resource ceilings. It does not prove an exact release is deployed, that the host kernel/container runtime is patched, or that an unknown native exploit is contained in production. Those require guarded deployed-host evidence. No production deployment occurred.

## Restored asset ownership and permission verification — 2026-10-03

FullWorth now treats statement storage and isolated PostgreSQL restore storage as permission-sensitive recovery assets alongside both Data Protection rings. API startup runs under `umask 077`, rejects linked or special statement entries, and repairs statement directories to `0700` plus files to `0600`. Backup capture and isolated extraction share a fail-closed policy that rejects wrong owners, links, special files, and non-private modes. The disposable PostgreSQL restore tmpfs is mounted directly as Alpine PostgreSQL UID/GID `70:70` with mode `0700`, its root ownership/mode is a readiness condition, and CI audits final database directories/files after restore.

Generated POSIX tests cover valid private trees and rejection of group-readable directories/files, symbolic links, FIFOs, and wrong owners. The production-container gate proves live statement permissions, encrypted capture, dual-ring extraction, statement reconciliation, database restore, and final PostgreSQL storage modes. `RECOVERY_PERMISSION_POLICY.md` documents the required owners/modes and the clean-host evidence boundary.

PR #643 exposed three useful CI integration defects before acceptance: direct repository backup tests could not find the container-installed policy path; Compose consumed the initial `PGDATA` reference before the health shell saw it; and the restore tmpfs initially arrived root-owned. Policy discovery now supports both the checked repository sibling and installed image path, the health probe uses the fixed isolated data path, and the tmpfs establishes PostgreSQL ownership at mount time. Exact corrected head `548ce2b262028a5c35eb3568425f3e7527f9efba` passed FullWorth CI #1564 (run `37116611780`: backend tests, MAUI Android, production images, live parser containment, statement permission checks, encrypted backup, isolated file/database restore, and API recovery) and Dependency Security #661 (run `37116611783`) before squash merge to `development` as `8bc8f2f7af6562907caebc17730075443686a474`.

This completes repository and CI verification that restored Data Protection keys, statements, and database storage retain their intended private permissions. It does not prove a clean production host restored the exact release or decrypted protected production values. No production deployment occurred.

## Data Protection key lifecycle hardening — 2026-10-03

FullWorth now treats both persisted ASP.NET Core Data Protection rings as explicit security-critical recovery assets. API and Web retain their separate legacy-compatible application discriminators, use an explicit 90-day rolling key lifetime, and start through a shared fail-closed entrypoint that applies `umask 077`, rejects relative/linked/nested/non-regular key-ring paths, and enforces mode `0700` on each ring directory plus `0600` on existing and newly generated files.

Encrypted production backup capture now quiesces the edge plus both API and Web key writers, includes both separated key rings, and records their counts in a versioned checksum-protected manifest. The restore verifier extracts both rings only into disposable isolated storage, rejects unsafe entries or permissions, reconciles restored counts with the manifest, and retains the existing database/statement/recovery checks. `DATA_PROTECTION_KEY_LIFECYCLE.md` documents routine rotation, historical-key retention, compromise response, and the remaining clean-host production proof.

PR #641 initially exposed two pre-container CI defects: changing the chaining around `SetApplicationName("BillWatch")` violated the literal compatibility guard even though the value was unchanged, and a string-replacement error duplicated the tail of the backup verifier. Both were corrected without changing the discriminators or weakening the lifecycle checks. Exact corrected head `ac201679ca04d6dc726168c7017e80cc175fad24` passed FullWorth CI #1551 (run `37111997150`: backend build/tests, MAUI Android, production images, live 0700/0600 key creation, encrypted dual-ring capture, isolated restore, parser containment, HTTP security, visual acceptance, and API recovery) and Dependency Security #648 (run `37111997119`) before squash merge to `development` as `2247b48e4cf8734fe27b3c6a79d8515683ae041c`.

This completes the repository review of Data Protection key permissions, backup treatment, routine rotation semantics, and isolated recovery verification. It does not prove an exact release has been deployed, that a clean production host can decrypt protected values, or that a compromised-ring drill has occurred. Those require guarded same-release production evidence. No production deployment occurred.

## HTTP framing and request media-type hardening — 2026-10-03

FullWorth now rejects unsupported or missing media types on body-bearing unsafe application requests before authentication/model binding/provider/proxy work. The API boundary applies to `/api`; the Web boundary applies to `/auth` and `/bff`. Accepted families are the formats FullWorth intentionally uses: `application/json`, `application/*+json`, `application/x-www-form-urlencoded`, and `multipart/form-data`. Endpoint-specific validation remains narrower where required, including Stripe's JSON-only webhook contract and statement signature/type checks.

The production HTTP verifier now exercises raw HTTP/1.1 framing through the real Caddy→Kestrel stack. A deliberately conflicting `Content-Length` paired with `Transfer-Encoding: chunked` must resolve to the complete chunked logout body and reach the API as HTTP 204 with the server-generated request ID. Conflicting duplicate `Content-Length` values must return HTTP 400 before the API boundary and therefore must not carry `X-FullWorth-Request-Id`. Existing Kestrel limits remain 8 KiB request line, 32 KiB aggregate headers, and 1 MiB ordinary request bodies, with the separately reviewed statement-upload limits unchanged.

Initial PR #639 head `da26be652693aef4f3a7cce4338ee25e35b3099e` failed FullWorth CI #1544 because two existing Stripe tests sent JSON bodies without an `application/json` media type and the pre-container HTTP verifier harness had not yet modeled the new raw-TLS probes. Those fixture defects were corrected without weakening the production boundary; a final harness matcher ordering defect was also fixed before exact-head acceptance. Exact corrected head `8544659e8d4286fce181933bffe9ff338fe5846a` passed FullWorth CI #1547 (run `37102074906`: backend build/tests, MAUI Android, production images and Compose, live Caddy/Kestrel HTTP framing verification, parser containment, visual acceptance, and encrypted recovery) and Dependency Security #644 (run `37102074911`) before squash merge to `development` as `04ddb2f8b1c34b9b7727acd48c8f2f0c7ae39d09`.

This completes the current content-type enforcement and HTTP request-framing/request-smuggling review item. It does not claim that the merged release is deployed or that an external production network path has been observed; guarded release and exact deployed-host evidence remain separate. No production deployment occurred.

## Stripe webhook replay and input security — 2026-10-02

FullWorth now persists a minimal receipt for each successfully handled Stripe event: the bounded provider event ID and completion timestamp only. The receipt primary key makes concurrent duplicate delivery database-enforced, while the ordinary lookup skips provider calls and entitlement reconciliation for later retries and process restarts. Receipt insertion and any paid-entitlement mutations share one EF `SaveChanges` transaction, so a failed handler does not leave a receipt that would suppress Stripe's retry. Customer-subscription events continue to reconcile current provider state rather than trusting delivery order.

The endpoint retains its five-minute timestamp tolerance, raw-body HMAC-SHA256 verification, constant-time comparison, 256 KiB declared/chunked body bound, and hidden response when billing is unconfigured. It now also requires `application/json` and a bounded Stripe-shaped event ID. Completed receipts older than 32 days are pruned in batches of at most 500. `STRIPE_WEBHOOK_SECURITY.md` documents the authentication, replay, retention, and production-evidence boundaries without treating repository tests as live-provider proof.

Initial PR #637 head `0b3da6609f9684ac554139caa944095d8a08b2ec` failed FullWorth CI #1537 because the new tests used synthetic event IDs containing disallowed underscores after `evt_`, and the custom chunked-body fixtures omitted their JSON content type. Those fixture defects were corrected without weakening the production validator. Exact corrected head `cf04ded9891a0c622db7d12bd1fa60d5deeafb23` passed FullWorth CI #1539 (run `37095790133`: 1,193 backend tests, EF pending-model verification, MAUI Android, production images and Compose, parser containment, HTTP security boundaries, and encrypted recovery) and Dependency Security #636 (run `37095790132`) before squash merge to `development` as `03f7ea6422bbc90a64c7bf8b3430f2982c6e939b`.

This completes the repository webhook-authentication/replay-resistance checklist item. It does not prove live Stripe delivery, production clock health, edge source filtering, or deployed migration state; those require a guarded release and provider-observed evidence from that exact deployed release. No production deployment occurred.

## Ownership-scoped resource-miss security alerts — 2026-10-02

FullWorth now emits metadata-only security event 29004, `ownership_scoped_resource_not_found`, when an authenticated request receives 404 on one of fourteen fixed ownership-scoped API method/route templates. The signal preserves the existing non-enumerating 404 boundary and does not perform a second cross-owner existence lookup. It contains only the normalized method, application-owned route template, status, authentication state, and server-generated request ID; resource/user identifiers, raw URLs, IP addresses, credentials, provider data, statements, and financial values remain excluded.

Twenty matching events in five minutes produce bounded alert 29105, `repeated_ownership_scoped_resource_misses`, through the existing fifteen-minute cooldown and hard 512-bucket cap. Because a 404 can also mean a genuinely missing resource, this is explicitly a scanning or stale-client probe signal rather than proof that another user's resource exists. Operator documentation requires separate evidence before describing it as a confirmed cross-user attempt and prohibits adding user/resource identifiers to the aggregation key.

Exact documentation-complete PR #634 head `aad518d082d49dc0819843f75b6a8d374e399d0b` passed FullWorth CI #1535 (run `37091664148`: backend build/tests, MAUI Android, production images and Compose, parser containment, HTTP security boundaries, and encrypted recovery) and Dependency Security #632 (run `37091664144`) before squash merge to `development` as `8d7887ab26ddac87a164739c8ddcdc35a288af36`.

This completes the current security-detection checklist across repeated authentication/authorization/rate-limit failures, ownership-scoped misses, administrative mutations, provider-attention transitions, and suspicious export/delete activity. The telemetry remains process-local under the current one-API-instance production rule. No production deployment occurred.

## Financial operation abuse boundaries — 2026-10-02

FullWorth now applies authenticated-user-partitioned named rate limits to the expensive financial operations identified by the executable endpoint inventory. The `financial-refresh` policy permits six requests per ten minutes for bill discovery, monitoring refresh, and Plaid account/transaction synchronization. The `financial-provider` policy permits twenty requests per ten minutes for Plaid Link creation, update, completion, and public-token exchange so normal interactive polling/retries remain viable without inheriting the broad API-wide budget.

Matching policies run at exposed Web/BFF entry points before antiforgery body processing and proxy work, while the API policies independently protect direct clients. Rejections return 429 with a positive `Retry-After`. Integration tests exhaust every API and Web policy, prove a second authenticated user retains an independent budget, and the endpoint inventory now locks fourteen financial routes to the named policies. The reviewed inventory contains 44 named-policy method-route exposures in total.

The initial PR head exposed a test-authentication mistake: Web's test user header is not an API authentication mechanism, so the API isolation assertion shared the IP fallback partition and correctly received 429. The corrected test uses real registered bearer sessions for API partitions. Exact corrected PR #633 head `76a144863af04abf0eda1836c0856d1d493a61d1` passed FullWorth CI #1532 (run `37080787691`: 1,174 backend tests, MAUI Android, production images and Compose, parser containment, HTTP security boundaries, and encrypted recovery) and Dependency Security #629 (run `37080787714`) before squash merge to `development` as `823721e88481c0df9174e6e5f4be20dfd3be4f81`.

This completes the endpoint cost/sensitivity classification and rate-limit checklist item for the current 198-route inventory. Future routes remain fail-visible through the inventory snapshot and must receive an intentional classification. Content-type/request-smuggling review and webhook replay/idempotency review remain separate work. No production deployment occurred.

## Executable endpoint security inventory — 2026-10-02

FullWorth now has a runtime-derived, CI-enforced inventory of all 198 application-owned API and Web method-route exposures. The snapshot records each route template's authentication metadata and effective rate-limit class across 83 API exposures and 115 Web exposures. Any route addition, removal, HTTP-method change, authentication change, or endpoint rate-limit metadata change produces an exact test diff that requires intentional review.

The inventory distinguishes the API host's partitioned 300-per-minute global limiter from Web routes that have no Web-layer limiter, rather than conflating missing endpoint metadata with a global control. Named authentication, statement upload/download, account export, subscription, and Web authentication policies remain visible per route. The accompanying `ENDPOINT_SECURITY_INVENTORY.md` documents counts, classification semantics, endpoint-family risk review, exclusions, and the required change procedure. Development-only OpenAPI exposure is explicitly identified, and static/framework infrastructure is excluded.

Exact PR #631 head `bffdc39194240701513a1f7167304436082a6ff2` passed FullWorth CI #1529 (run `37076261576`: backend build/tests, MAUI Android, and the production-container relevance gate) and Dependency Security #626 (run `37076261741`) before squash merge to `development` as `2d3550a197d4760fc5f0009e83f9b3603ba15b09`.

This completes the endpoint-inventory checklist item and establishes the review gate needed for broader endpoint cost/sensitivity classification. Cost-based limiter refinement, content-type/request-smuggling review, and webhook replay/idempotency review remain separate work. No production deployment occurred.

## Bounded high-risk security-event alerts — 2026-10-02

FullWorth now turns repeated high-risk authenticated events 29011–29014 into bounded `FullWorth.SecurityAlerts` records. Five-minute thresholds are five matching events for one allowlisted administrative action, ten application-wide account exports, three application-wide account deletions, and five provider-attention transitions for one allowlisted Plaid operation. Alert IDs 29111–29114 use a fifteen-minute per-key cooldown and report suppressed threshold crossings on the next emitted alert.

The detector admits only twelve fixed keys: eight administrative actions, application-wide export and deletion scopes, and the two provider operations `accounts_sync` and `transactions_sync`. Unknown dimension values fail before state allocation. Alert payloads exclude source request IDs, actor/user/account identifiers, IP addresses, raw paths, provider identifiers and errors, token material, statements, and financial values. The operator guide records routing and initial threshold-tuning guidance, while retaining the existing process-local and single-API-instance boundary.

Exact PR #629 head `211de20920023e9505fd9a418f9ef0559212bf27` passed FullWorth CI #1523 (run `37071364822`: backend build/tests, MAUI Android, production images and Compose, parser containment, HTTP security boundaries, and encrypted recovery) and Dependency Security #620 (run `37071364764`) before squash merge to `development` as `e252b98b6a5d7cbc922d93c303058df5990a1581`.

This completes bounded alerting for the implemented request-boundary, administrative, account-export/deletion, and provider-attention security events. Cross-instance aggregation remains future work if the one-API-instance production rule changes. No production deployment occurred.

## Security incident response and credential rotation runbook — 2026-10-02

FullWorth now has a production security-incident runbook covering SEV-1 through SEV-3 classification, the first fifteen minutes of containment, secret-safe evidence handling, affected-user and broad identity containment, ordered credential rotation, compromised-host recovery, and an explicit closure gate. It prohibits feature-branch deployment, direct Identity-table edits, credential exposure in tickets or shell arguments, destructive evidence cleanup, and production claims based only on CI.

The rotation matrix covers the production database, Web-session Redis, parser-worker authentication, Plaid, Stripe API/webhook, Resend, Google, Apple, operations-alert webhook, append-only backup storage, Restic keys, and the GitHub production SSH key. The session section is bound to the implemented strongly reauthenticated `sessions/revoke-all` path and `SecurityStamp` rotation: refresh access ends immediately, the current Web session signs out, and already-issued bearer access remains bounded by its existing fifteen-minute lifetime rather than being falsely described as instantly revoked.

A new POSIX regression contract verifies every required section, every protected production setting, every referenced operational path, the feature-branch prohibition, the deployed-evidence boundary, the current revocation endpoint/mechanism, and the production-operations guide link. The contract runs from the existing production operations suite.

Exact PR #627 head `95a4ce7ca4f8b2df30661ae02f3c6700ccec3cdb` passed FullWorth CI #1521 (run `37064373112`: backend build/tests, MAUI Android, Linux production images and Compose, parser containment, HTTP security boundaries, and encrypted recovery) and Dependency Security #618 (run `37064373180`) before squash merge to `development` as `a60f87f91fb58357e934fa52ccaddb6e81ac36ba`.

This completes repository documentation and executable regression enforcement for incident response, credential rotation, and session-revocation procedures. It does not prove that operators have exercised a production incident or credential rotation; direct deployed-host/provider evidence remains required during a real incident or scheduled drill. No production deployment occurred.

## Financial provider attention security event — 2026-10-02

FullWorth now emits metadata-only security event 29014, `financial_provider_attention_required`, when a Plaid account- or transaction-sync error is classified as requiring user action and the owned bank connection is successfully persisted from `Active` to `RequiresAttention`. The event contains only the fixed provider name `plaid` and one allowlisted operation name, `accounts_sync` or `transactions_sync`.

The event deliberately excludes Plaid error code/type, user IDs, bank-connection/item/account identifiers, institution data, provider request IDs, access tokens, request/response bodies, and financial values. Provider/API errors that do not require user action leave the connection active and do not emit this security event. A concurrent/no-op path that no longer transitions an active connection likewise emits nothing.

Exact corrected PR #625 head `1da0811ec5208f8493b5846613c3a5e9bc06525f` passed FullWorth CI #1519 (run `37049123705`: backend build/tests, MAUI Android, production images and Compose, parser containment, HTTP security boundaries, and encrypted recovery) and Dependency Security #616 (run `37049123724`) before squash merge to `development` as `09ce3dbc539bc31835e890bed267fe8593f5ffcd`.

This closes the first financial-provider failure telemetry slice. PR #629 subsequently added bounded alerting for provider-attention events, and PR #627 added the incident-response/credential-rotation runbook. Broader ownership-violation detection and other provider-failure classes remain separate follow-up work. No production deployment occurred.

## High-risk authenticated action security events — 2026-10-02

FullWorth now emits dedicated metadata-only `FullWorth.SecurityEvents` records when sensitive authenticated actions actually complete, closing the next observability gap after generic 401/403/429 telemetry. Event 29011 records only a fixed allowlisted administrative action name after persisted staff-role, entitlement, program-membership, or subscription-access-key mutations. Event 29012 records successful strongly reauthenticated account export, and event 29013 records committed account deletion, including the accepted statement-cleanup-pending case.

These events deliberately exclude actor/target/resource IDs, emails, financial values, request bodies, credentials, provider tokens, statement content, and other user-controlled text. Account export/delete records include only a validated server-generated 128-bit lowercase-hex request ID; invalid values are replaced with `<unavailable>`. Staff-role mutation events are emitted only after the surrounding relational transaction commits, so a failed commit cannot produce a false “completed” security event. Focused tests lock the dedicated log category, fixed event IDs, admin-action allowlist, and fail-closed request-ID sanitization.

Exact PR #623 head `bce1ff54fbc91f04b63b63e07b4c2e407dedcfeb` passed FullWorth CI #1515 (run `37046644058`: backend build/tests, MAUI gating, production image builds and Compose, parser containment, HTTP security boundaries, and encrypted recovery) and Dependency Security #612 (run `37046644044`) before squash merge to `development` as `91267421173ee7819a98a728b533fd0e66d97f28`.

This completes telemetry for successful admin mutations and account export/deletion. PR #629 subsequently added bounded alerting for those actions, PR #625 added the first provider-attention event, and PR #627 added the incident-response/credential-rotation runbook. Broader ownership-violation detection remains separate follow-up work. No production deployment occurred.

## Bounded repeated-security-event aggregation — 2026-10-02

The API now turns repeated request-boundary 401, 403, and 429 events into a separate `FullWorth.SecurityAlerts` stream instead of requiring operators to alert on every ordinary authentication failure. Fixed five-minute thresholds are 25 matching authentication rejections, 10 matching authorization denials, and 5 matching rate-limit rejections. Each safe aggregation key can emit at most once every 15 minutes; threshold crossings during the cooldown are suppressed and reported on the next emitted alert.

Aggregation keys contain only the fixed event name, normalized method, application-owned route template, and authentication state. Alerts omit request IDs, users, IP addresses, raw URLs, credentials, statements, and financial values. Process memory is hard-bounded to 512 buckets including a reserved overflow bucket; inactive buckets expire after 20 minutes, and excess dimensions produce a fixed capacity alert instead of allocating unbounded state. The operator document records alert IDs 29101–29104, routing, capacity handling, and the current process-local/single-API-instance boundary.

Exact PR #620 head `85330b2e757a85ffc67f6760f9244c71a0163ee2` passed FullWorth CI #1511 (run `37038951999`: backend build/tests, MAUI Android, production images and Compose, parser containment, HTTP security boundaries, and encrypted recovery) and Dependency Security #608 (run `37038951995`) before squash merge to `development` as `3e6419cf00542b3b875aeb855623f61553279310`.

This completes bounded repeated-event alerting for the request-boundary telemetry introduced by PR #618. PRs #623, #625, and #629 subsequently added high-risk action/provider events and their bounded alerts, and PR #627 added the incident-response runbook. Broader ownership-violation detection remains separate follow-up work. No production deployment occurred.

## Structured request-boundary security events — 2026-10-02

The API now emits authentication rejection, authorization denial, and rate-limit enforcement to the dedicated `FullWorth.SecurityEvents` category with fixed event IDs 29001–29003. The middleware surrounds rate limiting, authorization, and endpoint execution so downstream short circuits are observable. Events contain only a normalized fixed-set HTTP method, application-owned route template, response status, authentication state, and the existing server-generated request ID.

Raw paths, query strings, bodies, email addresses, user IDs, provider IDs, IP addresses, claims, cookies, authorization headers, tokens, statement content, financial values, and custom HTTP method text are excluded. Focused tests prove the event mapping, ordinary-response silence, route-template use, sensitive-input exclusion, and custom-method normalization.

Exact corrected PR #618 head `54c6e2abddb26880cc71629fc34d66e4e1303930` passed FullWorth CI #1509 (run `37032350735`: 1,148 backend tests, MAUI Android, production images and Compose, parser containment, visual/security boundaries, and encrypted recovery) and Dependency Security #606 (run `37032350638`) before squash merge to `development` as `5221c3ee8a81b81d6f8c699e5159ed1f41a7a5b8`.

This establishes a separate secret-safe security-event stream. Repeated-event aggregation, bounded alert thresholds, broader ownership/provider/export/delete detection, and incident-response runbooks remain separate follow-up work. No production deployment occurred.

## Provenance-preserving guarded release image deployment — 2026-10-02

The guarded GitHub production workflow now resolves the successful FullWorth CI run for the exact approved `master` SHA, downloads the release-scoped API, parser-worker, and Web image artifact, verifies both build-provenance and SPDX SBOM attestations for every archive, and creates a checksum manifest before transfer over pinned SSH. The production host verifies that manifest, loads those application images without rebuilding them, validates the release labels, and requires each running application container to use the exact loaded image ID before the verified release marker can advance. The operations-only backup image remains host-built.

Exact PR #616 head `8c167b11f0ba35895bd7d892649437c4a4950ea0` passed FullWorth CI #1505 (run `37024933210`: backend tests, MAUI Android, production-container deployment regressions, parser containment, visual/security boundaries, and encrypted recovery) and Dependency Security #602 (run `37024933482`) before squash merge to `development` as `bcd198c52ab2f19ed01757f1e437fcf201e80fa2`.

This closes the repository path from GitHub-attested application image archives to guarded runtime image-identity verification. It does not establish production acceptance because no deployment occurred. Same-release deployed-host containment and provenance evidence remain required after promotion to `master`. A direct host build remains a recovery option but is explicitly not provenance-preserving.

## Per-image OCR cgroup containment — 2026-10-02

Every admitted standalone or PDF-extracted OCR image now runs in a fresh sibling cgroup-v2 leaf beneath the delegated parser-container parent. The one-shot image process is moved from its per-document cgroup into the image scope before encoded bytes are released or native Tesseract/Leptonica state is initialized. Linux execution fails closed when the scope cannot be created, constrained, entered, or verified. Each image scope enforces a finite 1-CPU quota, a 384 MiB hard memory ceiling, zero swap, and OOM-group behavior; configured image limits cannot exceed the enclosing document limits. Safe failure codes distinguish pre-decode admission rejection from containment setup failure without returning parser diagnostics or document content.

The production-container gate sends two real admitted PNG requests, concurrently observes their live document and image scopes, verifies exact limits and process membership, proves unique names and cleanup, and then completes the existing kernel OOM, parser-survival, HTTPS, visual, security-boundary, and encrypted recovery checks. The initial red runs exposed a sequential observer race and then a malformed synthetic PNG whose Python byte literals contained escaped backslashes. The final binary fixture and bounded CI-only pre-input observation window make the kernel proof deterministic; production defaults to zero observation delay.

Exact PR #611 head `470d65e195cd3a4dfc01a4f79ee6fa81af16ebe1` passed FullWorth CI #1503 (run `36996242801`: backend tests, MAUI Android, Linux production images/Compose, live document/image cgroups, visual acceptance, HTTP security boundaries, and encrypted backup/restore) and Dependency Security #600 (run `36996242700`) before squash merge to `development` as `cfa9de2dde23f9818e4429f781776fab7064e9d3`.

This closes repository and CI evidence for OCR process isolation, per-document cgroups, and hard OS-enforced per-image memory limits. It does not establish deployed-host containment. The guarded production deployment and same-release `deploy/verify-parser-containment.sh` evidence remain required after promotion to `master`. No production deployment occurred.

## Release image SBOM attestations — 2026-10-02

Master production-container builds now generate SPDX JSON SBOMs from the exact API, parser-worker, and Web images, bind each SBOM to its matching exported image archive with GitHub attestations, retain the SBOMs beside the seven-day image artifacts, and preserve the separate signed build-provenance attestations. The SBOM generator, Syft version, attestation action, and artifact action are immutable-pinned and regression-checked.

Exact PR #613 head `ef0a16778b1beadf7723a247b5705e1cc06e730e` passed FullWorth CI #1477 (run `36983809331`) and Dependency Security #574 (run `36983809299`) before squash merge to `development` as `8d330e9fc635b456a2d13b7e180a01e59740a8b7`.

These attestations prove what GitHub CI built and bound to each archive. They do not prove that the VPS deployed those exact bytes; consuming verified GitHub-built artifacts and checking deployed-image identity remain follow-up release work. No production deployment occurred.

## Signed GitHub build provenance — 2026-10-02

GitHub Actions now emits verifiable build provenance for the three production image archives built by the Linux production-container job on pushes to `master`. The container job alone receives the minimal `id-token: write` and `attestations: write` permissions; it exports the API, parser-worker, and Web images tagged with the exact Git commit, attests the resulting tar archives through the immutable commit-pinned `actions/attest-build-provenance` action, and retains those attested artifacts for seven days. A regression contract locks the permissions, master-only gate, exact action pin, subjects, image tags, and retention policy.

Exact PR #609 head `3f736a1d75140e6d1fadaa3d897cf9b46b26167e` passed FullWorth CI #1466 (run `36977359332`) and Dependency Security #563 (run `36977359405`) before squash merge to `development` as `33aec7a027304d30edfcbc153d59c5a52e4195a9`.

This provenance covers the image archives built by GitHub CI. PR #613 subsequently added SPDX SBOM generation and signed SBOM attestations for those exact archives. The guarded production deployment still rebuilds images on the VPS, so no claim is made that deployed images are derived from or covered by these attestations. A deploy path that consumes verified GitHub-built artifacts remains separate supply-chain work. No production deployment occurred.

## Immutable production and recovery container inputs — 2026-10-02

Every external container image used by FullWorth's production and isolated-recovery Dockerfiles and Compose topologies is now pinned to an immutable multi-architecture SHA-256 manifest digest while retaining a readable upstream version tag. This covers the .NET SDK/runtime bases for API, parser worker, and Web; the PostgreSQL backup base; PostgreSQL production and restore services; Redis; and Caddy. Repository-built `billwatch-*` images remain bound to the exact 40-character `BILLWATCH_RELEASE_ID` and are rebuilt from the guarded release checkout.

A repository-wide POSIX shell contract rejects future mutable external `FROM`, Dockerfile frontend, and production/recovery Compose image references. The contract runs inside the Linux production-container gate, and `SUPPLY_CHAIN_SECURITY.md` records the review/update policy. Dependabot continues monitoring the Docker and Compose inputs on `development`; updates must advance reviewed digests rather than removing them.

Exact PR #608 head `4bac7d005fa16e42f01d0b02e9b4376f92a42558` passed FullWorth CI #1465 (run `36976631341`: backend build/tests, MAUI Android, production image builds, production Compose readiness, parser isolation and per-document cgroup containment, visual acceptance, HTTP security boundaries, and encrypted backup/restore) and Dependency Security #562 (run `36976631448`). It was squash-merged to `development` as `e42115887a80ffef917b4b8ea3b6ed1af01eeb6e`.

This closes mutable external container inputs for production and recovery. Signed build provenance and SBOM publication for FullWorth-built release images remain separate supply-chain work. No production deployment occurred.

## Immutable GitHub Actions supply chain — 2026-10-02

Every external action used by FullWorth's GitHub workflows is now pinned to an immutable 40-character commit SHA. The production deployment checkout was pinned first in PR #604; PR #605 then pinned the Android/iOS packaging, production auth smoke, external readiness, repository-governance, and artifact transfer actions without changing their behavior. A repository-wide shell contract rejects future mutable external action tags and unpinned Docker action images while permitting repository-local actions. Every workflow-file change now enters the production-container validation path, so editing a previously unlisted workflow cannot bypass that contract.

The first PR #605 exact-head run correctly exposed Android and iOS packaging contracts that still asserted mutable artifact tags. Those tests were corrected to require the exact reviewed artifact-action commits. Corrected head `31e370bb3957dbbd32cf085d57d1764cb3b1a5e4` passed FullWorth CI #1452 (run `36971569424`), Dependency Security #549 (run `36971569415`), Android Internal APK #63 (run `36971569617`, including signed package and emulator launch), and iOS Internal Simulator #57 (run `36971569427`, including simulator build/install/launch). PR #605 was squash-merged to `development` as `3a9d8a5617a39f0d0b1560fbbd65dbbae1892937`.

PR #604 head `2be7933d815bddcba252fca862efd826acf6c7d3` separately passed FullWorth CI #1449 (run `36968503452`) and Dependency Security #546 (run `36968503734`) before squash merge as `a57b52b3a04e0a18dbd3e7f62b1156693e1e55cc`.

This completes immutable pinning and regression enforcement for GitHub Actions references. Container base/runtime image digest pinning and signed build provenance remain separate supply-chain milestones. No production deployment occurred.

## Per-document parser cgroup containment — 2026-10-01

Every admitted PDF or OCR document now runs in a unique cgroup-v2 child scope before its bytes are released to the native parser. Each child is constrained to 1 CPU, 384 MiB memory, zero swap, and 48 PIDs, while the parent parser container remains capped at 1 CPU, 512 MiB memory, and 64 PIDs. The trusted bootstrap performs only cgroup subtree delegation, then executes the HTTP supervisor as UID/GID 1654 with cleared supplementary groups and an empty inheritable, ambient, effective, and bounding capability set.

Production-container validation proves the running supervisor identity and capability boundary, two real OCR requests receive distinct finite cgroups, completed document cgroups are removed, and unmanaged page pressure produces a kernel cgroup-v2 `oom_kill` event without killing the supervisor. The guarded production deployment now runs `deploy/verify-parser-containment.sh` and refuses to advance the verified release marker unless live OCR placement, cleanup, hard memory enforcement, readiness survival, and sanitized recent logs all pass.

Exact PR #601 head `de1f652c78c2f0364fb5d74e1da15acce15d4093` passed FullWorth CI #1446 (run `36953420697`: backend build/tests, production Compose security/deployment regressions, live parser cgroup delegation, hard cgroup OOM enforcement, isolated PDF/OCR paths, per-document uniqueness and cleanup, production-container readiness/security boundaries, and encrypted backup/restore) and Dependency Security #543 (run `36953420631`). It was squash-merged to `development` as `99feea1211d178b98139b56e5a15023243ed54be`.

This completes repository- and CI-proven OCR process isolation, per-document cgroups, and hard OS-enforced native-image memory containment. It does not claim deployed-host acceptance. Direct production evidence remains a human/operator gate after this exact change reaches `master`; no production deployment occurred.

## Standalone OCR image pre-decode admission — 2026-10-01

Standalone PNG and JPEG statement uploads now pass a bounded, allocation-light header inspection before any native Tesseract/Leptonica decode. PNG admission requires the exact signature and canonical 13-byte IHDR, while JPEG admission safely walks length-delimited marker segments to a supported start-of-frame marker. Malformed, truncated, unsupported, zero-dimension, and over-limit inputs fail closed. Accepted dimensions reuse the PDF OCR memory admission model: encoded bytes plus a conservative eight decoded bytes per pixel must remain within 256 MiB.

Focused tests cover bounded PNG/JPEG acceptance, an oversized 50-million-pixel PNG, malformed and unsupported inputs, zero dimensions, missing JPEG SOF, and truncated JPEG segments; existing memory-estimate tests retain exact-limit, over-limit, invalid, and 20-million-pixel coverage. The boundary and its limitations are documented in `PARSER_WORKER_BOUNDARY.md`.

The initial PR build exposed missing media-type plumbing and was corrected on the same branch. Exact corrected PR #598 head `f3fa8d70d43e12983b53e0f8eaa005a5c39c1b5a` passed FullWorth CI #1397 (run `36903617911`: backend build/tests, migrations, transaction regression, MAUI Android, production images and Compose health, isolated parser path, HTTP security boundaries, and encrypted backup/restore) and Dependency Security #494 (run `36903618117`). It was squash-merged to `development` as `978f4729bc145f350c5467411517df709ac7c7e5`.

This is defense-in-depth admission control, not hard native-memory containment. OCR still executes inside the API process; separate OCR isolation, a distinct per-document cgroup, a hard OS-enforced per-image peak-memory limit, and deployed-host containment evidence remain open. No production deployment occurred.

## Pinned parser-worker transport encryption — 2026-10-01

Production API-to-parser-worker traffic now requires HTTPS. The worker creates a fresh 3072-bit RSA certificate at startup, retains the private key only in memory, and atomically publishes only the public PEM certificate through a dedicated volume. The API mounts that volume read-only, requires an HTTPS worker endpoint outside development, and accepts a certificate only when its current DER bytes match the published pin through a fixed-time SHA-256 comparison. Reloading the public pin on each new TLS handshake permits certificate rotation after a worker restart without requiring an API restart.

Exact corrected PR #596 head `578b7df2acd416ffd6c9998d7e883f6b372dbbf6` passed FullWorth CI #1394 (run `36874493467`: 1,115 backend tests, migration and transaction checks, MAUI Android, production images/Compose health, pinned HTTPS extraction, replay rejection, body-substitution rejection, plaintext HTTP rejection, visual acceptance, security boundaries, and encrypted backup/restore) and Dependency Security #491 (run `36874493170`). It was squash-merged to `development` as `40239ac793cb4ee32ee812a97e2fa3a0156e8e1c`.

The first exact-head run correctly exposed use of a PEM loader that requires private-key material; the corrected implementation uses the certificate-only loader, preserving the design rule that the worker private key is never written or shared. This closes transport confidentiality for the production PDF text-layer worker channel. OCR isolation, a distinct per-document cgroup, a hard peak-memory bound for one permitted image, and deployed-host containment evidence remain open. No production deployment occurred.

## Parser-worker request-body integrity — 2026-10-01

The API now computes a bounded SHA-256 digest of every PDF sent to the isolated parser worker and includes that digest in the nonce- and timestamp-bound HMAC canonical request. Seekable streams are hashed in a bounded pre-pass and restored to their original position; non-seekable streams are staged in an owned, bounded 15 MiB memory buffer. The worker reads the already bounded body, compares its actual digest with the signed digest using a fixed-time comparison, and rejects a mismatch before launching the parser child process.

Exact PR #594 head `57e356ceea2ac3f587d2fb4ba246180c43255815` passed FullWorth CI #1388 (run `36866395277`: 1,111 backend tests, migration and transaction checks, MAUI Android, production images/Compose health, valid digest-bound PDF extraction, replay rejection, body-substitution rejection, visual acceptance, security boundaries, and encrypted backup/restore) and Dependency Security #485 (run `36866395299`). It was squash-merged to `development` as `2abfa8c11fca0f66323495014cfd3439f8db27ad`.

This closes exact-byte message integrity for the PDF text-layer worker request. Transport encryption, OCR isolation, a distinct per-document cgroup, a hard peak-memory bound for one permitted image, and deployed-host containment evidence remain open. No production deployment occurred.

## Parser-worker per-request replay protection — 2026-10-01

The API now signs every parser extraction request with HMAC-SHA-256 over the HTTP method, request path, Unix timestamp, and a fresh 128-bit random nonce while retaining the dedicated bearer credential. The worker rejects malformed, incorrectly signed, more-than-60-seconds stale or future, and previously consumed nonces before content-type validation or PDF body reads. Its process-local replay window is bounded at 4,096 entries and prunes expired entries.

The first exact-head CI run exposed two canonicalization defects: the API signer read `AbsolutePath` from a relative request URI, and independent test/probe strings encoded literal backslash-n text instead of newline separators. The corrected implementation preserves the relative path safely and uses identical newline-delimited canonical bytes across API, worker, tests, and the production probe; no authentication boundary was relaxed.

Exact corrected PR #592 head `7fd0ffe81ef7ec11b688ff0be986cf473c02b992` passed FullWorth CI #1386 (run `36860264539`: 1,110 backend tests, migration and transaction checks, MAUI Android, production images/Compose health, valid signed PDF extraction, duplicate signed-request rejection, visual acceptance, security boundaries, and encrypted backup/restore) and Dependency Security #483 (run `36860264674`). It was squash-merged to `development` as `93c3dd8e509f788740b321d0eae072c44e97dfe6`.

This closes per-request replay protection for a running worker instance. The replay cache resets on worker restart, but the 60-second timestamp window continues to bound accepted requests. The signed metadata does not encrypt the internal channel or bind the PDF body; transport confidentiality and full message integrity remain open. OCR isolation, distinct per-document cgroups, hard peak memory for one permitted image, and deployed-host containment evidence also remain open. No production deployment occurred.

## Adversarial parser-worker PDF corpus — 2026-10-01

The isolated parser now independently rejects bodies without the PDF file signature before invoking PdfPig. A generated, user-data-free regression corpus covers non-PDF payloads, truncated object graphs, dangling trailer roots, oversized declared streams, invalid ASCIIHex and Flate data, and a valid high-compression expansion fixture. Rejections return stable codes without parser diagnostics or document content; the compressed-expansion fixture proves extracted output remains capped at 250,000 characters.

Exact PR #590 head `99eb783b99165ac73d3190b4a5116715269aa991` passed FullWorth CI #1383 (run `36847786316`: backend build/tests, MAUI Android, production images/Compose health, authenticated parser request path, visual acceptance, security boundaries, and encrypted backup/restore) and Dependency Security #480 (run `36847786269`). It was squash-merged to `development` as `e1339a4455dc723bbab58d9a0b558ceb29d2623a`.

This closes the generated malicious/corrupt PDF corpus gap for text-layer parsing. It does not isolate scanned-document OCR, establish a distinct per-document cgroup, provide replay protection or transport encryption, or prove containment on the deployed production host. No production deployment occurred.

## Authenticated parser-worker boundary — 2026-10-01

The API-to-parser-worker extract path now requires a dedicated bearer credential in addition to the internal-only Docker network. Production startup fails closed when the credential is missing or malformed; production environment validation requires at least 32 characters and rejects placeholder or reused database/Web-session credentials. The worker rejects missing, duplicate, malformed, and incorrect authorization before inspecting content type or reading the PDF body, using fixed-time comparison of SHA-256 token hashes. The API attaches the credential only to worker requests. CI exercises the authenticated production-container request path, unit tests cover exact/missing/wrong/duplicate credentials, and the container boundary test verifies both services receive the same strong credential.

Exact PR #588 head `ce41a92784c61eba989122f983bf059eae091ffb` passed FullWorth CI #1379 (run `36840910131`: backend build/tests, MAUI Android, production images/Compose health, unauthenticated and incorrect-credential rejection, authenticated PDF extraction, visual acceptance, security boundaries, and encrypted backup/restore) and Dependency Security #476 (run `36840910005`). It was squash-merged to `development` as `52fd77c2cb405c9dc161d9bf822a41f7c6815173`.

This is service authentication, not per-request replay protection or transport encryption. The worker network remains internal-only and attached only to the API and parser worker. No production deployment occurred.

## Parser worker diagnostic-memory hardening — 2026-10-01

PR #586 replaces the API process's unbounded parser-subprocess stderr string with a fixed 8 KiB discard buffer. A generated 32 MiB diagnostic-stream regression test verifies complete draining without materializing the stream as a result; cancellation remains connected to the parser deadline. `PARSER_WORKER_BOUNDARY.md` records this bound. This closes diagnostic-output accumulation in the API process; it does not add parser/worker transport authentication or move OCR out of the API.

Exact PR #586 head `17d532c5d569e7bca9f429fc67e210c81c2c8834` passed FullWorth CI #1375 (run `36834026029`; backend build/tests and MAUI Android passed, and the Linux production-container job completed with container-only steps skipped as not applicable) and Dependency Security #472 (run `36834025989`). It was squash-merged to `development` as `95c2d8daf029c3f70f0cffc2505a970bcb10bae2`. No production deployment occurred.

## Product strategy consolidation — 2026-09-30

FullWorth's durable product strategy is centered on **financial change intelligence**: detect meaningful financial changes, explain why they happened, quantify their impact, prioritize what deserves attention, and preserve the evidence behind the answer. The recurring-bill promise — **Know when your bills change — and why** — remains the acquisition/product wedge.

`FULLWORTH_PRODUCT_STRATEGY.md` defines the change-intelligence hierarchy, moat, change-first Financial Home direction, time-to-value and calm-state experience, monetization discipline, illustrative ARR scale math, growth strategy, business scorecard, stage gates, and guardrails. `FULLWORTH_ROADMAP.md` now links that strategy into its north star, Financial Home, revenue-readiness, and metrics sections.

This is planning documentation only. It does not enable subscription enforcement, production AI, money movement, new data collection, or production deployment. Release, security, and financial-correctness gates remain ahead of speculative feature expansion. The strategic sequence is: trustworthy monitoring → retention → paid recurring value → repeatable acquisition → scalable recurring revenue.

## Isolated PDF text parser worker — 2026-10-01

PR #585 adds a dedicated production parser-worker service for PDF text-layer extraction. API uploads are streamed with a 15 MiB cap; the worker accepts one request at a time, runs PdfPig in a fresh bounded subprocess with a 20-second deadline, enforces 100 pages and 250,000 extracted characters, and limits its output. The production Compose worker is on an internal-only network with a read-only filesystem, dedicated unprivileged UID, no Linux capabilities, no-new-privileges, 64 PID cap, and 1 CPU / 512 MiB memory and swap limits. Readiness fails closed unless finite cgroup v2 limits are visible. OCR remains in the API process and is explicitly outside this isolation milestone.

Exact PR #585 head `23b8184e9f8f1dac90b4c162a2c906d80fa0bd07` passed FullWorth CI #1372 (run `36831039032`: backend build/tests, MAUI Android, production images/Compose health, API-to-worker synthetic PDF request, visual acceptance, security boundaries, and encrypted backup/restore) and Dependency Security #469 (run `36831038995`). No production deployment occurred. API-to-worker authentication is not yet implemented and relies on internal network membership; OCR isolation, malicious-document corpus testing, and deployed-host containment verification remain follow-ups.

## PDF parser bound regression tests — 2026-09-29

PR #559 adds synthetic valid-PDF tests for the existing 100-page processing ceiling and 250,000-character extraction ceiling. A generated 101-page PDF is rejected with the page-limit error; a generated 260,000-character text page returns no more than 250,000 extracted characters. No user financial documents are used.

Exact PR #559 head `fcd81e16266e8aa65b42e72205e812122e986312` passed FullWorth CI #1306 (run `36668488125`; backend/tests, model migration check, transaction regression, Linux production-container encrypted-backup/restore, and applicable jobs passed) and dependency security #406 (run `36668488148`). It was squash-merged into `development` as `03ae1be661801006ab707a805cb288edc2ff8311`.

This verifies the existing page/text limits. Separate-process CPU/RAM containment, peak native memory for one image, parser sandboxing, and malicious/corrupt/decompression-bomb fixtures remain open in security issue #291. No production deployment occurred.

## Cumulative PDF OCR work bound — 2026-09-29

PR #557 adds a 500-million cumulative declared-image-pixel budget per PDF OCR operation. The budget is checked before image decoding and native OCR, and processing stops at the limit while retaining any already recognized text. Boundary tests cover exact limit, overflow, non-positive input, and overflow-safe accounting.

Exact PR #557 head `83177bfb3e0c36a1dc038a32b5438064e416f4a3` passed FullWorth CI #1303 (run `36649099657`; backend/tests and isolated production-container encrypted-backup/restore verification passed; Android build job skipped as not applicable) and dependency security #403 (run `36649099693`). It was squash-merged into `development` as `3117d6d93ca274d0fea7a3f765837f9aea441b27`.

This reduces cumulative OCR work but does not impose a hard CPU or memory timeout, sandbox PdfPig/Tesseract, cap peak native memory for one allowed 50-million-pixel image, or add a malicious/corrupt-document corpus. Those security issue #291 items remain open. No production deployment occurred.

## Web authentication request rate limits — 2026-09-29

PR #555 applies an IP-partitioned 20-request-per-minute policy to Web login, registration, password recovery/reset, and external two-factor/registration-completion POSTs before body binding. The policy matches the API authentication limiter and ignores client-claimed account IDs. Regression coverage exercises all six routes and confirms request 21 receives HTTP 429 even when the test client changes its claimed user ID.

Exact corrected PR #555 head `8b9d6cb27407685b727f52e7f8433d2d45a8512a` passed FullWorth CI #1301 (run `36646736845`, including backend/tests, visual acceptance, and isolated backup/recovery) and dependency security #401 (run `36646736922`). It was squash-merged into `development` as `9f9b9132dfc47ad063d9a0f27edfb560ddf4f82a`.

Together with PR #553, Web/BFF statement uploads are rate-limited per user before multipart parsing at 12 per 10 minutes, matching the API policy. PR #553 exact head `a10e351e745b1b709992bb71aa2b0707969af717` passed CI #1298 (run `36645416390`) and dependency security #398 (run `36645416440`) before merge as `122859d100ef4d8a9c7b7144b052ee7c907c7973`.

These changes close two pre-processing rate-limit gaps; the broader anonymous/authenticated endpoint cost and rate-limit inventory remains open in security issue #291. No production deployment occurred.

## Web/BFF statement-upload rate limit — 2026-09-29

PR #553 closes the body-buffering gap before the API's existing statement-upload limiter. The Web/BFF route now applies a fixed-window limit of 12 uploads per authenticated user every 10 minutes before form parsing, matching the API policy. Rejected requests return HTTP 429 and include Retry-After when provided by the limiter. A Web integration test proves the thirteenth request is rejected and a second user receives an independent quota.

Exact PR #553 head `a10e351e745b1b709992bb71aa2b0707969af717` passed FullWorth CI #1298 (run `36645416390`, including backend/tests, visual acceptance, and isolated backup/recovery) and dependency security #398 (run `36645416440`). It was squash-merged into `development` as `122859d100ef4d8a9c7b7144b052ee7c907c7973`. No production deployment occurred. The broader anonymous/authenticated endpoint cost and rate-limit inventory remains open in security issue #291.

## API/Web inbound request bounds — 2026-09-29

PR #548 applied route-level 16 MiB request and multipart limits to the Web/BFF statement upload, preserving the 15 MiB per-file limit. PR #550 set a 1 MiB Kestrel default request-body limit in both API and Web hosts, while statement upload endpoints retain their explicit 16 MiB overrides. PR #551 pinned Kestrel request-line limits at 8 KiB and aggregate header limits at 32 KiB. Together these bound ordinary JSON/form bodies and HTTP request metadata; tests verify API/Web Kestrel settings and BFF upload route metadata.

Exact heads passed all required checks before merge:
- PR #548 head `eb2626aa5dab2f2b50de1a641162a5d87cf05f5d`: FullWorth CI #1293 (run `36642741581`) and dependency security #393 (run `36642741583`); merged as `1a95e4dc2c590a1660b39ee3ccda4f5b3e9fc0cc`.
- PR #550 head `07e5d74bcd30ea88111cf47b5f78654ff838c7eb`: FullWorth CI #1295 (run `36643714939`) and dependency security #395 (run `36643714919`); merged as `6aec7e897f0a4519a10fb9530df20dbe9cb2b0f1`.
- PR #551 head `3f6e5f01d8d6a7b7ba9cb85f53a7d5405e2f7731`: FullWorth CI #1296 (run `36644294595`) and dependency security #396 (run `36644294660`); merged as `8ed9cbea6e89b92ae1f3a8904b2199a442220ecc`.

The corresponding request-size bounds item is complete in security issue #291. Broader anonymous/authenticated endpoint cost and rate-limit inventory remains open. No production deployment occurred.

## BFF statement upload body bound — 2026-09-29

PR #548 adds `RequestSizeLimit` and matching `RequestFormLimits` metadata of 16 MiB to the authenticated Web/BFF statement-upload endpoint. This enforces the total request limit at the server before form parsing, including requests without `Content-Length`, while preserving the existing 15 MiB individual-file limit and API-side request/form limits. A regression test verifies both route metadata values.

Exact PR #548 head `eb2626aa5dab2f2b50de1a641162a5d87cf05f5d` passed FullWorth CI #1293 (run `36642741581`, including backend/tests, visual acceptance, and isolated backup/recovery) and dependency security #393 (run `36642741583`). It was squash-merged into `development` as `1a95e4dc2c590a1660b39ee3ccda4f5b3e9fc0cc`. No production deployment occurred. The broader API/BFF endpoint input-bounds inventory remains open in security issue #291.

## Web Content Security Policy checkpoint — 2026-09-29

PR #544 established a response-scoped script nonce, nonce-bound Blazor import map, and same-origin policy. Follow-up PR #546 removed every audited inline-style dependency: experience preference rules now live in the static theme stylesheet; provider-logo styles use CSS classes; theme scripts no longer write CSSStyleDeclaration properties; and the Plaid about:blank popup no longer creates styled elements. The CSP now uses `style-src 'self'` with no `'unsafe-inline'`. Regression coverage verifies this strict style source and continues to verify a fresh per-response script nonce bound to Blazor's import map.

Exact PR #546 head `a5271523078a525f06d414fec1c8e1e61317605a` passed FullWorth CI #1291 (run `36640645905`, including backend/tests, visual acceptance, and isolated backup/recovery) and dependency security #391 (run `36640645824`). It was squash-merged into `development` as `2ef2deaaba5b70f78615eb8e0f6bb100d29da289`. No production deployment occurred; the verified live release remains `7e8571a26447538db249c862ad009487cce119bc`.

The next release gates remain real Plaid sandbox acceptance using posted payroll transactions and the authorized local-Qwen/private-corpus benchmark. Production AI and AI-derived persistence remain disabled pending that evidence.

## Planning payday-history release promotion — 2026-09-29

Release PR #540 promoted the verified payday-history detail slice and current handoff from `development` to `master` at `f00e930cdbab4167f38796debd2e983f54cd152e`. Exact release head `77fd150e052df5623dde1029ad58815da27b3958` passed FullWorth CI #1275 (run `36528961096`), including backend/tests, Android, and Linux production-container validation, plus dependency security #376 (run `36528961112`). After merge, `development` was fast-forwarded to the same verified master commit. There are no open PRs at this checkpoint.

This is a repository promotion only. No production deployment occurred. Plaid sandbox acceptance using posted payroll transactions and the authorized local-Qwen/private-corpus benchmark remain separate evidence gates. Production AI remains disabled pending benchmark results.

## Planning payday-history detail checkpoint — 2026-09-29

PR #538 merged into `development` as `5cc0cee2eec15996fb35369b4c83c004e8083c2e`. Exact head `9071e5269320308a62a1f1120518efb6bf79464f` passed FullWorth CI #1273 (run `36504317342`) and dependency security #374 (run `36504317403`). Recent immutable payday history now includes owner-scoped per-bill recommendations, safe provider display names, due dates, amounts, and currencies. A paycheck-level shortfall is shown separately from those recommendations; the UI states that no money is moved or reserved. Tests exclude allocations owned by another user even when payroll transaction IDs match.

Together with PR #531, the authenticated Planning Web slice now covers schedule settings, default/per-bill planning horizons, Change Watch, payday history, and shortfall detail. The Web UX checklist in issue #293 can be considered complete. Real Plaid sandbox acceptance with posted payroll transactions remains an external pre-production gate; no production deployment is implied.

# FullWorth Current Context

Last updated: 2026-09-28

## Planning Web first-slice checkpoint — 2026-09-28

PR #531 merged into `development` as `bbac208651766796d9064622d6bb46db5e52e427`. Exact head `b8830d02ffb3835e7f2ab44f7c0121545a699089` passed FullWorth CI #1263 (run `36469189607`) and dependency security #365 (run `36469189643`). The authenticated `/app/planning` experience covers pay-schedule configuration, default and per-bill planning horizons, and evidence-backed Upcoming Bill Change Watch presentation. It uses antiforgery-protected authenticated BFF writes and adds anonymous-access regression coverage. No money is moved or reserved.

The first CI attempt exposed a test-fixture identity mismatch: the fake Web auth handler generated a new user ID for each request, invalidating the antiforgery token. The test now pins the same identity across the token and mutation requests; no authentication or antiforgery boundary was relaxed. The corrected exact head passed the full gate, including production-container visual checks and isolated encrypted backup/recovery. A detailed per-bill payday shortfall review remains unfinished; Activity currently carries summary payday alerts. Plaid sandbox acceptance with real posted payroll transactions remains a separate pre-production gate. No deployment is implied.

## Repository consolidation / payday alert checkpoint — 2026-09-28

The Planning payday path has advanced from manual generation to automatic, recovery-safe user alerts. PR #515 added the Bills-owned `PaydayPlan` alert persistence boundary with an opaque source-event ID and owner-scoped idempotency. PR #518 then connected Planning to recent posted payroll facts in the monitoring cycle: the service scans a bounded seven-day posted-payroll window, generates or replays the immutable payday plan, emits one idempotent user-level alert per payroll transaction, stops cleanly when pay-schedule configuration is missing, skips unsupported payroll facts rather than inventing evidence, and isolates alert failure from a successful bank/bill refresh. No part of this flow claims that money was moved, reserved, protected, held, or insured. Issue #293 now marks automatic alerts from actual posted paycheck events complete.

PR #521 updated the existing Activity feed to present and localize `PaydayPlan` alerts. PR #531 then added the first dedicated authenticated Planning Web screen: users can configure pay frequency, anchor payday, semi-monthly timing, and default paychecks-ahead guidance; set or remove per-bill overrides; and review confirmed upcoming bill changes with per-period and annualized impact plus remaining-to-plan amounts. The screen links to Activity for generated payday guidance. The broader Planning Web UX checkpoint remains open because shortfalls are currently summarized in Activity rather than shown as a detailed per-bill payday review. Real-world Plaid sandbox acceptance with posted payroll transactions also remains open before production use of this feature.

Repository hygiene was completed with fail-closed branch cleanup. PRs #516/#517 hardened cleanup against overlapping-run deletion races and expanded cleanup to closed-PR source refs plus separately audited SHA-pinned stale refs while always preserving `master`, `development`, protected branches, and open-PR branches. PRs #525/#526 added the final three audited legacy refs to that SHA-pinned retirement set after current `development` was verified to contain the later statement-airlock, MFA/session-revocation, and refresh-token-replay semantics. The owner-triggered cleanup reduced the repository from roughly 100 branches to the two long-lived branches after this handoff branch was merged and retired. Closed PR history remains the review record for removed source branches.

PR #522 exempts repository-maintenance-only workflow edits from unnecessary MAUI builds while changes to `.github/workflows/ci.yml` itself continue to exercise the Android gate. PR #527 refreshed `coverlet.collector` from 10.0.1 to 10.1.0 and the production Redis image from 8.2.9-alpine to 8.10.0-alpine with its pinned digest on the exact current `development` base after full CI/security validation.

Current long-lived branch heads at this checkpoint are `development` = `908306a773f78b791c1b37148357c7fc0907ee65` and `master` = `47163a1490bf048e1a37768bb021e5e212c006f1` before this documentation-only merge. No production deployment is implied by these repository changes.

`HUMAN-TODO.md` still contains exactly the genuinely human-only release prerequisites currently identified: configure GitHub branch protection/rulesets for `master` and `development`, and obtain qualified commercial legal/license review before commercial launch. The connected GitHub tooling can read protection state but still exposes no mutation for creating those rulesets.

Next product checkpoint: complete the real Planning Web UX (settings, overrides, shortfall review, and Change Watch presentation), then perform Plaid sandbox acceptance with real posted payroll transactions. The local-Qwen/private-corpus benchmark also remains a separate release-readiness evidence gate; production AI remains disabled until real evaluation evidence supports enabling it.

## Planning / security continuation checkpoint — 2026-09-27

The Planning payday stack is now integrated through immutable replay-safe plan persistence. PR #510 merged to `development` as `02c38f6c18237cf9f37745c61892c683b0d999c0` after exact head `66457ee0eed0ece7c8cf9229fc643aca6ad2ea8f` passed FullWorth CI run `36374917272` and dependency security run `36374917348`. Ready payday plans now persist the immutable paycheck-plan run and positive bill allocations through one Planning-owned save boundary; zero-allocation ready plans are replayable, and replay checks the frozen Planning snapshot before live payroll facts so later provider changes cannot rewrite an already-recorded recommendation. Planning still records guidance only and does not claim money was moved, reserved, protected, or held.

The preceding Planning chain is also merged: recommendation-ledger persistence (#501), replay-safe allocation storage (#502), bill-decrease handling (#503), owner-validated posted-paycheck orchestration (#504), authenticated payday-plan API (#505), confirmed statement-backed bill-change facts (#506), deterministic Change Watch (#507), Change Watch API (#508), and immutable paycheck-plan-run persistence (#509). Issue #293 now records payday generation, Change Watch, and ownership/security ratchets as completed. The next functional checkpoint is automatic payday alert generation from actual posted wage transactions, followed by Web UX and Plaid sandbox acceptance.

External-identity disclosure hardening was refreshed as PR #512 and merged after exact-head CI/security passed. Repository branch-protection detection is refreshed in PR #513; it is automation only and does not substitute for configuring protection. `master` and `development` still require human-admin branch protection and remain tracked in `HUMAN-TODO.md`.

The prior proprietary-license draft PR #368 was deliberately closed rather than merged. Qualified legal review is now tracked in `HUMAN-TODO.md`; do not recreate or merge license language until that review is complete.


## CI simulator reliability / human handoff checkpoint — 2026-09-27

PR #459 hardens the GitHub-hosted iOS simulator smoke after repeated runner failures occurred after successful iOS compilation. The smoke still requires the real FullWorth simulator app to install, launch, and expose its app container. Simulator boot now gets one bounded recovery attempt, installation gets 180 seconds instead of 90 seconds plus one bounded reboot/retry, and shutdown/cleanup calls are bounded so a wedged CoreSimulator process cannot hang the job indefinitely. The gate still fails closed after those retries.

PR #459 also introduces `HUMAN-TODO.md` as the durable list for genuinely human-only actions. Its first item records the currently verified absence of branch protection on `master` and `development`; the connected GitHub tooling can detect that state but does not expose the repository-administration mutation needed to create the ruleset.

PR #459 merged to `development` as `cde1816562941b1fabe2ccc048ee6aa8757fa1ec` after exact head `ceb441dff4c98eb501446dd4d48c069d72357222` passed FullWorth CI run `36352791146`, iOS simulator run `36352791181`, and dependency security run `36352791203`. PR #466's boot-command bound was superseded by PR #476, which bounds the `simctl boot` command itself. PR #476 merged to `development` as `7c30e636bddcae230f5d6dbdf77e238509fd1167`; exact head `b8dafb71c3ea872897a3ef9e370398e0abac4d72` passed FullWorth CI run `36357649564`, iOS simulator run `36357649775`, and dependency security run `36357649587`. The Planning stack remains separate: PR #442 is stale/diverged from current `development`; PR #444 is stacked on that older Planning base and must be refreshed after the persistence layer is rebased/recreated on current `development`.



## Active AI evaluation checkpoint — 2026-09-27

### Current local-AI recognition checkpoint — 2026-09-27

The selected path remains evaluation of externally trained local models, not training a FullWorth model from scratch. There is no OpenAI/cloud statement-extraction fallback in the API. PR #405 removed that provider and its selection/configuration code; PR #404 explicitly sets local inference, shadow mode, and shadow provider calls to false in production. These changes merged to development as `af193d1761c3531ea11518c9bb65f9fb2bb01b66` (after #404 merge `4761931fda8df9c3e9ae21679857cb52bd60a08a`). Exact PR heads passed FullWorth CI and dependency security before merge.

Prompt evaluation now compares preserved v1 and v2 on the same sorted case population. PR #402 added fixed field-level accuracy metrics. PR #403 made the comparison field-aware: each of TotalAmount, BillingPeriodStart, BillingPeriodEnd, StatementDate, DueDate, CurrencyCode, and LineItems must have a matching expected-fact population, reconcile with aggregate counts, and show no regression in correct/incorrect/missed counts, precision, or recall before v2 qualifies for promotion review. An overall improvement cannot conceal a loss in one field. This is review evidence only; no automatic prompt promotion or production activation is possible.

PR #403 exact head `95e2979252de68d1599784ec63c084b32a5ca984` passed FullWorth CI run `36287918706` and dependency security run `36287918783`, then merged to development as `7222be0fd61c67760393cbaed380c0ed53a27538`. The CI run also skipped the MAUI workload for these non-mobile AI changes. PR #406 introduced the safe-path MAUI skip rule and merged as `e9acaaed4497e83c17e9acd5aa3027400a065e05`; MAUI source and project changes still require the Android build. PR #404 exact head `89d6fcd9c5f1864b5a9a6893c715b4ec75aaea75` and PR #405 exact head `c99170193af80a99ce1caacbe3dcb2a9bc5983cb` each passed their exact-head FullWorth CI and dependency security checks.

PR #408 tightened the offline candidate evidence validator after identifying substring-only citation matches. String and date values now require whole-value boundaries in their cited excerpts; complete date expressions are excluded from numeric money evidence; a short account suffix may still be cited from the end of a longer account identifier. Focused regression tests cover rejected provider/date/amount fragments and legitimate excerpt/suffix cases. Exact PR head `4ffd44622be32cd5401a086b9e5a0caab8d03d5f` passed FullWorth CI #1095 (run `36289147073`) across backend tests and the production-container gate, with the MAUI workload correctly skipped for this AI-only change. Dependency security #201 (run `36289147058`) passed, then PR #408 merged into development as `7b4f6eba01f38dabb826cb09f73dc6d072440f85`. This is citation-validation hardening; no real-model accuracy improvement is claimed.

PR #412 then accepted month-first date evidence without a comma (for example, `Sep 20 2026` or `September 20 2026`) while preserving the whole-value boundary. Two regression cases verify the accepted forms. Exact head `2b680cd10b107b1db7c069366e374f85dc69b6d4` passed FullWorth CI #1098 (run `36289445528`) and dependency security #204 (run `36289445550`), then merged into development as `2465a44e248b597f8634d5ae26c5143efb5ed26d`. No real-model accuracy measurement is implied.

PRs #415 and #416 added typed, vendor-neutral local-inference failure classification and aggregate-only failure counts for offline evaluation. PR #418 rejects an over-limit statement instead of silently truncating it before inference. Both keep sensitive response details and statement text out of aggregate diagnostics.

PR #422 added a stricter prompt-review gate at each anonymous provider × fixed-field intersection. It requires the same expected-fact population within every cell, reconciles the cell counts with both provider and field totals, and vetoes correct/incorrect/missed, precision or recall regressions even if both margins improve. Aggregate-only comparison remains diagnostic and cannot qualify a prompt. Exact head `5e48966633b429722c8e62104967338e1269a68a` passed FullWorth CI run `36291600822` and dependency security run `36291600841`, then merged as `3b8f8905f9496644de0f0ca64c36a3e4cac47a0d`.

PR #423 removed model-supplied fact keys from validation error messages, using evidence item numbers instead. Exact head `2c950eac1981b332f7b3e3ec4e371830f456398b` passed FullWorth CI run `36291896256` and dependency security run `36291896124`, then merged as `e1414ba4108d6ab6ed97fc4e0e5bdc9d9466d9fa`. PR #426 then rejected unknown evidence fact keys and line-item keys outside the candidate's actual rows, preventing arbitrary evidence keys from passing into converted results. Its exact head `884c93af62739d329a66d9f04edd0ae1fe085705` passed FullWorth CI run `36292105928` and dependency security run `36292105939`, then merged as `8d6c8a311ac4094725b9cfbc4b3b9318118c478e`. The earlier stacked PR #424 was closed in favor of the clean development-based #426.

PR #427 made the offline chunk reconciler reject missing chunk/candidate/text records and zero-length chunks through its structured result instead of throwing. Exact head `523a9122fd667af0d62ebfc5cbb11a4df613a411` passed FullWorth CI run `36292183740` and dependency security run `36292183896`, then merged as `7528164680fc21aedbafa254b6e3efa8357feead`. This only hardens offline evaluation and does not register chunking for production extraction.

PR #431 aligned deterministic date evidence matching with lexical recognition for unambiguous year-first slash/dot dates, variable-width numeric components, and padded-day month-name dates including `Sept`. This accepts correctly cited complete dates without dropping whole-value boundaries. Exact head `7cfdf471791e7d57100daa5dcb7df37daeb903bc` passed FullWorth CI run `36292522540` and dependency security run `36292522531`, then merged as `68f579be7bf4a140c91028eb0821194941eee202`. Its older base PR #430 was closed in favor of the clean current-development PR. These deterministic tests do not measure real-model statement accuracy.

PR #452 connected the real `LocalAiBillStatementAiExtractor` to the existing aggregate inference-call counter so chunked offline evaluation measures actual local HTTP inference sends rather than assuming one model call per statement attempt. Exact head `29d6acdbeab02cd506a22e4e75b68652a66a1384` passed FullWorth CI run `36294855557` and dependency security run `36294855588`, then squash-merged as `e40243142d2ee06e8d3d384612f968be60957c94`. Pre-send rejections remain zero, failed HTTP responses after send initiation count as attempts, and no statement/provider/case detail is exposed.

PR #454 added aggregate chunk-efficiency diagnostics on top of that real metering: the number of statement attempts requiring more than one underlying inference call and the maximum inference-call count observed for one statement. Exact head `603aa6e2fc1e5486614ef0492bdd52c9a756b921` passed FullWorth CI run `36295058189` and dependency security run `36295058207`, then squash-merged as `61f58ed1be374d42f9db0e6cfc9ac9262a9f0094`.

PR #455 then added an aggregate-only count of statement attempts rejected by the chunked extraction/reconciliation path. This separates chunk-pipeline rejection from provider transport failure and ordinary accepted-candidate scoring without returning rejection text or private statement data. Coverage-rejected runs expose null rather than fabricated counts. Exact head `4171506195b3303ac025559e797b3237ee2fcfd2` passed FullWorth CI run `36295294991` and dependency security run `36295294987`, then squash-merged as `ac1f1f9ec908c7d50c9f2aa262d1486be36826b0`.

PR #449 aligned deterministic date evidence matching with the existing numeric-date lexer for month-first dotted four-digit-year forms such as `9.2.2026`, `09.02.2026`, and mixed-width equivalents. It merged into development as `a98eb7a4dd0178377a6d81a43e1a183a5c43ee2a`. This removes a deterministic false rejection; it is not evidence of real-model accuracy by itself.

PR #457 fixed paired prompt-comparison consistency. The offline runner now loads the selected cases through `BillStatementAiPrivateCorpusLoader.LoadSnapshotAsync`, which creates a bounded, loader-validated snapshot that callers cannot construct directly. Both v1 and v2 evaluate the same in-memory statement text and reviewer-approved labels; the evaluator requires explicit provider-call authorization before reading snapshot contents. A regression test changes the source files after snapshot creation and verifies scoring uses the loaded data. Exact head `fc89072f0bd0122a65ca577bd62943cc462b606b` passed FullWorth CI run `36296932600` (backend/tests, production containers, encrypted backup/recovery, and MAUI Android) and dependency security run `36296932608`, then squash-merged as `f78ef4d385b37eab60ab4c7c37aca3b30f4d64c0`. This improves comparison integrity but does not measure real-model accuracy.

PR #468 moved the local-runtime-key/authorization check ahead of private-corpus reads, so an unauthorized run cannot use the evaluator to inspect corpus contents. Exact head `8bc9053880df68b62b247cd36bb0cde0721642fb` passed FullWorth CI run `36355017015` and dependency security run `36355017018`, then merged. PR #469 fixed the remaining preflight-to-evaluation population race: catalog inspection now returns its validated, sorted case selection in memory, and the runner reuses it rather than re-enumerating case directories after preflight. A regression test verifies later catalog changes cannot alter the selected population. Exact head `b01272ded0b3739b7eeafdf7cef75a54dd022e3d` passed FullWorth CI run `36355684516` and dependency security run `36355684557`, then merged to `development` as `c3c541fe3bf7272d21a89a2463527451d7848345`.

PR #486 adds scored-document exact-match measurement across the currently labeled fields and line items, then requires the aggregate rate and each anonymous provider bucket's exact-match count to avoid regression before promotion review. The metric is explicitly limited to TotalAmount, BillingPeriodStart, BillingPeriodEnd, StatementDate, DueDate, CurrencyCode, and LineItems; it does not measure candidate fields the private ground-truth schema does not yet label. Exact head `6d8cbd9a4b0a8ca0da6657761866b0c6138beee1` passed FullWorth CI run `36359541894` and dependency security run `36359541906`, then squash-merged as `dd51aa62afa24744ff95cf8d7fe45e2d24875893`. This improves evaluation sensitivity but is not a real-model accuracy result.

No real Qwen accuracy benchmark has been run. The next meaningful recognition-quality step is still the guarded local evaluation on an authorized host with the pinned Qwen3-4B Q4_K_M artifact and the private held-out corpus. The runner requires at least 100 cases across five providers, with at least 10 cases for each provider. Keep statement text, ground truth, secrets, and case-level outputs outside GitHub and logs. The current extraction-only benchmark does not measure false alerts and cannot establish full shadow readiness. Production AI and AI-derived persistence remain disabled.


The product direction is local evaluation of externally trained models; from-scratch model training is no longer the active plan. The abandoned first-party tokenizer/decoder/initializer/checkpoint/training source and its dedicated tests have been removed from the active product tree. Do not reintroduce that path unless a separately justified R&D effort is approved.

PR #371 added the isolated local evaluation runtime and its fail-closed configuration validation. Exact head `9f1e76c6fb9d5b70781f4e9cbab802d1d39fe448` passed FullWorth CI #1038 and dependency security #144 before merge.

PR #372 pinned Qwen3-4B Q4_K_M at upstream revision `a9a60d009fa7ff9606305047c2bf77ac25dbec49`, 2,497,280,256 bytes, SHA-256 `7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5`, with Apache-2.0 recorded in the manifest. Exact head `431f835b91fcdc520ac655f6e749d94ec36aa131` passed FullWorth CI #1039 and dependency security #145 before merge as `6a99162d445eb56e86e52f4e3fa468a76dba04bb`. Model weights are not in Git. Neither merge enables production AI or AI-derived persistence.

PR #375 added the authenticated local runtime smoke and merged as `008b4b5eb97f5db9bcb2775b242af65d705ff029`. PR #377 strengthened it to require strict JSON-schema output. Exact head `d5876cda6e3a779614679fc671430064f3d548d5` passed FullWorth CI #1044 and dependency security #150, including the MAUI Android build, Linux production-container validation, and backend tests. PR #377 was squash-merged as `2fe14d8be16156dfa165c722e4cc1d7e5eedf1d6`. The smoke verifies the strict structured-output API contract and authentication boundary; CI uses a fake server and does not prove model quality.

PR #379 added the standalone `FullWorth.AiEvaluation` command for deterministic-baseline and explicitly authorized local-model evaluation. It validates the private corpus before inference, uses the pinned loopback runtime, routes candidates through deterministic evidence validation, and emits aggregate metrics only. It cannot persist results, enable runtime shadow mode, or influence production persistence.

PR #379 was squash-merged as `c29d4859d3397d5ea03792bb7067ec13267f19cf`. Exact head `825e0697d5485d3aceea641e6ee10da3bdebae36` passed FullWorth CI, dependency review, Linux production-container and isolated recovery validation, backend/tests and migration verification, MAUI Android build, Android internal APK build/emulator launch, and iOS simulator build. Model weights, private statements, and case-level outputs were not added to GitHub. No real Qwen benchmark has been run.

Next: on an authorized evaluation host, start the pinned local runtime and complete its authenticated structured-output smoke against Qwen3-4B Q4_K_M. Then run the deterministic baseline and local-model evaluator against an authorized held-out private corpus after reviewing ground truth and numerical thresholds. The local runner requires at least 100 cases across at least five providers, with at least 10 cases for each provider. Keep statement text, ground truth, secrets, and case-level output outside GitHub and logs. The extraction-only runner does not measure false alerts and cannot by itself establish full shadow readiness. Production AI and AI-derived persistence remain disabled.

## Guarded production deployment checkpoint — 2026-09-25

GitHub Actions production deploy run #7 (`36221860082`) completed successfully for exact `master` release `7e8571a26447538db249c862ad009487cce119bc`. This is the current verified live production release.

The guarded workflow verified the requested SHA against current `master`, used pinned SSH host verification, passed production configuration preflight, built release-tagged API/Web/backup images, created a verified encrypted pre-replacement recovery snapshot beginning `15a529e1844b...`, and completed the guarded deployment without bypassing the repository deployment script.

Post-deploy server-side verification passed release-integrity, production exposure, runtime, API/Web readiness, private-beta readiness, active backup-timer, retention-configuration, and backup/runtime alert-configuration checks. Independent GitHub-runner readiness probes also passed for `https://api.fullworth.org` and `https://fullworth.org`.

This deployment supersedes the prior live release `9d156f4c88d3929cc3873983a66e06938672c0c9`. That prior release had deployed successfully but its workflow run was marked failed because Docker network inspection emitted a blank line that the exposure verifier treated as an unexpected network. PR #354 corrected only that verifier normalization and its regression fixture; the production topology itself was already healthy.

This checkpoint does not prove the remaining real-environment beta gates such as controlled cross-user fixture evidence, Plaid provider observation/Hosted Link human completion, representative statement/OCR review, controlled reboot, provider-enforced backup immutability, or qualified legal review.

## Release preparation / email delivery checkpoint — 2026-09-25

PR #351 passed exact-head FullWorth CI #996 and dependency security #105 on `d021c32f7623528dccaa75cf1b22cfca27e46fbc`, then squash-merged into development as `9a1a867ce55ce9e28532d41da833f65105f6e050`. Anonymous registration, forgot-password and confirmation resend now keep the same public response during email-provider rejection, transport failure and timeout. Generic event 4101 records delivery failure without recipient/token/provider details. Thirteen regression cases cover provider failures, state preservation, sanitized exceptions and propagation boundaries. Delivery is not guaranteed by the public acknowledgement; no retry queue was added.

Release preparation combines that development head with master `d798e29ac916a9ab7b23b1f987c6cdbc09826cd1`. A three-way merge is conflict-free; master's `SECURITY.md` is retained unchanged. The promotion needs its own exact-head CI before merging to master. This checkpoint does not claim the promotion has passed, been merged or been deployed.

The broader enumeration audit remains open for external registration/linking and timing behavior. Existing private-beta real-environment acceptance gates remain open. Production deployment must use the guarded workflow against the final approved master SHA, followed by release/readiness verification.

## Direct registration/confirmation response checkpoint — 2026-09-25

PR #350 closes the direct API duplicate-registration disclosure. A narrowly scoped endpoint filter unwraps Identity's registration result and maps duplicate-only email/username errors to the same empty 200 response as successful registration. Other validation failures remain failures. Framework registration, legal acceptance, password validation, rate limiting, and existing account state remain intact.

Seven integration cases cover case-normalized duplicate registration with a different password, preservation of the existing password/account/security stamp, invalid-password error equivalence, malformed and invalid confirmation/change-email proofs for known versus unknown user IDs, and successful valid email confirmation.

Exact head `a3813ea57c9b9aa56633a9ea8fe16ac3f7a15571` passed FullWorth CI #995 and dependency security #104 before squash merge as `1b442a00b828efd3c1ca665f627aa4686b605441`. Backend build/tests, migration verification, transaction-stream regression, API/Web production-container build/readiness/HTTP security, encrypted backup, isolated restore, and API recovery ran successfully. MAUI execution and visual acceptance were skipped by existing change detection. Production was not deployed.

Issue #291's broader enumeration review remains open. Next inspect mail-provider failure responses and external registration/linking for existence disclosure. The current email sender throws on provider rejection; determine and test the public response behavior for known versus unknown accounts before selecting a bounded fix. Timing-side-channel resistance is not established. Do not reopen the response-equivalence cases already covered by PRs #349/#350 without new evidence.

## Email/recovery response checkpoint — 2026-09-25

PR #349 adds six integration cases for anonymous email/recovery responses. Confirmation resend and forgot-password now have response-equivalence coverage for both confirmed and unconfirmed existing accounts versus unknown email addresses. Invalid password-reset proof has error-equivalence coverage for both states, plus assertions that rejected reset attempts preserve the existing password. Users are arranged directly through Identity, without relying on registration/login response behavior.

Exact head `17440d34290914a97f945e20034c2126e14f80ed` passed FullWorth CI #994 and dependency security #103, then squash-merged into development as `769cdcc0d2734d27a4d5f4d093b60b02bf6bae88`. Backend build/tests, migration/model verification, and transaction-stream regression ran successfully. Existing change detection skipped the unrelated MAUI and production-container execution steps; this is not new production/container acceptance evidence.

Issue #291's broader email/recovery enumeration item remains open. These tests cover public status/content-type/payload equivalence, not timing side channels or mail-provider failure behavior. Continue by inspecting direct API registration and invalid confirmation-link responses; the existing Web registration test alone does not establish direct API enumeration resistance. Production remains unchanged.

## Web/BFF cookie fixation checkpoint — 2026-09-25

PR #337 completed the session-fixation slice of the Web/BFF cookie-hardening review. The audit confirmed that the primary Web authentication cookie is a `__Host-` cookie with `HttpOnly`, `Secure=Always`, `SameSite=Lax`, root path, and sliding expiration disabled; the antiforgery cookie is also `__Host-`, `HttpOnly`, `Secure=Always`, root-scoped, and `SameSite=Strict`. The production Web session store keeps only an opaque protected browser reference, uses a cryptographically random 256-bit server key, Data-Protection-protects the serialized authentication ticket, applies the ticket's absolute expiration to the distributed cache, removes expired/tampered tickets, and removes the stored ticket on normal sign-out.

A concrete fixation boundary remained because ASP.NET Core cookie authentication with a custom `SessionStore` can renew an existing server-side session identifier when application code signs in during an already-authenticated request. FullWorth now rejects password login, password registration, external login, and external registration when the Web request is already authenticated, preventing an existing session reference from being repurposed across an account/identity change while preserving intentional same-session token refresh. Focused regression tests prove those flows fail before creating an API client.

Exact PR #337 head `ad3ec5d5871353266f7b0b147457920435d2da51` passed dependency security #84 and full CI #972 before squash merge as `c2bd58c4559fd25277874325ba86a67eb7d5bbfe`. Production was not changed.

Continue the Web/BFF cookie-hardening review with lifetime and renewal alignment. In particular, verify the durable Web session upper bound against the actual refresh-token-family lifetime before changing code; do not assume the 30-day remember-me ticket is justified merely because it is already configured.

## Web/BFF cookie lifetime/renewal checkpoint — 2026-09-25

PR #339 completed the remaining lifetime/renewal proof for the Web/BFF cookie-hardening review without changing production behavior. The primary Web authentication cookie remains host-only `__Host-`, `HttpOnly`, `Secure=Always`, `SameSite=Lax`, root-scoped, bound to the protected distributed server-side ticket store, and non-sliding. Password/external sign-in and registration fixation was already closed by PR #337.

The Web authentication ticket has a fixed absolute expiry of 12 hours for ordinary sessions or 30 days when Remember me is selected. API access tokens remain 15 minutes and refresh tokens 14 days. Successful refresh rotation may issue another 14-day refresh credential, but BFF refresh reuses the existing `AuthenticationProperties` and therefore does not extend the Web ticket's fixed absolute upper bound. PR #339 adds regression proof that the original Web-session expiry survives a server-side token refresh unchanged at cookie timestamp precision.

The first PR #339 CI attempt failed only because the test compared sub-second `DateTimeOffset` precision even though authentication-cookie serialization normalizes expiry to whole seconds. The corrected exact head `df29de1540b3102a05472c01411d98eb814cf946` compares Unix-second precision, passed dependency security #87 and full CI #975, then squash-merged as `c8514750893204bdd585cb5feb3a766cdfefb127`.

Issue #291's cookie-prefix/Secure/HttpOnly/SameSite/lifetime/fixation/renewal item is complete through PRs #337 and #339. The next unchecked identity/security slice is the broader email/recovery account-enumeration review. Do not reopen the completed cookie slice without new evidence.

## MFA recovery/enumeration checkpoint — 2026-09-25

PR #335 completed the remaining MFA recovery/enumeration slice without changing production code. The audit confirmed that the ASP.NET Core Identity-backed password-login path redeems a recovery code once, decrements the remaining-code set, and rejects replay. The forgot-password endpoint returns the same public success for a known confirmed account and an unknown email, and invalid reset-password requests return the same public error fingerprint for a known confirmed account and an unknown email.

The first exact-head CI attempt exposed a test-fixture defect: it had set the 2FA flag without configuring an authenticator key, allowing password login to bypass the recovery-code path. The corrected test configures an authenticator-backed MFA state before login. Corrected exact head `02453b745eac4dfce8dbeb5f86d74902fde2cc60` passed dependency security #82 and full CI #970; PR #335 then squash-merged as `67317a10ec554890cfebc834ac49882cd2191633`.

Issue #291's MFA enrollment/disable/recovery-code replay/enumeration item is complete through PRs #327, #328/#331, #333, and #335. This does not claim globally single-use TOTP verification or a complete MFA concurrency proof. The next unchecked security slice is Web/BFF cookie hardening: review prefixes, Secure/HttpOnly/SameSite, lifetime, fixation, and renewal behavior before changing code.

## MFA setup/reset atomicity checkpoint — 2026-09-25

PR #333 completed the setup/reset atomicity slice. Initial `two-factor/setup` now rejects an already-enabled account with 409 so setup cannot silently disable an active factor; on an MFA-disabled account it uses the framework `ResetAuthenticatorKeyAsync` transition directly instead of performing a redundant false-to-false `SetTwoFactorEnabledAsync` write first.

Authenticator replacement now requires MFA to be enabled, stages `TwoFactorEnabled=false`, a newly generated authenticator key, and a new `SecurityStamp` through the configured Identity store, then persists the staged user/token state through one `UserManager.UpdateAsync` call. This removes the prior two-independent-write replacement sequence. Regression coverage verifies enabled-account setup preserves the existing MFA/key/stamp/recovery-code state, successful replacement stores a different key, disables MFA, rotates revocation state, and rejects a refresh token issued after MFA enrollment. A source-boundary test locks the single-persisted-transition shape.

Exact PR #333 head `07e524984395e1235390323302a3a3b846e0ab5e` passed dependency security #79 and full CI #967 across backend build/tests, migration verification, transaction-stream regression, MAUI gating, production API/Web containers, HTTPS/HTTP security, encrypted backup and isolated recovery before squash merge as `93e0d72b10b9b1b128a3dcd90b19ac6f834da13c`.

The remaining MFA review is recovery semantics and enumeration/error behavior. Preserve Identity's one-time recovery-code redemption behavior and PR #308 regeneration/session-revocation semantics; inspect current source and patch only confirmed gaps. Do not claim globally single-use TOTP verification or a complete MFA concurrency audit.

## MFA disable/session revocation checkpoint — 2026-09-25

PR #327 fixed replayed MFA enrollment: a repeated successful `two-factor/enable` request now returns 409 before mutation, preserving the recovery codes already issued and leaving the security stamp unchanged. Exact head `0e0359305122532af224e43c90fdb3fe8b93846c` passed dependency security #70 and full CI #958 before squash merge as `eb5cf04420f599694bbd96a00f9a1a0b3f14d459`.

PR #328 fixed the product-level MFA-disable gap: successful Web/BFF disable removes the current protected Web session and returns the browser to sign-in, while focused regression coverage proves refresh tokens issued after MFA enrollment are invalidated and invalid proof preserves MFA/session state. During concurrent follow-up work, PR #329 became a superseded test-only exploration and was closed unmerged. The authoritative framework check was performed directly against ASP.NET Core Identity v10.0.12 source: `UserManager.SetTwoFactorEnabledAsync` stages the 2FA flag, calls `UpdateSecurityStampInternal`, and persists both through the same user update. PR #331 therefore removed FullWorth's redundant explicit stamp write and now relies on that framework-owned MFA-disable + stamp transition while preserving the Web signout and regression tests. Existing remote bearer access remains bounded by the normal 15-minute bearer lifetime; FullWorth does not claim instant remote bearer invalidation.

PR #328 initially exposed one Web compile error because the Settings page did not inject `NavigationManager`; the missing injection was fixed without changing security semantics. Corrected exact head `dab33016099419ba7732c24ed1c85bfb72decc47` passed dependency security #73 and full CI #961 across backend/tests, migration verification, MAUI gating, production API/Web containers, visual acceptance, HTTP security, encrypted backup and isolated recovery before squash merge as `8be4e68967632b505a421af1f845209ff5fb3d86`. PR #329's corrected test-only exact head `e9541c17806ee981c2de3a08c5a85ef79ef9a7df` passed dependency security #75 and full CI #963 but was intentionally closed unmerged after overlapping #328. PR #331 exact head `af0426ef3e4702ff95ca9842ab0be88315348d47` passed dependency security #77 and full CI #965 before squash merge as `42209c96391db5675945768e508b605114548df1`; its rationale is grounded in the verified ASP.NET Core Identity v10.0.12 implementation rather than treating the concurrent #329 run as proof of the pre-#328 production state.

The MFA review remains open. Setup/reset failure atomicity is complete through PR #333. Continue with broader recovery semantics and enumeration/error behavior. Do not claim globally single-use TOTP verification or a complete MFA concurrency audit.

## MFA enrollment replay checkpoint — 2026-09-25

The enrollment audit reproduced a confirmed defect: replaying a successful `two-factor/enable` request silently replaced the recovery codes just issued and rotated revocation state again. The enable endpoint now rejects an already-enabled account with 409 after password verification and before further mutation. Intentional recovery-code replacement remains on the strongly reauthenticated regeneration endpoint. The Web explains how to obtain replacement codes, with Spanish localization.

Regression coverage checks that the original ten codes remain usable after a rejected replay, each is redeemable only once, the security stamp stays unchanged, and invalid enrollment proof neither enables MFA nor issues codes. This protects the completed enrollment transition; it does not claim globally single-use TOTP verification across operations or resolve every concurrent enrollment/reset transition.

Read the associated PR and exact-head CI for merge/validation evidence. Continue #291 with setup/reset failure atomicity, disabling behavior, and recovery/enumeration boundaries. No production deployment or complete MFA-audit claim is made.

## Architecture modularization checkpoint — 2026-09-24

FullWorth is a modular monolith with deny-by-default ownership boundaries and explicit contracts between modules.

Completed architecture checkpoints:
- #261 established project dependency airlocks and CI-enforced project graph rules.
- #263 established direct sibling service-module coupling enforcement.
- #264 introduced the provider-neutral bank synchronization airlock.
- #265–#279 incrementally moved Accounts, Bills, Plaid, and Statements cross-owner reads/writes behind owner contracts and drove the controller/service ownership ratchets to zero exceptions for the enforced domains.
- #281 removed the cross-domain database foreign key/navigation from Bills-owned alerts to Statements-owned bill changes while retaining opaque correlation metadata.
- #286 introduced the Bills-owned bank-transaction association bridge/backfill.
- #287 moved recurring-discovery/runtime behavior to the Bills-owned transaction association.
- #288 removed the legacy Plaid-owned `BankTransactions.BillStreamId` column/indexes/foreign key after the bridge and runtime cutover were proven.
- #289 extended service ownership enforcement to Subscriptions and routed Statements quarantine user-existence reads through Identity.
- #290 routed Admin user/role/subscription mutations through Identity/Subscriptions owner contracts and added Admin to the zero-exception service ownership ratchet.

Active architecture state:
- Issue #260 remains the modular-architecture tracker.
- The previously documented `BillAlerts -> BillChanges` and `BankTransactions.BillStreamId -> BillStreams` schema couplings are resolved by #281 and #286–#288; do not reopen them unless new evidence shows a regression.
- Continue ownership enforcement only module-by-module after inspecting the current source. Do not introduce broad allowances or perform a big-bang rewrite.

Architecture rules:
- Modules depend on contracts, not sibling implementations.
- Controllers and services must not access another module's private persistence directly where an owner contract exists.
- Shared scoped DbContext transactions are an explicit modular-monolith coordination mechanism. A future network split requires an outbox/coordinator or equivalent rather than silently losing atomicity.
- Sensitive provider credentials, statement storage identifiers, authentication material, and cross-user financial evidence must not leak through contracts, logs, export payloads, or exceptions.

Production remains unchanged by architecture/security merges unless a separate guarded production deployment is explicitly approved.

## Current migration/security checkpoint — 2026-09-24

This checkpoint supersedes older branch/domain summaries below. Current GitHub source and exact-head CI remain authoritative.

- Repository: `Fullworth/FullWorth`; release branch: `master`; integration branch: `development`.
- Latest verified code checkpoint on `development`: `1b442a00b828efd3c1ca665f627aa4686b605441` after PR #350 closed direct registration disclosure and verified confirmation responses. Later handoff-only commits may advance the branch.
- Current `master`: `a4d60bc25d680dfc3b786fb476e4e7f42f84eba1`. A GitHub branch head is not evidence of a production deployment.
- The last operator-reported live production release remains `81a74f11941f6ed67ba5de61b9ef186ef09bae3c`. No later architecture/security merge is being claimed as deployed.
- Production remains untouched unless a guarded deployment is separately and explicitly approved.

Security program:
- Issue #291 is the active ASVS-based defense-in-depth tracker. It is a hardening/verification program, not a claim of ASVS certification.
- PR #292 hardened the Web antiforgery cookie and added a commit-pinned pull-request dependency-review gate. GitHub CodeQL default setup is already enabled, and NuGet vulnerability warnings NU1901–NU1904 remain build-blocking.
- PR #297 split production networking into least-connectivity edge/API/Web/data/egress networks, made the data path internal-only, and proved read-only API/Web roots, bounded noexec/nosuid/nodev temporary storage, PID ceilings, HTTPS, statement handling, encrypted backup, isolated restore, and recovery.
- PR #298 confined the Caddy edge with a read-only root, `no-new-privileges`, all Linux capabilities dropped except `NET_BIND_SERVICE`, a 128-PID ceiling, and bounded noexec/nosuid/nodev temporary storage.
- PR #299 moved Web authentication tickets into a Data-Protection-protected distributed server-side store backed in production by an isolated, password-protected, non-persistent Redis service. The browser auth cookie now carries an opaque protected session reference rather than API access/refresh tokens.
- PR #337 prevents authenticated Web requests from switching to a different password/external identity or creating another account in-place, closing the confirmed session-fixation boundary while preserving intentional same-session token refresh. Exact head `ad3ec5d5871353266f7b0b147457920435d2da51` passed dependency security #84 and full CI #972 before squash merge as `c2bd58c4559fd25277874325ba86a67eb7d5bbfe`.
- PR #300 explicitly set Identity bearer access-token lifetime to 15 minutes and refresh-token lifetime to 14 days, and added regression coverage for password security-stamp rotation.
- PR #302 ends the current server-side Web session immediately after a successful password change, preserves the session on rejected password changes, and returns the browser to sign-in.
- PR #305 locks the existing external OIDC security invariants with regression coverage for authorization-code flow, PKCE, HTTPS metadata, issuer/audience validation, provider-token non-persistence, response modes, and hardened nonce/correlation cookies.
- PR #306 revokes refresh sessions when an existing account adds or removes an external sign-in method.
- PR #307 revokes refresh sessions atomically with staff role promotion/demotion.
- PR #308 revokes refresh sessions atomically with two-factor recovery-code regeneration.
- PR #309 adds bounded single-use refresh-token families, replay rejection, PostgreSQL-backed concurrency control, and distributed Web/BFF refresh-race recovery. Review after merge found that its advisory lock did not serialize against ordinary Identity security-stamp writes.
- PR #310 replaces that advisory lock with a transaction-scoped `AspNetUsers` row lock and revalidates the current security stamp after the row is locked, closing the refresh/security-change race. Exact head `c11658733ba29820237e6e8501d026741894146b` passed dependency security and full CI #934 before squash merge as `ac722b0f5f68dc3699374ccc183e8df8654f106f`.
- PR #312 revokes the current enrolled refresh family on first-party Web/MAUI logout while preserving independent sessions. Exact head `79e25b1f24189030b4b0d8c2b96c520ef66545d5` passed dependency security and full CI #937 before squash merge as `2109bb8d1950aeab3229cf79c9d959d4c23eb9fb`.
- PR #313 adds strongly reauthenticated account-wide refresh revocation using ASP.NET Core Identity `SecurityStamp` rotation. Existing refresh tokens across sessions fail immediately; already-issued bearer access is still bounded by the explicit 15-minute lifetime. Exact head `b2947dcc24404005a620d096138b3bd48bf0f3df` passed dependency security and full CI #938 before squash merge as `8a338197139a1ad63094eab589b590060aff92e4`.
- PR #314 exposes account-wide revocation through an antiforgery-protected Web/BFF `Sign out everywhere` control, signs out the current Redis-backed Web session after a successful revocation, discloses the bounded 15-minute remote access-token window, and adds Spanish localization plus antiforgery coverage. Exact head `4722c7f9bd421cb6c3c91659ba17057cb0ae6354` passed dependency security and full CI #939 before squash merge as `489568293aa0af3deea1682f9f7ce2f2f2362cf0`; the generated desktop/mobile Settings screenshots were visually inspected and the new card rendered cleanly.
- The current token/session lifecycle checkpoint is complete: refresh replay is single-use/bounded, first-party logout revokes the current refresh family, account-wide revocation rotates Identity `SecurityStamp`, and the Web sign-out-everywhere flow removes the current server-side Web session on success. Already-issued remote bearer access remains bounded by the explicit 15-minute access-token lifetime; FullWorth does not claim instant remote bearer-token invalidation.
- PR #316 closes the first confirmed strong-reauthentication gap: Admin/Owner subscription access-key creation now requires the actor's current password and, when 2FA is enabled, a current authenticator code before any privilege-granting key is issued. Defensive access-key revocation intentionally remains friction-light for incident response. Exact head `f35648e4da969ce071b44cf4ddf9272cab55f095` passed dependency security and full CI #941 across backend/tests, MAUI gating, production-container/browser/security/recovery before squash merge as `c699ea86a9f8c18d6e3fbcf476b36631efcdc006`.
- PR #318 fixes the admin Web client regression exposed by #316: the three known strong-reauthentication 401 ProblemDetails responses now remain on the current admin page and surface as credential errors, while ordinary/unrecognized 401 responses still redirect to sign-in. Exact head `60749c1cb758b6fc92baf9e63758a97b3ae3c2e7` passed dependency security and full CI #943 across backend/tests, MAUI gating, production-container/browser/security/recovery before squash merge as `0f102e70c32ba2ac032cd4b72d9a4b002c018364`.
- PR #320 requires the acting Admin/Owner's current password and, when enabled, current authenticator code before assigning Admin or Moderator roles. Rejected reauthentication fails before mutation; role-removal DELETEs deliberately retain the existing friction-light incident-response path. The Web/BFF carries the credentials only for role grants and surfaces known credential errors on-page. Exact corrected head `d5505c1948c57f2eb6ad17f52ca1cba89662e8ca` passed dependency security and full CI #946 across backend/tests, MAUI gating, production-container/browser/security/recovery before squash merge as `e2eb0cceaf1cb7fbacbaddd791f235da490ff364`.
- PR #322 extends that strong reauthentication to subscription entitlement grants and program-membership activation. Entitlement revocation and program deactivation stay friction-light for incident response, and the Web clears entered password/2FA values after every mutation attempt. Exact head `65604f56fa61a688b94c068ffc5152c83801cc38` passed dependency security and full CI #948 across backend/tests, MAUI gating, production-container/browser/security/recovery before squash merge as `8984145aded753af228a8a6783128ac22080a9bc`.
- PR #324 hardens sensitive account export and completes the current strong-reauthentication audit. The API and Web/BFF export surfaces are POST-only, the Web path is antiforgery-protected, fresh account verification is required before export construction, and the previous GET path is regression-blocked. Existing ownership, no-store, rate-limit, and forbidden-secret/storage checks remain covered. Export-bearing smoke harnesses reject recovery-only mode before network use and keep sensitive request material out of process arguments. Exact head `ba5f89c266b8f6b76a10ab977bb70f9c2de3d25a` passed dependency security and full CI #955 before squash merge as `3d8187b4f81a44f2248bbeda66914260a5d0a721`. Generated desktop/mobile privacy screenshots were inspected; the default layout remained clean and responsive, while the automated capture did not open the expanded export-confirmation form.
- External OIDC uses authorization-code flow, PKCE, HTTPS metadata, issuer/audience validation, provider-token non-persistence, explicit provider response modes, and hardened nonce/correlation cookies; PR #305 regression-locks these invariants.
- Database runtime/migration privilege separation, stronger production secret injection, Data Protection key-at-rest/rotation design, parser-worker isolation, host/SSH/firewall hardening, security-event detection, SBOM/provenance, and immutable/off-host recovery proof remain open security work.

Platform/acceptance:
- Android physical installed-PWA acceptance remains open under issue #251. CI, Chromium, and emulator evidence do not satisfy that gate.
- iOS Add to Home Screen failure remains tracked separately in issue #258.
- No real-device, provider, backup-immutability, legal-review, production, or human acceptance evidence may be fabricated or inferred from CI.

## FullWorth brand transition

- Product brand: **FullWorth**.
- Tagline/positioning: **Your entire financial life. One app.**
- The authoritative continuation file is `FULLWORTH_CONTEXT.md`. `BILLWATCH_CONTEXT.md` is retained only as a temporary compatibility pointer for older project instructions.
- Production compatibility identifiers such as `BILLWATCH_*` environment variables, `/opt/billwatch`, Data Protection application/purpose strings, persisted secure-storage keys, Stripe metadata keys, and current public domains remain unchanged until a dedicated guarded operations migration is completed.
- PR #113 promoted the recurring-discovery coverage fix to `master` as `1b2b52cd77f45143d4b741d77f1cb79f292f86c2`; that merge is **not** claimed as deployed production.
- PR #114 merged the approved FullWorth brand assets into `development` as `b51a49705d0cf062abba0ca9c3522f1d3c6994fe` after exact-head CI passed all three required jobs.
- The FullWorth product and repository identity migration is merged. Remaining `BILLWATCH_*` and `billwatch` infrastructure identifiers stay behind the guarded compatibility boundary described above.

## Authority / continuation rules

This is the durable FullWorth development handoff. Current source, exact-head CI, and verified production state win over this file for implementation/runtime truth.

- Stop immediately for compile/runtime/test/CI/deployment failures caused by current work, destructive migration risk, genuine security problems, or unresolved architecture uncertainty.
- Never weaken authentication, BFF isolation, antiforgery, HTTPS, ownership checks, trusted-proxy rules, token protection, statement protections, backup protections, migration safety, or financial-data boundaries to pass a check.
- Work in coherent slices. Do not merge a feature branch until its exact final head has passed the complete CI/container/recovery gate.
- Never deploy a feature branch directly to production. Use the guarded release path from a verified `master` commit.
- Prefer useful code or real acceptance work over repeated audits and synthetic acceptance artifacts.

## Product promise

**Your entire financial life. One app.**

FullWorth remains transaction-first while expanding into a broader financial-life hub. Bank transactions discover recurring bills. Provider statements/evidence explain why bills changed. AI may produce structured candidate facts, but deterministic code validates evidence, performs arithmetic, enforces ownership/security, compares history, and makes final persistence/alert decisions. AI output is never evidence by itself.

## Client platform direction

FullWorth is moving to a **PWA-first client architecture**.

- `FullWorth.Web` is the target single client for desktop browsers, mobile browsers, and installed home-screen/desktop web-app use.
- The MAUI client remains transitional until the installed PWA has verified feature parity for authentication, Plaid connection/update flows, transactions, bills, activity, statements, account security/settings, localization, and session-expiry behavior.
- Do not remove the MAUI project or its CI gate until that parity/cutover checkpoint is explicitly complete.
- PWA caching must preserve FullWorth financial-data boundaries. Authenticated HTML, BFF/API responses, financial data, statements, tokens, cookies, and account-specific content must never be intentionally persisted into Cache Storage for offline use.
- The initial service worker may cache only a generic public offline fallback; live FullWorth navigation remains network-first.
- If native app-store packaging is later required, prefer a thin web-oriented native wrapper after PWA parity rather than rebuilding a second product UI.

## FullWorth v2 UI position

The consumer-facing v2 overhaul, installed-PWA polish, schema-drift repair, refreshed public landing experience, GitHub-rendered visual-acceptance fixes, browser PWA/offline proof, responsive-browser Back navigation proof, browser session-expiry hardening, browser security-dialog acceptance, and the user-visible PWA update path are current on `development` at `6db99dda0bc02fbb0440579da4924a82257ba786`.

Covered consumer surfaces:
- App shell/navigation and installed-PWA/mobile navigation.
- Overview / Financial Pulse.
- Bills and Bill Detail.
- Activity.
- Account and connected-bank surfaces.
- Transactions.
- Settings / account security.
- Data & Privacy.
- Subscription / plan & access.
- Profile.
- Public/auth brand consistency using the approved FullWorth mark.
- Installed-PWA launch/theme chrome polish and FullWorth user-visible export/statement filenames from PR #173.
- Forward-only repair migration for known `TimestampDisplayMode` / subscription-key label schema drift from PR #174.
- Broader FullWorth financial-hub public landing experience from PR #175.

`/app/admin` remains intentionally internal/staff-oriented and was not part of the consumer-fintech visual overhaul.

GitHub-rendered visual acceptance is now established for the public/auth experience plus the authenticated desktop and responsive-mobile browser surfaces.

- PR #182 added the production-like Playwright screenshot harness.
- PR #183 fixed broken public landing-page navigation anchors found in the first screenshot review.
- PR #184 expanded visual coverage and added public-anchor assertions.
- PR #185 fixed the duplicate mobile active-navigation state when the Menu sheet is open; exact head `8669734e21153836fd9f9681382fbaaf65fba805` passed CI #678 before merge.
- PR #186 made Menu represent secondary mobile routes such as Profile and Subscription; exact head `fd1f2c891cd3d376d51d2943ce4a550df6b9a6d8` passed CI #679 before merge. The resulting screenshots were visually checked and confirmed the intended active-state behavior.
- PR #188 added real Chromium service-worker/offline acceptance. Exact head `feda0a755c1f813ece70c07e554cf89873e217d8` proved that the FullWorth service worker registers with `updateViaCache=none` and that authenticated `/app` navigation falls back to the generic public offline page rather than cached financial UI when Chromium is forced offline. The Linux production-container/PWA/recovery job passed on the original successful attempt and then passed two additional same-commit reruns to check for flakiness.
- PR #189 added responsive-mobile browser history acceptance. Exact head `2607ab8d21b2166cb29caaf5d707b13aa8d04376` passed CI #687; Chromium navigates Overview → Bills → Activity through the real bottom nav, then browser Back twice must restore the prior URLs and active route states. This is browser-history proof, not Android/iOS hardware-back proof.
- PR #191 exposed and fixed a real browser session-expiry defect: unauthenticated cookie challenges for `/bff` could redirect `fetch()` to login HTML, preventing the BFF JavaScript from observing the expected 401. `/bff` cookie challenges now return 401 and access-denied responses return 403, while ordinary page requests keep login redirects. Exact head `703d3e474827f557550a45b9735c15ef4d5ab43c` passed full CI #691 and an additional same-head Linux production-container/PWA/recovery rerun; Chromium clears the authenticated cookie, invokes the real BFF module, and requires fail-closed navigation to `/login` with no authenticated app shell remaining.
- PR #193 added responsive-mobile Chromium acceptance for the real Change password and Change email security dialogs. Exact head `77b22b34ca757b8f92f7ba08f89c2edec2bed9f0` passed CI run `35687185230` across backend/tests, MAUI Android, and Linux production-container/PWA/recovery gates, then merged to `development` as `d11f528`. The acceptance verifies dialog titles, accessible descriptions, close behavior, and Escape dismissal; it does not replace installed-device validation.
- PR #196 added a user-visible installed-PWA update path. Review caught and fixed an activation-lifecycle defect before merge: the service worker still called `skipWaiting()` during install, which bypassed the waiting-worker state required by the Refresh-to-update flow. Exact final head `fc188d5bf4098aab7abf6693bd855a9c59655113` passed full CI #699 across backend/tests, MAUI gating, Chromium/production-container security checks, and recovery, then squash-merged to `development` as `6db99dda0bc02fbb0440579da4924a82257ba786`. The regression test now enforces the controlled `SKIP_WAITING` activation path. Real installed-device update acceptance remains open.

The remaining UI acceptance gate is real installed-device / interaction acceptance rather than another browser screenshot redesign:
- installed Android PWA;
- installed/added-to-home-screen iOS PWA where available;
- keyboard resize and Android/iOS hardware-back behavior;
- installed-PWA install/update behavior on real devices (the visible waiting-worker update path is code/CI verified by PR #196, but installed-device behavior is not yet proven);
- Plaid Hosted Link return;
- statement file picker;
- security dialogs in installed/mobile contexts (browser-dialog behavior is covered by PR #193; installed-device behavior remains open).

Do not claim those installed-device or human-interaction gates complete from Playwright screenshots alone.

## Cost-aware validation cadence

FullWorth is pre-revenue, so validation is risk-based rather than continuously exhaustive:
- ordinary documentation and low-risk changes use the cheapest relevant local or CI checks;
- UI-only changes use targeted browser acceptance when the affected Web surface changes, without automatically requiring unrelated backend or MAUI validation;
- authentication, ownership, database, payment, deployment, security, or workflow changes retain the full required CI gate;
- installed-device and full end-to-end acceptance are release-candidate or materially affected-change gates, not hourly activity;
- external production readiness monitoring is manual-only until beta/revenue operations justify recurring monitoring cost.

Existing browser and offline proofs should not be rerun without relevant source changes or new evidence.

## Repository / stack

Repository: `Fullworth/FullWorth`

Default/release branch: `master`
Active integration branch: `development`

### Current GitHub baseline

- `master`: `a4d60bc25d680dfc3b786fb476e4e7f42f84eba1` (current GitHub release-branch head; this is not by itself production-deployment evidence).
- `development`: `2cb7a585323ca7be76360fcf1c5e7e0e80012c35` after PR #302 completed the password-change Web-session invalidation checkpoint with exact-head CI green.
- Latest code-bearing `development` merge before the later maintenance/handoff work: `d86c7ac09650a6605f9262ede22a339a3f25d757` (PR #197 Web/BFF protected-secret newline normalization; exact head `7e47db358c0fea0c6b7b4a1b5c5347ef99b68770`, full CI #700). Later handoff-only commits may advance the branch without changing product/runtime behavior.
- PR #163 promoted the frozen development release to `master` as `cbcf261e13636f0330cb9d7be2ce413871e413aa`. Its exact promotion head `e8a512f62b188c24158abaec581e45217d3e9e58` passed FullWorth CI #644 across backend/tests, MAUI Android, and the Linux production-container/security/recovery gate before merge.
- `development` was then fast-forwarded to the verified master merge so both long-lived branches are synchronized at the same release baseline.
- Since that release baseline, the FullWorth v2 consumer UI overhaul was completed on `development`. PRs #168–#171 finished Account/Settings, Bill Detail/Transactions, Privacy/Subscription, Profile, and final brand consistency. PR #173 added installed-PWA theme/chrome and remaining user-visible FullWorth filename polish and merged as `623ed33fc50f09b42e72b85daf34c049ca1dd2b7` after CI #655 passed. PR #174 added the forward-only idempotent repair migration for `AspNetUsers.TimestampDisplayMode` / `SubscriptionAccessKeys.Label`; exact head `b49b1b4392b5ee4fa840df2039d0cada85f1d46c` passed CI #656 before squash merge `91d4e2028504648c2f3f7be8489f0460f99c7c00`. PR #175 redesigned the public landing page; exact head `ae8d693b356e310ea5cd0604e6e0d5af716cde5c` passed CI #657 before merge `f16dcf6f5ae1ec6ca35e4aec8ecada1b12693d80`. PR #178 then promoted that verified development state to `master` as merge commit `19f83716a475c9ab5060a6681e06eb86dad62394`; that GitHub promotion is **not** evidence that production was redeployed. PRs #181–#186 subsequently added efficient docs-only CI detection, GitHub-rendered visual acceptance, and the concrete browser/mobile navigation fixes described above. PR #176 updated `Microsoft.NET.Test.Sdk` to 18.10.1 after a fresh rebase/exact-head CI pass; PR #177 updated `actions/cache` from v4 to v6 after a fresh rebase and full three-job CI pass. PR #188 then added the real-browser PWA/offline boundary proof described above. PR #189 added responsive-browser Back navigation acceptance. PR #191 fixed the BFF cookie-challenge/session-expiry defect found by Chromium acceptance and added production-cookie plus real-browser regression coverage. PR #193 added responsive-mobile security-dialog acceptance. PR #195 adopted the cost-aware validation cadence and disabled recurring production-readiness scheduling while preserving manual dispatch. PR #196 added the controlled user-visible PWA update flow described above. PR #197 independently closed the repository-visible Web/BFF smoke newline-handling defect: protected password/authenticator/recovery files are normalized into protected one-line temporary copies, ordinary LF/CRLF endings are not submitted as part of the secret, empty or multi-line files fail closed, and regression coverage now detects the behavior. Exact head `7e47db358c0fea0c6b7b4a1b5c5347ef99b68770` passed full CI #700 before squash merge `d86c7ac09650a6605f9262ede22a339a3f25d757`. That commit remains a historical code-bearing baseline; current branch heads are recorded above.
- PR #96 synchronized the Slack-compatible readiness-alert payload into `development` as `0253f08581417f9e41293481fccbcaf5da301ede`.
- PR #98 promoted the secure private-beta acceptance hardening to `master` as `3622b57c84c035c30a63bea070f53195635a62eb`. CI #534 passed all three required jobs on exact head `0253f085...`.
- PR #99 fixed HTML-encoded ASP.NET Core Identity confirmation-link parsing and merged into `development` as `847e17a20c97352114aafb7ef407da8a40882591`.
- PR #100 promoted that focused registration hotfix to `master` as `7824cc5f6ddb0231c986a793f15654d0314a8ad5`. Its exact PR head `847e17a...` passed backend build/tests, MAUI Android build, and Linux production container/recovery checks.
- No post-merge CI run is being claimed for merge commit `7824cc5...`; the verified automated gate is the exact PR #100 head.

Stack: .NET 10 MAUI + ASP.NET Core API + Blazor Interactive Server Web/BFF, PostgreSQL/EF Core, ASP.NET Core Identity bearer auth, opaque HttpOnly Web session references with Data-Protection-protected server-side Redis auth tickets, Plaid, xUnit, PdfPig, Tesseract, Docker Compose/Caddy/systemd, encrypted Restic recovery.

Public Web: `https://fullworth.org`
Public API: `https://api.fullworth.org`
Legacy compatibility aliases: `https://billbeacon.net` and `https://api.billbeacon.net`
Production path: `/opt/billwatch`

## Security invariants

- Plaid access tokens remain server-side/protected at rest.
- Web bearer/refresh tokens remain server-side inside Data-Protection-protected distributed authentication tickets. The browser receives only an opaque protected HttpOnly session reference; bearer/refresh material is not intentionally exposed to browser JavaScript.
- External provider ID tokens/proofs must not be exposed to browser JavaScript.
- User financial resources and statements remain ownership-scoped; cross-user IDs normally return 404 where appropriate.
- Staff roles do not grant access to another user's financial evidence.
- Statement storage paths never leave the API; signature/type/size validation remains enforced.
- Financial/auth API and BFF responses remain no-store.
- Cookie-auth challenges under `/bff` return status codes (401/403) rather than login-page redirects so browser BFF clients can fail closed on expired/invalid sessions; normal page authentication redirects remain intact.
- Production requires persistent Data Protection keys, explicit statement storage, Plaid credentials, AllowedHosts, and trusted reverse-proxy configuration.
- Never log raw statements, full account numbers, auth/Plaid/provider tokens, passwords, recovery codes, provider/database/Restic secrets, or private operations webhooks.
- AI-derived persistence remains disabled; deterministic extraction remains production persistence.

## Current production state

The current operator-reported live production release is:

`81a74f11941f6ed67ba5de61b9ef186ef09bae3c`

On 2026-09-22, the operator reported that this exact `master` release was live after the requested guarded deployment flow. The detailed deployment transcript for this release is not stored in this context. The stronger per-step guarded-deployment evidence listed below remains historical evidence from the earlier verified deployment and must not be silently attributed to `81a74f...`.

- The guarded deployment completed successfully.
- The deployment created and verified encrypted Restic recovery snapshot `7449ac243947...` before replacing services. The full snapshot identifier remains an operator-side production artifact; do not infer or invent the omitted suffix in repository documentation.
- API, Web, database, and edge are healthy.
- The release marker and running image revisions match `cbcf261e13636f0330cb9d7be2ce413871e413aa`.
- Public API/Web readiness and HTTP security-boundary verification passed.
- Private-beta host readiness passed.
- Subscription enforcement remains safely disabled.
- The backup timer and runtime watchdog are active.
- Non-destructive direct-API smoke passed against the deployed release.
- Non-destructive authenticated Web/BFF smoke passed using a protected newline-normalized temporary credential.
- The deployed release does **not** contain the repository Web/BFF protected-secret newline normalization later merged through PR #197. An earlier VPS-only commit reported as `f9000be` remains unreviewed historical local state and is not source authority; PR #197 is the reviewed repository implementation. Do not infer that either the PR #197 merge or that old local commit is deployed.

This verifies the guarded release, host prerequisites, non-destructive direct-API smoke, and non-destructive authenticated Web/BFF smoke. It does **not** yet prove objective cross-user isolation, the controlled Plaid lifecycle, representative statement semantics/OCR, hosted-link human completion, reboot behavior, external alert receipt, clean-host recovery against the real off-host repository, provider-enforced immutable storage, account-deletion evidence, or qualified legal review.

## Verified P0/private-beta code position

The repository contains regression-tested P0 work covering identity/BFF isolation, ownership, Plaid state handling, statement validation/processing, subscription rollout gating, backup/recovery, operations alerts, release integrity, controlled reboot evidence, and private-beta evidence correlation.

Important verified slices include:

- centralized Web antiforgery, security headers, no-store boundaries, sensitive endpoint authorization, and user-partitioned authenticated rate limits;
- Owner/Admin and access-key privilege boundaries with role-claim freshness;
- versioned private-beta Terms/Privacy acceptance across Web, MAUI, and direct API registration;
- account deletion reauthentication/2FA/staff-role protections, Plaid revoke-first behavior, crash-safe statement quarantine/reconciliation, owned-data erasure, and deployed-release disposable-account deletion proof support;
- server-side BFF access-token refresh with rotated-token persistence and fail-closed sign-out on refresh failure;
- objective cross-user Web/BFF ownership smoke using a second controlled identity and real foreign-owned resources;
- external sign-in support and FullWorth 2FA/recovery-code flows;
- Plaid `RequiresAttention` classification/persistence and ownership isolation;
- PDF/JPG/JPEG/PNG statement signature validation, upload/status/download ownership, terminal-state semantics, OCR coverage, and storage-path secrecy;
- guarded direct API, authenticated Web/BFF, Owner/Admin, access-key, Plaid, statement lifecycle, statement semantic-review, subscription lifecycle, and account-deletion smoke/proof harnesses;
- Internal Beta 0 release-pinned acceptance runner;
- release-pinned Plaid Hosted Link observation proof;
- encrypted Restic backup/restore verification and guarded clean-host recovery support;
- append-only routine backup boundary, runtime watchdog, release-integrity checks, metadata-only operations alerts, independent external readiness alerts, and controlled reboot pre/postflight proof.

## Private-beta smoke hardening

PR #86 added secure second-factor support to `deploy/smoke-private-beta.sh` without weakening its existing ownership/admin/mutation safeguards.

The harness supports either:

- `BILLWATCH_SMOKE_TWO_FACTOR_CODE_FILE`, or
- `BILLWATCH_SMOKE_RECOVERY_CODE_FILE`

Secret files must be regular, non-symlink files with mode `600`; both cannot be supplied simultaneously. Authentication failures are handled without printing tokens or secret values.

The deployed Web/BFF smoke harness on current `development` likewise supports TOTP/recovery-code files, encrypted cookie-session verification, authenticated BFF probes, export secret/storage-boundary checks, antiforgery issuance, and protected logout. The cross-user Web/BFF harness supports a separate foreign identity with its own protected second factor and proves foreign bill-stream/statement-upload access returns 404.

These capabilities are code/CI verified. They are **not** proof that the deployed production environment has been exercised with controlled accounts.

## Current machine-verifiable P0 position

The exact PR #163 promotion head `e8a512f62b188c24158abaec581e45217d3e9e58` passed the complete CI #644 gate, and guarded production deployment of master release `cbcf261e13636f0330cb9d7be2ce413871e413aa` completed successfully.

Non-destructive direct-API smoke and authenticated Web/BFF smoke have now also passed against that deployed release.

Most remaining private-beta P0 items are real-environment acceptance gates, not missing generic application code. Do not manufacture synthetic evidence.

In particular:

- objective cross-user Web/BFF ownership proof requires a second controlled identity plus a controlled foreign-owned statement/resource fixture;
- the Plaid lifecycle requires a controlled account with a suitable real/sandbox connection, and Hosted Link completion remains a human interaction gate;
- representative statement lifecycle/semantic/OCR acceptance requires explicit approval to upload a controlled fixture with operator-known facts;
- disposable account-deletion evidence must still be produced and matched to the deployed release;
- controlled reboot proof remains an explicit operator action;
- independent external alert receipt must be observed;
- clean-host restore must use the actual off-host repository;
- provider-enforced immutable/Object-Lock/WORM/equivalent protection must be configured and proven;
- qualified legal review of the exact Terms/Privacy version remains external evidence.

## Production/rollout rules

- Global subscription enforcement remains OFF until its separate rollout gate is deliberately approved.
- Staff roles do not grant access to another user's financial evidence.
- AI-derived persistence remains disabled.
- Startup EF migrations mean production remains one API instance until migration ownership is redesigned.
- Never run `docker compose down --volumes` against production.
- Beta Terms/Privacy are operational drafts, not qualified legal review.
- Guarded-deploy only a fully green `master` release through the normal release path.

## Remaining real-environment private-beta gates

Before trusted external beta invitations:

1. Run objective cross-user Web/BFF ownership proof with a second controlled identity and controlled foreign-owned resource/statement fixture against deployed release `7e8571a26447538db249c862ad009487cce119bc`.
2. Run disposable account-deletion proof and feed same-release evidence into Internal Beta 0.
3. Exercise the controlled Plaid connect/update lifecycle with a suitable account and complete the Hosted Link human-interaction observation.
4. With explicit approval, upload controlled representative PDF/scanned-PDF/JPG/PNG fixtures and review extraction/OCR fields plus bill-change explanations against operator-known facts.
5. Run controlled reboot proof.
6. Run clean-host restore against the actual off-host repository.
7. Configure and prove provider-enforced immutable/protected backup recovery.
8. Run independent alert-observation proof and personally confirm intended destinations.
9. Combine same-release technical, alert, Plaid, recovery, and acceptance evidence.
10. Complete Internal Beta 0 on real controlled bills with explicit expected subscription state where known.
11. Obtain qualified review of the exact deployed Terms/Privacy version.
12. Run the trusted-beta launch evidence verifier only after every underlying real-world fact is genuinely complete.

## Immediate resume point

1. Read current GitHub `master`, `development`, open PRs, issue #291, this context, and the roadmap snapshot before changing release state.
2. Repository-side security hardening remains **61/65** on #291. The four remaining items are immutable/off-host storage proof, compromised-host clean recovery, the independent-review governance decision, and exact deployed-release evidence. Do not invent generic code work to make the count move.
3. The frozen master release candidate is `c092a9c76c5f4e811941400606c32d75a0a50a29`. Exact master-push FullWorth CI #1611 passed and produced the required attested production-image artifact. Repository-governance run #11 also passed.
4. `development` is `bbf5f0e1a8016bc6dcaae52963d339d82681de2f`, 2 history-only commits ahead and 0 behind `master`, with 0 file differences after PR #667 synced the release-promotion ancestry back.
5. The current verified live production release is still `7e8571a26447538db249c862ad009487cce119bc`, proven by guarded production deploy run #7 (`36221860082`). Do not describe `c092a9c7...` as deployed until a separate guarded production deployment proves it.
6. The next release action is a **separately approved guarded production deployment** of exact master SHA `c092a9c76c5f4e811941400606c32d75a0a50a29`. The production workflow is manual and requires explicit confirmation; do not substitute a normal GitHub merge for that approval.
7. After deployment, collect one same-release evidence bundle: deployed-host security/containment, objective two-user ownership proof, provider/Plaid lifecycle evidence, controlled statement/OCR semantics, alert receipt, account deletion, reboot, immutable off-host backup proof, and clean-host recovery.
8. Physical Android installed-PWA acceptance under #251 remains required; issue #293 still requires real Plaid payroll/sandbox acceptance; iOS issue #258 remains a separate re-test. Browser/CI evidence is not a substitute for those real-world checks.
9. The branch ruleset requires PRs for `master` and `development`, blocks deletion/non-fast-forward updates, has no bypass actors, and requires review-thread resolution. It still requires 0 approving reviews; any independent-review requirement is a repository-owner/admin policy decision.
10. Issue #260 is closed. Preserve its zero-exception module/data-ownership ratchets, and preserve authentication, server-side BFF sessions, antiforgery, HTTPS, ownership, provider-token, statement-storage, network isolation, parser containment, secret scoping, database role separation, backup/recovery, migration, and financial-data boundaries.
