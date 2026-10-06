import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { isAbsolute } from 'node:path';

const defaultTimeoutMs = 60_000;
const knownPwshExecutables = [
  '/usr/bin/pwsh',
  '/usr/local/bin/pwsh',
  '/opt/microsoft/powershell/7/pwsh',
];

function resolveTimeoutMs(explicit) {
  if (explicit != null) {
    return explicit;
  }

  const configured = Number.parseInt(process.env.QZ_PWSH_TEST_TIMEOUT_MS ?? '', 10);
  return Number.isFinite(configured) && configured > 0 ? configured : defaultTimeoutMs;
}

function resolvePwshExecutable() {
  const configured = process.env.PWSH;
  if (configured && isAbsolute(configured)) {
    return configured;
  }

  const found = knownPwshExecutables.find((candidate) => existsSync(candidate));
  if (found) {
    return found;
  }

  throw new Error(
    `pwsh not found. Set PWSH to an absolute path or install PowerShell at ${knownPwshExecutables.join(', ')}.`,
  );
}

export function spawnPwsh(args, options = {}) {
  return spawnSync(resolvePwshExecutable(), ['-NoProfile', ...args], {
    ...options,
    timeout: resolveTimeoutMs(options.timeout),
  });
}
