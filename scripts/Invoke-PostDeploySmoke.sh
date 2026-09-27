#!/usr/bin/env bash
# Public-domain smoke used by deploy.yml (and locally via bash).
# Default: wait for /warmup, then require 200 on the content routes.
# --warmup-only: /warmup gate used in the deploy job so this runner overlaps
# the zip-deploy worker restart instead of waiting for a second job. Pass
# --expect-build-version as well so warmup-only does not return green on the
# previous worker (/warmup is short-circuited and was ok in ~2s on the old
# process in #664). /warmup's remaining ceiling is its own dependency checks
# and cache priming (#674), not anything upstream of it (#681).
# Deploy still Kudu-recycles after the zip push: skipping that after #688 left
# /warmup on HTTP 500 even though the new data-build-version was already live.
# --sample-data omits the hard-coded production legacy topic while retaining
# the common route and API checks used by the isolated dev environment.
# /health/ready must be ASP.NET status Healthy (whitespace-tolerant).
# /health and /warmup stay on status ok.
#   bash ./scripts/Invoke-PostDeploySmoke.sh --self-test
set -euo pipefail

# Whitespace-tolerant JSON "status" matcher. /health/ready uses ASP.NET
# HealthStatus.ToString() ("Healthy"); /health and /warmup stay on "ok".
body_has_json_status() {
  local file="$1"
  local expected="$2"
  grep -qiE '"status"[[:space:]]*:[[:space:]]*"'"${expected}"'"' "$file"
}

if [[ "${1:-}" = "--self-test" ]]; then
  fail=0
  tmp="$(mktemp)"
  trap 'rm -f "${tmp}"' EXIT

  assert_status() {
    local name="$1"
    local body="$2"
    local expected="$3"
    local want="$4"
    printf '%s' "${body}" >"${tmp}"
    if body_has_json_status "${tmp}" "${expected}"; then
      got=0
    else
      got=1
    fi
    if [[ "${got}" -eq "${want}" ]]; then
      echo "PASS ${name}" >&2
    else
      echo "FAIL ${name} (want exit ${want}, got ${got})" >&2
      fail=1
    fi
  }

  assert_status ready-compact '{"status":"Healthy","entries":{}}' Healthy 0
  assert_status ready-spaced '{ "status" : "Healthy" }' Healthy 0
  assert_status ready-pretty $'{\n  "status": "Healthy"\n}\n' Healthy 0
  assert_status ready-rejects-ok '{"status":"ok"}' Healthy 1
  assert_status liveness-ok '{"status":"ok"}' ok 0
  assert_status liveness-spaced '{ "status" : "ok" }' ok 0
  assert_status liveness-rejects-healthy '{"status":"Healthy"}' ok 1
  assert_status warmup-ok '{"status":"ok"}' ok 0

  if [[ "${fail}" -ne 0 ]]; then
    echo "Invoke-PostDeploySmoke self-test failed." >&2
    exit 1
  fi
  echo "Invoke-PostDeploySmoke self-test passed." >&2
  exit 0
fi

BASE_URL="https://www.queenzone.org"
WARMUP_ONLY=0
MAX_ATTEMPTS=32
SLEEP_SECONDS=15
EXPECT_BUILD_VERSION=""
SAMPLE_DATA=0

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --warmup-only) WARMUP_ONLY=1 ;;
    --base-url) BASE_URL="${2:?}"; shift ;;
    --max-attempts) MAX_ATTEMPTS="${2:?}"; shift ;;
    --sleep-seconds) SLEEP_SECONDS="${2:?}"; shift ;;
    --expect-build-version) EXPECT_BUILD_VERSION="${2:?}"; shift ;;
    --sample-data) SAMPLE_DATA=1 ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
  shift
done

if [[ -n "$EXPECT_BUILD_VERSION" ]]; then
  EXPECT_BUILD_VERSION="${EXPECT_BUILD_VERSION:0:7}"
fi

WARMUP_PATH="/warmup"
PATHS=(
  "/health"
  "/health/ready"
  "/"
  "/news"
  "/forum"
  "/forum/1/queen-serious-discussion"
  "/articles"
  "/biography"
  "/photography"
  "/search"
  "/api/v1"
  "/api/v1/content/news?pageSize=1"
)

if [[ "$SAMPLE_DATA" -eq 0 ]]; then
  PATHS+=("/forum/topic/455095/forum-guidelines")
fi

check_path() {
  local path="$1"
  local url="${BASE_URL}${path}"
  local status body_file
  body_file="$(mktemp)"
  status=$(curl -s -o "$body_file" -w "%{http_code}" --max-time 30 -L "$url") || status="${status:-000}"
  if [[ "$status" != "200" ]]; then
    echo "  ✗ $path → HTTP $status"
    rm -f "$body_file"
    return 1
  fi
  if [[ "$path" = "/health/ready" ]]; then
    if ! body_has_json_status "$body_file" "Healthy"; then
      echo "  ✗ $path → 200 but body missing status Healthy"
      rm -f "$body_file"
      return 1
    fi
  elif [[ "$path" = "/health" ]] || [[ "$path" = "$WARMUP_PATH" ]]; then
    if ! body_has_json_status "$body_file" "ok"; then
      echo "  ✗ $path → 200 but body missing status ok"
      rm -f "$body_file"
      return 1
    fi
  fi
  if [[ "$path" = "/api/v1" ]]; then
    if ! grep -q '"version":"v1"' "$body_file"; then
      echo "  ✗ $path → 200 but body missing \"version\":\"v1\""
      rm -f "$body_file"
      return 1
    fi
  fi
  if [[ "$path" = "/api/v1/content/news?pageSize=1" ]]; then
    if ! grep -q '"items"' "$body_file"; then
      echo "  ✗ $path → 200 but body missing items array"
      rm -f "$body_file"
      return 1
    fi
  fi
  if [[ -n "$EXPECT_BUILD_VERSION" ]] && [[ "$path" = "/" ]]; then
    if ! grep -q "data-build-version=\"${EXPECT_BUILD_VERSION}\"" "$body_file"; then
      echo "  ✗ $path → 200 but build stamp is not ${EXPECT_BUILD_VERSION} (deployed package did not become the running app)"
      rm -f "$body_file"
      return 1
    fi
  fi
  echo "  ✓ $path → HTTP $status"
  rm -f "$body_file"
  return 0
}

if [[ "$WARMUP_ONLY" -eq 1 ]]; then
  echo "Mode: warmup-only (deploy job). Content-route smoke is a separate job."
else
  echo "Mode: full post-deploy smoke (warmup then content routes)."
fi
if [[ -n "$EXPECT_BUILD_VERSION" ]]; then
  echo "Expecting data-build-version=${EXPECT_BUILD_VERSION} on /."
fi
echo "Waiting for warmup on ${BASE_URL}${WARMUP_PATH} (up to ~$(( MAX_ATTEMPTS * SLEEP_SECONDS / 60 )) minutes)."
echo "/health is not sufficient readiness — App Service can answer liveness while pages still 500."
if [[ "$WARMUP_ONLY" -eq 1 ]] && [[ -n "$EXPECT_BUILD_VERSION" ]]; then
  echo "Warmup-only also requires / to serve data-build-version=${EXPECT_BUILD_VERSION} so a live old worker cannot pass."
fi

for attempt in $(seq 1 "$MAX_ATTEMPTS"); do
  echo "Warmup attempt ${attempt}/${MAX_ATTEMPTS}..."
  if check_path "$WARMUP_PATH"; then
    if [[ "$WARMUP_ONLY" -eq 1 ]] && [[ -n "$EXPECT_BUILD_VERSION" ]]; then
      if check_path "/"; then
        echo "Warmup and build stamp passed on attempt ${attempt}."
        exit 0
      fi
      echo "Warmup is up but / is not serving ${EXPECT_BUILD_VERSION} yet."
    else
      echo "Warmup passed on attempt ${attempt}."
      if [[ "$WARMUP_ONLY" -eq 1 ]]; then
        exit 0
      fi
      break
    fi
  fi

  if [[ "$attempt" -eq "$MAX_ATTEMPTS" ]]; then
    if [[ "$WARMUP_ONLY" -eq 1 ]] && [[ -n "$EXPECT_BUILD_VERSION" ]]; then
      echo "::error::Warmup or build stamp ${EXPECT_BUILD_VERSION} failed against ${BASE_URL} after ${MAX_ATTEMPTS} attempts. If / still serves the previous stamp, the zip mount did not become the running app."
    else
      echo "::error::Warmup failed against ${BASE_URL}${WARMUP_PATH} after ${MAX_ATTEMPTS} attempts. Check App Service logs."
    fi
    exit 1
  fi

  echo "Warmup not ready yet. Retrying in ${SLEEP_SECONDS}s..."
  sleep "$SLEEP_SECONDS"
done

echo "Polling content routes on $BASE_URL after warmup."
for attempt in $(seq 1 "$MAX_ATTEMPTS"); do
  echo "Attempt ${attempt}/${MAX_ATTEMPTS}..."
  failed=0
  for path in "${PATHS[@]}"; do
    if ! check_path "$path"; then
      failed=1
    fi
  done

  if [[ "$failed" -eq 0 ]]; then
    echo "All post-deploy smoke checks passed on attempt ${attempt}."
    exit 0
  fi

  if [[ "$attempt" -lt "$MAX_ATTEMPTS" ]]; then
    echo "Site not ready (App Service recycle/warmup). Retrying in ${SLEEP_SECONDS}s..."
    sleep "$SLEEP_SECONDS"
  fi
done

echo "::error::Post-deploy smoke failed against $BASE_URL after ${MAX_ATTEMPTS} attempts. Check App Service logs."
exit 1
