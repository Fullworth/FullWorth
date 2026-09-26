#!/bin/sh

set -eu

app_path=${1:-}
expected_bundle_id=${2:-}

fail()
{
    printf '%s\n' "iOS simulator smoke failed: $1" >&2
    exit 1
}

[ -n "$app_path" ] ||
    fail "app path is required."
[ -n "$expected_bundle_id" ] ||
    fail "expected bundle ID is required."
[ -d "$app_path" ] ||
    fail "app bundle does not exist."

for command_name in xcrun python3
do
    command -v "$command_name" >/dev/null 2>&1 ||
        fail "$command_name is required."
done

info_plist="$app_path/Info.plist"
[ -f "$info_plist" ] ||
    fail "app Info.plist is missing."

actual_bundle_id=$(
    /usr/libexec/PlistBuddy         -c 'Print :CFBundleIdentifier'         "$info_plist"
)

[ "$actual_bundle_id" = "$expected_bundle_id" ] ||
    fail "built bundle ID does not match the expected identifier."

runtime_id=$(
    xcrun simctl list runtimes --json |
        python3 -c '
import json, sys
data=json.load(sys.stdin)
items=[
    r for r in data.get("runtimes", [])
    if r.get("isAvailable") and
       r.get("identifier", "").startswith("com.apple.CoreSimulator.SimRuntime.iOS-26-0")
]
if not items:
    raise SystemExit(1)
items.sort(key=lambda r: tuple(int(x) for x in r.get("version", "0").split(".")))
print(items[-1]["identifier"])
'
) || fail "no available iOS Simulator runtime was found."

device_type_id=$(
    xcrun simctl list devicetypes --json |
        python3 -c '
import json, sys
data=json.load(sys.stdin)
items=[
    d for d in data.get("devicetypes", [])
    if d.get("name", "").startswith("iPhone")
]
if not items:
    raise SystemExit(1)
preferred=[
    d for d in items
    if "Pro" in d.get("name", "")
]
choice=(preferred or items)[-1]
print(choice["identifier"])
'
) || fail "no iPhone Simulator device type was found."

device_name="FullWorth-CI-$$"
udid=$(
    xcrun simctl create         "$device_name"         "$device_type_id"         "$runtime_id"
) || fail "could not create the iOS Simulator device."

cleanup()
{
    if [ -n "${udid:-}" ]; then
        xcrun simctl shutdown "$udid" >/dev/null 2>&1 || true
        xcrun simctl delete "$udid" >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT HUP INT TERM

xcrun simctl boot "$udid" ||
    fail "could not boot the iOS Simulator device."

attempt=0
while [ "$attempt" -lt 90 ]
do
    state=$(
        xcrun simctl list devices --json |
            python3 -c '
import json, sys
udid=sys.argv[1]
data=json.load(sys.stdin)
for devices in data.get("devices", {}).values():
    for device in devices:
        if device.get("udid") == udid:
            print(device.get("state", ""))
            raise SystemExit(0)
raise SystemExit(1)
' "$udid"
    ) || state=""

    if [ "$state" = "Booted" ]; then
        break
    fi

    attempt=$((attempt + 1))
    sleep 2
done

[ "$state" = "Booted" ] ||
    fail "iOS Simulator did not reach Booted state within 180 seconds."

xcrun simctl install "$udid" "$app_path" ||
    fail "FullWorth could not be installed into the iOS Simulator."

launch_output=$(
    xcrun simctl launch "$udid" "$expected_bundle_id"
) || fail "FullWorth could not be launched in the iOS Simulator."

printf '%s\n' "$launch_output" |
    grep -Fq "$expected_bundle_id:" ||
    fail "simctl launch did not return the expected FullWorth process identifier."

sleep 3

xcrun simctl get_app_container     "$udid"     "$expected_bundle_id"     app >/dev/null ||
    fail "FullWorth app container was not available after launch."

printf '%s\n'     "FullWorth iOS simulator install/launch smoke passed for $expected_bundle_id."
