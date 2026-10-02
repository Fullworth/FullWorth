#!/bin/sh

set -eu

deployment_directory="${1:-}"

fail()
{
    printf '%s\n' "Parser containment verification failed: $1" >&2
    exit "${2:-1}"
}

[ -n "$deployment_directory" ] ||
    fail "a FullWorth deployment directory is required." 64

[ -f "$deployment_directory/compose.production.yml" ] ||
    fail "compose.production.yml was not found." 66

deployment_directory="$(cd "$deployment_directory" && pwd -P)"
environment_file="$deployment_directory/.env.production"

[ -f "$environment_file" ] &&
[ ! -L "$environment_file" ] ||
    fail "the protected production environment file is missing or unsafe." 66

for command_name in docker python3 openssl sha256sum awk grep
do
    command -v "$command_name" >/dev/null 2>&1 ||
        fail "required command is unavailable: $command_name" 69
done

compose()
{
    docker compose \
        --env-file "$environment_file" \
        --file "$deployment_directory/compose.production.yml" \
        "$@"
}

for service in parser-worker api web
do
    compose ps --status running --services |
        grep -qx "$service" ||
        fail "required production service is not running: $service" 69
done

parser_container="$(compose ps -q parser-worker)"

[ -n "$parser_container" ] ||
    fail "the parser-worker container could not be resolved." 69

parser_pid="$(
    docker inspect \
        --format '{{.State.Pid}}' \
        "$parser_container"
)"

case "$parser_pid" in
    ''|*[!0-9]*|0)
        fail "the parser-worker host PID is invalid." 69
        ;;
esac

parser_uid="$(
    awk '/^Uid:/ { print $2; exit }' \
        "/proc/$parser_pid/status"
)"

parser_caps="$(
    awk '/^CapEff:/ { print $2; exit }' \
        "/proc/$parser_pid/status"
)"

[ "$parser_uid" = "1654" ] ||
    fail "the running parser supervisor is not the dedicated unprivileged identity." 77

[ "$parser_caps" = "0000000000000000" ] ||
    fail "the running parser supervisor retains effective Linux capabilities." 77

parser_relative="$(
    awk -F: '$1 == "0" && $2 == "" { print $3; exit }' \
        "/proc/$parser_pid/cgroup"
)"

case "$parser_relative" in
    /*/fullworth-supervisor)
        ;;
    *)
        fail "the parser supervisor is not inside its delegated leaf cgroup." 77
        ;;
esac

case "$parser_relative" in
    *..*)
        fail "the parser cgroup path is invalid." 77
        ;;
esac

parent_relative="${parser_relative%/fullworth-supervisor}"
parent_cgroup="/sys/fs/cgroup$parent_relative"

[ -d "$parent_cgroup" ] ||
    fail "the parser container cgroup is unavailable on the host." 77

cpu_max="$(cat "$parent_cgroup/cpu.max")"
memory_max="$(cat "$parent_cgroup/memory.max")"
swap_max="$(cat "$parent_cgroup/memory.swap.max")"
pids_max="$(cat "$parent_cgroup/pids.max")"

case "$cpu_max" in
    max\ *|'')
        fail "the parser container CPU limit is not finite." 77
        ;;
esac

case "$memory_max" in
    max|'')
        fail "the parser container memory limit is not finite." 77
        ;;
esac

case "$swap_max" in
    max|'')
        fail "the parser container swap limit is not finite." 77
        ;;
esac

case "$pids_max" in
    max|'')
        fail "the parser container PID limit is not finite." 77
        ;;
esac

for controller in cpu memory pids
do
    tr ' ' '\n' < "$parent_cgroup/cgroup.subtree_control" |
        grep -qx "$controller" ||
        fail "the delegated parser subtree is missing controller: $controller" 77
done

work_directory="$(mktemp -d)"
request_pid=

cleanup()
{
    status=$?
    trap - EXIT HUP INT TERM

    if [ -n "$request_pid" ] &&
       kill -0 "$request_pid" 2>/dev/null; then
        kill "$request_pid" 2>/dev/null || true
        wait "$request_pid" 2>/dev/null || true
    fi

    rm -rf "$work_directory"
    exit "$status"
}

trap cleanup EXIT HUP INT TERM

image_file="$work_directory/containment-proof.png"
response_file="$work_directory/containment-response.json"
logs_file="$work_directory/containment-logs.txt"
proof_marker="FullWorthContainmentProof-$(openssl rand -hex 16)"

PROOF_MARKER="$proof_marker" python3 - <<'PY' > "$image_file"
import os
import struct
import sys
import zlib

width = 4000
height = 3000
marker = os.environ["PROOF_MARKER"].encode("ascii")
raw = b"".join(
    b"\x00" + (b"\xff\xff\xff" * width)
    for _ in range(height)
)

def chunk(kind, payload):
    body = kind + payload
    return (
        struct.pack(">I", len(payload))
        + body
        + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)
    )

png = (
    b"\x89PNG\r\n\x1a\n"
    + chunk(
        b"IHDR",
        struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0),
    )
    + chunk(b"tEXt", b"Comment\x00" + marker)
    + chunk(b"IDAT", zlib.compress(raw, 9))
    + chunk(b"IEND", b"")
)

sys.stdout.buffer.write(png)
PY

parser_token="$(
    awk -F= '
        $1 == "BILLWATCH_PARSER_AUTH_TOKEN" {
            print substr($0, length($1) + 2)
            exit
        }
    ' "$environment_file"
)"

[ "${#parser_token}" -ge 32 ] ||
    fail "the parser authentication credential is unavailable to the proof." 77

timestamp="$(date +%s)"
nonce="$(openssl rand -hex 16)"
content_sha256="$(sha256sum "$image_file" | awk '{ print $1 }')"

signature="$(
    printf '%s' "$parser_token" |
    python3 -c '
import hashlib
import hmac
import sys

key = sys.stdin.buffer.read()
message = "POST\\n/v1/ocr/extract\\n{}\\n{}\\n{}".format(
    sys.argv[1],
    sys.argv[2],
    sys.argv[3],
).encode("utf-8")
print(hmac.new(key, message, hashlib.sha256).hexdigest())
' "$timestamp" "$nonce" "$content_sha256"
)"

send_ocr_request()
{
    compose exec -T api \
        sh -c '
            exec curl \
                --cacert /var/run/fullworth-parser-tls/parser-worker.cer.pem \
                --fail --silent --show-error --request POST \
                --header "Content-Type: image/png" \
                --header "X-FullWorth-Ocr-Extension: .png" \
                --header "Authorization: Bearer ${ParserWorker__AuthenticationToken}" \
                --header "X-FullWorth-Parser-Timestamp: $1" \
                --header "X-FullWorth-Parser-Nonce: $2" \
                --header "X-FullWorth-Parser-Content-SHA256: $3" \
                --header "X-FullWorth-Parser-Signature: $4" \
                --data-binary @- \
                https://parser-worker:8081/v1/ocr/extract
        ' sh "$timestamp" "$nonce" "$content_sha256" "$signature" \
        < "$image_file" > "$response_file"
}

send_ocr_request &
request_pid=$!

document_cgroup=
attempt=1
while [ "$attempt" -le 500 ]
do
    set -- "$parent_cgroup"/fullworth-ocr-*

    if [ "$#" -gt 1 ]; then
        fail "multiple OCR document cgroups existed during a single admitted request." 77
    fi

    if [ "$#" -eq 1 ] &&
       [ -d "$1" ]; then
        document_cgroup=$1
        break
    fi

    if ! kill -0 "$request_pid" 2>/dev/null; then
        break
    fi

    sleep 0.01
    attempt=$((attempt + 1))
done

[ -n "$document_cgroup" ] ||
    fail "no per-document OCR cgroup was observed while OCR was active." 77

[ "$(cat "$document_cgroup/memory.max")" = "402653184" ] ||
    fail "the OCR child memory ceiling is not 384 MiB." 77

[ "$(cat "$document_cgroup/memory.swap.max")" = "0" ] ||
    fail "the OCR child swap ceiling is not zero." 77

[ "$(cat "$document_cgroup/pids.max")" = "48" ] ||
    fail "the OCR child PID ceiling is not 48." 77

set -- $(cat "$document_cgroup/cpu.max")
[ "$#" -eq 2 ] &&
[ "$1" = "100000" ] &&
[ "$2" = "100000" ] ||
    fail "the OCR child CPU ceiling is not the expected finite quota." 77

child_pid="$(head -n 1 "$document_cgroup/cgroup.procs")"

case "$child_pid" in
    ''|*[!0-9]*)
        fail "the OCR child PID was not present in its cgroup." 77
        ;;
esac

child_relative="$(
    awk -F: '$1 == "0" && $2 == "" { print $3; exit }' \
        "/proc/$child_pid/cgroup"
)"

expected_relative="/${document_cgroup#/sys/fs/cgroup/}"

[ "$child_relative" = "$expected_relative" ] ||
    fail "the OCR child is not executing in the observed document cgroup." 77

if ! wait "$request_pid"; then
    request_pid=
    fail "the synthetic OCR request failed." 77
fi
request_pid=

python3 - "$response_file" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    result = json.load(handle)

if result.get("pageCount") != 1:
    raise SystemExit("synthetic OCR did not traverse one admitted image")
if result.get("isUsable") is not False:
    raise SystemExit("blank containment image unexpectedly produced usable OCR")
if result.get("text") != "":
    raise SystemExit("blank containment image unexpectedly produced OCR text")
PY

attempt=1
while [ "$attempt" -le 200 ]
do
    [ ! -d "$document_cgroup" ] &&
        break

    sleep 0.01
    attempt=$((attempt + 1))
done

[ ! -d "$document_cgroup" ] ||
    fail "the per-document OCR cgroup remained after its child exited." 77

containment_evidence="$(
    compose run \
        --rm \
        --no-deps \
        parser-worker \
        dotnet FullWorth.ParserWorker.dll --containment-self-test
)"

containment_summary="$(
    printf '%s' "$containment_evidence" |
    python3 -c '
import json
import sys

result = json.load(sys.stdin)
if result.get("status") != "ok" or result.get("code") != "ok":
    raise SystemExit("hard memory containment probe failed")
if result.get("memoryMaxBytes") != 402653184:
    raise SystemExit("hard memory containment probe used an unexpected limit")
if result.get("memoryPeakBytes", 0) <= 0:
    raise SystemExit("hard memory containment probe did not record peak usage")
if result.get("oomKillDelta", 0) < 1:
    raise SystemExit("kernel OOM kill evidence was not recorded")

print(
    "memory.max={} memory.peak={} oom_kill_delta={}".format(
        result["memoryMaxBytes"],
        result["memoryPeakBytes"],
        result["oomKillDelta"],
    )
)
'
)"

compose ps --status running --services |
    grep -qx parser-worker ||
    fail "the production parser worker did not survive containment proof." 77

compose exec -T api \
    curl --fail --silent --show-error \
        --cacert /var/run/fullworth-parser-tls/parser-worker.cer.pem \
        https://parser-worker:8081/health/ready \
        >/dev/null ||
    fail "the production parser worker is not ready after containment proof." 77

compose logs --no-color --tail 500 api parser-worker > "$logs_file" 2>&1

if grep -Fq "$proof_marker" "$logs_file"; then
    fail "synthetic document bytes appeared in application or parser logs." 77
fi

if printf '%s\\n' "$parser_token" |
   grep -Fq -f - "$logs_file"; then
    fail "the parser authentication credential appeared in application or parser logs." 77
fi

if grep -Eiq 'tesseract|leptonica|traineddata' "$logs_file"; then
    fail "native OCR diagnostics appeared in application or parser logs." 77
fi

unset parser_token

release_id="$(git -C "$deployment_directory" rev-parse --verify HEAD^{commit} 2>/dev/null)" ||
    fail "the deployed release SHA could not be resolved." 77

printf '%s\n' \
    "FullWorth parser containment verification passed." \
    "Release: $release_id" \
    "Parser runtime: uid=1654 effective-capabilities=none" \
    "Document limits: cpu.max=100000/100000 memory.max=402653184 memory.swap.max=0 pids.max=48" \
    "Kernel memory proof: $containment_summary" \
    "Synthetic OCR: per-document cgroup observed and removed; parser readiness survived."
