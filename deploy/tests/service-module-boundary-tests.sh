#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)

python3 - "$root_dir" <<'PY'
import os
import re
import sys

root = os.path.abspath(sys.argv[1])
services_root = os.path.join(root, "FullWorth.API", "Services")

modules = {
    "Accounts",
    "Bills",
    "Identity",
    "Plaid",
    "Statements",
    "Subscriptions",
}

allowed_cross_module_references = set()

reference_pattern = re.compile(
    r"FullWorth\.API\.Services\.(Accounts|Bills|Identity|Plaid|Statements|Subscriptions)\b"
)

seen_allowed = set()
errors = []

for source_module in sorted(modules):
    module_dir = os.path.join(services_root, source_module)
    if not os.path.isdir(module_dir):
        errors.append(f"missing service module directory: {source_module}")
        continue

    for current_root, _, files in os.walk(module_dir):
        for filename in files:
            if not filename.endswith(".cs"):
                continue

            path = os.path.join(current_root, filename)
            rel = os.path.relpath(path, root).replace(os.sep, "/")

            with open(path, "r", encoding="utf-8-sig") as handle:
                content = handle.read()

            targets = set(reference_pattern.findall(content))
            targets.discard(source_module)

            for target_module in sorted(targets):
                edge = (source_module, target_module, rel)
                if edge in allowed_cross_module_references:
                    seen_allowed.add(edge)
                    continue

                errors.append(
                    f"forbidden direct service-module dependency: "
                    f"{source_module} -> {target_module} in {rel}; "
                    "introduce or use an explicit contract instead"
                )

stale_allowances = sorted(allowed_cross_module_references - seen_allowed)
for source_module, target_module, rel in stale_allowances:
    errors.append(
        f"stale service-module allowance: {source_module} -> {target_module} in {rel}; "
        "the dependency disappeared, so remove the allowance to keep the ratchet honest"
    )

if errors:
    for error in errors:
        print(f"Service module boundary regression failed: {error}", file=sys.stderr)
    raise SystemExit(1)

print("FullWorth service-module dependency ratchet passed.")
PY
