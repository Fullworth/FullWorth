#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)

fail()
{
    printf '%s\n' "Database role-separation regression failed: $1" >&2
    exit 1
}

compose_file="$root_dir/compose.production.yml"
materializer="$root_dir/deploy/materialize-container-secrets.sh"
provisioner="$root_dir/deploy/database/provision-runtime-role.sh"
preparer="$root_dir/deploy/prepare-database-runtime.sh"
deploy_script="$root_dir/deploy/deploy-production.sh"
backup_runner="$root_dir/deploy/run-backup.sh"
secret_verifier="$root_dir/deploy/verify-file-backed-secrets.sh"
api_program="$root_dir/FullWorth.API/Program.cs"

sh -n "$provisioner" ||
    fail "runtime-role provisioner has invalid shell syntax."

sh -n "$preparer" ||
    fail "database preparation wrapper has invalid shell syntax."

grep -Fq 'Username=fullworth_runtime;Password=$database_runtime_password' "$materializer" ||
    fail "steady-state API connection is not assembled with the runtime role."

grep -Fq 'migration-database-connection' "$materializer" ||
    fail "migration owner connection is not materialized separately."

grep -Fq 'database-runtime-password' "$materializer" ||
    fail "runtime database password is not materialized separately."

grep -Fq 'Database__MigrateOnStartup: "false"' "$compose_file" ||
    fail "steady-state API still enables startup migrations."

grep -Fq 'database-role-provisioner:' "$compose_file" ||
    fail "database role provisioner service is missing."

grep -Fq 'database-migrator:' "$compose_file" ||
    fail "one-shot database migrator service is missing."

grep -Fq 'Database__MigrationOnly: "true"' "$compose_file" ||
    fail "database migrator does not use migration-only mode."

grep -Fq '/tmp:rw,noexec,nosuid,nodev,size=16m,mode=1777' "$compose_file" ||
    fail "database role provisioner cannot securely stage its temporary passfiles."


grep -Fq 'source: migration_database_connection' "$compose_file" ||
    fail "database migrator does not receive the isolated owner connection."

grep -Fq 'source: database_runtime_password' "$compose_file" ||
    fail "role provisioner does not receive the separate runtime password."

for required_role_clause in \
    'NOSUPERUSER' \
    'NOCREATEDB' \
    'NOCREATEROLE' \
    'NOINHERIT' \
    'NOREPLICATION' \
    'NOBYPASSRLS'
do
    grep -Fq "$required_role_clause" "$provisioner" ||
        fail "runtime role is missing $required_role_clause."
done

grep -Fq 'REVOKE CREATE ON SCHEMA public' "$provisioner" ||
    fail "runtime role provisioning does not remove public schema creation."

grep -Fq 'GRANT SELECT, INSERT, UPDATE, DELETE' "$provisioner" ||
    fail "runtime role does not receive bounded table DML."

grep -Fq 'ALTER DEFAULT PRIVILEGES' "$provisioner" ||
    fail "future migration-created objects are not covered by default privileges."

grep -Fq '__FullWorthRuntimePrivilegeProbe' "$provisioner" ||
    fail "runtime-role verification does not prove permanent DDL is denied."

grep -Fq '"Database:MigrationOnly"' "$api_program" ||
    fail "API does not expose the one-shot migration mode."

grep -Fq 'Database:MigrateOnStartup is reserved for the one-shot migration process outside development.' "$api_program" ||
    fail "production API does not fail closed on steady-state startup migrations."

grep -Fq 'FULLWORTH_BACKUP_RESTORE_SERVICES=false' "$deploy_script" ||
    fail "guarded deployment backup can restart the old API before the runtime role exists."

prepare_line=$(grep -n 'prepare-database-runtime.sh' "$deploy_script" | tail -n 1 | cut -d: -f1)
startup_line=$(grep -n '^compose up \\' "$deploy_script" | tail -n 1 | cut -d: -f1)

[ -n "$prepare_line" ] && [ -n "$startup_line" ] ||
    fail "guarded deployment is missing database preparation or candidate startup."

[ "$prepare_line" -lt "$startup_line" ] ||
    fail "candidate API can start before database role preparation and migration."

grep -Fq 'database-role-provisioner \' "$preparer" ||
    fail "database preparation does not provision the runtime role."

grep -Fq 'database-migrator' "$preparer" ||
    fail "database preparation does not run the one-shot migrator."

grep -Fq 'The API must be stopped before database role preparation or schema migration.' "$preparer" ||
    fail "database preparation does not refuse schema changes while the steady API is running."


grep -Fq 'database-role-provisioner \' "$preparer" ||
    fail "database preparation does not verify grants after migration."

grep -Fq '[ "$exit_code" -ne 0 ] ||' "$backup_runner" ||
    fail "pre-deploy backup failure does not restore the last verified runtime."

grep -Fq 'Username=fullworth_runtime;' "$secret_verifier" ||
    fail "live secret verification does not prove the API uses the runtime database role."

grep -Fq 'retained a stopped container with elevated database credentials' "$secret_verifier" ||
    fail "live secret verification does not reject retained migration/provisioning containers."

printf '%s\n' "Database runtime/migration role-separation regression passed."
