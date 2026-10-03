# Data Protection key lifecycle

FullWorth has two intentionally separate ASP.NET Core Data Protection key rings:

- the API ring uses application discriminator `BillWatch` and protects API-side
  credentials and Identity material; and
- the Web ring uses `BillWatch.Web` and protects Web authentication, external
  sign-in, and antiforgery material.

The rings must never be merged or substituted for one another. Both use a
90-day default key lifetime, retain older keys for decryption, and persist in
separate Docker volumes.

## Runtime permissions

The API and Web images start through `deploy/application-entrypoint.sh`. Before
managed code runs, the entrypoint:

- applies `umask 077` so newly generated key files are owner-only;
- requires an absolute, real directory rather than a symbolic link;
- rejects nested, symbolic-link, or other non-regular key-ring entries;
- enforces mode `0700` on the directory; and
- enforces mode `0600` on every existing regular file.

Both services run as the non-root application UID after the image is built.
The key volumes are mounted only into the service that owns them and read-only
into the backup container.

## Rotation and revocation

ASP.NET Core creates a replacement key before the active key expires. Old keys
must remain available because previously protected values may still require
decryption. Routine rotation therefore means adding a new active key, not
deleting historical keys.

A key-ring compromise is not repaired by ordinary rotation alone: an attacker
who copied an old key may still decrypt or forge material protected by it.
Follow the incident-response runbook, revoke affected sessions and provider
credentials, preserve required evidence, generate clean separated rings, and
redeploy through the guarded release path. Removing a key is destructive and
requires evidence that no retained value or rollback release needs it.

## Backup and recovery

A production backup stops the public edge plus both API and Web writers before
capture. The encrypted Restic bundle contains:

- the API key ring;
- the Web key ring;
- the database dump;
- statement files and their database manifest; and
- a release/migration/key-count manifest protected by SHA-256 checksums.

Backup capture fails if either ring is empty, contains a link/non-regular entry,
or has group/other permission bits. Restore verification checks both archives,
their counts, and their preserved `0700`/`0600` permissions before accepting
the snapshot. Restored rings remain isolated verification artifacts; the
verifier never overwrites live volumes.

A successful repository or production-like restore check is not evidence that a
clean production host can decrypt live protected values. Before beta, the
operator must restore the exact encrypted snapshot on an isolated clean host
using the matching release, verify an API-protected value and Web authentication
flow, then destroy the drill environment. Never publish traffic during that
proof and never delete the source snapshot merely because the drill passed.
