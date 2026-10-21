#!/usr/bin/env bash
# Point this job at the newest installed Xcode via DEVELOPER_DIR.
# Never run sudo xcode-select on a self-hosted runner (machine-global).
# Hosted GitHub-hosted images may still sudo because the VM is ephemeral.
set -euo pipefail

if [[ -z "${GITHUB_ENV:-}" ]]; then
  echo "GITHUB_ENV is required." >&2
  exit 1
fi

apps_dir="${IOS_CI_XCODE_APPS_DIR:-/Applications}"
xcode_select_cmd="${IOS_CI_XCODE_SELECT:-xcode-select}"
xcodebuild_cmd="${IOS_CI_XCODEBUILD:-xcodebuild}"
sudo_cmd="${IOS_CI_SUDO:-sudo}"
runner_environment="${RUNNER_ENVIRONMENT:-}"

ls "${apps_dir}" | grep -i Xcode || true
latest=$(ls -d "${apps_dir}"/Xcode_*.app 2>/dev/null | sort -V | tail -n 1 || true)
if [[ -z "${latest}" && -d "${apps_dir}/Xcode.app" ]]; then
  latest="${apps_dir}/Xcode.app"
fi
if [[ -z "${latest}" ]]; then
  echo "::error::No Xcode application found on this runner."
  exit 1
fi

expected="${latest}/Contents/Developer"
echo "DEVELOPER_DIR=${expected}" >> "${GITHUB_ENV}"
export DEVELOPER_DIR="${expected}"
echo "Using ${latest} via DEVELOPER_DIR (per-job; not a global xcode-select change by default)."

current="$("${xcode_select_cmd}" -p 2>/dev/null || true)"
if [[ "${current}" == "${expected}" ]]; then
  echo "Xcode is already selected: ${latest}"
elif [[ "${runner_environment}" == "self-hosted" ]]; then
  echo "Self-hosted xcode-select is ${current:-unset}; leaving it unchanged and using DEVELOPER_DIR for this job."
else
  echo "Selecting ${latest} on hosted runner"
  "${sudo_cmd}" xcode-select -s "${expected}"
fi

"${xcodebuild_cmd}" -version
