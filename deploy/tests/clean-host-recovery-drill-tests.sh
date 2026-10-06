#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
runner="$root_dir/deploy/run-clean-host-recovery-drill.sh"
compose_file="$root_dir/compose.recovery-drill.yml"
temp_dir=$(mktemp -d)
env_file="$root_dir/.env.recovery-test-$$"

after_test()
{
    rm -f "$env_file"
    rm -rf "$temp_dir"
}

trap after_test EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Clean-host recovery drill test failed: $1" >&2
    exit 1
}

[ -f "$runner" ] || fail "recovery drill runner is missing."
[ -f "$compose_file" ] || fail "isolated recovery compose file is missing."
sh -n "$runner" || fail "recovery drill runner has invalid POSIX shell syntax."

if grep -Eq '^[[:space:]]*ports:' "$compose_file"; then
    fail "recovery drill topology must not publish host ports."
fi

for forbidden in postgres_data statement_files data_protection_keys web_data_protection_keys caddy_data
 do
    if grep -Fq "$forbidden" "$compose_file"; then
        fail "recovery drill topology references production state volume: $forbidden"
    fi
 done

if grep -Eq '^[[:space:]]+(api|web|edge|database):[[:space:]]*$' "$compose_file"; then
    fail "recovery drill topology must not define production application services."
fi

grep -Fq 'internal: true' "$compose_file" || fail "recovery drill network must remain isolated from external ingress."
grep -Fq 'BILLWATCH_ALLOW_LOCAL_BACKUP_REPOSITORY: "false"' "$compose_file" || fail "recovery verifier must force local repositories off."
grep -Fq 'AWS_SHARED_CREDENTIALS_FILE: /run/secrets/aws_credentials' "$compose_file" || fail "recovery verifier must use a mounted AWS shared-credentials file."
if grep -Eq '^[[:space:]]+AWS_(ACCESS_KEY_ID|SECRET_ACCESS_KEY):' "$compose_file"; then
    fail "recovery AWS keys must not be passed to the verifier as environment variables."
fi
grep -Fq 'file: ${BILLWATCH_RECOVERY_AWS_CREDENTIALS_FILE:?Set BILLWATCH_RECOVERY_AWS_CREDENTIALS_FILE}' "$compose_file" || fail "recovery AWS credentials must be mounted from the protected temporary file."
grep -Fq 'RESTIC_PASSWORD_FILE: /run/secrets/restic_password' "$compose_file" || fail "recovery verifier must read Restic credentials from a mounted secret file."
grep -Fq 'FULLWORTH_DATABASE_PGPASS_FILE: /run/secrets/database_pgpass' "$compose_file" || fail "recovery verifier must read database credentials from a mounted passfile secret."
if grep -Eq '^[[:space:]]*PGPASSWORD:' "$compose_file"; then
    fail "recovery verifier must not receive the database password through its environment."
fi
if grep -Eq '^[[:space:]]*RESTIC_PASSWORD:' "$compose_file"; then
    fail "recovery verifier must not receive the Restic password through its environment."
fi
grep -Fq 'file: ${BILLWATCH_RECOVERY_RESTIC_PASSWORD_FILE:?Set BILLWATCH_RECOVERY_RESTIC_PASSWORD_FILE}' "$compose_file" || fail "recovery Restic secret must be mounted from the protected temporary file."
grep -Fq 'file: ${BILLWATCH_RECOVERY_DATABASE_PGPASS_FILE:?Set BILLWATCH_RECOVERY_DATABASE_PGPASS_FILE}' "$compose_file" || fail "recovery database passfile must be mounted from the protected temporary file."
grep -Fq 'run --rm --no-deps verifier verify' "$runner" || fail "runner must invoke the existing cryptographic/database/file verifier."
grep -Fq 'down --volumes --remove-orphans' "$runner" || fail "runner must tear down isolated recovery state."

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
docker_log="$temp_dir/docker.log"
secret_file_log="$temp_dir/secret-files.log"
cat > "$fake_bin/docker" <<'EOF'
#!/bin/sh
set -eu

: "${BILLWATCH_TEST_DOCKER_LOG:?}"
: "${BILLWATCH_TEST_SECRET_FILE_LOG:?}"
: "${BILLWATCH_TEST_EXPECTED_RESTIC_PASSWORD:?}"
: "${BILLWATCH_TEST_EXPECTED_AWS_CREDENTIALS:?}"
original_args=$*
if [ "$original_args" = "compose version" ]; then
    printf '%s\n' "$original_args" >> "$BILLWATCH_TEST_DOCKER_LOG"
    exit 0
fi
env_file_count=0
supplemental_env_file=

while [ "$#" -gt 0 ]
do
    if [ "$1" = --env-file ]; then
        shift
        env_file_count=$((env_file_count + 1))
        supplemental_env_file=$1
    fi
    shift
done

[ "$env_file_count" -eq 2 ] || {
    echo "recovery Compose did not receive exactly one protected supplemental env file." >&2
    exit 1
}

secret_file="$(sed -n 's/^BILLWATCH_RECOVERY_RESTIC_PASSWORD_FILE=//p' "$supplemental_env_file")"
[ -n "$secret_file" ] && [ -f "$secret_file" ] && [ ! -L "$secret_file" ]
[ "$(stat -c '%a' "$secret_file")" = 644 ]
[ "$(stat -c '%a' "${secret_file%/*}")" = 700 ]
[ "$(stat -c '%a' "$supplemental_env_file")" = 600 ]
[ "$(cat "$secret_file")" = "$BILLWATCH_TEST_EXPECTED_RESTIC_PASSWORD" ]
database_secret_file="$(sed -n 's/^BILLWATCH_RECOVERY_DATABASE_PGPASS_FILE=//p' "$supplemental_env_file")"
[ -n "$database_secret_file" ] && [ -f "$database_secret_file" ] && [ ! -L "$database_secret_file" ]
[ "$(stat -c '%a' "$database_secret_file")" = 644 ]
[ "$(stat -c '%a' "${database_secret_file%/*}")" = 700 ]
[ "$(cat "$database_secret_file")" = 'restore-database:5432:*:billwatch:ci\\path\:isolated-restore-password' ]

aws_secret_file="$(sed -n 's/^BILLWATCH_RECOVERY_AWS_CREDENTIALS_FILE=//p' "$supplemental_env_file")"
[ -n "$aws_secret_file" ] && [ -f "$aws_secret_file" ] && [ ! -L "$aws_secret_file" ] || { echo "temporary AWS credentials file is missing or unsafe." >&2; exit 1; }
[ "$(stat -c '%a' "$aws_secret_file")" = 644 ] || { echo "temporary AWS credentials file mode is not 644." >&2; exit 1; }
[ "$(stat -c '%a' "${aws_secret_file%/*}")" = 700 ] || { echo "temporary AWS credentials directory mode is not 700." >&2; exit 1; }
[ "$(cat "$aws_secret_file")" = "$BILLWATCH_TEST_EXPECTED_AWS_CREDENTIALS" ] || { echo "temporary AWS credentials content mismatch." >&2; exit 1; }
printf '%s\n' "$original_args" >> "$BILLWATCH_TEST_DOCKER_LOG"
printf '%s\n' "$secret_file" "$database_secret_file" "$aws_secret_file" >> "$BILLWATCH_TEST_SECRET_FILE_LOG"
EOF
chmod 755 "$fake_bin/docker"

aws_credentials_file="$temp_dir/recovery-aws-credentials"
aws_credentials_expected='[default]
aws_access_key_id=ci-read-only-access-key
aws_secret_access_key=ci-read-only-secret-key'
printf '%s' "$aws_credentials_expected" > "$aws_credentials_file"
chmod 600 "$aws_credentials_file"
head_sha=$(git -C "$root_dir" rev-parse HEAD)

write_env()
{
    allow_value=$1
    repository_value=$2
    release_value=$3
    credentials_value=${4:-$aws_credentials_file}

    cat > "$env_file" <<EOF
BILLWATCH_RECOVERY_DRILL_ALLOW=$allow_value
BILLWATCH_RELEASE_ID=$release_value
RESTIC_REPOSITORY=$repository_value
BILLWATCH_RECOVERY_AWS_CREDENTIALS_FILE=$credentials_value
RESTIC_PASSWORD=ci-recovery-password-with-32-characters
BILLWATCH_DATABASE_PASSWORD=ci\path:isolated-restore-password
EOF
    chmod 600 "$env_file"
}

run_runner()
{
    PATH="$fake_bin:$PATH" BILLWATCH_TEST_DOCKER_LOG="$docker_log" BILLWATCH_TEST_SECRET_FILE_LOG="$secret_file_log" BILLWATCH_TEST_EXPECTED_RESTIC_PASSWORD=ci-recovery-password-with-32-characters BILLWATCH_TEST_EXPECTED_AWS_CREDENTIALS="$aws_credentials_expected" sh "$runner" "$env_file"
}

write_env false 's3:https://backup.example.invalid/billwatch' "$head_sha"
if run_runner >/dev/null 2>&1; then
    fail "recovery drill ran without explicit mutation/restore opt-in."
fi

write_env true '/repository' "$head_sha"
if run_runner >/dev/null 2>&1; then
    fail "recovery drill accepted a local Restic repository."
fi

write_env true 's3:https://backup.example.invalid/billwatch' '0000000000000000000000000000000000000000'
if run_runner >/dev/null 2>&1; then
    fail "recovery drill accepted a release that does not match the checkout."
fi

write_env true 's3:https://backup.example.invalid/billwatch' "$head_sha"
chmod 644 "$aws_credentials_file"
if run_runner >/dev/null 2>&1; then
    fail "recovery drill accepted a world-readable AWS credential file."
fi
chmod 600 "$aws_credentials_file"

write_env true 's3:https://backup.example.invalid/billwatch' "$head_sha" "$temp_dir/missing-credentials"
if run_runner >/dev/null 2>&1; then
    fail "recovery drill accepted a missing AWS credential file."
fi

chmod 644 "$env_file"
if run_runner >/dev/null 2>&1; then
    fail "recovery drill accepted a world-readable recovery environment file."
fi

write_env true 's3:https://backup.example.invalid/billwatch' "$head_sha"
: > "$docker_log"
: > "$secret_file_log"
runner_output_file="$temp_dir/valid-runner-output.log"
if ! run_runner >"$runner_output_file" 2>&1; then
    cat "$runner_output_file" >&2
    fail "valid isolated recovery drill configuration was rejected."
fi

if grep -Fq 'compose.production.yml' "$docker_log"; then
    fail "recovery drill invoked the production compose topology."
fi

grep -Fq "$compose_file" "$docker_log" || fail "recovery drill did not use the isolated compose topology."
grep -Fq 'up --detach --wait restore-database' "$docker_log" || fail "recovery drill did not start the isolated PostgreSQL restore target."
grep -Fq 'run --rm --no-deps verifier verify' "$docker_log" || fail "recovery drill did not run encrypted snapshot verification."
grep -Fq 'down --volumes --remove-orphans' "$docker_log" || fail "recovery drill did not clean up isolated state."

while IFS= read -r secret_file
do
    [ -z "$secret_file" ] || [ ! -e "$secret_file" ] || fail "temporary recovery secret file was not removed after the drill."
done < "$secret_file_log"

sh "$root_dir/deploy/tests/private-beta-technical-evidence-tests.sh" || fail "private-beta technical evidence regression suite failed."

printf '%s\n' 'Clean-host recovery drill tests passed.'
