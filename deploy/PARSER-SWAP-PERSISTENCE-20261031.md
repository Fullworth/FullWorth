# Parser swap persistence correction — October 31, 2026

## Demonstrated failure and scope

Issue [#1024](https://github.com/Fullworth-prod/FullWorth/issues/1024) records the old production9540 parser returning503 because its dedicated cgroup swap limit became unlimited despite equal Docker memory/total-swap limits. Reapplying those existing limits and setting the existing systemd scope's runtime MemorySwapMax=0 restored readiness without restart. The exact production trigger/timing remains unproven.

Installed runc1.3.5 vendors cgroupsv0.0.4. Its [systemd resource generator](https://github.com/opencontainers/cgroups/blob/v0.0.4/systemd/v2.go) omits converted swap zero; [upstream #74](https://github.com/opencontainers/cgroups/issues/74) describes loss of that limit on service-manager reload. This source match explains why a kernel-only repair does not correct the manager's future behavior. It does not prove which production event caused the incident.

## Correction

Only parser-worker receives the documented OCI annotation `org.systemd.property.MemorySwapMax: "uint64 0"` in production Compose. The manager owns the same no-swap boundary already intended by equal memory/memswap limits. See [tagged runc auxiliary properties](https://github.com/opencontainers/runc/blob/v1.3.5/docs/systemd.md#auxiliary-properties) and [Compose annotations](https://docs.docker.com/reference/compose-file/services/#annotations).

CPU, memory, PID limits, child containment, capabilities/identity drop, network isolation, TLS, readiness rejection and write behavior remain intact. There is no global scope drop-in, host package/runtime upgrade, new credential, production daemon reload or data change.

## Regression and acceptance

CI adds deploy/tests/parser-swap-persistence-tests.sh after existing delegation verification. It requires a disposable GitHub-hosted Actions context, systemd and cgroupv2; refuses local/self-hosted/missing-run contexts before Docker/systemd; checks actual kernel zero and manager zero; reloads only that isolated runner's manager; checks both again, unchanged parser PID/start and pinned internal readiness. It does not repair limits to make the test pass.

Local public-fixture shell syntax and three refusal contexts passed. The initial source c6f33f12ac6b6a3af02b56cd6ef2bddb19a6e026 left Compose unchanged. Actual container job124628095390/run41232038277 failed because systemd did not own the explicit zero boundary, after existing delegation checks passed. This establishes the manager mismatch on an isolated runner; it did not reload production. Record corrected final-head protected execution in [#1026](https://github.com/Fullworth-prod/FullWorth/pull/1026). Do not merge a failing test, bypass checks or infer combined success from older heads. Normal per-document memory/OCR and disposable backup/restore checks remain required.

This is a development correction. Approved masterccdb0cc6 and the freshly verified live9540 remain separate identities; the earlier exact-candidate approval does not silently promote this new configuration. No new release is deployed here. Keep #1024 open until the durable configuration and same-release live manager/kernel/readiness evidence are established. Physical devices, providers, real recovery and legal acceptance stay open.
