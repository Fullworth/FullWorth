#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
entrypoint="$root_dir/deploy/application-entrypoint.sh"
temp_dir=$(mktemp -d)

trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Data Protection permission test failed: $1" >&2
    exit 1
}

keys_path="$temp_dir/keys"
mkdir "$keys_path"
printf '%s\n' '<key />' > "$keys_path/key-test.xml"
chmod 0777 "$keys_path"
chmod 0666 "$keys_path/key-test.xml"

DataProtection__KeysPath="$keys_path" \
    sh "$entrypoint" \
    sh -c '
        [ "$(stat -c %a "$DataProtection__KeysPath")" = 700 ]
        [ "$(stat -c %a "$DataProtection__KeysPath/key-test.xml")" = 600 ]
        touch "$DataProtection__KeysPath/new-key.xml"
        [ "$(stat -c %a "$DataProtection__KeysPath/new-key.xml")" = 600 ]
    ' ||
    fail "entrypoint did not enforce directory, existing-key, and creation permissions."

target_path="$temp_dir/target"
mkdir "$target_path"
linked_path="$temp_dir/linked-keys"
ln -s "$target_path" "$linked_path"

if DataProtection__KeysPath="$linked_path" \
    sh "$entrypoint" true >/dev/null 2>&1
then
    fail "entrypoint accepted a symbolic-link key directory."
fi

nested_path="$temp_dir/nested-keys"
mkdir "$nested_path" "$nested_path/unexpected"

if DataProtection__KeysPath="$nested_path" \
    sh "$entrypoint" true >/dev/null 2>&1
then
    fail "entrypoint accepted a non-regular key-ring entry."
fi

if DataProtection__KeysPath=relative/keys \
    sh "$entrypoint" true >/dev/null 2>&1
then
    fail "entrypoint accepted a relative key path."
fi

statements_path="$temp_dir/statements"
mkdir "$statements_path"
mkdir "$statements_path/account"
printf '%s\n' statement > "$statements_path/account/statement.pdf"
chmod 0777 "$statements_path" "$statements_path/account"
chmod 0666 "$statements_path/account/statement.pdf"

DataProtection__KeysPath="$keys_path" \
BillStatementStorage__RootPath="$statements_path" \
    sh "$entrypoint" \
    sh -c '
        [ "$(stat -c %a "$BillStatementStorage__RootPath")" = 700 ]
        [ "$(stat -c %a "$BillStatementStorage__RootPath/account")" = 700 ]
        [ "$(stat -c %a "$BillStatementStorage__RootPath/account/statement.pdf")" = 600 ]
        touch "$BillStatementStorage__RootPath/account/new-statement.pdf"
        [ "$(stat -c %a "$BillStatementStorage__RootPath/account/new-statement.pdf")" = 600 ]
    ' ||
    fail "entrypoint did not enforce statement directory, file, and creation permissions."

linked_statement="$statements_path/account/linked.pdf"
ln -s "$statements_path/account/statement.pdf" "$linked_statement"

if DataProtection__KeysPath="$keys_path" \
   BillStatementStorage__RootPath="$statements_path" \
    sh "$entrypoint" true >/dev/null 2>&1
then
    fail "entrypoint accepted a symbolic link inside statement storage."
fi
rm "$linked_statement"

if DataProtection__KeysPath="$keys_path" \
   BillStatementStorage__RootPath=relative/statements \
    sh "$entrypoint" true >/dev/null 2>&1
then
    fail "entrypoint accepted a relative statement storage path."
fi

printf '%s\n' 'Data Protection and statement permission tests passed.'
