#!/bin/sh

set -eu

artifact_dir=${1:-}
package_name=${2:-com.companyname.billwatch}
avd_name=fullworth-internal-ci
system_image="system-images;android-35;google_apis;x86_64"

fail()
{
    printf '%s\n' "Android APK emulator smoke failed: $1" >&2
    exit "${2:-1}"
}

[ -n "$artifact_dir" ] || fail "usage: $0 <artifact-directory> [package-name]" 64
[ -d "$artifact_dir" ] || fail "artifact directory does not exist: $artifact_dir" 66

case "$package_name" in
    *[!A-Za-z0-9._]*) fail "package name contains unsupported characters." 64 ;;
esac

apk_count=$(find "$artifact_dir" -maxdepth 1 -type f -name '*.apk' | wc -l | tr -d ' ')
[ "$apk_count" -eq 1 ] || fail "expected exactly one APK artifact, found $apk_count." 65

digest_count=$(find "$artifact_dir" -maxdepth 1 -type f -name '*.apk.sha256' | wc -l | tr -d ' ')
[ "$digest_count" -eq 1 ] || fail "expected exactly one APK SHA-256 file, found $digest_count." 65

apk_path=$(find "$artifact_dir" -maxdepth 1 -type f -name '*.apk' -print -quit)
digest_path=$(find "$artifact_dir" -maxdepth 1 -type f -name '*.apk.sha256' -print -quit)

(
    cd "$artifact_dir"
    sha256sum -c "$(basename "$digest_path")"
) || fail "APK SHA-256 verification failed." 65

for command_name in adb emulator sdkmanager avdmanager
do
    command -v "$command_name" >/dev/null 2>&1 ||
        fail "$command_name is required on the Android smoke runner." 69
done

if [ -e /dev/kvm ]; then
    sudo chmod a+rw /dev/kvm
fi

yes | sdkmanager --licenses >/dev/null 2>&1 || true
sdkmanager "$system_image" >/dev/null

printf '%s\n' no |
    avdmanager create avd         --force         --name "$avd_name"         --package "$system_image"         --device pixel_6 >/dev/null

emulator_log="${RUNNER_TEMP:-/tmp}/fullworth-android-emulator.log"

emulator     -avd "$avd_name"     -no-window     -no-audio     -no-boot-anim     -no-snapshot     -wipe-data     -gpu swiftshader_indirect     >"$emulator_log" 2>&1 &
emulator_pid=$!

cleanup()
{
    status=$?
    trap - EXIT HUP INT TERM

    adb emu kill >/dev/null 2>&1 || true
    kill "$emulator_pid" >/dev/null 2>&1 || true
    wait "$emulator_pid" >/dev/null 2>&1 || true

    if [ "$status" -ne 0 ]; then
        printf '%s\n' "Sanitized emulator log tail:" >&2
        tail -n 120 "$emulator_log" >&2 || true
    fi

    exit "$status"
}

trap cleanup EXIT HUP INT TERM

adb wait-for-device

booted=false
attempt=0
while [ "$attempt" -lt 90 ]
do
    completed=$(adb shell getprop sys.boot_completed 2>/dev/null | tr -d '\r' || true)
    if [ "$completed" = 1 ]; then
        booted=true
        break
    fi

    if ! kill -0 "$emulator_pid" 2>/dev/null; then
        fail "Android emulator exited before boot completed." 70
    fi

    attempt=$((attempt + 1))
    sleep 2
done

[ "$booted" = true ] || fail "Android emulator did not finish booting." 70

adb shell input keyevent 82 >/dev/null 2>&1 || true
adb shell settings put global window_animation_scale 0 >/dev/null
adb shell settings put global transition_animation_scale 0 >/dev/null
adb shell settings put global animator_duration_scale 0 >/dev/null

adb install "$apk_path" >/dev/null ||
    fail "signed APK could not be installed." 65

installed_path=$(adb shell pm path "$package_name" 2>/dev/null | tr -d '\r')
case "$installed_path" in
    package:*) ;;
    *) fail "installed package could not be resolved." 65 ;;
esac

adb logcat -c
adb shell monkey     -p "$package_name"     -c android.intent.category.LAUNCHER     1 >/dev/null ||
    fail "launcher intent could not start the FullWorth package." 70

sleep 8

app_pid=$(adb shell pidof "$package_name" 2>/dev/null | tr -d '\r' || true)
[ -n "$app_pid" ] || fail "FullWorth process is not alive after launch." 70

activity_state=$(adb shell dumpsys activity activities 2>/dev/null || true)
printf '%s\n' "$activity_state" |
    grep -E 'mResumedActivity|topResumedActivity' |
    grep -F "$package_name" >/dev/null ||
    fail "FullWorth is not the resumed foreground activity after launch." 70

runtime_log=$(adb logcat -d AndroidRuntime:E '*:S' 2>/dev/null || true)
if printf '%s\n' "$runtime_log" |
    grep -A 20 -F 'FATAL EXCEPTION' |
    grep -F "Process: $package_name" >/dev/null
then
    fail "AndroidRuntime reported a fatal FullWorth exception after launch." 70
fi

printf '%s\n'     "FullWorth Android APK installed and launched successfully in the emulator."     "Package: $package_name"     "Process: $app_pid"
