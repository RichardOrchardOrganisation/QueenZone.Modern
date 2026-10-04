#!/usr/bin/env bash
# Licensed caller publishes the Web app first; this runs only an owned Testing
# host. It never enables the mobile contract fixture, reads SQL, or kills by name.
set -euo pipefail
qz_repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$qz_repo_root"
qz_publish_dir="${QZ_CROSSWORD_PUBLISH_DIR:?Set QZ_CROSSWORD_PUBLISH_DIR to the licensed published Web output}"
if [[ ! -f "$qz_publish_dir/QueenZone.Web.dll" ]]; then
  echo "Published QueenZone.Web.dll was not found." >&2
  exit 2
fi
qz_run_dir="$(mktemp -d /tmp/queenzone-crossword-browser.XXXXXX)"
qz_host_log="$qz_run_dir/host.log"
unset ConnectionStrings__QueenZoneLegacy ConnectionStrings__BlobStorage ConnectionStrings__SqlServerTest || true
unset QUEENZONE_MOBILE_CONTRACT_HOST QUEENZONE_MOBILE_CONTRACT_FIXTURE || true
export ASPNETCORE_ENVIRONMENT=Testing ASPNETCORE_URLS=http://127.0.0.1:0
export CrosswordBrowserFixture__Enabled=true
ASPNETCORE_CONTENTROOT="$qz_publish_dir" dotnet "$qz_publish_dir/QueenZone.Web.dll" >"$qz_host_log" 2>&1 &
qz_host_pid=$!
trap 'kill "$qz_host_pid" 2>/dev/null || true; wait "$qz_host_pid" 2>/dev/null || true' EXIT
export E2E_BASE_URL=""
for qz_attempt in $(seq 1 60); do
  if ! kill -0 "$qz_host_pid" 2>/dev/null; then
    echo "Owned Testing host exited; inspect $qz_host_log." >&2
    exit 2
  fi
  E2E_BASE_URL=$(python3 -c 'import re,sys;from pathlib import Path;m=re.search(r"Now listening on: (http://127\.0\.0\.1:\d+)",Path(sys.argv[1]).read_text());print(m.group(1) if m else "")' "$qz_host_log")
  if [[ -n "$E2E_BASE_URL" ]] && curl -sf "$E2E_BASE_URL/health" >/dev/null; then break; fi
  sleep 1
done
if [[ -z "$E2E_BASE_URL" ]] || ! curl -sf "$E2E_BASE_URL/health" >/dev/null; then
  echo "Owned Testing host did not become healthy; inspect $qz_host_log." >&2
  exit 2
fi
export E2E_ARTIFACT_DIR="${E2E_ARTIFACT_DIR:-$qz_repo_root/artifacts/proof/web.crosswords.play/web}"
dotnet test tests/QueenZone.Web.E2E --configuration "${QZ_TEST_CONFIGURATION:-Debug}" --no-build \
  --filter "${QZ_TEST_FILTER:-FullyQualifiedName~CrosswordPlayTests}" \
  --logger 'console;verbosity=normal' -- Playwright.BrowserName="${QZ_TEST_BROWSER:-chromium}"
