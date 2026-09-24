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
    "api": {"data", "api_edge", "web_api", "api_egress"},
    "web": {"web_edge", "web_api", "web_egress"},
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

for name in ("data", "api_edge", "web_edge", "web_api"):
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
web_environment = services["web"].get("environment", {})

if api_environment.get("ReverseProxy__KnownProxies__0") != "172.28.0.10":
    fail("API trusted proxy must be pinned to the API-edge Caddy address.")

if web_environment.get("ReverseProxy__KnownProxies__0") != "172.29.0.10":
    fail("Web trusted proxy must be pinned to the Web-edge Caddy address.")

print("Container security boundary tests passed.")
PY
