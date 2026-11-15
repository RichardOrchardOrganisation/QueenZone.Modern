#!/usr/bin/env bash
# Run a command with a hard deadline. Returns 124 on timeout (GNU timeout).
# Prefers timeout/gtimeout; falls back to background + wait so macOS runners
# without coreutils still fail-fast.
run_with_timeout() {
  local seconds="$1"
  shift
  if [[ "$#" -eq 0 ]]; then
    echo "run_with_timeout: missing command" >&2
    return 2
  fi

  if [[ "${ANDROID_ADB_TIMEOUT_IMPL:-}" != "bash" ]]; then
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
  while [[ "$elapsed" -lt "$seconds" ]]; do
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
    [[ -n "$child" ]] || continue
    kill -TERM "$child" 2>/dev/null || true
  done < <(pgrep -P "$pid" 2>/dev/null || true)
  kill -TERM "$pid" 2>/dev/null || true
  sleep 1
  while IFS= read -r child; do
    [[ -n "$child" ]] || continue
    kill -KILL "$child" 2>/dev/null || true
  done < <(pgrep -P "$pid" 2>/dev/null || true)
  kill -KILL "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  return 124
}

