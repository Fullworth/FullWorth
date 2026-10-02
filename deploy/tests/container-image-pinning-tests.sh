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
    [[ "$image" =~ ^[^[:space:]]+@sha256:[0-9a-f]{64}$ ]] ||       fail "$file contains an unpinned or malformed FROM image: $image"
  done < <(sed -nE 's/^FROM[[:space:]]+([^[:space:]]+).*/\1/p' "$file")
}

require_digest_pinned_from Dockerfile
require_digest_pinned_from Dockerfile.web

while IFS= read -r image; do
  case "$image" in
    billwatch-*)
      continue
      ;;
  esac

  [[ "$image" =~ ^[^[:space:]]+@sha256:[0-9a-f]{64}$ ]] ||     fail "compose.production.yml contains an unpinned or malformed external image: $image"
done < <(sed -nE 's/^[[:space:]]+image:[[:space:]]*([^[:space:]]+).*/\1/p' compose.production.yml)

printf '%s\n' 'All production Dockerfile and external Compose images are digest-pinned.'
