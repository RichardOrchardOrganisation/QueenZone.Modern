#!/usr/bin/env bash
# Boot an isolated Android emulator on the self-hosted Mac and run Maestro
# device smoke (#1454). Defaults are distinct from the P0 release wrapper
# (QueenZone_CI_API_36 on port 5556). Do not reuse that AVD or port.
#
# Usage (repo root):
#   ./scripts/run-mobile-android-smoke-self-hosted.sh <release-apk> [--prove-failure]
#   ./scripts/run-mobile-android-smoke-self-hosted.sh <release-apk> --suite proof --feature mobile.photos.viewer
set -euo pipefail

apk="${1:-}"
if [[ -z "$apk" ]] || [[ ! -f "$apk" ]]; then
  echo "Usage: $0 <release-apk> [--prove-failure]" >&2
  exit 2
fi
shift

avd_name="${ANDROID_SMOKE_AVD:-QueenZone_CI_Smoke_API_36}"
emulator_port="${ANDROID_SMOKE_EMULATOR_PORT:-5558}"
serial="emulator-${emulator_port}"
system_image="${ANDROID_SMOKE_SYSTEM_IMAGE:-system-images;android-36;google_apis;arm64-v8a}"
force_avd="${ANDROID_SMOKE_FORCE_AVD:-}"
results_dir="src/QueenZone.Mobile/maestro-results"
rm -rf "$results_dir"
mkdir -p "$results_dir"
printf 'avd=%s\nserial=%s\n' "$avd_name" "$serial" > "$results_dir/harness.log"

script_dir="$(cd "$(dirname "$0")" && pwd)"
sdk_root="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"

# Package form: system-images;android-<api>;<target>;<arch>
image_api="${system_image#system-images;android-}"
image_api="${image_api%%;*}"
image_rest="${system_image#system-images;android-"${image_api}";}"
image_target="${image_rest%%;*}"
image_arch="${image_rest#"${image_target};"}"
image_dir="${sdk_root}/system-images/android-${image_api}/${image_target}/${image_arch}"

infra_fail() {
  local class="$1"
  local reason="$2"
  local marker="$results_dir/android-${class//_/-}"
  echo "infra: Android smoke ${class}: ${reason}" >&2
  mkdir -p "$results_dir"
  {
    echo "class=${class}"
    echo "reason=${reason}"
    echo "avd=${avd_name}"
    echo "package=${system_image}"
    echo "image_dir=${image_dir}"
  } > "$marker"
  echo "android_failure_class=${class} package=${system_image}" >> "$results_dir/harness.log"
  if [[ -d "$image_dir" ]]; then
    echo "--- system image dir ${image_dir} ---" >&2
    ls -la "$image_dir" >&2 || true
  else
    echo "--- system image dir missing: ${image_dir} ---" >&2
  fi
  if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
    {
      echo "### infra: Android smoke ${class}"
      echo
      echo "${reason}"
      echo
      echo "- AVD: \`${avd_name}\`"
      echo "- package: \`${system_image}\`"
      echo "- image dir: \`${image_dir}\`"
    } >> "$GITHUB_STEP_SUMMARY"
  fi
  exit 1
}

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
  echo "Another Android emulator is active; close it before Android device smoke:" >&2
  printf '%s\n' "$other_emulators" >&2
  exit 1
fi

if [[ -z "$sdk_root" ]]; then
  infra_fail "boot_failure" "ANDROID_SDK_ROOT / ANDROID_HOME is not set; cannot provision ${system_image}"
fi
if [[ "$system_image" != system-images\;android-* ]] \
  || [[ -z "$image_api" || -z "$image_target" || -z "$image_arch" || "$image_arch" == "$image_rest" ]]; then
  infra_fail "boot_failure" "unsupported ANDROID_SMOKE_SYSTEM_IMAGE '${system_image}'"
fi

# Idempotent install of the exact AVD system image. Do not upgrade the
# self-hosted emulator package; the Mac already pins that toolchain.
ANDROID_SDK_ROOT="$sdk_root" \
ANDROID_HOME="$sdk_root" \
ANDROID_EMULATOR_API_LEVEL="$image_api" \
ANDROID_EMULATOR_TARGET="$image_target" \
ANDROID_EMULATOR_ARCH="$image_arch" \
ANDROID_EMULATOR_INSTALL_EMULATOR=0 \
  "$script_dir/preinstall-android-emulator.sh" --results-dir "$results_dir"

if [[ ! -d "$image_dir" || ! -s "$image_dir/system.img" ]]; then
  infra_fail "boot_failure" "system image ${system_image} is missing or empty at ${image_dir} after sdkmanager install"
fi

create_avd() {
  local avdmanager_bin
  avdmanager_bin="$(command -v avdmanager || true)"
  if [[ -z "$avdmanager_bin" ]] && [[ -n "${ANDROID_HOME:-}" ]]; then
    avdmanager_bin="${ANDROID_HOME}/cmdline-tools/latest/bin/avdmanager"
  fi
  if [[ -z "$avdmanager_bin" ]] || [[ ! -x "$avdmanager_bin" ]]; then
    infra_fail "boot_failure" "AVD '${avd_name}' is absent and avdmanager is not available to create it."
  fi
  if [[ ! -s "$image_dir/system.img" ]]; then
    infra_fail "boot_failure" "refusing to create AVD '${avd_name}': ${image_dir}/system.img is missing"
  fi
  echo "Creating isolated Android smoke AVD '$avd_name' from '$system_image'."
  set +e
  printf 'no\n' | "$avdmanager_bin" create avd \
    --force \
    --name "$avd_name" \
    --package "$system_image" \
    --device pixel_8
  local status=$?
  set -e
  if [[ "$status" -ne 0 ]]; then
    infra_fail "boot_failure" "avdmanager failed to create AVD '${avd_name}' from '${system_image}' (exit ${status})"
  fi
}

need_create=false
if [[ "$force_avd" = "1" ]] || ! "$emulator_bin" -list-avds | grep -Fxq "$avd_name"; then
  need_create=true
fi

if [[ "$need_create" == true ]]; then
  create_avd
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

# Port 5558 is reserved for scheduled/dispatch Android smoke so it cannot
# attach to the P0 release AVD on 5556 or an interactive emulator on 5554.
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
  if ! kill -0 "$emulator_pid" 2>/dev/null \
    || grep -q "No initial system image for this configuration" "$results_dir/emulator-self-hosted.log" 2>/dev/null; then
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
  boot_reason="Android emulator exited before boot completed or boot timed out for AVD '${avd_name}'."
  if grep -q "No initial system image for this configuration" "$results_dir/emulator-self-hosted.log" 2>/dev/null; then
    boot_reason="No initial system image for this configuration! AVD '${avd_name}' package '${system_image}' at ${image_dir}"
  fi
  infra_fail "boot_failure" "$boot_reason"
fi

export ANDROID_SERIAL="$serial"
export MAESTRO_TARGET_DEVICE="$serial"
suite="${DEVICE_SUITE:-smoke}"
feature="${DEVICE_FEATURE:-}"
smoke_args=(--platform android --suite "$suite" --skip-build --no-build --apk "$apk")
if [[ "$suite" = "proof" ]]; then
  if [[ -z "$feature" ]]; then
    echo "DEVICE_FEATURE is required when DEVICE_SUITE=proof." >&2
    exit 2
  fi
  smoke_args+=(--feature "$feature")
fi
./scripts/run-mobile-device-smoke.sh "${smoke_args[@]}" "$@"
