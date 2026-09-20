import { MIN_PULL_TO_REFRESH_VISIBLE_MS, waitForMinimumRefreshVisibility } from './refreshVisibility';

describe('waitForMinimumRefreshVisibility', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    jest.spyOn(globalThis, 'requestAnimationFrame').mockImplementation((callback) => {
      callback(0);
      return 1;
    });
  });

  afterEach(() => {
    jest.restoreAllMocks();
    jest.useRealTimers();
  });

  it('does not resolve until the next paint and the remaining min duration', async () => {
    const startedAt = Date.now();
    let settled = false;
    const pending = waitForMinimumRefreshVisibility(startedAt).then(() => {
      settled = true;
    });

    await Promise.resolve();
    expect(settled).toBe(false);

    jest.advanceTimersByTime(MIN_PULL_TO_REFRESH_VISIBLE_MS - 1);
    await Promise.resolve();
    expect(settled).toBe(false);

    jest.advanceTimersByTime(1);
    await pending;
    expect(settled).toBe(true);
  });

  it('skips the timer when the load already covered the min duration', async () => {
    const startedAt = Date.now() - MIN_PULL_TO_REFRESH_VISIBLE_MS;
    await waitForMinimumRefreshVisibility(startedAt);
  });
});
