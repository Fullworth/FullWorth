# Container image supply-chain policy

## Immutable production inputs

Every external container image used to build or run FullWorth's production and isolated recovery paths must use a readable tag plus an immutable `sha256` manifest digest. The tag identifies the intended upstream release; the digest is the authority used by the container runtime.

This policy covers:

- all external `FROM` references in `Dockerfile`, `Dockerfile.web`, and `deploy/backup/Dockerfile`;
- external runtime images in `compose.production.yml`;
- the isolated restore target in `compose.recovery-drill.yml`.

Repository-built `billwatch-*` images remain release-scoped by the exact 40-character `BILLWATCH_RELEASE_ID` and are rebuilt from the exact guarded release checkout. The offline AI evaluation image is separately required by its environment validator to use an explicit `ghcr.io/...:tag@sha256:...` reference.

## Regression enforcement

`deploy/tests/container-image-pinning-tests.sh` fails when a covered Dockerfile base, Dockerfile frontend, or external Compose image lacks a lowercase 64-character SHA-256 digest. It runs in the Linux production-container CI job for every workflow, deploy, Dockerfile, or Compose change.

The check is intentionally syntax- and scope-based. It proves that a mutable tag cannot silently change the reviewed bytes; it does not prove that an upstream image is vulnerability-free or that a deployed host pulled the intended digest.

## Updating images

Dependabot monitors the root Dockerfiles, backup Dockerfile, and root Compose files on the `development` branch. An image update must:

1. retain a human-readable version tag and replace the digest with the upstream manifest-list digest for that tag;
2. pass dependency review and the exact-head FullWorth CI production-container build;
3. preserve database/recovery compatibility and existing security boundaries;
4. merge through a focused pull request.

Do not remove a digest to obtain a security update. Review and advance the digest instead.

## Release image provenance and SBOMs

For production-container-relevant pushes to `master`, CI builds the API, parser-worker, and Web images from the exact commit. It generates an SPDX JSON SBOM from each built image, creates a GitHub SBOM attestation bound to that image's exported archive, and separately creates signed build-provenance attestations for the image archives. The workflow artifact contains all three image archives and their matching SBOM files and is retained for seven days; GitHub's attestations are published separately from that short-lived artifact.

The workflow uses commit-pinned GitHub Actions for SBOM generation and attestation.

The guarded GitHub production deployment resolves the successful FullWorth CI run for the exact approved `master` SHA, downloads only the release-named artifact, verifies both build-provenance and SPDX SBOM attestations for each application image archive, and creates a checksum manifest before transfer over pinned SSH. The host verifies that manifest, loads the three application images without rebuilding them, validates their release labels, and requires the running API, parser-worker, and Web containers to use the exact loaded image IDs before the release marker can advance. The backup image remains host-built because it is an operations image and is not part of the public application artifact set.

A direct host invocation of `deploy/deploy-production.sh` retains the recovery path that rebuilds images from the exact clean checkout. That path does not establish GitHub-attested deployed-image provenance. Production provenance acceptance therefore requires the guarded GitHub deployment and its successful same-release runtime checks.
