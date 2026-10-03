#!/bin/sh

set -eu

repository_root="$(CDPATH= cd -- "$(dirname "$0")/../.." && pwd -P)"
compose_file="$repository_root/compose.production.yml"
dockerfile="$repository_root/Dockerfile"
verifier="$repository_root/deploy/verify-parser-containment.sh"
review="$repository_root/PARSER_NATIVE_ATTACK_SURFACE.md"

fail()
{
    printf '%s\n' "Parser native boundary regression failed: $1" >&2
    exit 1
}

for file in "$compose_file" "$dockerfile" "$verifier" "$review"
do
    [ -f "$file" ] || fail "required file is missing"
done

parser_service="$(
    awk '
        /^  parser-worker:$/ { capture = 1 }
        capture && /^  web:$/ { exit }
        capture { print }
    ' "$compose_file"
)"

require_parser()
{
    printf '%s\n' "$parser_service" | grep -Eq "$1" ||
        fail "$2"
}

require_parser '^    init: true$' "parser-worker must use a minimal init"
require_parser '^    read_only: true$' "parser-worker root must be read-only"
require_parser '^    pids_limit: 64$' "parser-worker PID ceiling changed"
require_parser '^    cpus: ' "parser-worker CPU ceiling is missing"
require_parser '^    mem_limit: ' "parser-worker memory ceiling is missing"
require_parser '^    memswap_limit: ' "parser-worker swap ceiling is missing"
require_parser '^      core: 0$' "parser-worker core dumps must be disabled"
require_parser '^        soft: 512$' "parser-worker soft nofile ceiling changed"
require_parser '^        hard: 512$' "parser-worker hard nofile ceiling changed"
require_parser '^      - ALL$' "parser-worker must drop all capabilities"
require_parser '^      - no-new-privileges:true$' "no-new-privileges is missing"
require_parser '^      DOTNET_EnableDiagnostics: "0"$' "runtime diagnostics must remain disabled"
require_parser '^      - /tmp:rw,noexec,nosuid,nodev,size=64m,uid=1654,gid=1654,mode=0700$' "parser-worker temporary storage boundary changed"
require_parser '^      - parser_worker$' "parser-worker network attachment changed"

if printf '%s\n' "$parser_service" | grep -Eq '^    ports:|^    expose:'; then
    fail "parser-worker must not publish or expose a host port"
fi

grep -Fq 'HEALTHCHECK NONE' "$dockerfile" ||
    fail "Docker health-exec must stay disabled for the delegated cgroup parent"
grep -Fq 'DOTNET_EnableDiagnostics=0' "$dockerfile" ||
    fail "the parser image must disable runtime diagnostics"
grep -Fq 'libtesseract5' "$dockerfile" ||
    fail "the reviewed native OCR dependency is missing"
grep -Fq 'tesseract-ocr-eng' "$dockerfile" ||
    fail "the reviewed OCR model package is missing"

for evidence in     'minimal init'     'root filesystem is writable'     'core dumps are not disabled'     'file-descriptor ceiling'     'publishes a host port'     'attached to more than one network'     'runtime diagnostics are enabled'     'effective Linux capabilities'     'bounding set'     'memory ceiling is not finite'     'CPU limit is not finite'     'PID limit is not finite'     'per-document OCR cgroup'     'per-image OCR cgroup'     'kernel OOM kill evidence'
do
    grep -Fq "$evidence" "$verifier" ||
        fail "live verifier no longer checks: $evidence"
done

for heading in     '## Threat model'     '## Native components'     '## Enforced boundary'     '## Verification'     '## Residual risk'     '## Change procedure'
do
    grep -Fq "$heading" "$review" ||
        fail "review documentation is missing: $heading"
done

printf '%s\n' "Parser native boundary regression passed."
