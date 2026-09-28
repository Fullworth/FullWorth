#!/bin/sh

set -eu

fail()
{
    printf '%s\n' "Approved AI model verification failed: $1" >&2
    exit 64
}

mode=${1:-full}
env_file=${2:-.env.ai}

case "$mode" in
    full|metadata-only) ;;
    *) fail "mode must be full or metadata-only." ;;
esac

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
manifest_file="$script_dir/ai-models/qwen3-4b-q4_k_m.manifest"

[ -f "$manifest_file" ] ||
    fail "approved model manifest is missing."

[ -f "$env_file" ] ||
    fail "environment file is missing."

read_value()
{
    file=$1
    key=$2

    count=$(awk -F= -v key="$key" '
        $1 == key { count++ }
        END { print count + 0 }
    ' "$file")

    [ "$count" -eq 1 ] ||
        fail "$key must appear exactly once in $file."

    awk -v prefix="$key=" '
        index($0, prefix) == 1 {
            print substr($0, length(prefix) + 1)
            exit
        }
    ' "$file"
}

format_version=$(read_value "$manifest_file" FORMAT_VERSION)
model_id=$(read_value "$manifest_file" MODEL_ID)
runtime_alias=$(read_value "$manifest_file" RUNTIME_ALIAS)
upstream_repository=$(read_value "$manifest_file" UPSTREAM_REPOSITORY)
upstream_revision=$(read_value "$manifest_file" UPSTREAM_REVISION)
filename=$(read_value "$manifest_file" FILENAME)
approved_sha256=$(read_value "$manifest_file" SHA256)
approved_size=$(read_value "$manifest_file" SIZE_BYTES)
license=$(read_value "$manifest_file" LICENSE)
source_url=$(read_value "$manifest_file" SOURCE_URL)

[ "$format_version" = "fullworth-ai-model-manifest-v1" ] ||
    fail "unsupported manifest format."

[ "$model_id" = "qwen3-4b-q4-k-m" ] ||
    fail "unexpected approved model id."

[ "$runtime_alias" = "fullworth-local" ] ||
    fail "unexpected runtime alias."

[ "$upstream_repository" = "Qwen/Qwen3-4B-GGUF" ] ||
    fail "unexpected upstream repository."

printf '%s\n' "$upstream_revision" |
    grep -Eq '^[0-9a-f]{40}$' ||
    fail "upstream revision must be a full lowercase commit hash."

[ "$filename" = "Qwen3-4B-Q4_K_M.gguf" ] ||
    fail "unexpected approved model filename."

printf '%s\n' "$approved_sha256" |
    grep -Eq '^[0-9a-f]{64}$' ||
    fail "approved SHA-256 is malformed."

case "$approved_size" in
    ''|*[!0-9]*)
        fail "approved model size must be an integer."
        ;;
esac

[ "$approved_size" -gt 0 ] ||
    fail "approved model size must be positive."

[ "$license" = "Apache-2.0" ] ||
    fail "unexpected model license identifier."

expected_source_url="https://huggingface.co/$upstream_repository/resolve/$upstream_revision/$filename?download=true"

[ "$source_url" = "$expected_source_url" ] ||
    fail "source URL does not match the pinned repository, revision, and filename."

configured_sha256=$(read_value "$env_file" FULLWORTH_LOCAL_AI_MODEL_SHA256)
configured_alias=$(read_value "$env_file" FULLWORTH_LOCAL_AI_MODEL_ALIAS)

[ "$configured_sha256" = "$approved_sha256" ] ||
    fail "configured model SHA-256 does not match the approved artifact."

[ "$configured_alias" = "$runtime_alias" ] ||
    fail "configured model alias does not match the approved runtime alias."

if [ "$mode" = "metadata-only" ]; then
    printf '%s\n' "Approved AI model metadata valid."
    exit 0
fi

model_path=$(read_value "$env_file" FULLWORTH_LOCAL_AI_MODEL_PATH)

case "$model_path" in
    /*) ;;
    *) fail "configured model path must be absolute." ;;
esac

[ -f "$model_path" ] ||
    fail "approved model file is missing."

[ ! -L "$model_path" ] ||
    fail "approved model file must not be a symbolic link."

actual_size=$(stat -c '%s' "$model_path") ||
    fail "approved model size could not be read."

[ "$actual_size" = "$approved_size" ] ||
    fail "model file size does not match the approved artifact."

command -v sha256sum >/dev/null 2>&1 ||
    fail "sha256sum is required."

actual_sha256=$(
    sha256sum "$model_path" |
    awk '{ print $1 }'
) || fail "model SHA-256 could not be calculated."

[ "$actual_sha256" = "$approved_sha256" ] ||
    fail "model SHA-256 does not match the approved artifact."

printf '%s\n' "Approved AI model verified."
