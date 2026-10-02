#!/bin/sh
set -eu

repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
workflow_dir="$repo_root/.github/workflows"
failed=0

for workflow in "$workflow_dir"/*.yml "$workflow_dir"/*.yaml; do
  [ -f "$workflow" ] || continue
  line_number=0

  while IFS= read -r line || [ -n "$line" ]; do
    line_number=$((line_number + 1))
    value=$(printf '%s\n' "$line" | sed -n 's/^[[:space:]]*uses:[[:space:]]*//p')
    [ -n "$value" ] || continue

    value=$(printf '%s\n' "$value" |
      sed -e 's/[[:space:]]*#.*$//'           -e 's/[[:space:]]*$//'           -e "s/^[\"']//"           -e "s/[\"']$//")

    case "$value" in
      ./*)
        continue
        ;;
    esac

    if printf '%s\n' "$value" |
      grep -Eq '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(/[^[:space:]@]+)*@[0-9a-f]{40}$'; then
      continue
    fi

    if printf '%s\n' "$value" |
      grep -Eq '^docker://[^[:space:]@]+@sha256:[0-9a-f]{64}$'; then
      continue
    fi

    printf '%s:%s: external action is not pinned to an immutable digest: %s\n'       "${workflow#"$repo_root"/}" "$line_number" "$value" >&2
    failed=1
  done < "$workflow"
done

if [ "$failed" -ne 0 ]; then
  exit 1
fi

printf '%s\n' "All external workflow actions are pinned to immutable commits or digests."
