#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
flow="$root/src/QueenZone.Mobile/maestro/app-review-video.yaml"
results_dir="$root/src/QueenZone.Mobile/maestro-results/app-review"
project_id="1c16fd2d-4bfb-4eb7-8357-b49400233490"
device=""

usage() {
  echo "Usage: $0 --device <ios-simulator-udid>" >&2
  echo "Maestro cannot run this flow on a physical iPhone. Use it to rehearse the" >&2
  echo "walkthrough, then repeat the same screenplay while recording TestFlight." >&2
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --device)
      if [ "$#" -lt 2 ]; then
        usage
        exit 2
      fi
      device="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 2
      ;;
  esac
done

if [ -z "$device" ]; then
  usage
  exit 2
fi

for command_name in bws jq maestro xcrun; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "Required command is not available: $command_name" >&2
    exit 1
  fi
done

if [ -z "${BWS_ACCESS_TOKEN:-}" ]; then
  echo "BWS_ACCESS_TOKEN is not available in this shell." >&2
  exit 1
fi

if ! xcrun simctl list devices available | grep -Fq "$device"; then
  echo "Device is not an available iOS Simulator: $device" >&2
  echo "Physical iOS devices are not supported by Maestro." >&2
  exit 1
fi

secret_list="$(bws secret list "$project_id" --output json)"
reviewer_email="$(printf '%s' "$secret_list" | jq -er '.[] | select(.key == "REVIEWER_EMAIL") | .value')"
reviewer_password="$(printf '%s' "$secret_list" | jq -er '.[] | select(.key == "REVIEWER_PASSWORD") | .value')"

mkdir -p "$results_dir"

# Maestro includes injected environment values in commands.json and diagnostic
# logs. Scrub the password from every retained text artifact, including when a
# flow fails, so a reusable QA run cannot leave reviewer credentials on disk.
redact_reviewer_password() {
  if [ -z "${reviewer_password:-}" ] || [ ! -d "$results_dir" ]; then
    return
  fi

  export QZ_MAESTRO_SECRET_TO_REDACT="$reviewer_password"
  while IFS= read -r -d '' artifact; do
    if LC_ALL=C grep -Iq . "$artifact" && \
       LC_ALL=C grep -qF -- "$QZ_MAESTRO_SECRET_TO_REDACT" "$artifact"; then
      perl -0pi -e 's/\Q$ENV{QZ_MAESTRO_SECRET_TO_REDACT}\E/[REDACTED]/g' "$artifact"
    fi
  done < <(find "$results_dir" -type f -print0)
  unset QZ_MAESTRO_SECRET_TO_REDACT
}
trap redact_reviewer_password EXIT

maestro --device "$device" test \
  --test-output-dir "$results_dir" \
  --format JUNIT \
  --output "$results_dir/results.xml" \
  -e "REVIEWER_EMAIL=$reviewer_email" \
  -e "REVIEWER_PASSWORD=$reviewer_password" \
  "$flow"

echo "Rehearsal artifacts: $results_dir"
