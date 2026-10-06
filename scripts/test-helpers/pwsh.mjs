import { spawnSync } from 'node:child_process';

const defaultTimeoutMs = 60_000;

function resolveTimeoutMs(explicit) {
  if (explicit != null) {
    return explicit;
  }

  const configured = Number.parseInt(process.env.QZ_PWSH_TEST_TIMEOUT_MS ?? '', 10);
  return Number.isFinite(configured) && configured > 0 ? configured : defaultTimeoutMs;
}

export function spawnPwsh(args, options = {}) {
  return spawnSync('pwsh', ['-NoProfile', ...args], {
    ...options,
    timeout: resolveTimeoutMs(options.timeout),
  });
}
