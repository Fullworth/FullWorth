#!/bin/sh

set -eu

umask 077

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
env_file=${1:-"$root_dir/.env.recovery"}
compose_file="$root_dir/compose.recovery-drill.yml"
project_name="billwatch-recovery-drill-$$"
started=false
secret_dir=
compose_env_file=

fail()
{
    printf '%s\n' "Recovery drill refused: $1" >&2
    exit 64
}

read_required_env_value()
{
    key=$1
    file=$2

    value=$(awk -v key="$key" '
        BEGIN { count = 0 }
        index($0, key "=") == 1 {
            count++
            value = substr($0, length(key) + 2)
            sub(/\r$/, "", value)
        }
        END {
            if (count != 1 || value == "") exit 65
            print value
        }
    ' "$file") || fail "$key must appear exactly once with a non-empty value in the protected recovery environment file."

    printf '%s' "$value"
}

cleanup()
{
    exit_code=$?
    trap - EXIT HUP INT TERM

    if [ "$started" = true ]; then
        docker compose \
            --project-name "$project_name" \
            --env-file "$env_file" \
            --env-file "$compose_env_file" \
            --file "$compose_file" \
            down --volumes --remove-orphans >/dev/null 2>&1 || true
    fi

    if [ -n "$secret_dir" ]; then
        rm -rf "$secret_dir"
    fi

    exit "$exit_code"
}

trap cleanup EXIT
trap 'exit 130' HUP INT TERM

[ -f "$compose_file" ] || fail "the isolated recovery compose file is missing."
[ -f "$env_file" ] || fail "the protected recovery environment file is missing: $env_file"
[ ! -L "$env_file" ] || fail "the recovery environment file must not be a symbolic link."

mode=$(stat -c '%a' "$env_file")
[ "$mode" = 600 ] || fail "the recovery environment file must have mode 600."

owner_uid=$(stat -c '%u' "$env_file")
[ "$owner_uid" = "$(id -u)" ] || fail "the recovery environment file must be owned by the current deployment operator."

case "$env_file" in
    "$root_dir"/*)
        relative_env=${env_file#"$root_dir"/}
        git -C "$root_dir" check-ignore -q -- "$relative_env" || fail "a repository-local recovery environment file must be Git-ignored."
        if git -C "$root_dir" ls-files --error-unmatch -- "$relative_env" >/dev/null 2>&1; then
            fail "the recovery environment file must never be tracked by Git."
        fi
        ;;
esac

[ -z "$(git -C "$root_dir" status --porcelain --untracked-files=normal)" ] || fail "the recovery drill requires a clean Git checkout."

release_id=$(read_required_env_value BILLWATCH_RELEASE_ID "$env_file")
allow_drill=$(read_required_env_value BILLWATCH_RECOVERY_DRILL_ALLOW "$env_file")
repository=$(read_required_env_value RESTIC_REPOSITORY "$env_file")

[ "$allow_drill" = true ] || fail "set BILLWATCH_RECOVERY_DRILL_ALLOW=true explicitly before running a clean-host recovery drill."

case "$release_id" in
    [0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]) ;;
    *) fail "BILLWATCH_RELEASE_ID must be an exact lowercase 40-character Git commit SHA." ;;
esac

head_sha=$(git -C "$root_dir" rev-parse HEAD)
[ "$head_sha" = "$release_id" ] || fail "BILLWATCH_RELEASE_ID must match the clean-host checkout exactly."

case "$repository" in
    s3:*|b2:*|azure:*|gs:*|rclone:*|rest:*|sftp:*|swift:*) ;;
    *) fail "the recovery drill requires an explicitly off-host Restic repository; local repository paths are refused." ;;
esac

aws_credentials_file=$(read_required_env_value BILLWATCH_RECOVERY_AWS_CREDENTIALS_FILE "$env_file")
case "$aws_credentials_file" in
    /*) ;;
    *) fail "BILLWATCH_RECOVERY_AWS_CREDENTIALS_FILE must be an absolute path to a separate recovery credential file." ;;
esac
[ -f "$aws_credentials_file" ] && [ ! -L "$aws_credentials_file" ] ||
    fail "the recovery AWS credential must be a regular, non-symlink file."
aws_credentials_mode=$(stat -c '%a' "$aws_credentials_file")
[ "$aws_credentials_mode" = 600 ] ||
    fail "the recovery AWS credential file must have mode 600."
aws_credentials_owner=$(stat -c '%u' "$aws_credentials_file")
[ "$aws_credentials_owner" = "$(id -u)" ] ||
    fail "the recovery AWS credential file must be owned by the current deployment operator."
case "$aws_credentials_file" in
    "$root_dir"/*)
        relative_aws_credentials=${aws_credentials_file#"$root_dir"/}
        git -C "$root_dir" check-ignore -q -- "$relative_aws_credentials" ||
            fail "a repository-local recovery AWS credential file must be Git-ignored."
        if git -C "$root_dir" ls-files --error-unmatch -- "$relative_aws_credentials" >/dev/null 2>&1; then
            fail "the recovery AWS credential file must never be tracked by Git."
        fi
        ;;
esac


if ! awk '
    function trim(value) {
        sub(/^[ \t]+/, "", value)
        sub(/[ \t]+$/, "", value)
        return value
    }
    BEGIN {
        profile = ""
        default_sections = 0
        access_key_count = 0
        secret_key_count = 0
        invalid_value = 0
    }
    {
        line = $0
        sub(/\r$/, "", line)
        trimmed = trim(line)
        if (trimmed == "" || trimmed ~ /^[#;]/) next
        if (trimmed ~ /^\[[^]]+\]$/) {
            profile = substr(trimmed, 2, length(trimmed) - 2)
            if (profile == "default") default_sections++
            next
        }
        if (profile != "default") next
        equals = index(trimmed, "=")
        if (equals == 0) next
        key = trim(substr(trimmed, 1, equals - 1))
        value = trim(substr(trimmed, equals + 1))
        if (key == "aws_access_key_id") {
            access_key_count++
            if (value == "") invalid_value = 1
        }
        if (key == "aws_secret_access_key") {
            secret_key_count++
            if (value == "") invalid_value = 1
        }
    }
    END {
        if (default_sections != 1 || access_key_count != 1 ||
            secret_key_count != 1 || invalid_value) exit 1
    }
' "$aws_credentials_file"; then
    fail "the recovery AWS credentials file must contain one [default] profile with non-empty access and secret keys."
fi

restic_password=$(read_required_env_value RESTIC_PASSWORD "$env_file")
database_password=$(read_required_env_value BILLWATCH_DATABASE_PASSWORD "$env_file")

secret_dir=$(mktemp -d "${TMPDIR:-/tmp}/fullworth-recovery-secret.XXXXXX") || fail "a private temporary recovery-secret directory could not be created."
chmod 700 "$secret_dir"
restic_password_file="$secret_dir/restic_password"
database_pgpass_file="$secret_dir/database_pgpass"
aws_credentials_secret_file="$secret_dir/aws_credentials"
compose_env_file="$secret_dir/compose.env"
printf '%s' "$restic_password" > "$restic_password_file"
cat "$aws_credentials_file" > "$aws_credentials_secret_file"
pgpass_password=$(printf '%s' "$database_password" | sed 's/\\/\\\\/g; s/:/\\:/g')
printf 'restore-database:5432:*:billwatch:%s\n' "$pgpass_password" > "$database_pgpass_file"
unset restic_password database_password pgpass_password
printf 'BILLWATCH_RECOVERY_RESTIC_PASSWORD_FILE=%s\nBILLWATCH_RECOVERY_DATABASE_PGPASS_FILE=%s\nBILLWATCH_RECOVERY_AWS_CREDENTIALS_FILE=%s\n' "$restic_password_file" "$database_pgpass_file" "$aws_credentials_secret_file" > "$compose_env_file"
chmod 644 "$restic_password_file" "$database_pgpass_file" "$aws_credentials_secret_file"
chmod 600 "$compose_env_file"

command -v docker >/dev/null 2>&1 || fail "Docker is required on the clean recovery host."
docker compose version >/dev/null 2>&1 || fail "Docker Compose v2 is required on the clean recovery host."

started=true

docker compose \
    --project-name "$project_name" \
    --env-file "$env_file" \
    --env-file "$compose_env_file" \
    --file "$compose_file" \
    build verifier

docker compose \
    --project-name "$project_name" \
    --env-file "$env_file" \
    --env-file "$compose_env_file" \
    --file "$compose_file" \
    up --detach --wait restore-database

docker compose \
    --project-name "$project_name" \
    --env-file "$env_file" \
    --env-file "$compose_env_file" \
    --file "$compose_file" \
    run --rm --no-deps verifier verify

echo "Clean-host encrypted recovery drill passed for release $release_id."
