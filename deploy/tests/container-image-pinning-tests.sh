#!/usr/bin/env bash
set -euo pipefail

fail() {
  printf 'container-image-pinning: %s\n' "$1" >&2
  exit 1
}

require_digest_pinned_from() {
  local file="$1"
  local image

  while IFS= read -r image; do
    if ! [[ "$image" =~ ^[^[:space:]]+@sha256:[0-9a-f]{64}$ ]]; then
      fail "$file contains an unpinned or malformed FROM image: $image"
    fi
  done < <(sed -nE 's/^FROM[[:space:]]+([^[:space:]]+).*/\1/p' "$file")
}

require_digest_pinned_from Dockerfile
require_digest_pinned_from Dockerfile.web

for compose_file in compose.production.yml compose.recovery-drill.yml; do
  while IFS= read -r image; do
    case "$image" in
      billwatch-*)
        continue
        ;;
    esac

    if ! [[ "$image" =~ ^[^[:space:]]+@sha256:[0-9a-f]{64}$ ]]; then
      fail "$compose_file contains an unpinned or malformed external image: $image"
    fi
  done < <(sed -nE 's/^[[:space:]]+image:[[:space:]]*([^[:space:]]+).*/\1/p' "$compose_file")
done

printf '%s\n' 'All production Dockerfile and external Compose images are digest-pinned.'
