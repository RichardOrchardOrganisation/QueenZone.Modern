#!/usr/bin/env bash
# Prefer an idle self-hosted ios-build Mac; fail open to hosted macos-26.
# Used by .github/workflows/choose-ios-runner.yml (ci.yml + mobile-device-smoke.yml).
# GitHub has no native self-hosted/hosted runs-on fallback (issue #893).
#
# Environment:
#   GITHUB_OUTPUT          required Actions output file
#   GITHUB_REPOSITORY      owner/repo for the runners API
#   GH_TOKEN               optional PAT with Administration: read (runner list)
#   IOS_RUNNER_ADMIN_TOKEN fallback when GH_TOKEN is empty (Bitwarden set_env)
#   FORCE_HOSTED           "true" skips the Mac even when it is idle
#   MAC_CLAIMED_BY_SIBLING "true" when a sibling job in this run will use the Mac
set -euo pipefail

if [[ -z "${GITHUB_OUTPUT:-}" ]]; then
  echo "GITHUB_OUTPUT is required." >&2
  exit 1
fi

hosted='["macos-26"]'
self_hosted='["self-hosted","macOS","ARM64","ios-build"]'

echo "runs_on=${hosted}" >> "${GITHUB_OUTPUT}"
echo "runs_on_secondary=${hosted}" >> "${GITHUB_OUTPUT}"
echo "runner_class=github-hosted macos-26" >> "${GITHUB_OUTPUT}"

use_hosted() {
  echo "${1}; using GitHub-hosted macos-26."
}

if [[ "${FORCE_HOSTED:-}" == "true" ]]; then
  use_hosted "FORCE_HOSTED=true"
  exit 0
fi

if [[ "${MAC_CLAIMED_BY_SIBLING:-}" == "true" ]]; then
  use_hosted "A sibling Android job in this workflow run is scheduled on the Mac"
  exit 0
fi

if [[ -z "${GH_TOKEN:-}" && -n "${IOS_RUNNER_ADMIN_TOKEN:-}" ]]; then
  GH_TOKEN="${IOS_RUNNER_ADMIN_TOKEN}"
  export GH_TOKEN
  echo "Using IOS_RUNNER_ADMIN_TOKEN as GH_TOKEN (length=${#GH_TOKEN})."
fi

if [[ -z "${GH_TOKEN:-}" ]]; then
  use_hosted "Runner-list token is unavailable (length=0)"
  exit 0
fi

if [[ -z "${GITHUB_REPOSITORY:-}" ]]; then
  use_hosted "GITHUB_REPOSITORY is unset"
  exit 0
fi

echo "Runner-list token length=${#GH_TOKEN} repo=${GITHUB_REPOSITORY}"

org="${GITHUB_REPOSITORY%%/*}"
tmp="$(mktemp -d)"
trap 'rm -rf "${tmp}"' EXIT
: > "${tmp}/runners.ndjson"

fetch_runners() {
  local path="$1"
  local tag="$2"
  local out="${tmp}/${tag}.json"
  local err="${tmp}/${tag}.err"
  set +e
  GH_PAGER=cat gh api --paginate \
    -H "Accept: application/vnd.github+json" \
    -H "X-GitHub-Api-Version: 2022-11-28" \
    "${path}" >"${out}" 2>"${err}"
  local status=$?
  set -e
  local bytes
  bytes="$(wc -c <"${out}" | tr -d ' ')"
  echo "Probe ${tag} GET ${path} exit=${status} bytes=${bytes}"
  if [[ "${status}" -ne 0 ]]; then
    echo "Probe ${tag} failed: $(tr '\n' ' ' <"${err}" | head -c 240)"
    return 0
  fi
  if [[ ! -s "${out}" ]]; then
    echo "Probe ${tag} returned an empty body."
    return 0
  fi
  if ! jq -s '
      .[]
      | if type == "array" then .[]
        elif type == "object" and has("runners") then .runners[]?
        elif type == "object" and has("labels") then .
        else empty
        end
    ' "${out}" >> "${tmp}/runners.ndjson"; then
    echo "Probe ${tag} JSON was not a runner list."
  fi
}

# Repo-registered runners and org runners (the Mini is often org-scoped).
fetch_runners "/repos/${GITHUB_REPOSITORY}/actions/runners?per_page=100" "repo"
fetch_runners "/orgs/${org}/actions/runners?per_page=100" "org"

if [[ ! -s "${tmp}/runners.ndjson" ]]; then
  use_hosted "No runners parsed from repo or org APIs"
  exit 0
fi

jq -s '
  def labels_of:
    (.labels // []) | map((.name // .) | ascii_downcase);
  def has($ls; $n):
    any($ls[]; . == $n or endswith(":" + $n));
  [
    unique_by(.id // .name)[]
    | labels_of as $labels
    | {
        name: (.name // "unnamed"),
        status: (.status // "unknown"),
        busy,
        labels: ($labels | join(",")),
        match: (
          .status == "online"
          and (.busy == false)
          and has($labels; "self-hosted")
          and has($labels; "macos")
          and has($labels; "arm64")
          and has($labels; "ios-build")
        ),
        reject: (
          if .status != "online" then "status=\(.status // "unknown")"
          elif .busy != false then "busy=\(.busy)"
          elif (has($labels; "self-hosted") | not) then "missing self-hosted"
          elif (has($labels; "macos") | not) then "missing macos"
          elif (has($labels; "arm64") | not) then "missing arm64"
          elif (has($labels; "ios-build") | not) then "missing ios-build"
          else "ok"
          end
        )
      }
  ]
' "${tmp}/runners.ndjson" > "${tmp}/considered.json"

count="$(jq 'length' "${tmp}/considered.json")"
echo "Considered ${count} runner(s) after merging repo and org lists:"
jq -r '.[] | "  \(.name) status=\(.status) busy=\(.busy) labels=\(.labels) reject=\(.reject)"' "${tmp}/considered.json"

if jq -e 'any(.[]; .match)' "${tmp}/considered.json" >/dev/null; then
  chosen="$(jq -r '[.[] | select(.match) | .name][0]' "${tmp}/considered.json")"
  echo "runs_on=${self_hosted}" >> "${GITHUB_OUTPUT}"
  echo "runner_class=self-hosted ios-build" >> "${GITHUB_OUTPUT}"
  echo "An online, idle ios-build runner is available (${chosen})."
  exit 0
fi

use_hosted "${count} runner(s) considered; none online, idle, and labeled ios-build"
