#!/usr/bin/env bash
# Prefer an idle self-hosted ios-build Mac; fail open to hosted macos-26.
# Used by .github/workflows/choose-ios-runner.yml (ci.yml + mobile-device-smoke.yml).
# GitHub has no native self-hosted/hosted runs-on fallback (issue #893).
#
# Environment:
#   GITHUB_OUTPUT          required Actions output file
#   GITHUB_REPOSITORY      owner/repo for the runners API
#   GH_TOKEN               optional PAT with Administration: read (runner list)
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

if [[ "${FORCE_HOSTED:-}" == "true" ]]; then
  echo "FORCE_HOSTED=true; using GitHub-hosted macos-26."
  exit 0
fi

if [[ "${MAC_CLAIMED_BY_SIBLING:-}" == "true" ]]; then
  echo "A sibling Android job in this workflow run is scheduled on the Mac; using GitHub-hosted macos-26."
  exit 0
fi

if [[ -z "${GH_TOKEN:-}" ]]; then
  echo "Runner-list token is unavailable; using GitHub-hosted macos-26."
  exit 0
fi

if [[ -z "${GITHUB_REPOSITORY:-}" ]]; then
  echo "GITHUB_REPOSITORY is unset; using GitHub-hosted macos-26."
  exit 0
fi

set +e
response=$(gh api --paginate \
  -H "Accept: application/vnd.github+json" \
  "/repos/${GITHUB_REPOSITORY}/actions/runners?per_page=100" 2>/dev/null)
status=$?
set -e

if [[ "${status}" -ne 0 || -z "${response}" ]]; then
  echo "Runner status could not be read; using GitHub-hosted macos-26."
  exit 0
fi

available=$(jq -s 'any(.[].runners[]?;
  .status == "online" and .busy == false and
  ([.labels[].name] | contains(["self-hosted", "macOS", "ARM64", "ios-build"])))' \
  <<< "${response}" 2>/dev/null || true)

if [[ "${available}" == "true" ]]; then
  echo "runs_on=${self_hosted}" >> "${GITHUB_OUTPUT}"
  echo "runner_class=self-hosted ios-build" >> "${GITHUB_OUTPUT}"
  echo "An online, idle ios-build runner is available."
else
  echo "No online, idle ios-build runner is available; using GitHub-hosted macos-26."
fi
