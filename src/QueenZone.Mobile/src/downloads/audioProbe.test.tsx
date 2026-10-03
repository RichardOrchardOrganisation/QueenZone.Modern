import { defaultProbeAudio } from './manager';
const requestUrl = 'https://api.example.com/audio/187';
function response(headers: Record<string, string> = {}, overrides: Partial<Response> = {}): Response {
  return { status: 206, url: requestUrl, redirected: false,
    headers: { get: (name: string) => headers[name] ?? null },
    body: { cancel: jest.fn().mockResolvedValue(undefined) }, ...overrides,
  } as unknown as Response;
}
describe('real audio HTTP probe', () => {
  beforeEach(() => jest.useFakeTimers());
  afterEach(() => { expect(jest.getTimerCount()).toBe(0); jest.useRealTimers(); });
  it('sends bearer and one-byte Range headers and prefers total size over partial length', async () => {
    const reply = response({ 'content-range': 'bytes 0-0/12345', 'content-length': '1', etag: '  "revision-1"  ', 'content-type': 'audio/mpeg' });
    const fetch = jest.spyOn(global, 'fetch').mockResolvedValue(reply);
    const result = await defaultProbeAudio(requestUrl, 'test-token');
    expect(fetch).toHaveBeenCalledWith(requestUrl, { headers: { Authorization: 'Bearer test-token', Range: 'bytes=0-0' }, signal: expect.any(AbortSignal) });
    expect(reply.body!.cancel).toHaveBeenCalledTimes(1);
    expect(result).toEqual({ status: 206, sourceRevision: '"revision-1"', byteSize: 12345, contentType: 'audio/mpeg', contentLength: 1, redirected: false, finalTarget: 'api.example.com/audio/187' });
  });
  it('cancels a 200 full-body response before returning Content-Length', async () => {
    const reply = response({ 'content-length': '67890', etag: '   ' }, { status: 200 });
    jest.spyOn(global, 'fetch').mockResolvedValue(reply);
    expect(await defaultProbeAudio(requestUrl, 'test-token')).toMatchObject({ status: 200, byteSize: 67890, sourceRevision: null });
    expect(reply.body!.cancel).toHaveBeenCalledTimes(1);
  });
  it.each([undefined, '', '0', 'abc', 'Infinity'])('rejects unusable Content-Length %s', async (length) => {
    jest.spyOn(global, 'fetch').mockResolvedValue(response(length === undefined ? {} : { 'content-length': length }));
    expect(await defaultProbeAudio(requestUrl, 'test-token')).toMatchObject({ byteSize: null, contentLength: null, sourceRevision: null });
  });
  it.each([[true, requestUrl, true, 'api.example.com/audio/187'], [false, 'https://cdn.example.com/audio/187', true, 'cdn.example.com/audio/187'], [false, '', false, null]])('detects redirect flag %s and final target %s', async (redirected, url, expected, finalTarget) => {
    jest.spyOn(global, 'fetch').mockResolvedValue(response({}, { redirected: redirected as boolean, url: url as string }));
    expect(await defaultProbeAudio(requestUrl, 'test-token')).toMatchObject({ redirected: expected, finalTarget });
  });
  it.each([null, {}, { cancel: jest.fn().mockRejectedValue(new Error('Already consumed')) }])('continues when body cancellation is unavailable or fails', async (body) => {
    jest.spyOn(global, 'fetch').mockResolvedValue(response({}, { body: body as Response['body'] }));
    expect(await defaultProbeAudio(requestUrl, 'test-token')).toMatchObject({ status: 206 });
  });
  it('aborts at 12 seconds, returns unavailable metadata, and clears its timer', async () => {
    let signal: AbortSignal | undefined;
    jest.spyOn(global, 'fetch').mockImplementation((_url, options) => {
      signal = options?.signal as AbortSignal;
      return new Promise((_resolve, reject) => signal!.addEventListener('abort', () => {
        const error = new Error('Timed out'); error.name = 'AbortError'; reject(error);
      }, { once: true }));
    });
    const pending = defaultProbeAudio(requestUrl, 'test-token');
    await jest.advanceTimersByTimeAsync(11_999); expect(signal!.aborted).toBe(false);
    await jest.advanceTimersByTimeAsync(1); expect(signal!.aborted).toBe(true);
    await expect(pending).resolves.toEqual({ status: 0, sourceRevision: null, byteSize: null });
  });
  it('rethrows ordinary network errors and clears its timer', async () => {
    const error = new Error('Network unavailable'); jest.spyOn(global, 'fetch').mockRejectedValue(error);
    await expect(defaultProbeAudio(requestUrl, 'test-token')).rejects.toBe(error);
  });
});
