#!/usr/bin/env bash
# Finds a ci.yml run whose head_sha exactly equals the release SHA and
# that uploaded a web-publish artifact. Prints `found=true|false`,
# `run_id=<id>`, and `run_head_sha=<sha>` for GitHub Actions. Missing
# artifact or SHA mismatch is a soft miss (found=false) so Deploy can
# publish from the checked-out main/tag SHA instead of failing.
#
# Exact-match guard: never reuse a run built from a different commit.
# Remapping to an associated PR head shipped #1826's stale PR-head zip
# (10 commits behind main) for the production release of a later main
# SHA and dropped already-merged work. HEAD_SHA must be github.sha /
# the tagged or dispatched release commit — not a PR-head rewrite.
#
# Why not `conclusion == success` alone?
# Mixed web + mobile PRs keep `ci.yml` in_progress for ~10+ minutes after
# required web checks finish (Mobile iOS / Android native builds). Branch
# protection does not wait on those jobs, so a merge can land — and
# deploy.yml can start — while the overall workflow conclusion is still
# empty. The web-publish artifact is already uploaded by then (build
# finished with the required suite). Requiring overall success made
# deploy fail on #860 / #866 even though the zip existed.
#
# Selection rules (first match wins):
#   1. Newest ci.yml run whose head_sha exactly equals the release SHA
#      with a non-expired web-publish-* artifact, preferring
#      conclusion=success, then status in_progress/queued/completed
#      (artifact present). Event is not filtered: a pull_request,
#      merge_group, or workflow_dispatch run is eligible only when its
#      head_sha matches.
#   2. Brief poll when a matching-SHA run exists but the artifact has
#      not appeared yet (build still uploading).
#
# Usage:
#   REPO=owner/name HEAD_SHA=abc123 bash ./scripts/Resolve-CiPublishRun.sh
#   bash ./scripts/Resolve-CiPublishRun.sh --self-test
#
# Prints:
#   found=true|false
#   run_id=<id>          (empty when found=false)
#   run_head_sha=<sha>   (empty when found=false)
set -euo pipefail

MAX_ATTEMPTS="${MAX_ATTEMPTS:-6}"
SLEEP_SECONDS="${SLEEP_SECONDS:-10}"

# Overridable for self-test (inject fake API responses).
gh_api() {
  gh api "$@"
}

list_runs_for_sha() {
  local repo="$1"
  local head_sha="$2"
  # Server-side head_sha filter — do not rely on gh run list's recent-only page.
  # Do not also filter event=pull_request: a matching merge_group or
  # workflow_dispatch run for this exact SHA is reusable; a PR-head run
  # whose SHA differs is not.
  # Pipe through jq: `gh api --jq` does not forward jq --arg flags.
  gh_api "repos/${repo}/actions/workflows/ci.yml/runs?head_sha=${head_sha}&per_page=30" \
    | jq '.workflow_runs // [] | map({id, status, conclusion, head_sha})'
}

artifact_name_for_run() {
  local run_id="$1"
  echo "web-publish-${run_id}"
}

run_has_web_publish() {
  local repo="$1"
  local run_id="$2"
  local expected
  expected="$(artifact_name_for_run "${run_id}")"
  local found
  found="$(gh_api "repos/${repo}/actions/runs/${run_id}/artifacts?per_page=100" \
    | jq --arg name "${expected}" \
      '[.artifacts[]? | select(.name == $name and (.expired | not))] | length')"
  [[ "${found:-0}" -gt 0 ]]
}

# Rank: success first, then any run that still might expose the artifact.
rank_run() {
  local conclusion="$1"
  local status="$2"
  if [[ "${conclusion}" = "success" ]]; then
    echo 0
  elif [[ "${status}" = "in_progress" ]] || [[ "${status}" = "queued" ]] || [[ "${status}" = "pending" ]]; then
    echo 1
  elif [[ "${conclusion}" = "failure" ]] || [[ "${conclusion}" = "cancelled" ]] || [[ "${conclusion}" = "timed_out" ]]; then
    # Mobile native jobs can fail after web checks + merge; web-publish is still valid.
    echo 2
  else
    echo 3
  fi
}

pick_run_id_with_artifact() {
  local repo="$1"
  local runs_json="$2"
  local expected_sha="$3"
  # Defense in depth: even if the list API is called without head_sha or
  # returns a stale associated-PR run, never pick a different commit.
  local ranked
  ranked="$(printf '%s\n' "${runs_json}" | jq -r \
    --arg expected "${expected_sha}" '
    map(select((.head_sha // "") == $expected)) |
    sort_by(.id) | reverse | .[] |
    [.id, (.conclusion // ""), (.status // "")] | @tsv
  ')"

  local best_id=""
  local best_rank=99
  local id conclusion status rank
  while IFS=$'\t' read -r id conclusion status; do
    [[ -n "${id}" ]] || continue
    if ! run_has_web_publish "${repo}" "${id}"; then
      continue
    fi
    rank="$(rank_run "${conclusion}" "${status}")"
    if [[ "${rank}" -lt "${best_rank}" ]]; then
      best_rank="${rank}"
      best_id="${id}"
    fi
    # success is best possible; stop early
    if [[ "${best_rank}" -eq 0 ]]; then
      break
    fi
  done <<<"${ranked}"

  printf '%s' "${best_id}"
}

# True when at least one run is still running (artifact may appear soon).
has_active_run() {
  local runs_json="$1"
  printf '%s\n' "${runs_json}" | jq -e '
    any(.[]; .status == "in_progress" or .status == "queued" or .status == "pending")
  ' >/dev/null 2>&1
}

resolve() {
  local repo="$1"
  local head_sha="$2"
  local attempt runs_json run_id

  for attempt in $(seq 1 "${MAX_ATTEMPTS}"); do
    runs_json="$(list_runs_for_sha "${repo}" "${head_sha}")"
    run_count="$(printf '%s\n' "${runs_json}" | jq 'length')"
    echo "Attempt ${attempt}/${MAX_ATTEMPTS}: ${run_count} ci.yml run(s) listed for exact SHA ${head_sha}." >&2

    run_id="$(pick_run_id_with_artifact "${repo}" "${runs_json}" "${head_sha}")"
    if [[ -n "${run_id}" ]]; then
      echo "Using ci.yml run ${run_id} (head_sha=${head_sha}, artifact web-publish-${run_id})." >&2
      echo "found=true"
      echo "run_id=${run_id}"
      echo "run_head_sha=${head_sha}"
      return 0
    fi

    if [[ "${attempt}" -lt "${MAX_ATTEMPTS}" ]] && has_active_run "${runs_json}"; then
      echo "No web-publish artifact yet; waiting ${SLEEP_SECONDS}s for an in-progress build to upload it." >&2
      sleep "${SLEEP_SECONDS}"
      continue
    fi

    break
  done

  echo "No ci.yml run with a web-publish artifact whose head_sha exactly equals ${head_sha}. Deploy will publish from the checked-out SHA." >&2
  echo "found=false"
  echo "run_id="
  echo "run_head_sha="
  return 0
}

assert_eq() {
  local name="$1"
  local expected="$2"
  local got="$3"
  if [[ "${got}" != "${expected}" ]]; then
    echo "FAIL ${name}" >&2
    echo " expected: ${expected}" >&2
    echo " got:      ${got}" >&2
    return 1
  fi
  echo "PASS ${name}" >&2
}

if [[ "${1:-}" = "--self-test" ]]; then
  fail=0
  tmp="$(mktemp -d)"
  trap 'rm -rf "${tmp}"' EXIT

  # Fake gh api: path-based fixtures under $tmp/fixtures (raw JSON on stdout).
  gh_api() {
    local path="$1"
    # Strip query string for fixture lookup
    local key="${path%%\?*}"
    key="${key//\//_}"
    local fixture="${tmp}/fixtures/${key}.json"
    if [[ ! -f "${fixture}" ]]; then
      echo "missing fixture for API path: ${path} (key=${key})" >&2
      return 1
    fi
    cat "${fixture}"
  }

  mkdir -p "${tmp}/fixtures"

  # --- success run with artifact ---
  cat >"${tmp}/fixtures/repos_owner_name_actions_workflows_ci.yml_runs.json" <<'EOF'
{
  "workflow_runs": [
    {"id": 111, "status": "completed", "conclusion": "success", "head_sha": "aaa"},
    {"id": 100, "status": "completed", "conclusion": "failure", "head_sha": "aaa"}
  ]
}
EOF
  cat >"${tmp}/fixtures/repos_owner_name_actions_runs_111_artifacts.json" <<'EOF'
{"artifacts":[{"name":"web-publish-111","expired":false}]}
EOF
  cat >"${tmp}/fixtures/repos_owner_name_actions_runs_100_artifacts.json" <<'EOF'
{"artifacts":[{"name":"web-publish-100","expired":false}]}
EOF

  got="$(REPO=owner/name HEAD_SHA=aaa MAX_ATTEMPTS=1 SLEEP_SECONDS=0 resolve owner/name aaa | grep -E '^(found|run_id)=')"
  assert_eq prefers-success $'found=true\nrun_id=111' "${got}" || fail=1

  # --- in_progress with artifact (the #860/#866 race) ---
  cat >"${tmp}/fixtures/repos_owner_name_actions_workflows_ci.yml_runs.json" <<'EOF'
{
  "workflow_runs": [
    {"id": 222, "status": "in_progress", "conclusion": null, "head_sha": "bbb"}
  ]
}
EOF
  cat >"${tmp}/fixtures/repos_owner_name_actions_runs_222_artifacts.json" <<'EOF'
{"artifacts":[{"name":"web-publish-222","expired":false},{"name":"mobile-ios-222","expired":false}]}
EOF

  got="$(REPO=owner/name HEAD_SHA=bbb MAX_ATTEMPTS=1 SLEEP_SECONDS=0 resolve owner/name bbb | grep -E '^(found|run_id)=')"
  assert_eq in-progress-with-artifact $'found=true\nrun_id=222' "${got}" || fail=1

  # --- failure after merge (mobile native failed) but web-publish present ---
  cat >"${tmp}/fixtures/repos_owner_name_actions_workflows_ci.yml_runs.json" <<'EOF'
{
  "workflow_runs": [
    {"id": 333, "status": "completed", "conclusion": "failure", "head_sha": "ccc"}
  ]
}
EOF
  cat >"${tmp}/fixtures/repos_owner_name_actions_runs_333_artifacts.json" <<'EOF'
{"artifacts":[{"name":"web-publish-333","expired":false}]}
EOF

  got="$(REPO=owner/name HEAD_SHA=ccc MAX_ATTEMPTS=1 SLEEP_SECONDS=0 resolve owner/name ccc | grep -E '^(found|run_id)=')"
  assert_eq failure-with-artifact $'found=true\nrun_id=333' "${got}" || fail=1

  # --- no artifact → soft miss (fallback publish) ---
  cat >"${tmp}/fixtures/repos_owner_name_actions_workflows_ci.yml_runs.json" <<'EOF'
{
  "workflow_runs": [
    {"id": 444, "status": "completed", "conclusion": "success", "head_sha": "ddd"}
  ]
}
EOF
  cat >"${tmp}/fixtures/repos_owner_name_actions_runs_444_artifacts.json" <<'EOF'
{"artifacts":[{"name":"mobile-android-444","expired":false}]}
EOF

  got="$(REPO=owner/name HEAD_SHA=ddd MAX_ATTEMPTS=1 SLEEP_SECONDS=0 resolve owner/name ddd | grep -E '^(found|run_id)=')"
  assert_eq no-artifact-soft-miss $'found=false\nrun_id=' "${got}" || fail=1

  # --- empty runs → soft miss ---
  cat >"${tmp}/fixtures/repos_owner_name_actions_workflows_ci.yml_runs.json" <<'EOF'
{"workflow_runs":[]}
EOF
  got="$(REPO=owner/name HEAD_SHA=eee MAX_ATTEMPTS=1 SLEEP_SECONDS=0 resolve owner/name eee | grep -E '^(found|run_id)=')"
  assert_eq empty-runs-soft-miss $'found=false\nrun_id=' "${got}" || fail=1

  # --- different head_sha must never be reused (stale PR-head zip) ---
  # Simulates the API returning a run for an associated PR head that is
  # not the release SHA. Exact-match guard must soft-miss and fall back.
  cat >"${tmp}/fixtures/repos_owner_name_actions_workflows_ci.yml_runs.json" <<'EOF'
{
  "workflow_runs": [
    {"id": 555, "status": "completed", "conclusion": "success", "head_sha": "537abc8staleprhead"}
  ]
}
EOF
  cat >"${tmp}/fixtures/repos_owner_name_actions_runs_555_artifacts.json" <<'EOF'
{"artifacts":[{"name":"web-publish-555","expired":false}]}
EOF
  got="$(REPO=owner/name HEAD_SHA=c378628releasetip MAX_ATTEMPTS=1 SLEEP_SECONDS=0 resolve owner/name c378628releasetip | grep -E '^(found|run_id|run_head_sha)=')"
  assert_eq mismatched-sha-rejected $'found=false\nrun_id=\nrun_head_sha=' "${got}" || fail=1

  # --- exact SHA match still logs the chosen run id and SHA ---
  cat >"${tmp}/fixtures/repos_owner_name_actions_workflows_ci.yml_runs.json" <<'EOF'
{
  "workflow_runs": [
    {"id": 666, "status": "completed", "conclusion": "success", "head_sha": "c378628releasetip"}
  ]
}
EOF
  cat >"${tmp}/fixtures/repos_owner_name_actions_runs_666_artifacts.json" <<'EOF'
{"artifacts":[{"name":"web-publish-666","expired":false}]}
EOF
  got="$(REPO=owner/name HEAD_SHA=c378628releasetip MAX_ATTEMPTS=1 SLEEP_SECONDS=0 resolve owner/name c378628releasetip | grep -E '^(found|run_id|run_head_sha)=')"
  assert_eq exact-sha-logs-run $'found=true\nrun_id=666\nrun_head_sha=c378628releasetip' "${got}" || fail=1

  # --- rank helper ---
  assert_eq rank-success 0 "$(rank_run success completed)" || fail=1
  assert_eq rank-in-progress 1 "$(rank_run "" in_progress)" || fail=1
  assert_eq rank-failure 2 "$(rank_run failure completed)" || fail=1

  if [[ "${fail}" -ne 0 ]]; then
    echo "Resolve-CiPublishRun self-test failed." >&2
    exit 1
  fi
  echo "Resolve-CiPublishRun self-test passed." >&2
  exit 0
fi

REPO="${REPO:?REPO is required (owner/name)}"
HEAD_SHA="${HEAD_SHA:?HEAD_SHA is required}"

resolve "${REPO}" "${HEAD_SHA}"
