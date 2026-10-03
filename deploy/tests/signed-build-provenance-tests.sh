#!/usr/bin/env bash
set -euo pipefail

workflow=".github/workflows/ci.yml"
test -f "$workflow"

grep -Fq 'id-token: write' "$workflow"
grep -Fq 'attestations: write' "$workflow"
grep -Fq 'actions/attest-build-provenance@4d101475d8b20a2381f78447822ac1eab6504dd8' "$workflow"
grep -Fq 'subject-path: provenance/*.tar' "$workflow"
grep -Fq 'docker save billwatch-api:${{ github.sha }} -o provenance/billwatch-api.tar' "$workflow"
grep -Fq 'docker save billwatch-parser-worker:${{ github.sha }} -o provenance/billwatch-parser-worker.tar' "$workflow"
grep -Fq 'docker save billwatch-web:${{ github.sha }} -o provenance/billwatch-web.tar' "$workflow"
grep -Fq 'anchore/sbom-action@da167eac915b4e86f08b264dbdbc867b61be6f0c' "$workflow"
grep -Fq 'actions/attest-sbom@4651f806c01d8637787e274ac3bdf724ef169f34' "$workflow"
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
