#!/bin/sh

set -eu

running_services=$(cat)

service_state()
{
    service_name=$1

    if printf '%s\n' "$running_services" | grep -Fqx "$service_name"; then
        printf '%s' running
    else
        printf '%s' stopped
    fi
}

printf 'api=%s web=%s edge=%s\n' \
    "$(service_state api)" \
    "$(service_state web)" \
    "$(service_state edge)"
