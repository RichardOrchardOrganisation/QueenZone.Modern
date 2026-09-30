#!/usr/bin/env bash
# Preinstall Expo SDK 57's NDK so Gradle assemble does not download it
# during configuration (#1247). Verify source.properties, then export
# ANDROID_NDK_HOME. Do not change android.ndkVersion.
set -euo pipefail

NDK_VERSION="${ANDROID_NDK_VERSION:-27.1.12297006}"
SDK_ROOT="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"

if [[ -z "$SDK_ROOT" ]]; then
  echo "::error::ANDROID_HOME / ANDROID_SDK_ROOT is not set; cannot preinstall NDK ${NDK_VERSION}."
  exit 1
fi

if [[ ! -d "$SDK_ROOT" ]]; then
  echo "::error::Android SDK root does not exist: $SDK_ROOT"
  exit 1
fi

find_sdkmanager() {
  local candidate
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

SDKMANAGER="$(find_sdkmanager)" || {
  echo "::error::sdkmanager not found under $SDK_ROOT"
  ls -la "$SDK_ROOT/cmdline-tools" 2>/dev/null || true
  exit 1
}

NDK_HOME="$SDK_ROOT/ndk/$NDK_VERSION"

ndk_ok() {
  [[ -f "$NDK_HOME/source.properties" ]] \
    && grep -Eq "Pkg\\.Revision[[:space:]]*=[[:space:]]*${NDK_VERSION}" "$NDK_HOME/source.properties"
}

if ndk_ok; then
  echo "NDK ${NDK_VERSION} already installed at $NDK_HOME"
else
  echo "Preinstalling ndk;${NDK_VERSION} via $SDKMANAGER (sdk_root=$SDK_ROOT)"
  # Licenses are already accepted on GitHub-hosted runners; ignore SIGPIPE from yes.
  yes | "$SDKMANAGER" --sdk_root="$SDK_ROOT" --licenses >/dev/null || true

  attempt=1
  max=3
  while [[ "$attempt" -le "$max" ]]; do
    echo "sdkmanager --install ndk;${NDK_VERSION} (attempt ${attempt}/${max})"
    set +e
    "$SDKMANAGER" --sdk_root="$SDK_ROOT" --install "ndk;${NDK_VERSION}"
    status=$?
    set -e
    if [[ "$status" -eq 0 ]] && ndk_ok; then
      break
    fi
    echo "NDK install attempt ${attempt} failed (exit ${status}); removing partial tree"
    rm -rf "$NDK_HOME"
    rm -rf "$SDK_ROOT/.temp" "$SDK_ROOT/.downloadIntermediates" 2>/dev/null || true
    if [[ "$attempt" -eq "$max" ]]; then
      echo "::error::Failed to install ndk;${NDK_VERSION} after ${max} attempts"
      exit 1
    fi
    attempt=$((attempt + 1))
    sleep $((attempt * 4))
  done
fi

if ! ndk_ok; then
  echo "::error::NDK source.properties missing or Pkg.Revision is not ${NDK_VERSION} at $NDK_HOME"
  if [[ -f "$NDK_HOME/source.properties" ]]; then
    cat "$NDK_HOME/source.properties"
  fi
  exit 1
fi

echo "=== $NDK_HOME/source.properties ==="
cat "$NDK_HOME/source.properties"

if [[ -n "${GITHUB_ENV:-}" ]]; then
  echo "ANDROID_NDK_HOME=$NDK_HOME" >> "$GITHUB_ENV"
fi
export ANDROID_NDK_HOME="$NDK_HOME"
echo "ANDROID_NDK_HOME=$NDK_HOME"
