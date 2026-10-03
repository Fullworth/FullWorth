#!/bin/sh

# Shared capture/restore policy for security-sensitive persisted assets.
# The caller chooses the expected numeric owner so the same checks can run
# inside the production backup container and in isolated regression tests.
validate_private_asset_tree()
{
    asset_root=$1
    asset_label=$2
    expected_uid=$3
    expected_gid=$4

    if [ ! -d "$asset_root" ] || [ -L "$asset_root" ]; then
        echo "$asset_label root is not a real directory." >&2
        return 1
    fi

    unexpected_entry="$(find "$asset_root" -xdev -mindepth 1 ! -type d ! -type f -print -quit)"

    if [ -n "$unexpected_entry" ]; then
        echo "$asset_label contains a symbolic link or non-regular entry." >&2
        return 1
    fi

    invalid_directory="$(find "$asset_root" -xdev -type d ! -perm 0700 -print -quit)"

    if [ -n "$invalid_directory" ]; then
        echo "$asset_label directories must have mode 0700." >&2
        return 1
    fi

    invalid_file="$(find "$asset_root" -xdev -type f ! -perm 0600 -print -quit)"

    if [ -n "$invalid_file" ]; then
        echo "$asset_label files must have mode 0600." >&2
        return 1
    fi

    invalid_owner="$(
        find "$asset_root" -xdev \( -type d -o -type f \) -exec stat -c '%u:%g' {} \; |
            grep -Fvx "$expected_uid:$expected_gid" |
            head -n 1 ||
            true
    )"

    if [ -n "$invalid_owner" ]; then
        echo "$asset_label entries must remain owned by $expected_uid:$expected_gid." >&2
        return 1
    fi

    find "$asset_root" -xdev -type f -print | wc -l | tr -d ' '
}
