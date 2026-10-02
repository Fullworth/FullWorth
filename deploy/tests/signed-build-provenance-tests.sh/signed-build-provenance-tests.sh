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
grep -Fq "github.ref == 'refs/heads/master'" "$workflow"
grep -Fq 'retention-days: 7' "$workflow"

echo "Signed production-image provenance gate is configured and pinned."
