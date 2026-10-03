#!/bin/sh

set -eu

fail()
{
    printf '%s\n' "Host hardening verification failed: $1" >&2
    exit 1
}

[ "$(id -u)" -eq 0 ] ||
    fail "run this verifier as root so effective SSH and host-update state can be inspected."

deployment_user=${FULLWORTH_DEPLOYMENT_USER:-deploy}

id "$deployment_user" >/dev/null 2>&1 ||
    fail "the dedicated deployment account does not exist."

[ "$(id -u "$deployment_user")" -ne 0 ] ||
    fail "the deployment account must not be root."

command -v sshd >/dev/null 2>&1 ||
    fail "OpenSSH server is not installed."

sshd_effective="$(sshd -T 2>/dev/null)" ||
    fail "effective sshd configuration could not be read."

sshd_value()
{
    key=$1
    printf '%s\n' "$sshd_effective" |
        awk -v key="$key" '$1 == key { print $2; exit }'
}

require_sshd_value()
{
    key=$1
    expected=$2
    actual="$(sshd_value "$key")"
    [ "$actual" = "$expected" ] ||
        fail "effective sshd setting $key must be $expected."
}

require_sshd_value permitrootlogin no
require_sshd_value passwordauthentication no
require_sshd_value kbdinteractiveauthentication no
require_sshd_value permitemptypasswords no
require_sshd_value pubkeyauthentication yes
require_sshd_value allowtcpforwarding no
require_sshd_value x11forwarding no

max_auth_tries="$(sshd_value maxauthtries)"
case "$max_auth_tries" in
    ''|*[!0-9]*)
        fail "effective sshd maxauthtries is not numeric."
        ;;
esac
[ "$max_auth_tries" -le 4 ] ||
    fail "effective sshd maxauthtries must be 4 or lower."

login_grace_time="$(sshd_value logingracetime)"
case "$login_grace_time" in
    ''|*[!0-9]*)
        fail "effective sshd logingracetime is not expressed as seconds."
        ;;
esac
[ "$login_grace_time" -le 60 ] ||
    fail "effective sshd logingracetime must be 60 seconds or lower."

docker_group="$(getent group docker 2>/dev/null || true)"
[ -n "$docker_group" ] ||
    fail "the docker group is missing."

docker_members="$(printf '%s\n' "$docker_group" | awk -F: '{ print $4 }')"
found_deployment_user=false

old_ifs=$IFS
IFS=,
for member in $docker_members
do
    [ -n "$member" ] || continue

    if [ "$member" = "$deployment_user" ]; then
        found_deployment_user=true
        continue
    fi

    fail "the docker group contains an account other than the dedicated deployment account."
done
IFS=$old_ifs

[ "$found_deployment_user" = true ] ||
    fail "the deployment account is not the sole explicit docker-group member."

command -v dpkg-query >/dev/null 2>&1 ||
    fail "dpkg-query is unavailable; this verifier targets the supported Ubuntu production host."

unattended_status="$(dpkg-query -W -f='${Status}' unattended-upgrades 2>/dev/null || true)"
[ "$unattended_status" = "install ok installed" ] ||
    fail "unattended-upgrades is not installed."

for timer in apt-daily.timer apt-daily-upgrade.timer
do
    systemctl is-enabled "$timer" >/dev/null 2>&1 ||
        fail "$timer is not enabled."

    systemctl is-active "$timer" >/dev/null 2>&1 ||
        fail "$timer is not active."
done

if [ -e /var/run/reboot-required ]; then
    fail "the host requires a reboot to finish applying updates."
fi

printf '%s\n' "FullWorth host operator hardening verification passed."
