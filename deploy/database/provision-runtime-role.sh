#!/bin/sh

set -eu

umask 077

fail()
{
    printf '%s\n' "Database runtime-role preparation failed: $1" >&2
    exit 1
}

mode=${1:-provision}

case "$mode" in
    provision|verify) ;;
    *) fail "mode must be provision or verify." ;;
esac

owner_passfile_source=/run/secrets/database_pgpass
runtime_password_file=/run/secrets/database_runtime_password
owner_passfile=/tmp/fullworth-owner.pgpass
runtime_passfile=/tmp/fullworth-runtime.pgpass
runtime_role=fullworth_runtime

for secret_file in "$owner_passfile_source" "$runtime_password_file"
do
    [ -f "$secret_file" ] && [ ! -L "$secret_file" ] && [ -s "$secret_file" ] ||
        fail "a required database credential file is missing or unsafe."
done

cleanup()
{
    rm -f "$owner_passfile" "$runtime_passfile"
}

trap cleanup EXIT HUP INT TERM

cp "$owner_passfile_source" "$owner_passfile"
chmod 0600 "$owner_passfile"
export PGPASSFILE="$owner_passfile"

psql \
    --host=database \
    --port=5432 \
    --username=billwatch \
    --dbname=billwatch \
    --no-psqlrc \
    --set=ON_ERROR_STOP=1 <<'SQL'
\set runtime_password `cat /run/secrets/database_runtime_password`

SELECT
    'CREATE ROLE fullworth_runtime LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS'
WHERE NOT EXISTS (
    SELECT 1
    FROM pg_roles
    WHERE rolname = 'fullworth_runtime'
)
\gexec

ALTER ROLE fullworth_runtime
    WITH LOGIN
    NOSUPERUSER
    NOCREATEDB
    NOCREATEROLE
    NOINHERIT
    NOREPLICATION
    NOBYPASSRLS;

ALTER ROLE fullworth_runtime
    PASSWORD :'runtime_password';

REVOKE ALL PRIVILEGES ON DATABASE billwatch
    FROM fullworth_runtime;

GRANT CONNECT ON DATABASE billwatch
    TO fullworth_runtime;

REVOKE CREATE ON SCHEMA public
    FROM PUBLIC;

REVOKE ALL PRIVILEGES ON SCHEMA public
    FROM fullworth_runtime;

GRANT USAGE ON SCHEMA public
    TO fullworth_runtime;

GRANT SELECT, INSERT, UPDATE, DELETE
    ON ALL TABLES IN SCHEMA public
    TO fullworth_runtime;

GRANT USAGE, SELECT, UPDATE
    ON ALL SEQUENCES IN SCHEMA public
    TO fullworth_runtime;

ALTER DEFAULT PRIVILEGES
    FOR ROLE billwatch
    IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE
    ON TABLES
    TO fullworth_runtime;

ALTER DEFAULT PRIVILEGES
    FOR ROLE billwatch
    IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE
    ON SEQUENCES
    TO fullworth_runtime;
SQL

[ "$mode" = verify ] || {
    printf '%s\n' "FullWorth database runtime role provisioned."
    exit 0
}

role_ok="$(
    psql \
        --host=database \
        --port=5432 \
        --username=billwatch \
        --dbname=billwatch \
        --no-psqlrc \
        --tuples-only \
        --no-align \
        --set=ON_ERROR_STOP=1 \
        --command="
            SELECT
                r.rolcanlogin
                AND NOT r.rolsuper
                AND NOT r.rolcreatedb
                AND NOT r.rolcreaterole
                AND NOT r.rolinherit
                AND NOT r.rolreplication
                AND NOT r.rolbypassrls
                AND NOT EXISTS (
                    SELECT 1
                    FROM pg_auth_members m
                    WHERE m.member = r.oid
                )
                AND NOT has_database_privilege(
                    r.rolname,
                    'billwatch',
                    'CREATE')
                AND NOT has_schema_privilege(
                    r.rolname,
                    'public',
                    'CREATE')
                AND has_schema_privilege(
                    r.rolname,
                    'public',
                    'USAGE')
                AND NOT EXISTS (
                    SELECT 1
                    FROM pg_class c
                    JOIN pg_namespace n
                        ON n.oid = c.relnamespace
                    WHERE n.nspname = 'public'
                      AND c.relkind IN ('r', 'p')
                      AND (
                          NOT has_table_privilege(
                              r.rolname,
                              c.oid,
                              'SELECT')
                          OR NOT has_table_privilege(
                              r.rolname,
                              c.oid,
                              'INSERT')
                          OR NOT has_table_privilege(
                              r.rolname,
                              c.oid,
                              'UPDATE')
                          OR NOT has_table_privilege(
                              r.rolname,
                              c.oid,
                              'DELETE')
                      )
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM pg_class c
                    JOIN pg_namespace n
                        ON n.oid = c.relnamespace
                    WHERE n.nspname = 'public'
                      AND c.relkind = 'S'
                      AND (
                          NOT has_sequence_privilege(
                              r.rolname,
                              c.oid,
                              'USAGE')
                          OR NOT has_sequence_privilege(
                              r.rolname,
                              c.oid,
                              'SELECT')
                          OR NOT has_sequence_privilege(
                              r.rolname,
                              c.oid,
                              'UPDATE')
                      )
                )
            FROM pg_roles r
            WHERE r.rolname = 'fullworth_runtime';
        "
)"

[ "$role_ok" = t ] ||
    fail "the runtime role does not satisfy the least-privilege contract."

runtime_password="$(
    cat "$runtime_password_file"
)"

escaped_runtime_password="$(
    printf '%s' "$runtime_password" |
        sed \
            -e 's/\\/\\\\/g' \
            -e 's/:/\\:/g'
)"

printf '%s\n' \
    "database:5432:billwatch:$runtime_role:$escaped_runtime_password" \
    > "$runtime_passfile"

chmod 0600 "$runtime_passfile"
export PGPASSFILE="$runtime_passfile"
unset runtime_password escaped_runtime_password

psql \
    --host=database \
    --port=5432 \
    --username="$runtime_role" \
    --dbname=billwatch \
    --no-psqlrc \
    --set=ON_ERROR_STOP=1 \
    --command='SELECT COUNT(*) FROM "__EFMigrationsHistory";' \
    >/dev/null

if psql \
    --host=database \
    --port=5432 \
    --username="$runtime_role" \
    --dbname=billwatch \
    --no-psqlrc \
    --set=ON_ERROR_STOP=1 \
    --command='BEGIN; CREATE TABLE public."__FullWorthRuntimePrivilegeProbe" ("Id" integer); ROLLBACK;' \
    >/dev/null 2>&1
then
    fail "the runtime role can create permanent schema objects."
fi

printf '%s\n' "FullWorth database runtime role verified."
