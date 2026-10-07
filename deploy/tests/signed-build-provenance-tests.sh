#!/usr/bin/env bash
set -euo pipefail

workflow=".github/workflows/ci.yml"
test -f "$workflow"

grep -Fq 'id-token: write' "$workflow"
grep -Fq 'attestations: write' "$workflow"

is_sha_pinned_action_reference()
{
  local action="$1"
  local reference="$2"
  [[ "$reference" =~ ^${action}@[0-9a-f]{40}$ ]]
}

assert_sha_pinned_action_references()
{
  local action="$1"
  local expected_count="$2"
  local reference
  local -a references=()

  mapfile -t references < <(
    grep -E "^[[:space:]]*uses:[[:space:]]*${action}@" "$workflow" |
      sed -E 's/^[[:space:]]*uses:[[:space:]]*//; s/[[:space:]]+#.*$//'
  )

  if [ "${#references[@]}" -ne "$expected_count" ]; then
    printf 'Expected %s SHA-pinned uses of %s, found %s.\\n' \
      "$expected_count" "$action" "${#references[@]}" >&2
    return 1
  fi

  for reference in "${references[@]}"; do
    if ! is_sha_pinned_action_reference "$action" "$reference"; then
      printf 'Action reference for %s must use a 40-character immutable commit SHA.\\n' \
        "$action" >&2
      return 1
    fi
  done
}

# The global workflow pinning test enforces immutable SHA references. This test
# also checks the required provenance actions/counts without freezing their SHAs,
# so a reviewed Dependabot version bump does not break the provenance contract.
assert_sha_pinned_action_references actions/attest-build-provenance 1
assert_sha_pinned_action_references anchore/sbom-action 3
assert_sha_pinned_action_references actions/attest-sbom 3

if is_sha_pinned_action_reference actions/attest-sbom actions/attest-sbom@v4.1.0; then
  echo "Signed provenance validation accepted a mutable semantic-version tag." >&2
  exit 1
fi
if ! is_sha_pinned_action_reference actions/attest-sbom actions/attest-sbom@4651f806c01d8637787e274ac3bdf724ef169f34; then
  echo "Signed provenance validation rejected an immutable 40-character commit pin." >&2
  exit 1
fi

grep -Fq 'subject-path: provenance/*.tar' "$workflow"
grep -Fq 'docker save billwatch-api:${{ github.sha }} -o provenance/billwatch-api.tar' "$workflow"
grep -Fq 'docker save billwatch-parser-worker:${{ github.sha }} -o provenance/billwatch-parser-worker.tar' "$workflow"
grep -Fq 'docker save billwatch-web:${{ github.sha }} -o provenance/billwatch-web.tar' "$workflow"
grep -Fq 'image: billwatch-api:${{ github.sha }}' "$workflow"
grep -Fq 'image: billwatch-parser-worker:${{ github.sha }}' "$workflow"
grep -Fq 'image: billwatch-web:${{ github.sha }}' "$workflow"
test "$(grep -Fc 'syft-version: v1.54.0' "$workflow")" -eq 3
grep -Fq 'sbom-path: provenance/billwatch-api.spdx.json' "$workflow"
grep -Fq 'sbom-path: provenance/billwatch-parser-worker.spdx.json' "$workflow"
grep -Fq 'sbom-path: provenance/billwatch-web.spdx.json' "$workflow"
grep -Fq 'provenance/*.spdx.json' "$workflow"
grep -Fq "github.ref == 'refs/heads/master'" "$workflow"
grep -Fq 'retention-days: 7' "$workflow"

echo "Signed production-image provenance and SBOM gates are configured and pinned."
