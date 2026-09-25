#!/usr/bin/env bash
# Start QueenZone.Web in the deterministic Testing environment and run the
# mobile API consumer-contract suite against the real /api/v1 pipeline.
#
# Never points Testing at a real database, blob store, or live site.
# Usage (repo root):
#   ./scripts/run-mobile-api-contracts.sh
#   ./scripts/run-mobile-api-contracts.sh --no-build
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

no_build=false
if [[ "${1:-}" = "--no-build" ]]; then
  no_build=true
fi

unset ConnectionStrings__QueenZoneLegacy || true
unset ConnectionStrings__BlobStorage || true
unset ConnectionStrings__SqlServerTest || true

export ASPNETCORE_ENVIRONMENT=Testing
export QUEENZONE_MOBILE_CONTRACT_HOST=1
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:0}"
export QUEENZONE_MOBILE_CONTRACT_FIXTURE="${QUEENZONE_MOBILE_CONTRACT_FIXTURE:-$root/src/QueenZone.Mobile/contracts/host.json}"

rm -f "$QUEENZONE_MOBILE_CONTRACT_FIXTURE" "${QUEENZONE_MOBILE_CONTRACT_FIXTURE}.tmp"

if [[ "$no_build" != true ]]; then
  dotnet build src/QueenZone.Web/QueenZone.Web.csproj --configuration Release
fi

log="$(mktemp /tmp/queenzone-mobile-api-contract-host.XXXXXX.log)"
host_pid=""

cleanup() {
  if [[ -n "${host_pid}" ]] && kill -0 "$host_pid" 2>/dev/null; then
    kill "$host_pid" 2>/dev/null || true
    wait "$host_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

echo "Starting Testing contract host (log: $log)..."
dotnet run \
  --project src/QueenZone.Web/QueenZone.Web.csproj \
  --configuration Release \
  --no-build \
  --no-launch-profile \
  >"$log" 2>&1 &
host_pid=$!

for i in $(seq 1 60); do
  if [[ -f "$QUEENZONE_MOBILE_CONTRACT_FIXTURE" ]]; then
    break
  fi
  if ! kill -0 "$host_pid" 2>/dev/null; then
    echo "Contract host exited before writing the fixture." >&2
    cat "$log" >&2 || true
    exit 1
  fi
  sleep 1
done

if [[ ! -f "$QUEENZONE_MOBILE_CONTRACT_FIXTURE" ]]; then
  echo "Timed out waiting for $QUEENZONE_MOBILE_CONTRACT_FIXTURE" >&2
  cat "$log" >&2 || true
  exit 1
fi

base_url="$(node -e "const fs=require('fs'); process.stdout.write(JSON.parse(fs.readFileSync(process.env.QUEENZONE_MOBILE_CONTRACT_FIXTURE,'utf8')).baseUrl)")"
echo "Contract host ready at $base_url"

if ! curl -sf "$base_url/health" >/dev/null; then
  echo "Health check failed for $base_url/health" >&2
  cat "$log" >&2 || true
  exit 1
fi

(
  cd src/QueenZone.Mobile
  npm run test:api-contracts
)
