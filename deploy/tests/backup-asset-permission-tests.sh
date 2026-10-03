#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)

trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Backup permission policy test failed: $1" >&2
    exit 1
}

# shellcheck source=../backup/permission-policy.sh
. "$root_dir/deploy/backup/permission-policy.sh"

owner_uid=$(id -u)
owner_gid=$(id -g)
asset_root="$temp_dir/statements"
mkdir "$asset_root"
chmod 0700 "$asset_root"
mkdir "$asset_root/owner"
chmod 0700 "$asset_root/owner"
printf '%s\n' statement > "$asset_root/owner/statement.pdf"
chmod 0600 "$asset_root/owner/statement.pdf"

[ "$(validate_private_asset_tree "$asset_root" statements "$owner_uid" "$owner_gid")" = 1 ] ||
    fail "valid private tree did not report its file count."

chmod 0750 "$asset_root/owner"
if validate_private_asset_tree "$asset_root" statements "$owner_uid" "$owner_gid" >/dev/null 2>&1
then
    fail "group-accessible directory was accepted."
fi
chmod 0700 "$asset_root/owner"

chmod 0640 "$asset_root/owner/statement.pdf"
if validate_private_asset_tree "$asset_root" statements "$owner_uid" "$owner_gid" >/dev/null 2>&1
then
    fail "group-readable statement was accepted."
fi
chmod 0600 "$asset_root/owner/statement.pdf"

ln -s "$asset_root/owner/statement.pdf" "$asset_root/owner/linked.pdf"
if validate_private_asset_tree "$asset_root" statements "$owner_uid" "$owner_gid" >/dev/null 2>&1
then
    fail "symbolic-link statement was accepted."
fi
rm "$asset_root/owner/linked.pdf"

mkfifo "$asset_root/owner/pipe"
if validate_private_asset_tree "$asset_root" statements "$owner_uid" "$owner_gid" >/dev/null 2>&1
then
    fail "non-regular statement entry was accepted."
fi
rm "$asset_root/owner/pipe"

if validate_private_asset_tree "$asset_root" statements "$((owner_uid + 1))" "$owner_gid" >/dev/null 2>&1
then
    fail "wrong statement owner was accepted."
fi

printf '%s\n' 'Backup permission policy tests passed.'
