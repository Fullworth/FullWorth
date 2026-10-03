#!/bin/sh

set -eu

# Data Protection key XML contains root cryptographic material. Keep both
# newly created and restored rings private to the non-root application UID.
umask 077

keys_path=${DataProtection__KeysPath:-}

if [ -z "$keys_path" ]; then
    echo "DataProtection__KeysPath must be configured." >&2
    exit 64
fi

case "$keys_path" in
    /*) ;;
    *)
        echo "DataProtection__KeysPath must be absolute." >&2
        exit 64
        ;;
esac

if [ -L "$keys_path" ]; then
    echo "The Data Protection key directory must not be a symbolic link." >&2
    exit 77
fi

mkdir -p -- "$keys_path"

if [ -L "$keys_path" ] || [ ! -d "$keys_path" ]; then
    echo "The Data Protection key path must resolve to a real directory." >&2
    exit 77
fi

unexpected_entry="$(find "$keys_path" -mindepth 1 -maxdepth 1 ! -type f -print -quit)"

if [ -n "$unexpected_entry" ]; then
    echo "The Data Protection key directory contains a non-regular entry." >&2
    exit 77
fi

chmod 0700 "$keys_path"
find "$keys_path" -mindepth 1 -maxdepth 1 -type f -exec chmod 0600 {} \;

statements_path=${BillStatementStorage__RootPath:-}

if [ -n "$statements_path" ]; then
    case "$statements_path" in
        /*) ;;
        *)
            echo "BillStatementStorage__RootPath must be absolute." >&2
            exit 64
            ;;
    esac

    if [ -L "$statements_path" ]; then
        echo "The statement storage directory must not be a symbolic link." >&2
        exit 77
    fi

    mkdir -p -- "$statements_path"

    if [ -L "$statements_path" ] || [ ! -d "$statements_path" ]; then
        echo "The statement storage path must resolve to a real directory." >&2
        exit 77
    fi

    unexpected_statement_entry="$(
        find "$statements_path" -xdev -mindepth 1 ! -type d ! -type f -print -quit
    )"

    if [ -n "$unexpected_statement_entry" ]; then
        echo "Statement storage contains a symbolic link or non-regular entry." >&2
        exit 77
    fi

    find "$statements_path" -xdev -type d -exec chmod 0700 {} \;
    find "$statements_path" -xdev -type f -exec chmod 0600 {} \;
fi

exec "$@"
