#!/usr/bin/env bash
# Resolves the git range Deploy / Deploy-dev should classify.
#
# Reuse-first deploy still needs a *skip* decision before resolve:
# workflow_dispatch used to force code=true, which doomed resolve after a
# mobile-only tip. Prefer skip over walking back to an older web commit.
#
# Ranges:
#   push to a branch with a real `before` SHA — that push span
#   workflow_dispatch (or missing/zero `before`) on a branch — tip vs first parent
#     (previous main commit). Mobile-only / docs-only tip → skip.
#   v* tag — previous v* tag (git describe on parent) … this tag.
#     Entire span non-web → skip. Span includes web → resolve or fallback.
#   No previous tag / no parent — fail closed (treat as web).
#
# Usage:
#   EVENT_NAME=push REF_TYPE=branch BEFORE=<sha> SHA=<sha> \
#     bash ./scripts/Resolve-DeployChangeRange.sh
#   EVENT_NAME=workflow_dispatch REF_TYPE=branch SHA=<sha> \
#     bash ./scripts/Resolve-DeployChangeRange.sh
#   EVENT_NAME=push REF_TYPE=tag SHA=<sha> \
#     bash ./scripts/Resolve-DeployChangeRange.sh
#   bash ./scripts/Resolve-DeployChangeRange.sh --self-test
#
# Prints:
#   base_sha=<sha>
#   head_sha=<sha>
#   fail_closed=true|false
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

is_zero_sha() {
  local value="${1:-}"
  [[ -n "${value}" ]] && [[ "${value}" =~ ^0+$ ]]
}

peel_commit() {
  git rev-parse "${1}^{commit}"
}

resolve_range() {
  local event_name="${EVENT_NAME:?EVENT_NAME is required}"
  local ref_type="${REF_TYPE:-branch}"
  local before="${BEFORE:-}"
  local sha="${SHA:?SHA is required}"
  local head
  head="$(peel_commit "${sha}")"

  if [[ "${ref_type}" = "tag" ]]; then
    local prev
    if prev="$(git describe --tags --abbrev=0 --match 'v*' "${head}^" 2>/dev/null)"; then
      echo "Classifying tag span ${prev}...${head}." >&2
      echo "base_sha=$(peel_commit "${prev}")"
      echo "head_sha=${head}"
      echo "fail_closed=false"
    else
      echo "No previous v* tag — failing closed as a website deploy." >&2
      echo "base_sha="
      echo "head_sha=${head}"
      echo "fail_closed=true"
    fi
    return 0
  fi

  if [[ "${event_name}" != "push" ]] || [[ -z "${before}" ]] || is_zero_sha "${before}"; then
    local -a parents
    parents=($(git rev-list --parents -n 1 "${head}"))
    if [[ "${#parents[@]}" -lt 2 ]]; then
      echo "No parent commit — failing closed as a website deploy." >&2
      echo "base_sha="
      echo "head_sha=${head}"
      echo "fail_closed=true"
      return 0
    fi
    echo "Classifying tip ${head} against previous commit ${parents[1]}." >&2
    echo "base_sha=${parents[1]}"
    echo "head_sha=${head}"
    echo "fail_closed=false"
    return 0
  fi

  echo "Classifying push ${before}...${head}." >&2
  echo "base_sha=$(peel_commit "${before}")"
  echo "head_sha=${head}"
  echo "fail_closed=false"
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

  repo="${tmp}/repo"
  git init -q -b main "${repo}"
  git -C "${repo}" config user.email "range-test@example.com"
  git -C "${repo}" config user.name "Range Test"

  mkdir -p "${repo}/src/QueenZone.Web" "${repo}/src/QueenZone.Mobile" "${repo}/docs"
  echo "web" >"${repo}/src/QueenZone.Web/Program.cs"
  git -C "${repo}" add src/QueenZone.Web/Program.cs
  git -C "${repo}" commit -q -m "web-1"
  web1="$(git -C "${repo}" rev-parse HEAD)"
  git -C "${repo}" tag v1.0.0

  echo "mobile" >"${repo}/src/QueenZone.Mobile/App.tsx"
  git -C "${repo}" add src/QueenZone.Mobile/App.tsx
  git -C "${repo}" commit -q -m "mobile-only"
  mobile="$(git -C "${repo}" rev-parse HEAD)"
  git -C "${repo}" tag v1.1.0

  echo "web2" >>"${repo}/src/QueenZone.Web/Program.cs"
  git -C "${repo}" add src/QueenZone.Web/Program.cs
  git -C "${repo}" commit -q -m "web-2"
  web2="$(git -C "${repo}" rev-parse HEAD)"
  git -C "${repo}" tag v1.2.0

  echo "docs" >"${repo}/docs/readme.md"
  git -C "${repo}" add docs/readme.md
  git -C "${repo}" commit -q -m "docs-only"
  docs="$(git -C "${repo}" rev-parse HEAD)"

  pushd "${repo}" >/dev/null

  got="$(EVENT_NAME=workflow_dispatch REF_TYPE=branch SHA="${mobile}" \
    resolve_range | grep -E '^(base_sha|head_sha|fail_closed)=')"
  assert_eq dispatch-mobile-tip \
    "base_sha=${web1}"$'\n'"head_sha=${mobile}"$'\n'"fail_closed=false" \
    "${got}" || fail=1

  got="$(EVENT_NAME=push REF_TYPE=tag SHA="${mobile}" \
    resolve_range | grep -E '^(base_sha|head_sha|fail_closed)=')"
  assert_eq tag-mobile-span \
    "base_sha=${web1}"$'\n'"head_sha=${mobile}"$'\n'"fail_closed=false" \
    "${got}" || fail=1

  got="$(EVENT_NAME=push REF_TYPE=tag SHA="${web2}" \
    resolve_range | grep -E '^(base_sha|head_sha|fail_closed)=')"
  assert_eq tag-web-span \
    "base_sha=${mobile}"$'\n'"head_sha=${web2}"$'\n'"fail_closed=false" \
    "${got}" || fail=1

  got="$(EVENT_NAME=push REF_TYPE=branch BEFORE="${mobile}" SHA="${web2}" \
    resolve_range | grep -E '^(base_sha|head_sha|fail_closed)=')"
  assert_eq push-with-before \
    "base_sha=${mobile}"$'\n'"head_sha=${web2}"$'\n'"fail_closed=false" \
    "${got}" || fail=1

  got="$(EVENT_NAME=push REF_TYPE=branch BEFORE="0000000000000000000000000000000000000000" SHA="${docs}" \
    resolve_range | grep -E '^(base_sha|head_sha|fail_closed)=')"
  assert_eq push-zero-before-uses-parent \
    "base_sha=${web2}"$'\n'"head_sha=${docs}"$'\n'"fail_closed=false" \
    "${got}" || fail=1

  got="$(EVENT_NAME=push REF_TYPE=tag SHA="${web1}" \
    resolve_range | grep -E '^(base_sha|head_sha|fail_closed)=')"
  assert_eq first-tag-fail-closed \
    "base_sha="$'\n'"head_sha=${web1}"$'\n'"fail_closed=true" \
    "${got}" || fail=1

  classify_mobile="$(git diff --name-only "${web1}...${mobile}" \
    | bash "${SCRIPT_DIR}/classify-pipeline-changes.sh" \
    | grep -E '^(code|migrations|mobile)=')"
  assert_eq classify-mobile-span \
    "code=false"$'\n'"migrations=false"$'\n'"mobile=true" \
    "${classify_mobile}" || fail=1

  classify_web="$(git diff --name-only "${mobile}...${web2}" \
    | bash "${SCRIPT_DIR}/classify-pipeline-changes.sh" \
    | grep -E '^(code|migrations|mobile)=')"
  assert_eq classify-web-span \
    "code=true"$'\n'"migrations=false"$'\n'"mobile=false" \
    "${classify_web}" || fail=1

  classify_docs="$(git diff --name-only "${web2}...${docs}" \
    | bash "${SCRIPT_DIR}/classify-pipeline-changes.sh" \
    | grep -E '^(code|migrations|mobile)=')"
  assert_eq classify-docs-span \
    "code=false"$'\n'"migrations=false"$'\n'"mobile=false" \
    "${classify_docs}" || fail=1

  popd >/dev/null

  if [[ "${fail}" -ne 0 ]]; then
    echo "Resolve-DeployChangeRange self-test failed." >&2
    exit 1
  fi
  echo "Resolve-DeployChangeRange self-test passed." >&2
  exit 0
fi

resolve_range
