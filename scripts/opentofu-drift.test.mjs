import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
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

test('production plan, apply, and drift pass QZ_SCM_DENY_IP as TF_VAR_scm_deny_ip', () => {
  const applyWorkflow = readText('.github/workflows/opentofu-apply.yml');
  for (const text of [planWorkflow, applyWorkflow, workflow]) {
    assert.match(text, /TF_VAR_scm_deny_ip: \$\{\{ matrix\.root == 'production' && vars\.QZ_SCM_DENY_IP \|\| '' \}\}|TF_VAR_scm_deny_ip: \$\{\{ vars\.QZ_SCM_DENY_IP \}\}/);
    assert.match(text, /Require production SCM deny IP/);
    assert.match(text, /\[\[ -z "\$\{QZ_SCM_DENY_IP\/\/\[\[:space:\]\]\/\}" \]\]/);
    assert.doesNotMatch(text, /BITWARDEN_OPENTOFU_(PLAN|APPLY)_SECRETS.*QZ_SCM_DENY_IP/);
  }
  assert.match(applyWorkflow, /TF_VAR_scm_deny_ip: \$\{\{ vars\.QZ_SCM_DENY_IP \}\}\n        run: tofu apply/);
});

test('drift skips the issue when the only change is the temp-veronica SCM action', () => {
  assert.match(workflow, /Test-OpenTofuExpectedScmToggle\.ps1/);
  assert.match(workflow, /expected_toggle != 'true'/);
  const issueStep = workflow.split('Open or update the drift issue')[1];
  assert.match(issueStep.slice(0, 250), /expected_toggle != 'true'/);
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

test('container publicAccess drift fails the job instead of only opening a plan-diff issue', () => {
  assert.match(workflow, /Fail on container publicAccess drift/);
  assert.match(workflow, /Test-OpenTofuContainerPublicAccessDrift\.ps1/);
  const accessStep = workflow.split('Fail on container publicAccess drift')[1].split('- name:')[0];
  assert.match(accessStep, /if: steps\.plan\.outputs\.exit_code == '2'/);
  assert.doesNotMatch(accessStep, /continue-on-error:\s*true/);
  assert.match(workflow, /Open or update the drift-check failure issue[\s\S]*if: failure\(\)/);
});

test('container access reads body before output and falls back only when the field is absent', () => {
  const directory = mkdtempSync(path.join(tmpdir(), 'container-drift-'));
  const planPath = path.join(directory, 'plan.json');
  const access = (value) => ({ properties: { publicAccess: value } });
  const cases = [
    [{ body: access('Blob'), output: access('None') }, 'Blob'],
    [{ body: JSON.stringify(access('Blob')) }, 'Blob'],
    [{ output: JSON.stringify(access('Blob')) }, 'Blob'],
    ...[null, '', '  ', { other: true }, { properties: null }, { properties: { other: true } }]
      .map((body) => [{ body, output: access('Blob') }, 'Blob']),
    [{ body: access(null), output: access('Blob') }, ''],
    [{ body: access(''), output: access('Blob') }, ''],
    [null, null], [{ output: null }, null],
  ];
  try {
    for (const [before, expected] of cases) {
      writeFileSync(planPath, JSON.stringify({ resource_changes: [{
        address: 'azapi_resource.builds_container', type: 'azapi_resource',
        change: { actions: ['update'], before, after: expected === null ? null : { body: access(expected) } },
      }] }));
      const result = spawnSync('pwsh', ['-NoProfile', '-File',
        path.join(repoRoot, 'scripts/Test-OpenTofuContainerPublicAccessDrift.ps1'), '-PlanJsonPath', planPath],
      { encoding: 'utf8' });
      assert.equal(result.status, 0, `${JSON.stringify(before)}: ${result.error || result.stderr}`);
      assert.match(result.stdout, /No blob container publicAccess drift/);
    }
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
