/** Smallest hold that still lets iOS paint the native RefreshControl spinner (#1632 Option A). */
export const MIN_PULL_TO_REFRESH_VISIBLE_MS = 400;

function afterNextPaint(signal?: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    if (signal?.aborted) {
      resolve();
      return;
    }
    const frame = requestAnimationFrame(() => resolve());
    signal?.addEventListener(
      'abort',
      () => {
        cancelAnimationFrame(frame);
        resolve();
      },
      { once: true },
    );
  });
}

function delay(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    if (signal?.aborted) {
      resolve();
      return;
    }
    const timer = setTimeout(resolve, ms);
    signal?.addEventListener(
      'abort',
      () => {
        clearTimeout(timer);
        resolve();
      },
      { once: true },
    );
  });
}

/**
 * Keep `refreshing` true for one painted frame and a short minimum duration
 * so an immediately resolved pull still shows the iOS spinner.
 */
export async function waitForMinimumRefreshVisibility(
  startedAtMs: number,
  signal?: AbortSignal,
): Promise<void> {
  if (signal?.aborted) {
    return;
  }
  await afterNextPaint(signal);
  if (signal?.aborted) {
    return;
  }
  const remaining = MIN_PULL_TO_REFRESH_VISIBLE_MS - (Date.now() - startedAtMs);
  if (remaining > 0) {
    await delay(remaining, signal);
  }
}
