#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)

python3 - "$root_dir" <<'PY'
import os
import re
import sys

root = os.path.abspath(sys.argv[1])
services_root = os.path.join(root, "FullWorth.API", "Services")

owners = {
    "BillStreams": "Bills",
    "BillAlerts": "Bills",
    "BankConnections": "Plaid",
    "BankAccounts": "Plaid",
    "BankTransactions": "Plaid",
    "PlaidLinkSessions": "Plaid",
    "BillStatements": "Statements",
    "BillLineItems": "Statements",
    "BillChanges": "Statements",
    "BillStatementUploads": "Statements",
    "BillStatementAiEvaluations": "Statements",
}

modules = {"Bills", "Plaid", "Statements"}

# Existing cross-owner table access is frozen here as a temporary ceiling.
# New cross-owner access must use an explicit contract instead.
allowed_cross_owner_access = {
    ("Bills", "BankTransactions", "FullWorth.API/Services/Bills/RecurringBillDiscoveryPersistenceService.cs"),
    ("Statements", "BillStreams", "FullWorth.API/Services/Statements/BillStatementProcessingService.cs"),
    ("Statements", "BillStreams", "FullWorth.API/Services/Statements/BillStatementPaymentDueAlertService.cs"),
    ("Statements", "BillStreams", "FullWorth.API/Services/Statements/BillStatementChangeDetectionService.cs"),
    ("Statements", "BillAlerts", "FullWorth.API/Services/Statements/BillStatementEvidenceAlertService.cs"),
    ("Statements", "BillAlerts", "FullWorth.API/Services/Statements/BillStatementPaymentDueAlertService.cs"),
    ("Statements", "BillAlerts", "FullWorth.API/Services/Statements/BillStatementChangeDetectionService.cs"),
}

set_pattern = re.compile(
    r"\b(" + "|".join(sorted(map(re.escape, owners), key=len, reverse=True)) + r")\b"
)

seen_allowances = set()
errors = []

for module in sorted(modules):
    module_dir = os.path.join(services_root, module)
    if not os.path.isdir(module_dir):
        errors.append(f"missing service module directory: {module}")
        continue

    for current_root, _, files in os.walk(module_dir):
        for filename in files:
            if not filename.endswith(".cs"):
                continue

            path = os.path.join(current_root, filename)
            rel = os.path.relpath(path, root).replace(os.sep, "/")

            with open(path, "r", encoding="utf-8-sig") as handle:
                content = handle.read()

            referenced_sets = set(set_pattern.findall(content))

            for db_set in sorted(referenced_sets):
                owner = owners[db_set]
                if owner == module:
                    continue

                edge = (module, db_set, rel)
                if edge in allowed_cross_owner_access:
                    seen_allowances.add(edge)
                    continue

                errors.append(
                    f"forbidden direct cross-owner data access: {module} -> "
                    f"{db_set} (owned by {owner}) in {rel}; use an explicit owner contract"
                )

stale = sorted(allowed_cross_owner_access - seen_allowances)
for module, db_set, rel in stale:
    errors.append(
        f"stale data-ownership allowance: {module} -> {db_set} in {rel}; "
        "remove the allowance in the same change that removes the dependency"
    )

if errors:
    for error in errors:
        print(f"Data ownership boundary regression failed: {error}", file=sys.stderr)
    raise SystemExit(1)

print("FullWorth data-ownership ratchet passed.")
PY
