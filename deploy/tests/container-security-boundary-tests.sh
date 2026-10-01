#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
config_file=$(mktemp)
trap 'rm -f "$config_file"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Container security boundary test failed: $1" >&2
    exit 1
}

env \
    ACME_EMAIL=ci@example.com \
    BILLWATCH_ALLOW_LOCAL_BACKUP_REPOSITORY=true \
    BILLWATCH_BACKUP_WORK_SIZE=1g \
    BILLWATCH_DATABASE_PASSWORD=ci-database-password \
    BILLWATCH_PARSER_AUTH_TOKEN=ci-parser-worker-authentication-token-more-than-32-characters \
    BILLWATCH_WEB_SESSION_REDIS_PASSWORD=ci-web-session-password-more-than-32-characters \
    BILLWATCH_HOST=api.fullworth.test \
    BILLWATCH_RELEASE_ID=0123456789abcdef0123456789abcdef01234567 \
    BILLWATCH_WEB_HOST=app.fullworth.test \
    PLAID_CLIENT_ID=ci-plaid-client \
    PLAID_ENVIRONMENT=sandbox \
    PLAID_SECRET=ci-plaid-secret \
    RESTIC_PASSWORD=ci-restic-password-with-more-than-24-chars \
    RESTIC_REPOSITORY=/repository \
    docker compose \
        --profile operations \
        --file "$root_dir/compose.production.yml" \
        config \
        --format json > "$config_file"

python3 - "$config_file" <<'PY'
import json
import sys

path = sys.argv[1]

with open(path, encoding="utf-8") as handle:
    config = json.load(handle)

services = config["services"]
networks = config["networks"]

def fail(message):
    raise SystemExit(f"Container security boundary test failed: {message}")

def service_networks(name):
    configured = services[name].get("networks", {})
    if isinstance(configured, list):
        return set(configured)
    return set(configured.keys())

expected_networks = {
    "api": {"data", "api_edge", "web_api", "api_egress", "parser_worker"},
    "parser-worker": {"parser_worker"},
    "web": {"web_edge", "web_api", "web_session", "web_egress"},
    "web-session-cache": {"web_session"},
    "database": {"data"},
    "backup": {"data", "backup_egress"},
    "restore-database": {"data"},
    "edge": {"edge_egress", "api_edge", "web_edge"},
}

for service, expected in expected_networks.items():
    actual = service_networks(service)
    if actual != expected:
        fail(
            f"{service} networks were {sorted(actual)}, "
            f"expected {sorted(expected)}."
        )

for name in ("data", "api_edge", "web_edge", "web_api", "web_session", "parser_worker"):
    if networks[name].get("internal") is not True:
        fail(f"{name} must be an internal-only Docker network.")

for name in ("api_egress", "web_egress", "backup_egress", "edge_egress"):
    if networks[name].get("internal") is True:
        fail(f"{name} must remain an explicit egress network.")

for name in ("api", "web"):
    service = services[name]

    if service.get("read_only") is not True:
        fail(f"{name} root filesystem must be read-only.")

    if service.get("pids_limit") != 256:
        fail(f"{name} must enforce a 256 PID ceiling.")
    expected_cpus = {"api": 2.0, "web": 1.0}[name]
    expected_memory = {"api": 1024 * 1024 * 1024, "web": 512 * 1024 * 1024}[name]
    if float(service.get("cpus", 0)) != expected_cpus:
        fail(f"{name} must enforce the expected CPU ceiling.")
    if int(service.get("mem_limit", 0)) != expected_memory:
        fail(f"{name} must enforce the expected memory ceiling.")
    if int(service.get("memswap_limit", 0)) != expected_memory:
        fail(f"{name} must disable swap expansion beyond the memory ceiling.")

    if "ALL" not in service.get("cap_drop", []):
        fail(f"{name} must drop all Linux capabilities.")

    security_opt = service.get("security_opt", [])
    if "no-new-privileges:true" not in security_opt:
        fail(f"{name} must disable privilege escalation.")

    tmpfs = service.get("tmpfs", [])
    tmp_entry = next(
        (
            entry
            for entry in tmpfs
            if entry == "/tmp"
            or entry.startswith("/tmp:")
        ),
        None,
    )

    if tmp_entry is None:
        fail(f"{name} must provide bounded writable /tmp storage.")

    for option in ("noexec", "nosuid", "nodev"):
        if option not in tmp_entry:
            fail(
                f"{name} /tmp is missing required option {option!r}: "
                f"{tmp_entry!r}"
            )

    if "size=256m" not in tmp_entry and "size=268435456" not in tmp_entry:
        fail(f"{name} /tmp must be capped at 256 MiB: {tmp_entry!r}")

parser_worker = services["parser-worker"]

if parser_worker.get("read_only") is not True:
    fail("parser-worker root filesystem must be read-only.")

if parser_worker.get("user") != "1654:1654":
    fail("parser-worker must run as its dedicated unprivileged user.")

if parser_worker.get("ports"):
    fail("parser-worker must not publish host ports.")

parser_volumes = parser_worker.get("volumes", [])
if len(parser_volumes) != 1:
    fail("parser-worker must mount only the TLS public-certificate volume.")

parser_tls_volume = parser_volumes[0]
if (
    parser_tls_volume.get("source") != "parser_worker_tls"
    or parser_tls_volume.get("target") != "/var/run/fullworth-parser-tls"
    or parser_tls_volume.get("read_only") is True
):
    fail("parser-worker TLS volume must be the only writable worker mount.")

if parser_worker.get("pids_limit") != 64:
    fail("parser-worker must enforce a 64 PID ceiling.")

if float(parser_worker.get("cpus", 0)) != 1.0:
    fail("parser-worker must enforce a 1 CPU ceiling.")

if int(parser_worker.get("mem_limit", 0)) != 512 * 1024 * 1024:
    fail("parser-worker must enforce a 512 MiB memory ceiling.")

if int(parser_worker.get("memswap_limit", 0)) != 512 * 1024 * 1024:
    fail("parser-worker must disable swap expansion beyond its memory ceiling.")

if "ALL" not in parser_worker.get("cap_drop", []):
    fail("parser-worker must drop all Linux capabilities.")

if "no-new-privileges:true" not in parser_worker.get("security_opt", []):
    fail("parser-worker must disable privilege escalation.")

if services["api"].get("depends_on", {}).get("parser-worker", {}).get("condition") != "service_healthy":
    fail("API must wait for parser-worker resource-limit readiness.")

api_environment = services["api"].get("environment", {})
parser_url = api_environment.get("ParserWorker__BaseUrl")
if parser_url != "https://parser-worker:8081":
    fail("API must use the encrypted internal parser-worker endpoint.")

certificate_path = "/var/run/fullworth-parser-tls/parser-worker.cer.pem"
if api_environment.get("ParserWorker__ServerCertificatePath") != certificate_path:
    fail("API must pin the parser-worker public certificate path.")
if parser_worker.get("environment", {}).get(
    "ParserWorker__TlsCertificatePath"
) != certificate_path:
    fail("parser-worker must publish its ephemeral public certificate.")

api_tls_volume = next(
    (
        volume
        for volume in services["api"].get("volumes", [])
        if volume.get("target") == "/var/run/fullworth-parser-tls"
    ),
    None,
)
if (
    api_tls_volume is None
    or api_tls_volume.get("source") != "parser_worker_tls"
    or api_tls_volume.get("read_only") is not True
):
    fail("API must mount the parser-worker certificate volume read-only.")

api_parser_token = api_environment.get("ParserWorker__AuthenticationToken")
worker_parser_token = parser_worker.get("environment", {}).get(
    "ParserWorker__AuthenticationToken"
)
if not api_parser_token or len(api_parser_token) < 32:
    fail("API must receive a strong parser-worker authentication token.")
if api_parser_token != worker_parser_token:
    fail("API and parser-worker must receive the same authentication token.")

session_cache = services["web-session-cache"]

if session_cache.get("read_only") is not True:
    fail("web-session-cache root filesystem must be read-only.")

if session_cache.get("pids_limit") != 128:
    fail("web-session-cache must enforce a 128 PID ceiling.")

if "ALL" not in session_cache.get("cap_drop", []):
    fail("web-session-cache must drop all Linux capabilities.")

if "no-new-privileges:true" not in session_cache.get("security_opt", []):
    fail("web-session-cache must disable privilege escalation.")

if session_cache.get("ports"):
    fail("web-session-cache must not publish host ports.")

if session_cache.get("user") != "999:1000":
    fail("web-session-cache must run as the Redis unprivileged user.")

redis_environment = session_cache.get("environment", {})
redis_password = redis_environment.get("REDIS_PASSWORD")

if not redis_password or len(redis_password) < 32:
    fail("web-session-cache must receive a strong runtime password.")

web_environment = services["web"].get("environment", {})

if web_environment.get("WebSession__RedisHost") != "web-session-cache":
    fail("Web must resolve its session store only through the isolated cache service.")

if web_environment.get("WebSession__RedisPassword") != redis_password:
    fail("Web and session cache must use the same protected session-cache credential.")

if session_cache.get("volumes"):
    fail("web-session-cache must remain ephemeral and must not mount persistent volumes.")

redis_command = " ".join(session_cache.get("command", []))

for required_fragment in (
    "--requirepass",
    "--appendonly no",
    "--maxmemory 128mb",
    "--maxmemory-policy volatile-ttl",
):
    if required_fragment not in redis_command:
        fail(
            "web-session-cache is missing required runtime control "
            f"{required_fragment!r}."
        )

edge = services["edge"]

if edge.get("read_only") is not True:
    fail("edge root filesystem must be read-only.")

if edge.get("pids_limit") != 128:
    fail("edge must enforce a 128 PID ceiling.")

if "ALL" not in edge.get("cap_drop", []):
    fail("edge must drop all Linux capabilities before adding the bind capability.")

if set(edge.get("cap_add", [])) != {"NET_BIND_SERVICE"}:
    fail("edge may add only NET_BIND_SERVICE.")

if "no-new-privileges:true" not in edge.get("security_opt", []):
    fail("edge must disable privilege escalation.")

edge_tmpfs = edge.get("tmpfs", [])
edge_tmp_entry = next(
    (
        entry
        for entry in edge_tmpfs
        if entry == "/tmp"
        or entry.startswith("/tmp:")
    ),
    None,
)

if edge_tmp_entry is None:
    fail("edge must provide bounded writable /tmp storage.")

for option in ("noexec", "nosuid", "nodev"):
    if option not in edge_tmp_entry:
        fail(
            f"edge /tmp is missing required option {option!r}: "
            f"{edge_tmp_entry!r}"
        )

if "size=64m" not in edge_tmp_entry and "size=67108864" not in edge_tmp_entry:
    fail(f"edge /tmp must be capped at 64 MiB: {edge_tmp_entry!r}")

for service_name in ("api", "web", "database"):
    if services[service_name].get("ports"):
        fail(f"{service_name} must not publish host ports.")

edge_ports = edge.get("ports", [])
if len(edge_ports) != 3:
    fail("edge must be the only service publishing the three public bindings.")

api_environment = services["api"].get("environment", {})

for key in (
    "StatementAi__Local__Enabled",
    "StatementAi__Shadow__Enabled",
    "StatementAi__Shadow__AllowProviderCalls",
):
    if str(api_environment.get(key, "")).lower() != "false":
        fail(f"Production API must keep {key} explicitly disabled.")

if api_environment.get("ReverseProxy__KnownProxies__0") != "172.30.0.10":
    fail("API trusted proxy must be pinned to the API-edge Caddy address.")

if web_environment.get("ReverseProxy__KnownProxies__0") != "172.31.0.10":
    fail("Web trusted proxy must be pinned to the Web-edge Caddy address.")

print("Container security boundary tests passed.")
PY
