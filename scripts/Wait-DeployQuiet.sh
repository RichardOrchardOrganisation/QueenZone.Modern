#!/usr/bin/env bash
# Poll deploy.yml until production is quiet, or until a bound.
# Used by livesite-readonly-sweep.yml (issue #2195). This is a condition
# wait, not a shared concurrency group: GitHub keeps one pending slot per
# group, so a queued sweep could cancel a queued release tag (or the reverse).
# Leave deploy.yml concurrency alone.
#
# Quiet = no deploy.yml run is unfinished (status != completed), and no run
# completed in the last 10 minutes.
#
# Usage:
#   GITHUB_REPOSITORY=owner/name GITHUB_TOKEN=... bash ./scripts/Wait-DeployQuiet.sh
#   IGNORE_DEPLOY_GATE=true bash ./scripts/Wait-DeployQuiet.sh
#   bash ./scripts/Wait-DeployQuiet.sh --self-test
#
# Prints:
#   run=true|false
# and writes the same to $GITHUB_OUTPUT when that file is set.
# Bound-hit is success with run=false (sweep job skips grey), not a failure.
# API / parse errors fail closed (exit 1) so the sweep does not start.
set -euo pipefail

BOUND_SECONDS="${BOUND_SECONDS:-1800}"
POLL_SECONDS="${POLL_SECONDS:-30}"
RECENT_SECONDS="${RECENT_SECONDS:-600}"

write_run() {
  local value="$1"
  echo "run=${value}"
  if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
    echo "run=${value}" >> "${GITHUB_OUTPUT}"
  fi
}

fetch_deploy_runs() {
  local repo="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"
  local token="${GITHUB_TOKEN:-${GH_TOKEN:-}}"
  if [[ -z "${token}" ]]; then
    echo "GITHUB_TOKEN is required to list deploy.yml runs." >&2
    return 1
  fi
  local api="${GITHUB_API_URL:-https://api.github.com}"
  curl -sS -f --max-time 30 --retry 2 \
    -H "Authorization: Bearer ${token}" \
    -H "Accept: application/vnd.github+json" \
    -H "X-GitHub-Api-Version: 2022-11-28" \
    "${api}/repos/${repo}/actions/workflows/deploy.yml/runs?per_page=30"
}

# Prints "id|status|updated_at" of the first blocking run, or empty if quiet.
first_blocker() {
  local json="$1"
  local now="$2"
  local recent="$3"
  jq -er --argjson now "${now}" --argjson recent "${recent}" '
    if (type != "object") then
      error("deploy.yml runs response is not an object")
    else
      (.workflow_runs // [])
      | map(select(
          .status != "completed"
          or (
            .status == "completed"
            and (.updated_at | type == "string")
            and (($now - (.updated_at | fromdateiso8601)) < $recent)
          )
        ))
      | if length == 0 then
          ""
        else
          "\(.[0].id)|\(.[0].status)|\(.[0].updated_at)"
        end
    end
  ' <<<"${json}"
}

wait_until_quiet() {
  if [[ "${IGNORE_DEPLOY_GATE:-}" == "true" ]]; then
    echo "Ignoring deploy-quiet gate (IGNORE_DEPLOY_GATE=true)." >&2
    write_run true
    return 0
  fi

  local started now deadline blocker json
  started="$(date +%s)"
  deadline=$((started + BOUND_SECONDS))

  while true; do
    now="$(date +%s)"
    json="$(fetch_deploy_runs)"
    blocker="$(first_blocker "${json}" "${now}" "${RECENT_SECONDS}")"

    if [[ -z "${blocker}" ]]; then
      echo "deploy.yml is quiet." >&2
      write_run true
      return 0
    fi

    local id status updated
    IFS='|' read -r id status updated <<<"${blocker}"
    echo "deploy.yml run ${id} is ${status} (updated ${updated}); not quiet yet." >&2

    if (( now >= deadline )); then
      echo "::warning title=Live-site sweep skipped::Deploy run ${id} active/recent" >&2
      if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
        {
          echo "## Live-site sweep skipped"
          echo
          echo "Deploy run \`${id}\` is active/recent (\`${status}\`, updated \`${updated}\`)."
          echo "The sweep did not start so it would not run against a mid-deploy site."
        } >> "${GITHUB_STEP_SUMMARY}"
      fi
      write_run false
      return 0
    fi

    echo "Waiting ${POLL_SECONDS}s for a quiet deploy window (bound ${BOUND_SECONDS}s)..." >&2
    sleep "${POLL_SECONDS}"
  done
}

iso_from_unix() {
  jq -nr --argjson t "$1" '$t | strftime("%Y-%m-%dT%H:%M:%SZ")'
}

assert_eq() {
  local name="$1"
  local want="$2"
  local got="$3"
  if [[ "${got}" != "${want}" ]]; then
    echo "FAIL ${name}" >&2
    echo " want:     ${want}" >&2
    echo " got:      ${got}" >&2
    return 1
  fi
  echo "PASS ${name}" >&2
}

assert_contains() {
  local name="$1"
  local needle="$2"
  local haystack="$3"
  if [[ "${haystack}" != *"${needle}"* ]]; then
    echo "FAIL ${name}" >&2
    echo " missing:  ${needle}" >&2
    echo " in:       ${haystack}" >&2
    return 1
  fi
  echo "PASS ${name}" >&2
}

if [[ "${1:-}" = "--self-test" ]]; then
  fail=0
  tmp="$(mktemp -d)"
  trap 'rm -rf "${tmp}"' EXIT

  now="$(date +%s)"
  recent_iso="$(iso_from_unix "$((now - 120))")"
  old_iso="$(iso_from_unix "$((now - 1200))")"

  got="$(first_blocker "{\"workflow_runs\":[{\"id\":11,\"status\":\"in_progress\",\"updated_at\":\"${recent_iso}\"}]}" "${now}" 600)"
  assert_eq active "11|in_progress|${recent_iso}" "${got}" || fail=1

  got="$(first_blocker "{\"workflow_runs\":[{\"id\":12,\"status\":\"queued\",\"updated_at\":\"${old_iso}\"}]}" "${now}" 600)"
  assert_eq queued "12|queued|${old_iso}" "${got}" || fail=1

  got="$(first_blocker "{\"workflow_runs\":[{\"id\":13,\"status\":\"waiting\",\"updated_at\":\"${old_iso}\"}]}" "${now}" 600)"
  assert_eq waiting "13|waiting|${old_iso}" "${got}" || fail=1

  got="$(first_blocker "{\"workflow_runs\":[{\"id\":14,\"status\":\"pending\",\"updated_at\":\"${old_iso}\"}]}" "${now}" 600)"
  assert_eq pending "14|pending|${old_iso}" "${got}" || fail=1

  got="$(first_blocker "{\"workflow_runs\":[{\"id\":15,\"status\":\"requested\",\"updated_at\":\"${old_iso}\"}]}" "${now}" 600)"
  assert_eq requested "15|requested|${old_iso}" "${got}" || fail=1

  got="$(first_blocker "{\"workflow_runs\":[{\"id\":21,\"status\":\"completed\",\"updated_at\":\"${recent_iso}\"}]}" "${now}" 600)"
  assert_eq recent "21|completed|${recent_iso}" "${got}" || fail=1

  got="$(first_blocker "{\"workflow_runs\":[{\"id\":31,\"status\":\"completed\",\"updated_at\":\"${old_iso}\"}]}" "${now}" 600)"
  assert_eq quiet "" "${got}" || fail=1

  got="$(first_blocker "{\"workflow_runs\":[]}" "${now}" 600)"
  assert_eq empty-quiet "" "${got}" || fail=1

  if first_blocker "[]" "${now}" 600 >/dev/null 2>&1; then
    echo "FAIL invalid-json-fails-closed (jq accepted a list)" >&2
    fail=1
  else
    echo "PASS invalid-json-fails-closed" >&2
  fi

  output="${tmp}/github-output"
  summary="${tmp}/summary"
  : >"${output}"
  : >"${summary}"

  fetch_deploy_runs() {
    cat "${tmp}/runs.json"
  }

  cat >"${tmp}/runs.json" <<EOF
{"workflow_runs":[{"id":41,"status":"in_progress","updated_at":"${recent_iso}"}]}
EOF
  got="$(
    GITHUB_OUTPUT="${output}" GITHUB_STEP_SUMMARY="${summary}" \
      BOUND_SECONDS=0 POLL_SECONDS=0 wait_until_quiet
  )"
  assert_eq bound-hit-stdout $'run=false' "${got}" || fail=1
  assert_eq bound-hit-output $'run=false' "$(cat "${output}")" || fail=1
  assert_contains bound-hit-warning "Live-site sweep skipped" "$(cat "${summary}")" || fail=1
  assert_contains bound-hit-run-id "41" "$(cat "${summary}")" || fail=1

  : >"${output}"
  cat >"${tmp}/runs.json" <<EOF
{"workflow_runs":[{"id":51,"status":"completed","updated_at":"${old_iso}"}]}
EOF
  got="$(GITHUB_OUTPUT="${output}" BOUND_SECONDS=0 POLL_SECONDS=0 wait_until_quiet)"
  assert_eq quiet-loop-stdout $'run=true' "${got}" || fail=1
  assert_eq quiet-loop-output $'run=true' "$(cat "${output}")" || fail=1

  : >"${output}"
  fetch_deploy_runs() {
    echo "fetch should not run when IGNORE_DEPLOY_GATE=true" >&2
    return 1
  }
  got="$(IGNORE_DEPLOY_GATE=true GITHUB_OUTPUT="${output}" wait_until_quiet)"
  assert_eq ignore-gate $'run=true' "${got}" || fail=1
  assert_eq ignore-gate-output $'run=true' "$(cat "${output}")" || fail=1

  if [[ "${fail}" -ne 0 ]]; then
    echo "Wait-DeployQuiet self-test failed." >&2
    exit 1
  fi
  echo "Wait-DeployQuiet self-test passed." >&2
  exit 0
fi

wait_until_quiet
