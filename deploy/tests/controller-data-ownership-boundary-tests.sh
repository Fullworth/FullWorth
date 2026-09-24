#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)

python3 - "$root_dir" <<'PY'
import os
import re
import sys

root = os.path.abspath(sys.argv[1])
controllers_root = os.path.join(
    root,
    "FullWorth.API",
    "Controllers")

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
    "SubscriptionEntitlements": "Subscriptions",
    "UserProgramMemberships": "Subscriptions",
    "SubscriptionAccessKeys": "Subscriptions",
    "SubscriptionAccessKeyRedemptions": "Subscriptions",
}

allowed_cross_owner_access = {
    ("Accounts", "BillStreams", "FullWorth.API/Controllers/AccountController.cs"),
    ("Accounts", "BillAlerts", "FullWorth.API/Controllers/AccountController.cs"),
    ("Accounts", "BankConnections", "FullWorth.API/Controllers/AccountController.cs"),
    ("Accounts", "BankAccounts", "FullWorth.API/Controllers/AccountController.cs"),
    ("Accounts", "BankTransactions", "FullWorth.API/Controllers/AccountController.cs"),
    ("Accounts", "PlaidLinkSessions", "FullWorth.API/Controllers/AccountController.cs"),
    ("Accounts", "BillStatements", "FullWorth.API/Controllers/AccountController.cs"),
    ("Accounts", "BillLineItems", "FullWorth.API/Controllers/AccountController.cs"),
    ("Accounts", "BillChanges", "FullWorth.API/Controllers/AccountController.cs"),
    ("Accounts", "BillStatementUploads", "FullWorth.API/Controllers/AccountController.cs"),
    ("Accounts", "BillStatementAiEvaluations", "FullWorth.API/Controllers/AccountController.cs"),
    ("Accounts", "SubscriptionEntitlements", "FullWorth.API/Controllers/AccountController.cs"),
    ("Accounts", "UserProgramMemberships", "FullWorth.API/Controllers/AccountController.cs"),
    ("Accounts", "SubscriptionAccessKeyRedemptions", "FullWorth.API/Controllers/AccountController.cs"),
}

def controller_module(filename):
    if filename.startswith("BillStatement"):
        return "Statements"
    if filename.startswith("BillStream") or filename.startswith("BillAlert"):
        return "Bills"
    if filename.startswith("Bank") or filename.startswith("Plaid"):
        return "Plaid"
    if filename.startswith("Subscription"):
        return "Subscriptions"
    if filename.startswith("Account"):
        return "Accounts"
    if filename.startswith("Admin"):
        return "Admin"
    if filename.startswith("Identity"):
        return "Identity"
    return None

set_pattern = re.compile(
    r"\b(?:_dbContext|dbContext)\s*\.\s*("
    + "|".join(
        sorted(
            map(re.escape, owners),
            key=len,
            reverse=True))
    + r")\b"
)

seen_allowances = set()
errors = []

if not os.path.isdir(controllers_root):
    errors.append(
        "missing controller directory: FullWorth.API/Controllers")
else:
    for filename in sorted(os.listdir(controllers_root)):
        if not filename.endswith(".cs"):
            continue

        path = os.path.join(
            controllers_root,
            filename)

        if not os.path.isfile(path):
            continue

        rel = os.path.relpath(
            path,
            root).replace(
                os.sep,
                "/")

        with open(
                path,
                "r",
                encoding="utf-8-sig") as handle:
            content = handle.read()

        referenced_sets = set(
            set_pattern.findall(
                content))

        if not referenced_sets:
            continue

        module = controller_module(
            filename)

        if module is None:
            errors.append(
                f"controller with direct owned-data access has no module mapping: {rel}")
            continue

        for db_set in sorted(
                referenced_sets):
            owner = owners[db_set]

            if owner == module:
                continue

            edge = (
                module,
                db_set,
                rel)

            if edge in allowed_cross_owner_access:
                seen_allowances.add(
                    edge)
                continue

            errors.append(
                f"forbidden direct controller cross-owner data access: "
                f"{module} -> {db_set} (owned by {owner}) in {rel}; "
                "use an explicit owner contract")

stale = sorted(
    allowed_cross_owner_access -
    seen_allowances)

for module, db_set, rel in stale:
    errors.append(
        f"stale controller data-ownership allowance: "
        f"{module} -> {db_set} in {rel}; remove the allowance "
        "in the same change that removes the dependency")

if errors:
    for error in errors:
        print(
            f"Controller data ownership boundary regression failed: {error}",
            file=sys.stderr)
    raise SystemExit(1)

print(
    "FullWorth controller data-ownership ratchet passed.")
PY
