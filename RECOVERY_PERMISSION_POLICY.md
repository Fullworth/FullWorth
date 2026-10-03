# Recovery permission policy

FullWorth treats restored key material, statement documents, and PostgreSQL
storage as private recovery assets. A snapshot is not accepted merely because
its checksums and database rows are readable.

## Required ownership and modes

| Asset | Owner | Directory mode | File mode |
|---|---|---:|---:|
| API Data Protection ring | application UID/GID 1654 | 0700 | 0600 |
| Web Data Protection ring | application UID/GID 1654 | 0700 | 0600 |
| Statement storage | application UID/GID 1654 | 0700 | 0600 |
| Isolated PostgreSQL restore data | image `postgres` UID/GID | 0700 | 0600 |

Symbolic links, device nodes, sockets, FIFOs, mount escapes, and other
non-directory/non-regular entries are rejected from protected file trees.

## Capture and restore behavior

The API startup entrypoint applies a restrictive umask, rejects linked or
non-regular statement-storage entries, and repairs existing statement
directories and files to the required modes before serving requests. Backup
capture validates the statement tree's type, owner, and modes before archiving
it. The Data Protection ring validation remains stricter and also requires at
least one non-empty key.

Restore verification extracts into disposable storage under `umask 077`,
then revalidates both key rings and the entire statement tree before loading
the database dump. Every database statement record must still resolve to a
regular restored file with the recorded size. The isolated PostgreSQL service is healthy only when its data root retains the
required owner and mode. After the dump has been loaded and the recovery checks
finish, CI audits every data directory and regular file for the exact private
modes before accepting the restore.

These checks do not mutate a Restic snapshot or restore over the live
application volumes.

## Evidence boundary

Repository tests cover accepted private trees and rejection of group-readable
directories/files, links, special files, and wrong owners. The production
container gate additionally observes the real application volumes, performs an
encrypted backup, restores into isolated file and database storage, and
requires the permission-aware PostgreSQL health check.

This is repository and CI evidence. A clean-host drill using the exact guarded
release is still required before claiming deployed recovery acceptance.
