#!/usr/bin/env bash
# Exercise the same main...HEAD path selection used by PR and merge-group CI.
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
ci_workflow="$repo_root/.github/workflows/ci.yml"
for expected in \
  'merge_group:' \
  'types: [checks_requested]' \
  'branches: [main]' \
  "github.event_name == 'merge_group'" \
  "github.event_name != 'merge_group'" \
  'if [ "${{ github.event_name }}" = "workflow_dispatch" ]; then' \
  'git diff --name-only origin/main...HEAD' \
  "COVERAGE_BASE_REF: \${{ github.event.pull_request.base.sha || 'origin/main' }}" \
  '-BaseRef $env:COVERAGE_BASE_REF -RequireBaseRef'; do
  grep -Fq -- "$expected" "$ci_workflow" || {
    printf 'CI workflow is missing merge-group contract: %s\n' "$expected" >&2
    exit 1
  }
done
for deploy_workflow in "$repo_root/.github/workflows/deploy-dev.yml" "$repo_root/.github/workflows/deploy.yml"; do
  if grep -Eq '^  merge_group:' "$deploy_workflow"; then
    printf 'Deploy workflow must not run on merge_group: %s\n' "$deploy_workflow" >&2
    exit 1
  fi
done
test_root=$(mktemp -d)
trap 'rm -rf "$test_root"' EXIT
cd "$test_root"
git init -q -b main
git config user.name 'CI test'
git config user.email 'ci-test@example.invalid'
mkdir -p docs src/QueenZone.Mobile/src src/QueenZone.Web src/QueenZone.Data/Entities
printf 'base\n' > README.md
git add .
git commit -qm base

assert_flags() {
  local label=$1 expected=$2 actual
  actual=$(git diff --name-only main...HEAD | bash "$repo_root/scripts/classify-pipeline-changes.sh" 2>/dev/null)
  if [[ "$actual" != "$expected" ]]; then
    printf '%s: unexpected classification\nexpected:\n%s\nactual:\n%s\n' "$label" "$expected" "$actual" >&2
    exit 1
  fi
}

expected_docs=$'code=false\nmigrations=false\nmobile=false\nmobile_native=false\nmobile_api_contracts=false\ndesign_tokens=false'
expected_mobile=$'code=false\nmigrations=false\nmobile=true\nmobile_native=false\nmobile_api_contracts=false\ndesign_tokens=false'
expected_web=$'code=true\nmigrations=false\nmobile=false\nmobile_native=false\nmobile_api_contracts=false\ndesign_tokens=false'
expected_migration=$'code=true\nmigrations=true\nmobile=false\nmobile_native=false\nmobile_api_contracts=false\ndesign_tokens=false'
expected_combined=$'code=true\nmigrations=true\nmobile=true\nmobile_native=false\nmobile_api_contracts=false\ndesign_tokens=false'

git switch -qc docs-candidate main
printf 'docs\n' > docs/example.md
git add . && git commit -qm docs
assert_flags docs "$expected_docs"

git switch -qc mobile-candidate main
printf 'mobile\n' > src/QueenZone.Mobile/src/example.ts
git add . && git commit -qm mobile
assert_flags mobile "$expected_mobile"

git switch -qc web-candidate main
printf 'web\n' > src/QueenZone.Web/example.cs
git add . && git commit -qm web
assert_flags web "$expected_web"

git switch -qc migration-candidate main
printf 'migration\n' > src/QueenZone.Data/Entities/Example.cs
git add . && git commit -qm migration
assert_flags migration "$expected_migration"

# A merge group can contain multiple PRs. Classify the union of their files.
git switch -qc combined-candidate main
git merge -q --no-ff docs-candidate -m 'queue docs'
git merge -q --no-ff mobile-candidate -m 'queue mobile'
git merge -q --no-ff migration-candidate -m 'queue migration'
assert_flags combined "$expected_combined"

# The coverage gate needs main to be an ancestor of the queued combined SHA.
git merge-base --is-ancestor main HEAD
printf 'Merge-group change range self-test passed.\n'
