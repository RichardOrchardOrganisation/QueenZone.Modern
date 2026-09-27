#!/usr/bin/env bash
set -euo pipefail

apk="${1:-}"
if [[ -z "$apk" ]] || [[ ! -f "$apk" ]]; then
  echo "Usage: $0 <release-apk>" >&2
  exit 2
fi

avd_name="${ANDROID_RELEASE_AVD:-QueenZone_CI_API_36}"
emulator_port="${ANDROID_RELEASE_EMULATOR_PORT:-5556}"
serial="emulator-${emulator_port}"
system_image="${ANDROID_RELEASE_SYSTEM_IMAGE:-system-images;android-36;google_apis;arm64-v8a}"
results_dir="src/QueenZone.Mobile/maestro-results"
rm -rf "$results_dir"
mkdir -p "$results_dir"
printf 'avd=%s\nserial=%s\n' "$avd_name" "$serial" > "$results_dir/harness.log"

emulator_bin="$(command -v emulator || true)"
if [[ -z "$emulator_bin" ]] && [[ -n "${ANDROID_HOME:-}" ]]; then
  emulator_bin="${ANDROID_HOME}/emulator/emulator"
fi
if [[ -z "$emulator_bin" ]] || [[ ! -x "$emulator_bin" ]]; then
  echo "Android emulator is not installed or is not on PATH." >&2
  exit 1
fi
if ! command -v adb >/dev/null; then
  echo "adb is not installed or is not on PATH." >&2
  exit 1
fi
other_emulators="$(adb devices | awk -v target="$serial" '$1 ~ /^emulator-/ && $2 == "device" && $1 != target { print $1 }')"
if [[ -n "$other_emulators" ]]; then
  echo "Another Android emulator is active; close it before P0 release acceptance:" >&2
  printf '%s\n' "$other_emulators" >&2
  exit 1
fi
if ! "$emulator_bin" -list-avds | grep -Fxq "$avd_name"; then
  avdmanager_bin="$(command -v avdmanager || true)"
  if [[ -z "$avdmanager_bin" ]] && [[ -n "${ANDROID_HOME:-}" ]]; then
    avdmanager_bin="${ANDROID_HOME}/cmdline-tools/latest/bin/avdmanager"
  fi
  if [[ -z "$avdmanager_bin" ]] || [[ ! -x "$avdmanager_bin" ]]; then
    echo "AVD '$avd_name' is absent and avdmanager is not available to create it." >&2
    exit 1
  fi
  echo "Creating isolated Android AVD '$avd_name' from '$system_image'."
  printf 'no\n' | "$avdmanager_bin" create avd \
    --force \
    --name "$avd_name" \
    --package "$system_image" \
    --device pixel_8
fi

emulator_pid=""
cleanup() {
  local status=$?
  if [[ -n "$emulator_pid" ]] && kill -0 "$emulator_pid" 2>/dev/null; then
    ANDROID_SERIAL="$serial" adb emu kill >/dev/null 2>&1 || true
    kill "$emulator_pid" 2>/dev/null || true
    wait "$emulator_pid" 2>/dev/null || true
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

# Port 5556 is reserved for this job so an interactive emulator on the Mac is
# not selected or cleared by the release suite.
ANDROID_SERIAL="$serial" adb emu kill >/dev/null 2>&1 || true
"$emulator_bin" \
  -avd "$avd_name" \
  -port "$emulator_port" \
  -no-window \
  -noaudio \
  -no-boot-anim \
  -no-snapshot-save \
  -gpu host \
  > "$results_dir/emulator-self-hosted.log" 2>&1 &
emulator_pid=$!

ready=false
for _ in $(seq 1 120); do
  if ! kill -0 "$emulator_pid" 2>/dev/null; then
    echo "Android emulator exited before boot completed." >&2
    break
  fi
  if [[ "$(ANDROID_SERIAL="$serial" adb get-state 2>/dev/null || true)" = "device" ]] \
    && [[ "$(ANDROID_SERIAL="$serial" adb shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]]; then
    ready=true
    break
  fi
  sleep 2
done
if [[ "$ready" != true ]]; then
  tail -n 200 "$results_dir/emulator-self-hosted.log" >&2 || true
  exit 1
fi

export ANDROID_SERIAL="$serial"
export MAESTRO_TARGET_DEVICE="$serial"
./scripts/run-mobile-device-smoke.sh \
  --platform android \
  --suite release \
  --skip-build \
  --no-build \
  --apk "$apk"
