#!/bin/sh
set -eu

repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
failed=0

check_digest_reference()
{
    source_file=$1
    line_number=$2
    reference=$3

    case "$reference" in
        *@sha256:????????????????????????????????????????????????????????????????)
            digest=${reference##*@sha256:}
            if printf '%s\n' "$digest" | grep -Eq '^[0-9a-f]{64}$'; then
                return
            fi
            ;;
    esac

    printf '%s:%s: external container image is not pinned to a sha256 digest: %s\n' \
        "${source_file#"$repo_root"/}" "$line_number" "$reference" >&2
    failed=1
}

for dockerfile in \
    "$repo_root/Dockerfile" \
    "$repo_root/Dockerfile.web" \
    "$repo_root/deploy/backup/Dockerfile"
do
    [ -f "$dockerfile" ] || {
        printf '%s: required Dockerfile is missing.\n' "${dockerfile#"$repo_root"/}" >&2
        failed=1
        continue
    }

    line_number=0
    while IFS= read -r line || [ -n "$line" ]; do
        line_number=$((line_number + 1))

        syntax=$(printf '%s\n' "$line" |
            sed -n 's/^[[:space:]]*#[[:space:]]*syntax=[[:space:]]*//p')
        if [ -n "$syntax" ]; then
            check_digest_reference "$dockerfile" "$line_number" "$syntax"
        fi

        reference=$(printf '%s\n' "$line" |
            sed -n 's/^[[:space:]]*FROM[[:space:]][[:space:]]*\([^[:space:]]*\).*/\1/p')
        [ -n "$reference" ] || continue

        case "$reference" in
            $*)
                printf '%s:%s: variable Dockerfile base images are not allowed: %s\n' \
                    "${dockerfile#"$repo_root"/}" "$line_number" "$reference" >&2
                failed=1
                ;;
            *)
                check_digest_reference "$dockerfile" "$line_number" "$reference"
                ;;
        esac
    done < "$dockerfile"
done

for compose_file in \
    "$repo_root/compose.production.yml" \
    "$repo_root/compose.recovery-drill.yml"
do
    [ -f "$compose_file" ] || {
        printf '%s: required Compose file is missing.\n' "${compose_file#"$repo_root"/}" >&2
        failed=1
        continue
    }

    line_number=0
    while IFS= read -r line || [ -n "$line" ]; do
        line_number=$((line_number + 1))
        reference=$(printf '%s\n' "$line" |
            sed -n 's/^[[:space:]]*image:[[:space:]][[:space:]]*//p')
        [ -n "$reference" ] || continue

        reference=$(printf '%s\n' "$reference" |
            sed -e 's/[[:space:]]*#.*$//' -e 's/[[:space:]]*$//')

        case "$reference" in
            billwatch-*)
                continue
                ;;
            *)
                check_digest_reference "$compose_file" "$line_number" "$reference"
                ;;
        esac
    done < "$compose_file"
done

if [ "$failed" -ne 0 ]; then
    exit 1
fi

printf '%s\n' "All production and recovery container images are pinned to immutable sha256 digests."
