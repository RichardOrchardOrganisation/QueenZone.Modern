/**
 * Fail-closed npm advisory policy for QueenZone.Mobile (#837 Option A).
 *
 * Parses `npm audit --json` for the full graph (do not use --omit=dev as the
 * only gate). High/critical findings fail unless their GHSA is in
 * npm-advisory-allowlist.json. Expired or malformed allowlists fail.
 * Missing audit output fails. Moderate/low findings print and do not fail.
 * Never run `npm audit fix` or `npm audit fix --force`.
 */
import { existsSync, readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const mobileRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const defaultAllowlistPath = path.join(mobileRoot, 'npm-advisory-allowlist.json');
const GHSA_RE = /^GHSA-[a-z0-9]{4}-[a-z0-9]{4}-[a-z0-9]{4}$/i;
const ISO_DATE_RE = /^(\d{4})-(\d{2})-(\d{2})$/;
const REQUIRED_FIELDS = ['ghsa', 'package', 'via', 'exploitability', 'owner', 'expires'];
const ACTIONABLE = new Set(['high', 'critical']);
const INFORMATIONAL = new Set(['low', 'moderate']);

export function utcDateOnly(date) {
  return date.toISOString().slice(0, 10);
}

export function parseIsoDate(value) {
  const match = ISO_DATE_RE.exec(String(value ?? '').trim());
  if (!match) {
    return null;
  }

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const date = new Date(Date.UTC(year, month - 1, day));
  if (
    date.getUTCFullYear() !== year ||
    date.getUTCMonth() !== month - 1 ||
    date.getUTCDate() !== day
  ) {
    return null;
  }

  return `${match[1]}-${match[2]}-${match[3]}`;
}

export function extractGhsa(value) {
  const text = String(value ?? '');
  const match = text.match(/GHSA-[a-z0-9]{4}-[a-z0-9]{4}-[a-z0-9]{4}/i);
  return match ? match[0].toUpperCase() : '';
}

export function parseAuditJson(text) {
  if (text == null || String(text).trim() === '') {
    throw new Error('Missing npm audit output.');
  }

  let parsed;
  try {
    parsed = JSON.parse(text);
  } catch {
    throw new Error('Missing npm audit output: JSON is unparseable.');
  }

  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error('Missing npm audit output: expected a JSON object.');
  }

  if (parsed.error && !parsed.vulnerabilities) {
    const summary = parsed.error.summary || parsed.error.detail || parsed.error.code || 'npm audit failed';
    const code = parsed.error.code ? ` code=${parsed.error.code}` : '';
    throw new Error(`Missing npm audit output: ${summary}${code}`);
  }

  if (!parsed.vulnerabilities || typeof parsed.vulnerabilities !== 'object' || Array.isArray(parsed.vulnerabilities)) {
    throw new Error('Missing npm audit output: vulnerabilities map is absent.');
  }

  return parsed;
}

export function collectAdvisories(audit) {
  const parsed = audit && audit.vulnerabilities ? audit : parseAuditJson(JSON.stringify(audit));
  const byKey = new Map();

  for (const [pkgName, vuln] of Object.entries(parsed.vulnerabilities)) {
    const vias = Array.isArray(vuln?.via) ? vuln.via : [];
    for (const via of vias) {
      if (typeof via === 'string') {
        continue;
      }

      if (!via || typeof via !== 'object') {
        throw new Error(`Malformed npm audit via entry for ${pkgName}.`);
      }

      const severity = String(via.severity || vuln.severity || '').toLowerCase();
      if (!ACTIONABLE.has(severity) && !INFORMATIONAL.has(severity)) {
        throw new Error(`Malformed npm audit severity for ${pkgName}.`);
      }

      const ghsa = extractGhsa(via.url || via.ghsa || '');
      const key = ghsa || `missing:${pkgName}:${via.source || via.title || 'unknown'}`;
      if (!byKey.has(key)) {
        byKey.set(key, {
          ghsa,
          package: via.name || via.dependency || vuln.name || pkgName,
          severity,
          title: via.title || '',
          url: via.url || '',
          range: via.range || vuln.range || '',
          nodes: [],
        });
      }

      byKey.get(key).nodes.push(pkgName);
    }
  }

  return [...byKey.values()];
}

function nonEmptyString(value) {
  return typeof value === 'string' && value.trim() !== '';
}

export function validateAllowlist(allowlist, now = new Date()) {
  if (!allowlist || typeof allowlist !== 'object' || Array.isArray(allowlist)) {
    throw new Error('Malformed allowlist: expected an object with an advisories array.');
  }

  if (!Array.isArray(allowlist.advisories)) {
    throw new Error('Malformed allowlist: advisories must be an array.');
  }

  const today = utcDateOnly(now);
  const seen = new Set();
  const entries = [];

  for (const [index, row] of allowlist.advisories.entries()) {
    if (!row || typeof row !== 'object' || Array.isArray(row)) {
      throw new Error(`Malformed allowlist: advisories[${index}] must be an object.`);
    }

    for (const field of REQUIRED_FIELDS) {
      if (!nonEmptyString(row[field])) {
        throw new Error(`Malformed allowlist: advisories[${index}].${field} is required.`);
      }
    }

    const ghsa = row.ghsa.trim().toUpperCase();
    if (!GHSA_RE.test(ghsa)) {
      throw new Error(`Malformed allowlist: advisories[${index}].ghsa is not a GHSA id.`);
    }

    if (seen.has(ghsa)) {
      throw new Error(`Malformed allowlist: duplicate ${ghsa}.`);
    }
    seen.add(ghsa);

    const expires = parseIsoDate(row.expires);
    if (!expires) {
      throw new Error(`Malformed allowlist: advisories[${index}].expires must be YYYY-MM-DD.`);
    }

    if (expires < today) {
      throw new Error(`Expired allowlist entry ${ghsa} (expired ${expires}).`);
    }

    entries.push({
      ghsa,
      package: row.package.trim(),
      via: row.via.trim(),
      exploitability: row.exploitability.trim(),
      owner: row.owner.trim(),
      expires,
    });
  }

  return entries;
}

export function evaluatePolicy({ audit, allowlist, now = new Date() }) {
  const entries = validateAllowlist(allowlist, now);
  const allowed = new Map(entries.map((entry) => [entry.ghsa, entry]));
  const findings = collectAdvisories(audit);
  const highCritical = findings.filter((finding) => ACTIONABLE.has(finding.severity));
  const informational = findings.filter((finding) => INFORMATIONAL.has(finding.severity));
  const failures = [];

  for (const finding of highCritical) {
    if (!finding.ghsa) {
      failures.push(
        `Unallowlisted ${finding.severity} advisory is missing a GHSA id (${finding.package}).`,
      );
      continue;
    }

    if (!allowed.has(finding.ghsa)) {
      failures.push(`Unallowlisted ${finding.severity} ${finding.ghsa} (${finding.package}).`);
    }
  }

  return {
    ok: failures.length === 0,
    failures,
    highCritical,
    informational,
    allowlist: entries,
  };
}

export function formatReport(result) {
  const lines = ['npm advisory policy (#837)'];
  lines.push(`High/critical advisories: ${result.highCritical.length}`);
  for (const finding of result.highCritical) {
    const exception = result.allowlist.find((entry) => entry.ghsa === finding.ghsa);
    const status = exception ? `ALLOW expires ${exception.expires}` : 'FAIL';
    lines.push(`  ${status} ${finding.ghsa || '(missing GHSA)'} ${finding.package} (${finding.severity})`);
  }

  lines.push(
    `Moderate/low advisories: ${result.informational.length} (informational; this gate does not fail)`,
  );
  for (const finding of result.informational) {
    lines.push(`  info  ${finding.ghsa || '(no GHSA)'} ${finding.package} (${finding.severity})`);
  }

  if (result.failures.length > 0) {
    lines.push('Failures:');
    for (const failure of result.failures) {
      lines.push(`  ${failure}`);
    }
  }

  return lines.join('\n');
}

export function loadAllowlist(filePath) {
  if (!existsSync(filePath)) {
    throw new Error(`Malformed allowlist: ${filePath} is missing.`);
  }

  let parsed;
  try {
    parsed = JSON.parse(readFileSync(filePath, 'utf8'));
  } catch {
    throw new Error('Malformed allowlist: JSON is unparseable.');
  }

  return parsed;
}

const AUDIT_ATTEMPTS = 2;
const AUDIT_FETCH_TIMEOUT_MS = 600_000;

export function auditStdout(result) {
  const stdout = String(result?.stdout ?? '').trim();
  if (stdout) {
    return stdout;
  }

  return String(result?.stderr ?? '');
}

export function npmAuditArgs(fetchTimeoutMs = AUDIT_FETCH_TIMEOUT_MS) {
  return [`--fetch-timeout=${fetchTimeoutMs}`, 'audit', '--json'];
}

export function runNpmAudit(cwd, { attempts = AUDIT_ATTEMPTS, fetchTimeoutMs = AUDIT_FETCH_TIMEOUT_MS } = {}) {
  let lastError;
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    // Pass fetch-timeout as an npm global CLI flag. GHA ignored NPM_CONFIG_*
    // and still died at the default 300s on a second audit after `npm ci`.
    const result = spawnSync('npm', npmAuditArgs(fetchTimeoutMs), {
      cwd,
      encoding: 'utf8',
      maxBuffer: 20 * 1024 * 1024,
    });

    if (result.error) {
      lastError = new Error(`Missing npm audit output: ${result.error.message}`);
    } else {
      try {
        return parseAuditJson(auditStdout(result));
      } catch (error) {
        lastError = error instanceof Error ? error : new Error(String(error));
      }
    }

    if (attempt < attempts) {
      console.error(`npm audit attempt ${attempt}/${attempts} failed (${lastError.message}); retrying.`);
    }
  }

  throw lastError;
}

function parseArgs(argv) {
  const args = {
    selfTest: false,
    allowlist: defaultAllowlistPath,
    auditJson: null,
    now: new Date(),
  };

  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--self-test') {
      args.selfTest = true;
    } else if (arg === '--allowlist') {
      args.allowlist = path.resolve(argv[i + 1] ?? '');
      i += 1;
    } else if (arg === '--audit-json') {
      args.auditJson = path.resolve(argv[i + 1] ?? '');
      i += 1;
    } else if (arg === '--now') {
      const parsed = parseIsoDate(argv[i + 1]);
      if (!parsed) {
        throw new Error('--now must be YYYY-MM-DD.');
      }
      args.now = new Date(`${parsed}T00:00:00.000Z`);
      i += 1;
    } else {
      throw new Error(`Unknown argument: ${arg}`);
    }
  }

  return args;
}

export function runGate({
  allowlistPath = defaultAllowlistPath,
  auditJsonPath = null,
  cwd = mobileRoot,
  now = new Date(),
} = {}) {
  const allowlist = loadAllowlist(allowlistPath);
  if (auditJsonPath && !existsSync(auditJsonPath)) {
    throw new Error('Missing npm audit output.');
  }
  const audit = auditJsonPath
    ? parseAuditJson(readFileSync(auditJsonPath, 'utf8'))
    : runNpmAudit(cwd);
  const result = evaluatePolicy({ audit, allowlist, now });
  const report = formatReport(result);
  console.log(report);
  if (!result.ok) {
    const error = new Error(result.failures.join(' '));
    error.result = result;
    throw error;
  }
  return result;
}

function runSelfTest() {
  const cases = [];
  const assert = (name, fn) => {
    try {
      fn();
      cases.push(`PASS ${name}`);
    } catch (error) {
      cases.push(`FAIL ${name}: ${error.message}`);
      throw error;
    }
  };

  const now = new Date('2026-08-26T00:00:00.000Z');
  const validRow = {
    ghsa: 'GHSA-w3rx-r6r6-pgpr',
    package: 'image-size',
    via: 'metro@0.84.4',
    exploitability: 'Build/CI tooling only.',
    owner: 'QueenZone maintainers',
    expires: '2026-11-24',
  };
  const highAudit = {
    vulnerabilities: {
      'image-size': {
        name: 'image-size',
        severity: 'high',
        via: [
          {
            name: 'image-size',
            severity: 'high',
            url: 'https://github.com/advisories/GHSA-w3rx-r6r6-pgpr',
            title: 'image-size DoS',
          },
        ],
      },
    },
  };
  const moderateAudit = {
    vulnerabilities: {
      uuid: {
        name: 'uuid',
        severity: 'moderate',
        via: [
          {
            name: 'uuid',
            severity: 'moderate',
            url: 'https://github.com/advisories/GHSA-w5hq-g745-h8pq',
            title: 'uuid bounds check',
          },
        ],
      },
    },
  };

  const expectThrow = (fn, pattern) => {
    let thrown = false;
    try {
      fn();
    } catch (error) {
      thrown = pattern.test(error.message);
      if (!thrown) {
        throw new Error(`threw unexpected: ${error.message}`);
      }
    }
    if (!thrown) {
      throw new Error('expected throw');
    }
  };

  assert('unallowlisted high fails', () => {
    const result = evaluatePolicy({
      audit: highAudit,
      allowlist: { advisories: [] },
      now,
    });
    if (result.ok || !/Unallowlisted high GHSA-W3RX-R6R6-PGPR/.test(result.failures[0])) {
      throw new Error(JSON.stringify(result.failures));
    }
  });

  assert('allowlisted high passes', () => {
    const result = evaluatePolicy({
      audit: highAudit,
      allowlist: { advisories: [validRow] },
      now,
    });
    if (!result.ok) {
      throw new Error(result.failures.join(' '));
    }
  });

  assert('expired allowlist fails', () => {
    expectThrow(
      () =>
        validateAllowlist(
          { advisories: [{ ...validRow, expires: '2026-08-25' }] },
          now,
        ),
      /Expired allowlist entry GHSA-W3RX-R6R6-PGPR/,
    );
  });

  const bracesRow = {
    ...validRow,
    ghsa: 'GHSA-vfj7-8cjw-p6xm',
    package: 'braces',
    via: 'micromatch@4.0.8',
    expires: '2026-10-17',
  };
  const bracesAudit = {
    vulnerabilities: {
      braces: {
        severity: 'high',
        via: [{
          name: 'braces',
          severity: 'high',
          url: 'https://github.com/advisories/GHSA-vfj7-8cjw-p6xm',
        }],
      },
    },
  };

  assert('braces exception valid through final UTC day', () => {
    const result = evaluatePolicy({
      audit: bracesAudit,
      allowlist: { advisories: [bracesRow] },
      now: new Date('2026-10-17T23:59:59.999Z'),
    });
    if (!result.ok) throw new Error(result.failures.join(' '));
  });

  assert('braces exception rejects at next UTC midnight', () => {
    expectThrow(() => evaluatePolicy({
      audit: bracesAudit,
      allowlist: { advisories: [bracesRow] },
      now: new Date('2026-10-18T00:00:00.000Z'),
    }), /Expired allowlist entry GHSA-VFJ7-8CJW-P6XM/);
  });

  assert('braces exception does not allow another high advisory', () => {
    const result = evaluatePolicy({
      audit: highAudit,
      allowlist: { advisories: [bracesRow] },
      now: new Date('2026-10-03T00:00:00.000Z'),
    });
    if (result.ok || !result.failures.some((failure) => failure.includes('GHSA-W3RX-R6R6-PGPR'))) {
      throw new Error('Unrelated high advisory must remain blocked.');
    }
  });

  assert('malformed allowlist missing field fails', () => {
    const { expires, ...rest } = validRow;
    expectThrow(() => validateAllowlist({ advisories: [rest] }, now), /expires is required/);
  });

  assert('malformed allowlist bad ghsa fails', () => {
    expectThrow(
      () => validateAllowlist({ advisories: [{ ...validRow, ghsa: 'CVE-2025-1' }] }, now),
      /not a GHSA id/,
    );
  });

  assert('malformed allowlist bad date fails', () => {
    expectThrow(
      () => validateAllowlist({ advisories: [{ ...validRow, expires: '2026-13-40' }] }, now),
      /must be YYYY-MM-DD/,
    );
  });

  assert('malformed allowlist raw array fails', () => {
    expectThrow(() => validateAllowlist([validRow], now), /expected an object/);
  });

  assert('duplicate allowlist ghsa fails', () => {
    expectThrow(
      () => validateAllowlist({ advisories: [validRow, { ...validRow }] }, now),
      /duplicate GHSA-W3RX-R6R6-PGPR/,
    );
  });

  assert('missing audit output fails', () => {
    expectThrow(() => parseAuditJson(''), /Missing npm audit output/);
  });

  assert('unparseable audit output fails', () => {
    expectThrow(() => parseAuditJson('{'), /unparseable/);
  });

  assert('audit without vulnerabilities map fails', () => {
    expectThrow(() => parseAuditJson('{"error":{"code":"ENOTFOUND"}}'), /Missing npm audit output/);
  });

  assert('audit fetch-timeout JSON fails closed', () => {
    expectThrow(
      () => parseAuditJson('{"error":{"summary":"npm audit failed","code":"ECONNRESET"}}'),
      /Missing npm audit output: npm audit failed/,
    );
  });

  assert('auditStdout prefers stdout then stderr', () => {
    if (auditStdout({ stdout: ' {"ok":true} ', stderr: 'warn' }) !== '{"ok":true}') {
      throw new Error('expected trimmed stdout');
    }
    if (auditStdout({ stdout: '', stderr: '{"error":true}' }) !== '{"error":true}') {
      throw new Error('expected stderr fallback');
    }
  });

  assert('npmAuditArgs passes fetch-timeout as an npm global flag', () => {
    const args = npmAuditArgs(600000);
    if (args[0] !== '--fetch-timeout=600000' || args[1] !== 'audit' || args[2] !== '--json') {
      throw new Error(JSON.stringify(args));
    }
  });

  assert('moderate does not fail', () => {
    const result = evaluatePolicy({
      audit: moderateAudit,
      allowlist: { advisories: [] },
      now,
    });
    if (!result.ok || result.informational.length !== 1) {
      throw new Error(JSON.stringify(result));
    }
  });

  assert('unallowlisted critical fails', () => {
    const result = evaluatePolicy({
      audit: {
        vulnerabilities: {
          demo: {
            name: 'demo',
            severity: 'critical',
            via: [
              {
                name: 'demo',
                severity: 'critical',
                url: 'https://github.com/advisories/GHSA-aaaa-bbbb-cccc',
              },
            ],
          },
        },
      },
      allowlist: { advisories: [] },
      now,
    });
    if (result.ok || !/Unallowlisted critical GHSA-AAAA-BBBB-CCCC/.test(result.failures[0])) {
      throw new Error(JSON.stringify(result.failures));
    }
  });

  assert('high via without GHSA fails', () => {
    const result = evaluatePolicy({
      audit: {
        vulnerabilities: {
          demo: {
            name: 'demo',
            severity: 'high',
            via: [{ name: 'demo', severity: 'high', title: 'no url' }],
          },
        },
      },
      allowlist: { advisories: [] },
      now,
    });
    if (result.ok || !/missing a GHSA id/.test(result.failures[0])) {
      throw new Error(JSON.stringify(result.failures));
    }
  });

  assert('allowlist expires on the listed day still passes', () => {
    validateAllowlist({ advisories: [validRow] }, new Date('2026-11-24T23:00:00.000Z'));
  });

  console.log(cases.join('\n'));
  console.log('check-npm-advisories self-test passed.');
}

const invokedDirectly = process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);

if (invokedDirectly) {
  try {
    const args = parseArgs(process.argv.slice(2));
    if (args.selfTest) {
      runSelfTest();
    } else {
      runGate({
        allowlistPath: args.allowlist,
        auditJsonPath: args.auditJson,
        now: args.now,
      });
    }
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
