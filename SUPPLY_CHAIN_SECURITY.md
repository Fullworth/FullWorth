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

## Remaining provenance work

Immutable inputs close tag drift. Signed build provenance and SBOM publication for FullWorth-built release images remain a separate milestone, as does direct verification of the images on a deployed production host.
