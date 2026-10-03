# File-backed production secrets

FullWorth keeps `.env.production` as the protected, host-local operator source, but no longer projects supported credentials into ordinary long-lived container environment variables.

## Boundary

`deploy/materialize-container-secrets.sh` validates its inputs and atomically creates `.fullworth-secrets/` with a mode-`0700` directory. Its files are mode `0644` only because local Docker Compose implements secrets as read-only bind mounts and the non-root container users must be able to read the mounted inode. Other host users cannot traverse the private parent directory.

The generated directory and every environment file are excluded from Git and Docker build contexts. The script never prints values and removes obsolete files from earlier layouts.

Compose scopes mounts by service:

- API: database connection, parser authentication, Plaid, Stripe, and Resend values.
- Parser worker: parser authentication only.
- Web: Redis and external-identity client secrets.
- PostgreSQL and isolated restore: the native `POSTGRES_PASSWORD_FILE` interface.
- Redis: a file read by the container shell without a password environment entry.
- Backup: database, Restic, and AWS credential files; Restic and AWS receive file paths, while PostgreSQL uses a private temporary passfile.

API, Web, and parser worker load `/run/secrets` through ASP.NET Core KeyPerFile after the default providers, so a mounted file is authoritative over any accidental lower-priority environment setting.

## Verification

`deploy/tests/file-backed-secret-tests.sh` covers materialization, permissions, stale-file removal, missing/duplicate credentials, symlink rejection, redacted failures, Compose wiring, application providers, deployment integration, and CI integration.

`deploy/verify-file-backed-secrets.sh` examines the live containers before a release is accepted. It rejects protected configuration keys in Docker's ordinary container environment and verifies required and forbidden per-service mounts. The existing non-disclosure gate then verifies public responses and retained logs.

## Residual boundary

The host deployment account and Docker daemon remain privileged trust boundaries. Docker Compose secret mounts improve service scoping and remove values from `docker inspect .Config.Env`; they do not provide a hardware secret store or protect values from root/Docker-daemon access. Production acceptance still requires a guarded deployment of the exact verified release.
