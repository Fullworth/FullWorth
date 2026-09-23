#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)

python3 - "$root_dir" <<'PY'
import os
import sys
import xml.etree.ElementTree as ET

root = os.path.abspath(sys.argv[1])

projects = {
    "FullWorth.Core": os.path.join(root, "FullWorth.Core", "FullWorth.Core.csproj"),
    "FullWorth.API": os.path.join(root, "FullWorth.API", "FullWorth.API.csproj"),
    "FullWorth.Web": os.path.join(root, "FullWorth.Web", "FullWorth.Web.csproj"),
    "FullWorth.MAUI": os.path.join(root, "FullWorth.csproj"),
    "FullWorth.Tests": os.path.join(root, "FullWorth.Tests", "FullWorth.Tests.csproj"),
}

allowed = {
    "FullWorth.Core": set(),
    "FullWorth.API": {"FullWorth.Core"},
    "FullWorth.Web": {"FullWorth.Core"},
    "FullWorth.MAUI": {"FullWorth.Core"},
    "FullWorth.Tests": {"FullWorth.API", "FullWorth.Core", "FullWorth.Web"},
}

canonical = {}
for name, path in projects.items():
    if not os.path.isfile(path):
        raise SystemExit(f"Project boundary regression failed: missing project file for {name}: {path}")
    canonical[os.path.normcase(os.path.realpath(path))] = name

errors = []

for source_name, project_path in projects.items():
    tree = ET.parse(project_path)
    refs = set()

    for element in tree.iter():
        if element.tag.split("}")[-1] != "ProjectReference":
            continue

        include = element.attrib.get("Include")
        if not include:
            errors.append(f"{source_name}: ProjectReference without Include attribute")
            continue

        normalized_include = include.replace("\\", os.sep).replace("/", os.sep)
        target_path = os.path.normcase(
            os.path.realpath(os.path.join(os.path.dirname(project_path), normalized_include))
        )
        target_name = canonical.get(target_path)

        if target_name is None:
            errors.append(
                f"{source_name}: references unregistered project {include}; "
                "add an explicit architecture decision before expanding the production graph"
            )
            continue

        refs.add(target_name)

    unexpected = sorted(refs - allowed[source_name])
    if unexpected:
        errors.append(
            f"{source_name}: forbidden project reference(s): {', '.join(unexpected)}; "
            f"allowed: {', '.join(sorted(allowed[source_name])) or 'none'}"
        )

if errors:
    for error in errors:
        print(f"Project boundary regression failed: {error}", file=sys.stderr)
    raise SystemExit(1)

print("FullWorth project dependency airlocks passed.")
PY
