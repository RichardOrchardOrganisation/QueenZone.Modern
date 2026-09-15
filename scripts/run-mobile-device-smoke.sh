#!/usr/bin/env bash
# Boot the Testing contract host and run the Maestro device smoke suite
# against a Release-embedded build baked at the loopback Testing origin
# (#872 Option A, #1322). Never Debug / expo-dev-client — that waits for Metro.
# Do not start a packager here.
#
# Never points Testing at a real database, blob store, live site, or OAuth.
# Usage (repo root):
#   ./scripts/run-mobile-device-smoke.sh --platform android
#   ./scripts/run-mobile-device-smoke.sh --platform ios
#   ./scripts/run-mobile-device-smoke.sh --platform android --skip-build --apk path/to/app-release.apk
#   ./scripts/run-mobile-device-smoke.sh --platform android --prove-failure
#   ./scripts/run-mobile-device-smoke.sh --platform android --suite journeys
#   ./scripts/run-mobile-device-smoke.sh --platform android --suite release
#   ./scripts/run-mobile-device-smoke.sh --dump-android-host
#   ./scripts/run-mobile-device-smoke.sh --self-test-adb-timeout
#
# Maestro selector and assertion failures are not retried. One Android device
# transport failure (DeviceServerDied / emulator gone) or pre-flow iOS
# driver-startup failure may retry after device recovery. android-transport-death
# is written only when the emulator is gone, ADB recover times out, or the
# in-process retry itself dies as transport, so CI can boot a fresh emulator.
# A selector miss on a live emulator must not retrigger that outer restart.
# Android smoke CI recreates an isolated AVD on that one outer retry (#1454);
# this script does not add a third attempt. A wedged `adb start-server` is
# hard-capped (~45s) so the step fails instead of hanging until the 90m job
# cancel (#1529).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

platform=""
skip_build=false
skip_host=false
no_build_host=false
prove_failure=false
dump_android_host=false
self_test_adb_timeout=false
suite="smoke"
apk=""
app=""
port="${SMOKE_PORT:-5098}"
results_dir="${SMOKE_RESULTS_DIR:-$root/src/QueenZone.Mobile/maestro-results}"
fixture="${QUEENZONE_MOBILE_CONTRACT_FIXTURE:-$root/src/QueenZone.Mobile/contracts/host.json}"
host_log="${SMOKE_HOST_LOG:-$results_dir/contract-host.log}"
android_logcat_pid=""
android_watchdog_pid=""

usage() {
  sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//'
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --platform)
      platform="${2:-}"
      shift 2
      ;;
    --skip-build)
      skip_build=true
      shift
      ;;
    --skip-host)
      skip_host=true
      shift
      ;;
    --no-build)
      no_build_host=true
      shift
      ;;
    --prove-failure)
      prove_failure=true
      shift
      ;;
    --suite)
      suite="${2:-}"
      shift 2
      ;;
    --apk)
      apk="${2:-}"
      shift 2
      ;;
    --app)
      app="${2:-}"
      shift 2
      ;;
    --port)
      port="${2:-}"
      shift 2
      ;;
    --results-dir)
      results_dir="${2:-}"
      shift 2
      ;;
    --dump-android-host)
      dump_android_host=true
      shift
      ;;
    --self-test-adb-timeout)
      self_test_adb_timeout=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [ "$dump_android_host" != true ] && [ "$self_test_adb_timeout" != true ] && [ "$platform" != "android" ] && [ "$platform" != "ios" ]; then
  echo "--platform android|ios is required." >&2
  exit 2
fi

if [ "$suite" != "smoke" ] && [ "$suite" != "journeys" ] && [ "$suite" != "release" ]; then
  echo "--suite smoke|journeys|release is required (default smoke)." >&2
  exit 2
fi

if [ "$prove_failure" = true ] && [ "$suite" != "smoke" ]; then
  echo "--prove-failure is only valid with --suite smoke." >&2
  exit 2
fi

if [ "$platform" = "ios" ] && [ "$(uname -s)" != "Darwin" ]; then
  echo "iOS Simulator smoke requires macOS. Document CI-only verification on Linux agents." >&2
  exit 2
fi

unset ConnectionStrings__QueenZoneLegacy || true
unset ConnectionStrings__BlobStorage || true
unset ConnectionStrings__SqlServerTest || true

mkdir -p "$results_dir"

# Hard deadline for ADB recover. A wedged `adb start-server` on the
# self-hosted Mac hung ~81m until the 90m job cancel, skipping classify and
# the existing fresh-AVD retry (#1529).
ANDROID_ADB_TIMEOUT_SECONDS="${ANDROID_ADB_TIMEOUT_SECONDS:-45}"
ANDROID_ADB_PROBE_TIMEOUT_SECONDS="${ANDROID_ADB_PROBE_TIMEOUT_SECONDS:-10}"

# Run a command with a hard deadline. Returns 124 on timeout (GNU timeout).
# Prefers timeout/gtimeout; falls back to background + wait so macOS runners
# without coreutils still fail-fast.
run_with_timeout() {
  local seconds="$1"
  shift
  if [ "$#" -eq 0 ]; then
    echo "run_with_timeout: missing command" >&2
    return 2
  fi

  if [ "${ANDROID_ADB_TIMEOUT_IMPL:-}" != "bash" ]; then
    if command -v timeout >/dev/null; then
      timeout -k 5 "$seconds" "$@"
      return $?
    fi
    if command -v gtimeout >/dev/null; then
      gtimeout -k 5 "$seconds" "$@"
      return $?
    fi
  fi

  "$@" &
  local pid=$!
  local elapsed=0
  while [ "$elapsed" -lt "$seconds" ]; do
    if ! kill -0 "$pid" 2>/dev/null; then
      wait "$pid"
      return $?
    fi
    sleep 1
    elapsed=$((elapsed + 1))
  done
  echo "Timed out after ${seconds}s: $*" >&2

  local child
  while IFS= read -r child; do
    [ -n "$child" ] || continue
    kill -TERM "$child" 2>/dev/null || true
  done < <(pgrep -P "$pid" 2>/dev/null || true)
  kill -TERM "$pid" 2>/dev/null || true
  sleep 1
  while IFS= read -r child; do
    [ -n "$child" ] || continue
    kill -KILL "$child" 2>/dev/null || true
  done < <(pgrep -P "$pid" 2>/dev/null || true)
  kill -KILL "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  return 124
}

# reconnect / kill-server / start-server share one deadline so a wedged
# daemon cannot sit on start-server until the job timeout (#1529).
android_recover_adb() {
  echo "Recovering ADB (deadline ${ANDROID_ADB_TIMEOUT_SECONDS}s)..."
  if run_with_timeout "$ANDROID_ADB_TIMEOUT_SECONDS" bash -c '
    adb reconnect offline || true
    adb kill-server || true
    adb start-server
  '; then
    return 0
  fi
  echo "ADB recover timed out or failed after ${ANDROID_ADB_TIMEOUT_SECONDS}s." >&2
  return 1
}

android_adb_state() {
  local state=""
  if ! command -v adb >/dev/null; then
    echo missing
    return 0
  fi
  state="$(run_with_timeout "$ANDROID_ADB_PROBE_TIMEOUT_SECONDS" adb get-state 2>/dev/null || true)"
  state="$(printf '%s' "$state" | tr -d '\r\n')"
  if [ -n "$state" ]; then
    printf '%s\n' "$state"
  else
    echo missing
  fi
}

# Host-side snapshot that still works after emulator-5554 is gone. Do not
# wait on adb logcat here — that is empty once the device is missing (#1454).
dump_android_host_diagnostics() {
  local tag="${1:-manual}"
  local out="$results_dir/emulator-host.txt"
  local avd_dir avd_name
  mkdir -p "$results_dir"
  {
    echo "===== dump tag=$tag at $(date -u +%Y-%m-%dT%H:%M:%SZ) ====="
    echo "adb_state=$(android_adb_state)"
    echo "=== adb devices -l ==="
    if command -v adb >/dev/null; then
      run_with_timeout "$ANDROID_ADB_PROBE_TIMEOUT_SECONDS" adb devices -l || echo "adb devices timed out or failed"
    else
      echo "adb not on PATH"
    fi
    echo "=== qemu/emulator processes ==="
    # Do not match this script, emulator-host.txt, or the hosted job name.
    ps -eo pid,ppid,stat,pcpu,pmem,rss,args 2>/dev/null | grep -E '[q]emu-system|[e]mulator/emulator|[e]mulator64-' || echo "none"
    echo "=== free -h ==="
    free -h 2>/dev/null || true
    echo "=== /proc/meminfo (head) ==="
    head -n 8 /proc/meminfo 2>/dev/null || true
    echo "=== dmesg oom/qemu ==="
    # Hosted GHA VMs are KVM guests; do not scrape kvm-clock boot noise.
    if dmesg -T 2>/dev/null | grep -Ei 'out of memory|killed process|oom-kill|qemu-system' | tail -n 40; then
      :
    elif command -v sudo >/dev/null && sudo -n dmesg -T 2>/dev/null | grep -Ei 'out of memory|killed process|oom-kill|qemu-system' | tail -n 40; then
      :
    else
      echo "dmesg unavailable or no matching lines"
    fi
    echo "=== AVD config.ini ==="
    for avd_dir in "$HOME"/.android/avd/*.avd; do
      [ -d "$avd_dir" ] || continue
      echo "--- $avd_dir/config.ini ---"
      cat "$avd_dir/config.ini" 2>/dev/null || true
    done
    echo "=== AVD lock files ==="
    ls -la "$HOME"/.android/avd/*.avd/*.lock 2>/dev/null || echo "no locks"
  } >> "$out" 2>&1 || true
  for avd_dir in "$HOME"/.android/avd/*.avd; do
    [ -d "$avd_dir" ] || continue
    avd_name="$(basename "$avd_dir" .avd)"
    cp "$avd_dir/config.ini" "$results_dir/avd-${avd_name}-config.ini" 2>/dev/null || true
  done
}

copy_android_disk_logs() {
  local f
  for f in "$HOME"/.android/*.log "$HOME"/.android/avd/*/*.log; do
    [ -f "$f" ] || continue
    cp "$f" "$results_dir/$(basename "$f")" 2>/dev/null || true
  done
}

write_android_transport_marker() {
  local reason="${1:-device-server-or-emulator-transport-loss}"
  local qemu_state="missing"
  local logcat_bytes=0
  dump_android_host_diagnostics "transport-marker-$reason"
  copy_android_disk_logs
  if ps -eo args 2>/dev/null | grep -Eq '[q]emu-system|[e]mulator/emulator|[e]mulator64-'; then
    qemu_state="running"
  fi
  if [ -f "$results_dir/logcat.txt" ]; then
    logcat_bytes="$(wc -c < "$results_dir/logcat.txt" | tr -d ' ')"
  fi
  {
    echo "class=transport_death"
    echo "reason=$reason"
    echo "detected_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "adb_state=$(android_adb_state)"
    echo "qemu_state=$qemu_state"
    echo "logcat_bytes=$logcat_bytes"
  } > "$results_dir/android-transport-death"
  echo "android_failure_class=transport_death reason=$reason qemu_state=$qemu_state logcat_bytes=$logcat_bytes" >> "$results_dir/harness.log"
}

run_adb_timeout_self_test() {
  local status elapsed start finish

  start="$(date +%s)"
  set +e
  run_with_timeout 1 sleep 8
  status=$?
  set -e
  finish="$(date +%s)"
  elapsed=$((finish - start))
  if [ "$status" -ne 124 ]; then
    echo "self-test: expected 124 from timed-out sleep, got $status" >&2
    exit 1
  fi
  if [ "$elapsed" -ge 6 ]; then
    echo "self-test: timeout wrapper took ${elapsed}s; expected fail-fast under 6s" >&2
    exit 1
  fi

  set +e
  run_with_timeout 5 true
  status=$?
  set -e
  if [ "$status" -ne 0 ]; then
    echo "self-test: expected 0 from true, got $status" >&2
    exit 1
  fi

  start="$(date +%s)"
  set +e
  ANDROID_ADB_TIMEOUT_IMPL=bash run_with_timeout 1 sleep 8
  status=$?
  set -e
  finish="$(date +%s)"
  elapsed=$((finish - start))
  if [ "$status" -ne 124 ]; then
    echo "self-test: bash fallback expected 124, got $status" >&2
    exit 1
  fi
  if [ "$elapsed" -ge 6 ]; then
    echo "self-test: bash fallback took ${elapsed}s; expected fail-fast under 6s" >&2
    exit 1
  fi

  tmpdir="$(mktemp -d)"
  cat > "$tmpdir/adb" <<'EOF'
#!/usr/bin/env bash
if [ "${1:-}" = "start-server" ]; then
  echo "* daemon not running; starting now at tcp:5037"
  sleep 120
  exit 0
fi
exit 0
EOF
  chmod +x "$tmpdir/adb"
  start="$(date +%s)"
  set +e
  PATH="$tmpdir:$PATH" ANDROID_ADB_TIMEOUT_IMPL=bash ANDROID_ADB_TIMEOUT_SECONDS=2 android_recover_adb
  status=$?
  set -e
  finish="$(date +%s)"
  elapsed=$((finish - start))
  rm -rf "$tmpdir"
  if [ "$status" -eq 0 ]; then
    echo "self-test: hung adb start-server should fail recover" >&2
    exit 1
  fi
  if [ "$elapsed" -ge 15 ]; then
    echo "self-test: hung adb start-server recover took ${elapsed}s; expected fail-fast under 15s" >&2
    exit 1
  fi

  marker_dir="$(mktemp -d)"
  results_dir="$marker_dir" write_android_transport_marker "adb-recover-timeout"
  if [ ! -f "$marker_dir/android-transport-death" ]; then
    echo "self-test: android-transport-death marker was not written" >&2
    rm -rf "$marker_dir"
    exit 1
  fi
  if ! grep -q '^class=transport_death$' "$marker_dir/android-transport-death"; then
    echo "self-test: marker class mismatch" >&2
    cat "$marker_dir/android-transport-death" >&2
    rm -rf "$marker_dir"
    exit 1
  fi
  if ! grep -q '^reason=adb-recover-timeout$' "$marker_dir/android-transport-death"; then
    echo "self-test: marker reason mismatch" >&2
    cat "$marker_dir/android-transport-death" >&2
    rm -rf "$marker_dir"
    exit 1
  fi
  echo "--- sample android-transport-death ---"
  cat "$marker_dir/android-transport-death"
  rm -rf "$marker_dir"

  echo "ADB timeout self-test passed."
}

if [ "$self_test_adb_timeout" = true ]; then
  run_adb_timeout_self_test
  exit 0
fi

if [ "$dump_android_host" = true ]; then
  dump_android_host_diagnostics "cli-dump-android-host"
  exit 0
fi

rm -f "$results_dir/android-transport-death"
{
  echo "started_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "platform=$platform"
  echo "suite=$suite"
  echo "prove_failure=$prove_failure"
} > "$results_dir/harness.log"
export QUEENZONE_MOBILE_CONTRACT_FIXTURE="$fixture"

host_pid=""

stop_android_side_processes() {
  if [ -n "${android_watchdog_pid}" ]; then
    kill "$android_watchdog_pid" 2>/dev/null || true
    wait "$android_watchdog_pid" 2>/dev/null || true
    android_watchdog_pid=""
  fi
  if [ -n "${android_logcat_pid}" ]; then
    kill "$android_logcat_pid" 2>/dev/null || true
    wait "$android_logcat_pid" 2>/dev/null || true
    android_logcat_pid=""
  fi
}

start_android_logcat() {
  if ! command -v adb >/dev/null; then
    return 0
  fi
  if [ -n "${android_logcat_pid}" ]; then
    kill "$android_logcat_pid" 2>/dev/null || true
    wait "$android_logcat_pid" 2>/dev/null || true
    android_logcat_pid=""
  fi
  if [ -s "$results_dir/logcat.txt" ]; then
    adb logcat -v threadtime >> "$results_dir/logcat.txt" 2>>"$results_dir/logcat.err" &
  else
    adb logcat -v threadtime > "$results_dir/logcat.txt" 2>"$results_dir/logcat.err" &
  fi
  android_logcat_pid=$!
  echo "android_logcat_pid=$android_logcat_pid" >> "$results_dir/harness.log"
}

start_android_watchdog() {
  if ! command -v adb >/dev/null; then
    return 0
  fi
  if [ -n "${android_watchdog_pid}" ]; then
    kill "$android_watchdog_pid" 2>/dev/null || true
    wait "$android_watchdog_pid" 2>/dev/null || true
    android_watchdog_pid=""
  fi
  (
    for _ in $(seq 1 3600); do
      sleep 2
      if [ "$(android_adb_state)" != "device" ]; then
        dump_android_host_diagnostics "watchdog-adb-lost"
        exit 0
      fi
    done
  ) &
  android_watchdog_pid=$!
  echo "android_watchdog_pid=$android_watchdog_pid" >> "$results_dir/harness.log"
}

collect_diagnostics() {
  local reason="${1:-unknown}"
  echo "collect_diagnostics reason=$reason" >> "$results_dir/harness.log"
  if [ "$platform" = "android" ]; then
    dump_android_host_diagnostics "collect-$reason"
    copy_android_disk_logs
    # Streaming logcat is started while the device is alive. A post-death
    # `adb logcat -d` overwrite would replace that file with empty output.
    if [ ! -s "$results_dir/logcat.txt" ] && command -v adb >/dev/null; then
      run_with_timeout 30 adb logcat -d > "$results_dir/logcat.txt" 2>/dev/null || true
    fi
  fi
  if [ "$platform" = "ios" ]; then
    xcrun simctl spawn booted log show --last 5m --style compact \
      > "$results_dir/simulator.log" 2>/dev/null || true
  fi
  if [ -f "$host_log" ]; then
    cp "$host_log" "$results_dir/contract-host.log" 2>/dev/null || true
  fi
}

cleanup() {
  local status=$?
  stop_android_side_processes
  if [ "$status" -ne 0 ]; then
    collect_diagnostics "exit-$status"
  fi
  if [ -n "${host_pid}" ] && kill -0 "$host_pid" 2>/dev/null; then
    kill "$host_pid" 2>/dev/null || true
    wait "$host_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

start_host() {
  export ASPNETCORE_ENVIRONMENT=Testing
  export QUEENZONE_MOBILE_CONTRACT_HOST=1
  export ASPNETCORE_URLS="http://127.0.0.1:${port}"

  rm -f "$fixture" "${fixture}.tmp"

  if [ "$no_build_host" != true ]; then
    dotnet build src/QueenZone.Web/QueenZone.Web.csproj --configuration Release
  fi

  echo "Starting Testing contract host on ${ASPNETCORE_URLS} (log: $host_log)..."
  dotnet run \
    --project src/QueenZone.Web/QueenZone.Web.csproj \
    --configuration Release \
    --no-build \
    --no-launch-profile \
    >"$host_log" 2>&1 &
  host_pid=$!

  for _ in $(seq 1 90); do
    if [ -f "$fixture" ]; then
      break
    fi
    if ! kill -0 "$host_pid" 2>/dev/null; then
      echo "Contract host exited before writing the fixture." >&2
      cat "$host_log" >&2 || true
      exit 1
    fi
    sleep 1
  done

  if [ ! -f "$fixture" ]; then
    echo "Timed out waiting for $fixture" >&2
    cat "$host_log" >&2 || true
    exit 1
  fi

  if ! curl -sf "http://127.0.0.1:${port}/health" >/dev/null; then
    echo "Health check failed for http://127.0.0.1:${port}/health" >&2
    cat "$host_log" >&2 || true
    exit 1
  fi

  echo "Contract host ready at http://127.0.0.1:${port}"
}

export_smoke_auth_url() {
  if [ ! -f "$fixture" ]; then
    echo "Contract fixture $fixture is missing; cannot build SMOKE_AUTH_URL." >&2
    exit 1
  fi

  SMOKE_AUTH_URL="$(
    node -e '
      const fs = require("fs");
      const fixture = JSON.parse(fs.readFileSync(process.env.QUEENZONE_MOBILE_CONTRACT_FIXTURE, "utf8"));
      if (fixture.environment !== "Testing") {
        throw new Error("Smoke auth refuses a non-Testing fixture environment: " + fixture.environment);
      }
      const token = fixture.member && fixture.member.accessToken;
      if (!token) {
        throw new Error("Contract fixture is missing member.accessToken.");
      }
      process.stdout.write("queenzone://smoke-auth?accessToken=" + encodeURIComponent(token));
    '
  )"
  export SMOKE_AUTH_URL
  echo "SMOKE_AUTH_URL is set (length ${#SMOKE_AUTH_URL}; token not printed)."
}

export_journey_env() {
  if [ ! -f "$fixture" ]; then
    echo "Contract fixture $fixture is missing; cannot export journey IDs." >&2
    exit 1
  fi

  ATTACH_TOPIC_ID="$(
    node -e '
      const fs = require("fs");
      const fixture = JSON.parse(fs.readFileSync(process.env.QUEENZONE_MOBILE_CONTRACT_FIXTURE, "utf8"));
      if (fixture.environment !== "Testing") {
        throw new Error("Journeys refuse a non-Testing fixture environment: " + fixture.environment);
      }
      const id = fixture.attachTopicId;
      if (!Number.isInteger(id) || id <= 0) {
        throw new Error("Contract fixture is missing attachTopicId.");
      }
      process.stdout.write(String(id));
    '
  )"
  export ATTACH_TOPIC_ID
  echo "ATTACH_TOPIC_ID is set."
}

push_attach_fixture() {
  local src="$root/src/QueenZone.Mobile/maestro/fixtures/attach.txt"
  if [ ! -f "$src" ]; then
    echo "Missing attach fixture at $src" >&2
    exit 1
  fi

  if [ "$platform" = "android" ]; then
    # The smoke-only Inject action writes the fixture into the app cache.
    # Android 16 denies Release apps access to shell-owned ADB-pushed files,
    # including files placed under their external app-specific directory.
    SMOKE_ATTACH_URL="$(
      node -e 'process.stdout.write("queenzone://smoke-attach?uri=" + encodeURIComponent("file:///data/user/0/org.queenzone.mobile/cache/attach.txt") + "&name=attach.txt&type=text/plain")'
    )"
  else
    local data
    data="$(xcrun simctl get_app_container booted org.queenzone.mobile data)"
    if [ -z "$data" ] || [ ! -d "$data" ]; then
      echo "Could not resolve the iOS Simulator data container for org.queenzone.mobile." >&2
      exit 1
    fi
    mkdir -p "$data/Documents"
    cp "$src" "$data/Documents/attach.txt"
    SMOKE_ATTACH_URL="$(
      node -e 'process.stdout.write("queenzone://smoke-attach?uri=" + encodeURIComponent(process.argv[1]) + "&name=attach.txt&type=text/plain")' \
        "file://${data}/Documents/attach.txt"
    )"
  fi
  export SMOKE_ATTACH_URL
  echo "SMOKE_ATTACH_URL is set (length ${#SMOKE_ATTACH_URL}; path not printed)."
}

android_release_apk() {
  local dir="$root/src/QueenZone.Mobile/android/app/build/outputs/apk/release"
  if [ -f "$dir/app-release.apk" ]; then
    printf '%s\n' "$dir/app-release.apk"
    return 0
  fi
  if [ -f "$dir/app-release-unsigned.apk" ]; then
    printf '%s\n' "$dir/app-release-unsigned.apk"
    return 0
  fi
  return 1
}

ios_release_app() {
  local dir="$root/src/QueenZone.Mobile/ios/build/Build/Products/Release-iphonesimulator"
  if [ -d "$dir/QueenZone.app" ]; then
    printf '%s\n' "$dir/QueenZone.app"
    return 0
  fi
  local found
  found="$(ls -d "$dir"/*.app 2>/dev/null | head -n 1 || true)"
  if [ -n "$found" ] && [ -d "$found" ]; then
    printf '%s\n' "$found"
    return 0
  fi
  return 1
}

build_android() {
  (
    cd src/QueenZone.Mobile
    export EXPO_PUBLIC_APP_ENV=development
    export EXPO_PUBLIC_API_BASE_URL="http://10.0.2.2:${port}"
    export EXPO_PUBLIC_SMOKE_EMBED=1
    export SENTRY_DISABLE_AUTO_UPLOAD=true
    export QUEENZONE_MOBILE_SMOKE_EMBED=1
    export FORCE_BUNDLING=1
    echo "Baking Android Release smoke APK for ${EXPO_PUBLIC_API_BASE_URL}"
    npx expo prebuild --platform android
    (
      cd android
      ./gradlew assembleRelease
    )
  )
  apk="$(android_release_apk || true)"
}

build_ios() {
  (
    cd src/QueenZone.Mobile
    export EXPO_PUBLIC_APP_ENV=development
    export EXPO_PUBLIC_API_BASE_URL="http://127.0.0.1:${port}"
    export EXPO_PUBLIC_SMOKE_EMBED=1
    export SENTRY_DISABLE_AUTO_UPLOAD=true
    export QUEENZONE_MOBILE_SMOKE_EMBED=1
    export FORCE_BUNDLING=1
    echo "Baking iOS Simulator Release smoke app for ${EXPO_PUBLIC_API_BASE_URL}"
    npx expo prebuild --platform ios --clean
    cd ios
    workspace="$(ls -d *.xcworkspace | head -n 1)"
    scheme="$(basename "$workspace" .xcworkspace)"
    xcodebuild \
      -workspace "$workspace" \
      -scheme "$scheme" \
      -sdk iphonesimulator \
      -configuration Release \
      -derivedDataPath build \
      CODE_SIGNING_ALLOWED=NO \
      build
  )
  app="$(ios_release_app || true)"
}

if [ "$skip_host" != true ]; then
  start_host
else
  echo "Skipping host start; expecting an already-running Testing contract host."
  if ! curl -sf "http://127.0.0.1:${port}/health" >/dev/null; then
    echo "Health check failed for http://127.0.0.1:${port}/health" >&2
    exit 1
  fi
fi

export_smoke_auth_url
if [ "$suite" = "journeys" ]; then
  export_journey_env
fi

if [ "$skip_build" != true ]; then
  if [ "$platform" = "android" ]; then
    build_android
  else
    build_ios
  fi
fi

if [ "$platform" = "android" ] && [ -z "$apk" ]; then
  apk="$(android_release_apk || true)"
fi
if [ "$platform" = "ios" ] && [ -z "$app" ]; then
  app="$(ios_release_app || true)"
fi

if [ "$platform" = "android" ]; then
  if [ -z "$apk" ] || [ ! -f "$apk" ]; then
    echo "Android smoke APK not found at '${apk:-<empty>}'." >&2
    exit 1
  fi
  if ! command -v adb >/dev/null; then
    echo "adb is required to install the Android smoke APK." >&2
    exit 1
  fi
  echo "Installing $apk"
  adb wait-for-device
  adb install -r "$apk"
  start_android_logcat
  start_android_watchdog
  if [ "$suite" = "journeys" ]; then
    # Clear before push_attach_fixture. Clearing from launchApp afterwards
    # deletes the app-private attachment URI while leaving its UI metadata.
    adb shell pm clear org.queenzone.mobile >/dev/null
  fi
else
  if [ -z "$app" ] || [ ! -d "$app" ]; then
    echo "iOS Simulator .app not found at '${app:-<empty>}'." >&2
    exit 1
  fi
  echo "Installing $app"
  if [ "$suite" = "journeys" ]; then
    # A Simulator install preserves an existing data container. Remove the
    # prior install so journeys start clean before their fixture is copied.
    xcrun simctl uninstall booted org.queenzone.mobile >/dev/null 2>&1 || true
  fi
  xcrun simctl install booted "$app"
fi

if [ "$suite" = "journeys" ]; then
  push_attach_fixture
fi

# Installer writes $HOME/.maestro/bin. GITHUB_PATH should expose it to later
# steps; iOS smoke 33733955768 still failed with "Maestro is not on PATH"
# after the install step. Always prepend before probing (#1281).
if [ -d "${HOME}/.maestro/bin" ]; then
  PATH="${HOME}/.maestro/bin:${PATH}"
  export PATH
fi

if ! command -v maestro >/dev/null; then
  echo "Maestro is not on PATH. Install with: curl -Ls \"https://get.maestro.mobile.dev\" | bash" >&2
  echo "Looked in: ${HOME}/.maestro/bin" >&2
  ls -la "${HOME}/.maestro/bin" >&2 || true
  exit 1
fi

flow="src/QueenZone.Mobile/maestro/smoke.yaml"
if [ "$prove_failure" = true ]; then
  flow="src/QueenZone.Mobile/maestro/prove-failure.yaml"
  echo "Running forced-assertion flow to prove failure artifacts."
elif [ "$suite" = "journeys" ]; then
  flow="src/QueenZone.Mobile/maestro/journeys.yaml"
  echo "Running on-demand Maestro journeys (#1071)."
elif [ "$suite" = "release" ]; then
  flow="src/QueenZone.Mobile/maestro/release.yaml"
  echo "Running the P0 Maestro release suite (#1411)."
fi

echo "Running Maestro ($flow). Selector and assertion failures are not retried."
maestro_args=()
if [ -n "${MAESTRO_TARGET_DEVICE:-}" ]; then
  maestro_args+=(--device "$MAESTRO_TARGET_DEVICE")
fi
maestro_args+=(
  test "$flow"
  --format junit
  --output "$results_dir/junit.xml"
  --debug-output "$results_dir/debug"
  --flatten-debug-output
  -e "SMOKE_AUTH_URL=${SMOKE_AUTH_URL}"
)
if [ "$suite" = "journeys" ]; then
  maestro_args+=(
    -e "SMOKE_ATTACH_URL=${SMOKE_ATTACH_URL}"
    -e "ATTACH_TOPIC_ID=${ATTACH_TOPIC_ID}"
  )
fi

maestro_console_log="$results_dir/maestro-console.log"
# DeviceServerDiedException, viewHierarchy DEADLINE_EXCEEDED, and a vanished
# emulator-5554 are transport death. JUnit "Unknown error" alone is not.
android_transport_re="DeviceServerDiedException|Device server died|device offline|DEADLINE_EXCEEDED|host:transport:|device 'emulator-[0-9]+' not found"

android_emulator_gone() {
  [ "$(android_adb_state)" != "device" ]
}

android_transport_died() {
  local dir="${1:-$results_dir}"
  if [ -f "$dir/junit.xml" ] && grep -Eq "$android_transport_re" "$dir/junit.xml"; then
    return 0
  fi
  if [ -f "$maestro_console_log" ] && grep -Eq "$android_transport_re" "$maestro_console_log"; then
    return 0
  fi
  if [ -d "$dir/debug" ] && grep -ERq "$android_transport_re" "$dir/debug"; then
    return 0
  fi
  if command -v adb >/dev/null && android_emulator_gone; then
    return 0
  fi
  return 1
}

# After the first-attempt JUnit/debug were moved aside, only the in-process
# retry artifacts remain. Do not read maestro-console.log (tee -a still holds
# the first DeviceServerDied lines) or android-transport-death (must not
# self-satisfy). A home-hero / smoke-auth miss on a live emulator is a
# selector miss.
android_latest_attempt_transport_died() {
  local dir="${1:-$results_dir}"
  if [ -f "$dir/junit.xml" ] && grep -Eq "$android_transport_re" "$dir/junit.xml"; then
    return 0
  fi
  if [ -d "$dir/debug" ] && grep -ERq "$android_transport_re" "$dir/debug"; then
    return 0
  fi
  if command -v adb >/dev/null && android_emulator_gone; then
    return 0
  fi
  return 1
}

run_maestro_once() {
  maestro "${maestro_args[@]}" 2>&1 | tee -a "$maestro_console_log"
  return "${PIPESTATUS[0]}"
}

set +e
run_maestro_once
maestro_status=$?
set -e

# Hosted Android emulators can drop off ADB while Maestro is running. Maestro
# has emitted both a generic Unknown-error JUnit failure and the full
# DeviceServerDiedException across observed runs. Preserve that attempt,
# recover ADB, reinstall the same APK, and retry once when the emulator is
# still alive. A hierarchy timeout is not a product assert failure. If the
# emulator process is gone or ADB recover times out, write
# android-transport-death so CI can boot a fresh emulator and rerun the same
# flows. A selector miss after a live in-process retry must not write that
# marker. Selector and assertion failures stay single-attempt.
if [ "$platform" = "android" ] \
  && [ "$maestro_status" -ne 0 ] \
  && android_transport_died; then
  echo "Maestro lost the Android device transport; recovering ADB and retrying once."
  if [ -d "$results_dir/debug" ]; then
    mv "$results_dir/debug" "$results_dir/debug-android-transport-first"
  fi
  if [ -f "$results_dir/junit.xml" ]; then
    mv "$results_dir/junit.xml" "$results_dir/junit-android-transport-first.xml"
  fi
  if ! android_recover_adb; then
    write_android_transport_marker "adb-recover-timeout"
    echo "Android ADB recover timed out or failed after Maestro transport loss; a fresh-emulator job retry is required." >&2
  else
    android_ready=false
    for i in $(seq 1 45); do
      if [ "$(android_adb_state)" = "device" ] \
        && [ "$(run_with_timeout "$ANDROID_ADB_PROBE_TIMEOUT_SECONDS" adb shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; then
        android_ready=true
        break
      fi
      # After ADB restarts, a still-running emulator may take a few seconds to
      # reappear. A vanished qemu process never will — stop waiting so CI can
      # boot a fresh emulator instead of burning 90s (#1432).
      if [ "$i" -ge 5 ] && android_emulator_gone; then
        echo "Android emulator is gone after the Maestro transport failure; a fresh-emulator job retry is required." >&2
        break
      fi
      sleep 2
    done
    if [ "$android_ready" = true ]; then
      start_android_logcat
      start_android_watchdog
      adb install -r "$apk"
      set +e
      run_maestro_once
      maestro_status=$?
      set -e
      if [ "$maestro_status" -eq 0 ]; then
        echo "android_failure_class=recovered_after_transport_death" >> "$results_dir/harness.log"
      elif android_latest_attempt_transport_died; then
        write_android_transport_marker "retry-still-transport-death"
      else
        echo "In-process Android retry failed on a selector or assertion miss. Not requesting a fresh emulator." >&2
        echo "android_failure_class=selector_miss" >> "$results_dir/harness.log"
      fi
    else
      write_android_transport_marker "emulator-gone"
      echo "Android emulator did not return online after the Maestro transport failure." >&2
    fi
  fi
fi

# Hosted macOS occasionally exits Maestro's xcodebuild driver process before
# XCTest starts listening. Run 34061996744 failed this way, while the same
# Maestro/Xcode/Simulator combination started successfully hours earlier. A
# missing JUnit file proves no app flow began. Reboot the Simulator and retry
# only that infrastructure startup; assertion and in-flow failures stay
# single-attempt.
if [ "$platform" = "ios" ] \
  && [ "$maestro_status" -ne 0 ] \
  && [ ! -s "$results_dir/junit.xml" ] \
  && grep -q "iOS driver not ready in time" "$maestro_console_log"; then
  echo "Maestro iOS driver failed before any flow began; rebooting the Simulator and retrying driver startup once."
  if [ -d "$results_dir/debug" ]; then
    mv "$results_dir/debug" "$results_dir/debug-driver-startup-first"
  fi
  retry_udid="${IOS_SIM_UDID:-$(xcrun simctl list devices booted | grep -oE '[0-9A-F-]{36}' | head -n 1)}"
  if [ -n "$retry_udid" ]; then
    xcrun simctl shutdown "$retry_udid" || true
    xcrun simctl boot "$retry_udid"
    xcrun simctl bootstatus "$retry_udid" -b
  fi
  set +e
  run_maestro_once
  maestro_status=$?
  set -e
fi

if [ "$maestro_status" -ne 0 ]; then
  echo "Maestro failed with status $maestro_status" >&2
  android_failure_class="selector_miss"
  if [ "$platform" = "android" ] \
    && { [ -f "$results_dir/android-transport-death" ] || android_latest_attempt_transport_died; }; then
    android_failure_class="transport_death"
    echo "Maestro failing cause: Android device transport death" >&2
    echo "A hierarchy timeout or dead emulator is not a selector miss." >&2
  else
    echo "Maestro failing cause: selector or assertion miss" >&2
  fi
  echo "android_failure_class=$android_failure_class" >> "$results_dir/harness.log"
  junit_report="$results_dir/junit.xml"
  if [ ! -f "$junit_report" ] && [ -f "$results_dir/junit-android-transport-first.xml" ]; then
    junit_report="$results_dir/junit-android-transport-first.xml"
  fi
  if [ -f "$junit_report" ]; then
    node -e '
      const fs = require("fs");
      const xml = fs.readFileSync(process.argv[1], "utf8");
      const printSelector = process.argv[2] !== "transport_death";
      const cases = [...xml.matchAll(/<testcase\b([^>]*)>([\s\S]*?)<\/testcase>/g)];
      for (const c of cases) {
        const name = /name="([^"]*)"/.exec(c[1])?.[1] ?? "?";
        const file = /file="([^"]*)"/.exec(c[1])?.[1] ?? "";
        const fail = /<failure(?:\s[^>]*)?>([^<]*)<\/failure>/.exec(c[2]);
        if (fail) {
          console.error("Maestro failing flow: " + (file || name));
          if (printSelector) {
            console.error("Maestro failing selector: " + fail[1].trim());
          }
        }
      }
    ' "$junit_report" "$android_failure_class" >&2 || true
  fi
  collect_diagnostics "maestro-$maestro_status"
fi

exit "$maestro_status"
