#!/usr/bin/env bash
# Reads changed paths on stdin (one per line) and writes GitHub Actions
# outputs `code=`, `migrations=`, `mobile=`, `mobile_native=`,
# `mobile_api_contracts=`, and `design_tokens=` to stdout.
#
# code=true means the .NET web app / test / deploy path should run.
# code=false when every path is docs / infra / design / root markdown /
# LICENSE / THIRD-PARTY-NOTICES / .github, except that a change to
# .github/workflows/ci.yml is always code (the suite must still run to
# validate itself), or mobile-only (src/QueenZone.Mobile/). Empty stdin
# fails closed as code=true (and mobile_api_contracts=true).
#
# mobile=true when any path is under src/QueenZone.Mobile/, or when the
# mobile coverage gate / floors change (those run inside mobile-js).
# A mobile-only change does not set code=true: the website binary is
# unchanged, so web tests and App Service deploy should not run.
# Deploy / Deploy-dev classify the range from
# scripts/Resolve-DeployChangeRange.sh (push span, dispatch tip vs parent,
# or previous v* tag … this tag) and skip rather than walk back to an
# older web tip. Mixed mobile + web still sets both flags.
# mobile_native=true only when a change can affect generated Android/iOS
# projects or native compilation. Pure TypeScript/TSX changes keep the faster
# mobile JS and contract checks without rebuilding both native apps.
#
# mobile_api_contracts=true is independent of mobile=true. It runs the
# consumer-contract suite (Testing host + real mobile client parsers)
# without forcing native compile jobs. Empty stdin fails closed as true.
#
# migrations=true when any path is under the EF migration / model set
# used by ci.yml's ef-migrations job.
#
# design_tokens=true when a copy of the design token CSS files changes:
# design/tokens/ (canonical), any design_handoff_*/tokens/ copy, the shipped
# src/QueenZone.Web/wwwroot/design-system/tokens/ copy, or the sync script
# itself. Independent of code= — design/tokens/ is otherwise skip-only, but
# these paths must always run scripts/Sync-DesignTokens.ps1 -Check so the
# copies can't silently drift (issue #1466).
set -euo pipefail

skip_re='^(docs/|infra/|design/|[^/]*\.md$|LICENSE$|THIRD-PARTY-NOTICES\.md$|\.github/)'
mobile_re='^src/QueenZone\.Mobile(/|$)'
mobile_coverage_re='^scripts/(Test-MobileCoverageGate\.mjs|mobile-coverage-floors\.json)$'
mobile_native_re='^(src/QueenZone\.Mobile/(package(-lock)?\.json|app\.json|app\.config\.(js|cjs|mjs|ts)|google-services\.json|plugins/|assets/(icon|splash-icon|android-icon-(foreground|background|monochrome)|ic-notification)\.png|src/widgets/(OnThisDayWidget\.ios|OnThisDayAndroidWidget)\.tsx)|\.github/workflows/ci\.yml$)'
migration_re='^(src/QueenZone\.Data/Migrations/|src/QueenZone\.Data/QueenZoneDbContext\.cs|src/QueenZone\.Data/QueenZoneDbContextFactory\.cs|src/QueenZone\.Data/Entities/)'
design_tokens_re='^(design/tokens/|design/design_handoff_[^/]+/tokens/|src/QueenZone\.Web/wwwroot/design-system/tokens/|scripts/Sync-DesignTokens\.ps1$)'
# Keep this list explicit so an unclassified contract path is a classifier
# bug, not a silent skip. Fail closed: empty input sets the flag true.
mobile_api_contracts_re='^(src/QueenZone\.Web/Api/|src/QueenZone\.Web/Infrastructure/MobileApiContractHost\.cs|src/QueenZone\.Web/Infrastructure/QueenZoneWebServiceCollectionExtensions\.cs|docs/architecture/json-api-v1\.md|docs/decisions/0010-versioned-json-api-conventions\.md|src/QueenZone\.Mobile/src/api/|src/QueenZone\.Mobile/src/config/|src/QueenZone\.Mobile/src/session/|src/QueenZone\.Mobile/src/screens/photos/photoGalleryMeta\.ts|src/QueenZone\.Mobile/apiEnvironments\.cjs|src/QueenZone\.Mobile/contracts/|src/QueenZone\.Mobile/scripts/run-api-contracts|scripts/run-mobile-api-contracts\.sh|scripts/classify-pipeline-changes\.sh|\.github/workflows/ci\.yml|tests/QueenZone\.Web\.Tests/MobileApiContract)'

classify() {
  local code=false
  local migrations=false
  local mobile=false
  local mobile_native=false
  local mobile_api_contracts=false
  local design_tokens=false
  local saw_any=false
  local path

  while IFS= read -r path || [[ -n "${path}" ]]; do
    [[ -z "${path}" ]] && continue
    saw_any=true

    if printf '%s\n' "${path}" | grep -qE "${mobile_re}|${mobile_coverage_re}"; then
      mobile=true
    fi

    if printf '%s\n' "${path}" | grep -qE "${design_tokens_re}"; then
      design_tokens=true
    fi

    if printf '%s\n' "${path}" | grep -qE "${mobile_native_re}"; then
      mobile_native=true
    fi

    if [[ "${path}" = ".github/workflows/ci.yml" ]]; then
      code=true
    elif printf '%s\n' "${path}" | grep -qE "${mobile_re}"; then
      :
    elif ! printf '%s\n' "${path}" | grep -qE "${skip_re}"; then
      code=true
    fi

    if printf '%s\n' "${path}" | grep -qE "${migration_re}"; then
      migrations=true
    fi

    if printf '%s\n' "${path}" | grep -qE "${mobile_api_contracts_re}"; then
      mobile_api_contracts=true
    fi
  done

  if [[ "${saw_any}" = false ]]; then
    echo "No changed paths on stdin — failing closed as code=true and mobile_api_contracts=true." >&2
    code=true
    mobile_api_contracts=true
  fi

  if [[ "${code}" = true ]]; then
    echo "Web/app/test/CI paths changed — full .NET suite (and deploy, after merge)." >&2
  elif [[ "${mobile}" = true ]]; then
    echo "Mobile-only change — skipping web build, tests, and deploy." >&2
  else
    echo "Docs/infra/design/workflow-only change — skipping build, tests, and deploy." >&2
  fi

  if [[ "${mobile}" = true ]]; then
    echo "Mobile client paths changed — will run mobile JS checks." >&2
  fi

  if [[ "${mobile_native}" = true ]]; then
    echo "Native mobile inputs changed — will compile Android and iOS." >&2
  fi

  if [[ "${mobile_api_contracts}" = true ]]; then
    echo "Mobile API contract paths changed — will run consumer-contract suite (not native compiles)." >&2
  fi

  if [[ "${migrations}" = true ]]; then
    echo "EF migration-related paths changed — will run Azure SQL migration gate." >&2
  fi

  if [[ "${design_tokens}" = true ]]; then
    echo "Design token copy changed — will run the design token sync check." >&2
  fi

  echo "code=${code}"
  echo "migrations=${migrations}"
  echo "mobile=${mobile}"
  echo "mobile_native=${mobile_native}"
  echo "mobile_api_contracts=${mobile_api_contracts}"
  echo "design_tokens=${design_tokens}"
}

assert_classify() {
  local name="$1"
  local expected="$2"
  shift 2
  local got
  got=$(printf '%s\n' "$@" | classify | grep -E '^(code|migrations|mobile|mobile_native|mobile_api_contracts|design_tokens)=')
  if [[ "${got}" != "${expected}" ]]; then
    echo "FAIL ${name}" >&2
    echo " expected:" >&2
    echo "${expected}" >&2
    echo " got:" >&2
    echo "${got}" >&2
    return 1
  fi
  echo "PASS ${name}" >&2
}

if [[ "${1:-}" = "--self-test" ]]; then
  fail=0
  nl=$'\n'

  assert_classify docs-only \
    "code=false${nl}migrations=false${nl}mobile=false${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    "docs/architecture/testing-policy.md" \
    || fail=1

  assert_classify json-api-docs \
    "code=false${nl}migrations=false${nl}mobile=false${nl}mobile_native=false${nl}mobile_api_contracts=true${nl}design_tokens=false" \
    "docs/architecture/json-api-v1.md" \
    || fail=1

  assert_classify web-cs \
    "code=true${nl}migrations=false${nl}mobile=false${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    "src/QueenZone.Web/Program.cs" \
    || fail=1

  assert_classify api-only \
    "code=true${nl}migrations=false${nl}mobile=false${nl}mobile_native=false${nl}mobile_api_contracts=true${nl}design_tokens=false" \
    "src/QueenZone.Web/Api/Content/ContentApiModels.cs" \
    || fail=1

  assert_classify contract-host-registration \
    "code=true${nl}migrations=false${nl}mobile=false${nl}mobile_native=false${nl}mobile_api_contracts=true${nl}design_tokens=false" \
    "src/QueenZone.Web/Infrastructure/QueenZoneWebServiceCollectionExtensions.cs" \
    || fail=1

  assert_classify mobile-js-only \
    "code=false${nl}migrations=false${nl}mobile=true${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    "src/QueenZone.Mobile/App.tsx" \
    || fail=1

  assert_classify mobile-native-package \
    "code=false${nl}migrations=false${nl}mobile=true${nl}mobile_native=true${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    "src/QueenZone.Mobile/package.json" \
    "src/QueenZone.Mobile/package-lock.json" \
    || fail=1

  assert_classify mobile-native-config-plugin \
    "code=false${nl}migrations=false${nl}mobile=true${nl}mobile_native=true${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    "src/QueenZone.Mobile/app.config.ts" \
    "src/QueenZone.Mobile/plugins/withAndroidWorkRuntimeAlignment.cjs" \
    || fail=1

  assert_classify mobile-native-generated-assets-and-widgets \
    "code=false${nl}migrations=false${nl}mobile=true${nl}mobile_native=true${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    "src/QueenZone.Mobile/assets/icon.png" \
    "src/QueenZone.Mobile/src/widgets/OnThisDayWidget.ios.tsx" \
    || fail=1

  assert_classify mobile-api-client \
    "code=false${nl}migrations=false${nl}mobile=true${nl}mobile_native=false${nl}mobile_api_contracts=true${nl}design_tokens=false" \
    "src/QueenZone.Mobile/src/api/client.ts" \
    || fail=1

  assert_classify mobile-plus-docs \
    "code=false${nl}migrations=false${nl}mobile=true${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    "src/QueenZone.Mobile/src/navigation/RootNavigator.tsx" \
    "AGENTS.md" \
    || fail=1

  assert_classify mixed-web-and-mobile \
    "code=true${nl}migrations=false${nl}mobile=true${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    "src/QueenZone.Mobile/App.tsx" \
    "src/QueenZone.Web/Program.cs" \
    || fail=1

  assert_classify mixed-api-and-mobile \
    "code=true${nl}migrations=false${nl}mobile=true${nl}mobile_native=false${nl}mobile_api_contracts=true${nl}design_tokens=false" \
    "src/QueenZone.Mobile/src/api/forum.ts" \
    "src/QueenZone.Web/Api/Forum/ForumApiEndpoints.cs" \
    || fail=1

  assert_classify ci-yml \
    "code=true${nl}migrations=false${nl}mobile=false${nl}mobile_native=true${nl}mobile_api_contracts=true${nl}design_tokens=false" \
    ".github/workflows/ci.yml" \
    || fail=1

  assert_classify deploy-yml-only \
    "code=false${nl}migrations=false${nl}mobile=false${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    ".github/workflows/deploy.yml" \
    || fail=1

  assert_classify deploy-range-script \
    "code=true${nl}migrations=false${nl}mobile=false${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    "scripts/Resolve-DeployChangeRange.sh" \
    || fail=1

  assert_classify dispatch-mobile-plus-docs \
    "code=false${nl}migrations=false${nl}mobile=true${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    "src/QueenZone.Mobile/App.tsx" \
    "docs/architecture/testing-policy.md" \
    || fail=1

  assert_classify mobile-coverage-gate \
    "code=true${nl}migrations=false${nl}mobile=true${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    "scripts/Test-MobileCoverageGate.mjs" \
    || fail=1

  assert_classify mobile-coverage-floors \
    "code=true${nl}migrations=false${nl}mobile=true${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    "scripts/mobile-coverage-floors.json" \
    || fail=1

  assert_classify migrations \
    "code=true${nl}migrations=true${nl}mobile=false${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    "src/QueenZone.Data/Migrations/20260821_Example.cs" \
    || fail=1

  assert_classify empty \
    "code=true${nl}migrations=false${nl}mobile=false${nl}mobile_native=false${nl}mobile_api_contracts=true${nl}design_tokens=false" \
    "" \
    || fail=1

  assert_classify design-tokens-canonical-only \
    "code=false${nl}migrations=false${nl}mobile=false${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=true" \
    "design/tokens/colors.css" \
    || fail=1

  assert_classify design-tokens-handoff-copy \
    "code=false${nl}migrations=false${nl}mobile=false${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=true" \
    "design/design_handoff_timeline/tokens/colors.css" \
    || fail=1

  assert_classify design-tokens-shipped-copy \
    "code=true${nl}migrations=false${nl}mobile=false${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=true" \
    "src/QueenZone.Web/wwwroot/design-system/tokens/colors.css" \
    || fail=1

  assert_classify design-tokens-unrelated-design-file \
    "code=false${nl}migrations=false${nl}mobile=false${nl}mobile_native=false${nl}mobile_api_contracts=false${nl}design_tokens=false" \
    "design/styles.css" \
    || fail=1

  if [[ "${fail}" -ne 0 ]]; then
    echo "classify-pipeline-changes self-test failed." >&2
    exit 1
  fi
  echo "classify-pipeline-changes self-test passed." >&2
  exit 0
fi

classify
