#!/usr/bin/env bash
# Create or delete a job-owned iPhone Simulator.
# Never erase, shutdown, or delete every simulator on the machine.
set -euo pipefail

usage() {
  echo "Usage: $0 create|cleanup" >&2
  exit 2
}

cmd="${1:-}"
if [[ "${cmd}" != "create" && "${cmd}" != "cleanup" ]]; then
  usage
fi

xcrun_cmd="${IOS_CI_XCRUN:-xcrun}"

if [[ "${cmd}" == "cleanup" ]]; then
  udid="${IOS_SIM_UDID:-}"
  if [[ -z "${udid}" ]]; then
    echo "IOS_SIM_UDID is empty; nothing to clean up."
    exit 0
  fi
  echo "Shutting down and deleting job simulator ${udid}"
  "${xcrun_cmd}" simctl shutdown "${udid}" >/dev/null 2>&1 || true
  "${xcrun_cmd}" simctl delete "${udid}" >/dev/null 2>&1 || true
  exit 0
fi

if [[ -z "${GITHUB_ENV:-}" ]]; then
  echo "GITHUB_ENV is required to create a job simulator." >&2
  exit 1
fi

device_type=$("${xcrun_cmd}" simctl list devicetypes available | awk -F '[()]' '/iPhone/ { print $(NF-1) }' | tail -n 1 || true)
runtime=$("${xcrun_cmd}" simctl list runtimes available | grep iOS | grep -oE 'com\.apple\.CoreSimulator\.SimRuntime\.[^[:space:]]+' | tail -n 1 || true)

if [[ -z "${device_type}" || -z "${runtime}" ]]; then
  echo "::error::No available iPhone Simulator runtime."
  "${xcrun_cmd}" simctl list devices || true
  "${xcrun_cmd}" simctl list runtimes || true
  exit 1
fi

name="QueenZone-CI-iOS-${GITHUB_RUN_ID:-local}-${GITHUB_JOB:-job}"
udid=$("${xcrun_cmd}" simctl create "${name}" "${device_type}" "${runtime}")
if [[ -z "${udid}" ]]; then
  echo "::error::Failed to create iPhone Simulator ${name}."
  exit 1
fi

echo "Booting ${udid} (${name}; ${device_type} / ${runtime})"
"${xcrun_cmd}" simctl boot "${udid}" || true
"${xcrun_cmd}" simctl bootstatus "${udid}" -b
echo "IOS_SIM_UDID=${udid}" >> "${GITHUB_ENV}"
echo "IOS_SIM_NAME=${name}" >> "${GITHUB_ENV}"
