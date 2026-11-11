#!/usr/bin/env bash
# Create or delete a job-owned iPhone Simulator.
# Never erase, shutdown, or delete every simulator on the machine.
# Pick an available iOS runtime/device from `simctl list -j` that matches
# the selected Xcode (DEVELOPER_DIR / iphonesimulator SDK), else newest.
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

# A runner killed mid-job (service restart, Mac sleep/reboot, cancel past the
# grace period) never reaches the always() cleanup, so its job Simulator stays
# behind, sometimes still booted. Delete only QueenZone-CI-iOS-* devices whose
# device.plist has not changed for IOS_CI_SIM_STALE_HOURS (default 6, well past
# the 90-minute job timeout). Never touches other simulators, and a failed
# sweep never fails the job.
sweep_stale_job_simulators() {
  local stale
  stale="$(
    IOS_CI_XCRUN="${xcrun_cmd}" \
    IOS_CI_SIM_DEVICES_DIR="${IOS_CI_SIM_DEVICES_DIR:-${HOME}/Library/Developer/CoreSimulator/Devices}" \
    IOS_CI_SIM_STALE_HOURS="${IOS_CI_SIM_STALE_HOURS:-6}" \
    python3 - <<'PY'
import json, os, subprocess, sys, time

xcrun = os.environ["IOS_CI_XCRUN"]
devices_dir = os.environ["IOS_CI_SIM_DEVICES_DIR"]
max_age = float(os.environ["IOS_CI_SIM_STALE_HOURS"]) * 3600
devices = json.loads(subprocess.check_output([xcrun, "simctl", "list", "-j", "devices"], text=True)).get("devices") or {}
now = time.time()
for runtime_devices in devices.values():
    for device in runtime_devices or []:
        name = str(device.get("name") or "")
        udid = str(device.get("udid") or "")
        if not name.startswith("QueenZone-CI-iOS-") or not udid:
            continue
        plist = os.path.join(devices_dir, udid, "device.plist")
        try:
            age = now - os.path.getmtime(plist)
        except OSError:
            continue
        if age > max_age:
            print(f"{udid}\t{name}\t{device.get('state', '?')}\t{age / 3600:.1f}")
PY
  )" || {
    echo "::warning::Could not list simulators to sweep stale QueenZone-CI-iOS-* devices; continuing."
    return 0
  }
  [[ -n "${stale}" ]] || return 0
  local udid name state hours
  while IFS=$'\t' read -r udid name state hours; do
    [[ -n "${udid}" ]] || continue
    echo "Deleting abandoned job simulator ${name} (${udid}; ${state}; untouched ${hours}h)"
    "${xcrun_cmd}" simctl shutdown "${udid}" >/dev/null 2>&1 || true
    "${xcrun_cmd}" simctl delete "${udid}" >/dev/null 2>&1 || true
  done <<< "${stale}"
}

sweep_stale_job_simulators

pick="$(
  IOS_CI_XCRUN="${xcrun_cmd}" python3 - <<'PY'
import json, os, subprocess, sys

xcrun = os.environ.get("IOS_CI_XCRUN", "xcrun")


def run(*args):
    return subprocess.check_output([xcrun, *args], text=True)


def load_list(*args):
    return json.loads(run("simctl", "list", "-j", *args))


def version_key(value):
    parts = []
    for piece in str(value or "0").replace("-", ".").split("."):
        try:
            parts.append(int(piece))
        except ValueError:
            parts.append(0)
    return parts


try:
    runtimes = load_list("runtimes").get("runtimes") or []
    device_types = load_list("devicetypes").get("devicetypes") or []
except Exception as exc:
    print(f"::error::simctl list -j failed: {exc}", file=sys.stderr)
    sys.exit(1)

ios_runtimes = [
    runtime
    for runtime in runtimes
    if runtime.get("isAvailable", True)
    and (
        str(runtime.get("identifier") or "").startswith("com.apple.CoreSimulator.SimRuntime.iOS")
        or str(runtime.get("name") or "").startswith("iOS")
    )
]
if not ios_runtimes:
    print("::error::No available iOS Simulator runtime in simctl list -j.", file=sys.stderr)
    sys.exit(1)

sdk = os.environ.get("IOS_CI_SIM_SDK", "").strip()
if not sdk:
    try:
        sdk = run("--sdk", "iphonesimulator", "--show-sdk-version").strip()
    except Exception:
        sdk = ""


def matches_sdk(runtime, wanted):
    if not wanted:
        return False
    version = str(runtime.get("version") or "")
    return version == wanted or version.startswith(f"{wanted}.")


matched = [runtime for runtime in ios_runtimes if matches_sdk(runtime, sdk)]
chosen = max(matched or ios_runtimes, key=lambda runtime: version_key(runtime.get("version")))

supported = chosen.get("supportedDeviceTypes") or []
iphones = [
    device
    for device in supported
    if "iPhone" in str(device.get("name") or "")
    or "iPhone" in str(device.get("identifier") or "")
]
if not iphones:
    iphones = [
        device
        for device in device_types
        if str(device.get("productFamily") or "") == "iPhone"
        or "iPhone" in str(device.get("name") or "")
    ]
if not iphones:
    print(
        f"::error::No iPhone device type for runtime {chosen.get('identifier')}.",
        file=sys.stderr,
    )
    sys.exit(1)

device = sorted(
    iphones,
    key=lambda item: version_key(str(item.get("name") or item.get("identifier") or "")),
)[-1]
device_id = device.get("identifier")
runtime_id = chosen.get("identifier")
if not device_id or not runtime_id:
    print("::error::Selected simulator identifiers were empty.", file=sys.stderr)
    sys.exit(1)

print(
    f"Selected {device.get('name')} / {chosen.get('name')} "
    f"(sdk={sdk or 'n/a'}; source=simctl list -j)",
    file=sys.stderr,
)
print(f"{device_id}\t{runtime_id}")
PY
)" || {
  echo "::error::No available iPhone Simulator runtime." >&2
  "${xcrun_cmd}" simctl list devices || true
  "${xcrun_cmd}" simctl list runtimes || true
  exit 1
}

device_type="${pick%%$'\t'*}"
runtime="${pick#*$'\t'}"
if [[ -z "${device_type}" || -z "${runtime}" || "${device_type}" == "${runtime}" ]]; then
  echo "::error::No available iPhone Simulator runtime." >&2
  "${xcrun_cmd}" simctl list devices || true
  "${xcrun_cmd}" simctl list runtimes || true
  exit 1
fi

name="QueenZone-CI-iOS-${GITHUB_RUN_ID:-local}-${GITHUB_JOB:-job}"
udid=$("${xcrun_cmd}" simctl create "${name}" "${device_type}" "${runtime}")
if [[ -z "${udid}" ]]; then
  echo "::error::Failed to create iPhone Simulator ${name}." >&2
  exit 1
fi

# Record the UDID before boot so an always() cleanup can delete this
# simulator even if bootstatus fails.
echo "IOS_SIM_UDID=${udid}" >> "${GITHUB_ENV}"
echo "IOS_SIM_NAME=${name}" >> "${GITHUB_ENV}"
echo "Booting ${udid} (${name}; ${device_type} / ${runtime})"
"${xcrun_cmd}" simctl boot "${udid}" || true
"${xcrun_cmd}" simctl bootstatus "${udid}" -b
