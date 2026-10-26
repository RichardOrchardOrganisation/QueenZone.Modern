#!/usr/bin/env bash
# Preinstall the hosted Android emulator + system image before
# reactivecircus/android-emulator-runner runs, so a corrupt or truncated
# download ("Error on ZipFile unknown archive", #2351) is retried instead of
# killing the journeys job. The runner's own sdkmanager call is then a no-op.
#
# On final failure writes <results-dir>/android-install-failure and prints an
# "install failure" error. That is never a selector or assertion miss.
#
# Usage:
#   ./scripts/preinstall-android-emulator.sh [--results-dir DIR]
# Env:
#   ANDROID_EMULATOR_API_LEVEL (34), ANDROID_EMULATOR_TARGET (google_apis),
#   ANDROID_EMULATOR_ARCH (x86_64), ANDROID_EMULATOR_CHANNEL (0 = stable),
#   ANDROID_EMULATOR_INSTALL_ATTEMPTS (3),
#   ANDROID_EMULATOR_INSTALL_BACKOFF_SECONDS (10; attempt n waits n*backoff),
#   QZ_SDKMANAGER (override sdkmanager path; tests only).
set -euo pipefail

API_LEVEL="${ANDROID_EMULATOR_API_LEVEL:-34}"
TARGET="${ANDROID_EMULATOR_TARGET:-google_apis}"
ARCH="${ANDROID_EMULATOR_ARCH:-x86_64}"
CHANNEL="${ANDROID_EMULATOR_CHANNEL:-0}"
MAX_ATTEMPTS="${ANDROID_EMULATOR_INSTALL_ATTEMPTS:-3}"
BACKOFF="${ANDROID_EMULATOR_INSTALL_BACKOFF_SECONDS:-10}"
SDK_ROOT="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
results_dir=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --results-dir)
      results_dir="${2:?--results-dir needs a value}"
      shift 2
      ;;
    *)
      echo "::error::Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

PACKAGE="system-images;android-${API_LEVEL};${TARGET};${ARCH}"

install_failure() {
  local reason="$1"
  echo "::error::Android emulator install failure: ${reason}. The emulator never booted, so no journey ran. Not a selector or assertion miss." >&2
  if [[ -n "$results_dir" ]]; then
    mkdir -p "$results_dir"
    {
      echo "class=install_failure"
      echo "package=${PACKAGE}"
      echo "reason=${reason}"
      echo "attempts=${MAX_ATTEMPTS}"
    } > "$results_dir/android-install-failure"
    echo "android_failure_class=install_failure package=${PACKAGE}" >> "$results_dir/harness.log"
  fi
  exit 1
}

if [[ -z "$SDK_ROOT" ]]; then
  install_failure "ANDROID_SDK_ROOT / ANDROID_HOME is not set"
fi
if [[ ! -d "$SDK_ROOT" ]]; then
  install_failure "Android SDK root does not exist: $SDK_ROOT"
fi

find_sdkmanager() {
  local candidate
  if [[ -n "${QZ_SDKMANAGER:-}" ]]; then
    printf '%s\n' "$QZ_SDKMANAGER"
    return 0
  fi
  for candidate in \
    "$SDK_ROOT/cmdline-tools/latest/bin/sdkmanager" \
    "$SDK_ROOT/cmdline-tools/bin/sdkmanager"; do
    if [[ -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done
  if command -v sdkmanager >/dev/null 2>&1; then
    command -v sdkmanager
    return 0
  fi
  return 1
}

SDKMANAGER="$(find_sdkmanager)" || install_failure "sdkmanager not found under $SDK_ROOT"

IMAGE_DIR="$SDK_ROOT/system-images/android-${API_LEVEL}/${TARGET}/${ARCH}"

image_ok() {
  [[ -f "$IMAGE_DIR/source.properties" ]] \
    && grep -Eq "AndroidVersion\\.ApiLevel[[:space:]]*=[[:space:]]*${API_LEVEL}([^0-9]|$)" "$IMAGE_DIR/source.properties" \
    && [[ -s "$IMAGE_DIR/system.img" ]]
}

emulator_ok() {
  [[ -x "$SDK_ROOT/emulator/emulator" ]] && [[ -f "$SDK_ROOT/emulator/source.properties" ]]
}

clear_partial() {
  # A truncated zip leaves a half-extracted image and cached archives behind;
  # sdkmanager then reuses them. Drop both before the next attempt.
  rm -rf "$IMAGE_DIR"
  rm -rf "$SDK_ROOT/.temp" "$SDK_ROOT/.downloadIntermediates" 2>/dev/null || true
  rm -rf "${HOME}/.android/cache" 2>/dev/null || true
}

if image_ok && emulator_ok; then
  echo "${PACKAGE} and emulator already installed (cache hit or runner image) at $IMAGE_DIR"
  exit 0
fi

# Restored-from-cache but incomplete images must not be trusted.
if [[ -e "$IMAGE_DIR" ]] && ! image_ok; then
  echo "Discarding incomplete ${PACKAGE} at $IMAGE_DIR"
  clear_partial
fi

yes | "$SDKMANAGER" --sdk_root="$SDK_ROOT" --licenses >/dev/null 2>&1 || true

attempt=1
while [[ "$attempt" -le "$MAX_ATTEMPTS" ]]; do
  echo "sdkmanager --install emulator ${PACKAGE} --channel=${CHANNEL} (attempt ${attempt}/${MAX_ATTEMPTS})"
  log="$(mktemp)"
  set +e
  "$SDKMANAGER" --sdk_root="$SDK_ROOT" --install emulator "$PACKAGE" --channel="$CHANNEL" 2>&1 | tee "$log"
  status=${PIPESTATUS[0]}
  set -e
  # sdkmanager can print "Error on ZipFile" as a Warning and still exit 0.
  if [[ "$status" -eq 0 ]] && ! grep -qiE 'Error on ZipFile|unknown archive|An error occurred while preparing SDK package' "$log" \
    && image_ok && emulator_ok; then
    rm -f "$log"
    echo "Installed ${PACKAGE} and emulator on attempt ${attempt}"
    exit 0
  fi
  rm -f "$log"
  echo "Emulator/system image install attempt ${attempt} failed (exit ${status}); clearing partial download"
  clear_partial
  if [[ "$attempt" -eq "$MAX_ATTEMPTS" ]]; then
    break
  fi
  sleep $((attempt * BACKOFF))
  attempt=$((attempt + 1))
done

install_failure "sdkmanager could not install ${PACKAGE} after ${MAX_ATTEMPTS} attempts"
