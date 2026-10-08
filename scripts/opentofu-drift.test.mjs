import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const repoRoot = fileURLToPath(new URL('..', import.meta.url));
const readText = (relativePath) => readFileSync(path.join(repoRoot, relativePath), 'utf8').replaceAll('\r\n', '\n');
const workflow = readText('.github/workflows/opentofu-drift.yml');
const planWorkflow = readText('.github/workflows/opentofu-plan.yml');
const passwordExport = readText('scripts/Export-EphemeralSqlAdminPassword.ps1');

test('drift check plans production and dev with the same read-only identity as opentofu-plan', () => {
  assert.match(workflow, /matrix:\n\s+root: \[production, dev\]/);
  assert.match(workflow, /fail-fast: false/);
  assert.match(workflow, /environment: opentofu-plan/);
  assert.match(workflow, /working-directory: infra\/environments\/\$\{\{ matrix\.root \}\}/);
  assert.match(workflow, /backend-config=\.\.\/\.\.\/backend\/\$\{\{ matrix\.root \}\}\.backend\.hcl/);
  assert.match(planWorkflow, /root: \[production, dev\]/);
  assert.match(planWorkflow, /environment: opentofu-plan/);
});

test('drift check keeps the existing Bitwarden plan mapping and only fetches it for production', () => {
  assert.ok(workflow.includes('secrets: ${{ vars.BITWARDEN_OPENTOFU_PLAN_SECRETS }}'));
  assert.match(workflow, /if: matrix\.root == 'production'/);
  assert.equal((workflow.match(/uses: \.\/\.github\/actions\/bitwarden-retry/g) || []).length, 1);
  assert.equal((workflow.match(/timeout-minutes: 8/g) || []).length, 1);
  assert.doesNotMatch(workflow, /steps\.bitwarden-secrets\.outputs\.TF_VAR_target_sql_admin_password/);
  assert.match(passwordExport, /TF_VAR_target_sql_admin_password=/);
  assert.match(workflow, /Export-EphemeralSqlAdminPassword\.ps1/);
});

test('drift and detector failures each open or update a labelled issue without succeeding the job', () => {
  assert.match(workflow, /if: steps\.plan\.outputs\.exit_code == '2'/);
  assert.match(workflow, /if: failure\(\)/);
  assert.match(workflow, /opentofu-drift-failure/);
  assert.match(workflow, /<!-- opentofu-drift-\$\{root\} -->/);
  assert.match(workflow, /<!-- opentofu-drift-failure-\$\{root\} -->/);
  assert.doesNotMatch(workflow, /continue-on-error: true\n\s+- name: Open or update the drift-check failure issue/);
  const failureStep = workflow.split('Open or update the drift-check failure issue')[1];
  assert.ok(failureStep);
  assert.doesNotMatch(failureStep.slice(0, 400), /continue-on-error:\s*true/);
});
