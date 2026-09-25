#!/bin/sh

set -eu

web_base_url="${1:-}"
foreign_email="${BILLWATCH_WEB_OWNERSHIP_FOREIGN_EMAIL:-}"
foreign_password_file="${BILLWATCH_WEB_OWNERSHIP_FOREIGN_PASSWORD_FILE:-}"
foreign_two_factor_code_file="${BILLWATCH_WEB_OWNERSHIP_FOREIGN_TWO_FACTOR_CODE_FILE:-}"
foreign_recovery_code_file="${BILLWATCH_WEB_OWNERSHIP_FOREIGN_RECOVERY_CODE_FILE:-}"
primary_email="${BILLWATCH_WEB_SMOKE_EMAIL:-}"
smoke_script="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)/smoke-web-bff.sh"

fail()
{
    printf '%s\n' "$1" >&2
    exit "${2:-1}"
}

require_https_url()
{
    value="$1"
    label="$2"

    case "$value" in
        https://*) ;;
        *) fail "$label must use HTTPS." 64 ;;
    esac

    case "$value" in
        *[[:space:]]*) fail "$label must not contain whitespace." 64 ;;
    esac
}

require_secret_file()
{
    path="$1"
    label="$2"

    [ -f "$path" ] || fail "$label must reference a regular file." 64
    [ ! -L "$path" ] || fail "$label must not be a symbolic link." 64

    mode="$(stat -c '%a' "$path" 2>/dev/null || true)"
    [ "$mode" = "600" ] || fail "$label must have mode 600." 64
}

require_uuid()
{
    value="$1"
    label="$2"

    printf '%s\n' "$value" |
        grep -Eq '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$' ||
        fail "$label was not a canonical UUID." 70
}

json_escape()
{
    printf '%s' "$1" |
        sed 's/\\/\\\\/g; s/"/\\"/g; s/\t/\\t/g'
}

[ -n "$web_base_url" ] || fail "Usage: $0 <https-web-base-url>" 64
require_https_url "$web_base_url" "The Web/BFF ownership smoke-test base URL"
web_base_url="${web_base_url%/}"

[ -f "$smoke_script" ] || fail "The authenticated Web/BFF smoke harness is missing: $smoke_script" 66
[ -n "$primary_email" ] || fail "BILLWATCH_WEB_SMOKE_EMAIL is required for the primary account." 64
[ -n "$foreign_email" ] || fail "BILLWATCH_WEB_OWNERSHIP_FOREIGN_EMAIL is required." 64
[ -n "$foreign_password_file" ] || fail "BILLWATCH_WEB_OWNERSHIP_FOREIGN_PASSWORD_FILE is required." 64
require_secret_file "$foreign_password_file" "BILLWATCH_WEB_OWNERSHIP_FOREIGN_PASSWORD_FILE"

primary_email_normalized="$(printf '%s' "$primary_email" | tr '[:upper:]' '[:lower:]')"
foreign_email_normalized="$(printf '%s' "$foreign_email" | tr '[:upper:]' '[:lower:]')"
[ "$primary_email_normalized" != "$foreign_email_normalized" ] ||
    fail "The primary and foreign ownership-smoke accounts must be different identities." 64
unset primary_email_normalized foreign_email_normalized

if [ -n "$foreign_two_factor_code_file" ] && [ -n "$foreign_recovery_code_file" ]; then
    fail "Set only one foreign second-factor credential file." 64
fi
if [ -n "$foreign_two_factor_code_file" ]; then
    require_secret_file "$foreign_two_factor_code_file" "BILLWATCH_WEB_OWNERSHIP_FOREIGN_TWO_FACTOR_CODE_FILE"
fi
if [ -n "$foreign_recovery_code_file" ]; then
    require_secret_file "$foreign_recovery_code_file" "BILLWATCH_WEB_OWNERSHIP_FOREIGN_RECOVERY_CODE_FILE"
    fail "The ownership smoke derives its fixture through account export strong reauthentication, which requires a current authenticator code. Use BILLWATCH_WEB_OWNERSHIP_FOREIGN_TWO_FACTOR_CODE_FILE instead of a recovery code." 64
fi

work_directory="$(mktemp -d)"
chmod 700 "$work_directory"
trap 'rm -rf "$work_directory"' EXIT HUP INT TERM

cookie_jar="$work_directory/foreign-cookies.txt"
login_page="$work_directory/foreign-login.html"
login_headers="$work_directory/foreign-login-headers.txt"
antiforgery_token_file="$work_directory/foreign-antiforgery-token.txt"
bff_antiforgery_response="$work_directory/foreign-bff-antiforgery.json"
bff_security_config="$work_directory/foreign-bff-security.curl"
export_payload="$work_directory/foreign-account-export-request.json"
export_response="$work_directory/foreign-account-export.json"

: > "$cookie_jar"
chmod 600 "$cookie_jar"

fetch_login_antiforgery()
{
    path="$1"
    code="$(
        curl \
            --silent \
            --show-error \
            --output "$login_page" \
            --write-out '%{http_code}' \
            --cookie "$cookie_jar" \
            --cookie-jar "$cookie_jar" \
            "$web_base_url$path"
    )"
    [ "$code" = "200" ] || fail "Unable to load the foreign-account login form; received HTTP $code." 69

    token="$(
        sed -n \
            's/.*name="__RequestVerificationToken"[^>]*value="\([^"]*\)".*/\1/p' \
            "$login_page" |
            head -n 1
    )"
    [ -n "$token" ] || fail "The foreign-account login page did not expose an antiforgery token." 70
    printf '%s' "$token" > "$antiforgery_token_file"
    chmod 600 "$antiforgery_token_file"
    unset token
}

post_foreign_login()
{
    second_factor_kind="${1:-}"
    second_factor_file="${2:-}"

    : > "$login_headers"
    set -- \
        --silent \
        --show-error \
        --output /dev/null \
        --dump-header "$login_headers" \
        --write-out '%{http_code}' \
        --request POST \
        --cookie "$cookie_jar" \
        --cookie-jar "$cookie_jar" \
        --header 'Content-Type: application/x-www-form-urlencoded' \
        --data-urlencode "email=$foreign_email" \
        --data-urlencode "password@$foreign_password_file" \
        --data-urlencode "__RequestVerificationToken@$antiforgery_token_file"

    case "$second_factor_kind" in
        authenticator)
            set -- "$@" --data-urlencode 'twoFactor=true' --data-urlencode "twoFactorCode@$second_factor_file"
            ;;
        recovery)
            set -- "$@" --data-urlencode 'twoFactor=true' --data-urlencode "recoveryCode@$second_factor_file"
            ;;
        '') ;;
        *) fail "Unsupported foreign-account second-factor mode." 70 ;;
    esac

    curl "$@" "$web_base_url/auth/login"
}

redirect_location()
{
    sed -n 's/^[Ll]ocation:[[:space:]]*\([^\r]*\)\r*$/\1/p' "$login_headers" |
        tail -n 1
}

fetch_login_antiforgery '/login'
login_code="$(post_foreign_login)"
case "$login_code" in
    302|303) ;;
    *) fail "Foreign-account Web login returned HTTP $login_code instead of a redirect." 69 ;;
esac

authenticated_with_recovery=false
location="$(redirect_location)"
case "$location" in
    /app|/app/*) ;;
    /login\?twoFactor=true*)
        if [ -n "$foreign_two_factor_code_file" ]; then
            second_factor_kind='authenticator'
            second_factor_file="$foreign_two_factor_code_file"
        elif [ -n "$foreign_recovery_code_file" ]; then
            second_factor_kind='recovery'
            second_factor_file="$foreign_recovery_code_file"
            authenticated_with_recovery=true
        else
            fail "The foreign account requires two-factor authentication. Supply a current protected authenticator-code or recovery-code file." 65
        fi

        fetch_login_antiforgery '/login?twoFactor=true'
        login_code="$(post_foreign_login "$second_factor_kind" "$second_factor_file")"
        case "$login_code" in
            302|303) ;;
            *) fail "Foreign-account two-factor login returned HTTP $login_code instead of a redirect." 69 ;;
        esac
        location="$(redirect_location)"
        case "$location" in
            /app|/app/*) ;;
            *) fail "Foreign-account two-factor login did not redirect to /app." 69 ;;
        esac
        ;;
    *) fail "Foreign-account login did not redirect to /app or the two-factor step." 69 ;;
esac

if [ "$authenticated_with_recovery" = "true" ]; then
    fail "The foreign account signed in with a recovery code, but account export strong reauthentication requires a current authenticator code. Supply BILLWATCH_WEB_OWNERSHIP_FOREIGN_TWO_FACTOR_CODE_FILE for this proof." 65
fi

antiforgery_code="$(
    curl \
        --silent \
        --show-error \
        --output "$bff_antiforgery_response" \
        --write-out '%{http_code}' \
        --cookie "$cookie_jar" \
        --cookie-jar "$cookie_jar" \
        "$web_base_url/bff/antiforgery"
)"
[ "$antiforgery_code" = "200" ] ||
    fail "Foreign-account BFF antiforgery endpoint returned HTTP $antiforgery_code." 69

bff_token="$(
    sed -n 's/.*"requestToken"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
        "$bff_antiforgery_response"
)"
rm -f "$bff_antiforgery_response"
[ -n "$bff_token" ] ||
    fail "Foreign-account BFF antiforgery endpoint did not return a request token." 70

printf 'header = "X-CSRF-TOKEN: %s"\n' "$bff_token" > "$bff_security_config"
chmod 600 "$bff_security_config"
unset bff_token

printf '{"currentPassword":"%s"' \
    "$(json_escape "$(cat "$foreign_password_file")")" \
    > "$export_payload"

if [ -n "$foreign_two_factor_code_file" ]; then
    printf ',"twoFactorCode":"%s"' \
        "$(json_escape "$(cat "$foreign_two_factor_code_file")")" \
        >> "$export_payload"
else
    printf ',"twoFactorCode":null' >> "$export_payload"
fi
printf '}' >> "$export_payload"
chmod 600 "$export_payload"

export_code="$(
    curl \
        --silent \
        --show-error \
        --output "$export_response" \
        --write-out '%{http_code}' \
        --request POST \
        --cookie "$cookie_jar" \
        --cookie-jar "$cookie_jar" \
        --header 'Content-Type: application/json' \
        --config "$bff_security_config" \
        --data-binary "@$export_payload" \
        "$web_base_url/bff/account/export"
)"
rm -f "$export_payload"
[ "$export_code" = "200" ] ||
    fail "Foreign-account BFF export returned HTTP $export_code. If this account uses two-factor authentication, supply a current authenticator-code file." 69

foreign_statement_upload_id="$(
    sed -n \
        's/.*"statementUploads"[[:space:]]*:[[:space:]]*\[[[:space:]]*{[[:space:]]*"id"[[:space:]]*:[[:space:]]*"\([0-9a-fA-F-]*\)".*/\1/p' \
        "$export_response" |
        head -n 1
)"
foreign_bill_stream_id="$(
    sed -n \
        's/.*"statementUploads"[[:space:]]*:[[:space:]]*\[[[:space:]]*{[^}]*"billStreamId"[[:space:]]*:[[:space:]]*"\([0-9a-fA-F-]*\)".*/\1/p' \
        "$export_response" |
        head -n 1
)"

[ -n "$foreign_statement_upload_id" ] ||
    fail "The foreign account export contains no statement upload. Seed one controlled foreign-account statement fixture before running the ownership proof." 65
[ -n "$foreign_bill_stream_id" ] ||
    fail "Unable to resolve the foreign statement upload's bill-stream ID from the account export." 70
require_uuid "$foreign_statement_upload_id" "Foreign statement-upload ID"
require_uuid "$foreign_bill_stream_id" "Foreign bill-stream ID"

if grep -Eiq \
    '"(accessToken|refreshToken|protectedAccessToken|encryptedAccessToken|plaidAccessToken|storagePath|storedFilePath|passwordHash|securityStamp)"[[:space:]]*:' \
    "$export_response"; then
    fail "Foreign-account export contained a forbidden secret or internal-storage field." 70
fi
rm -f "$export_response"

printf '%s\n' 'PASS foreign account authenticated and supplied an objectively owned statement fixture'

BILLWATCH_WEB_SMOKE_FOREIGN_BILL_STREAM_ID="$foreign_bill_stream_id" \
BILLWATCH_WEB_SMOKE_FOREIGN_STATEMENT_UPLOAD_ID="$foreign_statement_upload_id" \
    sh "$smoke_script" "$web_base_url"

printf '%s\n' 'PASS primary Web/BFF identity received 404 for foreign bill-stream and statement-upload resources'
printf '%s\n' 'FullWorth cross-user Web/BFF ownership smoke harness passed.'
