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

exec "$@"
