/** Smallest hold that still lets iOS paint the native RefreshControl spinner (#1632 Option A). */
export const MIN_PULL_TO_REFRESH_VISIBLE_MS = 400;

function afterNextPaint(): Promise<void> {
  return new Promise((resolve) => {
    requestAnimationFrame(() => resolve());
  });
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
}

/**
 * Keep `refreshing` true for one painted frame and a short minimum duration
 * so an immediately resolved pull still shows the iOS spinner.
 */
export async function waitForMinimumRefreshVisibility(startedAtMs: number): Promise<void> {
  await afterNextPaint();
  const remaining = MIN_PULL_TO_REFRESH_VISIBLE_MS - (Date.now() - startedAtMs);
  if (remaining > 0) {
    await delay(remaining);
  }
}
