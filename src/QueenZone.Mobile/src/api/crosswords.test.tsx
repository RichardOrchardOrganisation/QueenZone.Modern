import {
  fetchCrosswordDetail, fetchCrosswordsPage, checkCrossword, revealCrossword,
  fetchCrosswordProgress, saveCrosswordProgress, completeCrossword,
} from './crosswords';
import { jsonResponse } from '../test/fixtures';

jest.mock('../config', () => ({
  apiV1Url: (path: string) => `http://qz.test/api/v1${path}`,
}));

const playVersion = '11111111-2222-4333-8444-555555555555';

const fetchMock = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit?]>();

beforeEach(() => {
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
});

it('loads the default list and forwards pagination and cancellation for subsequent pages', async () => {
  fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
  await fetchCrosswordsPage();
  expect(String(fetchMock.mock.calls[0][0])).toBe('http://qz.test/api/v1/crosswords');
  const controller = new AbortController();
  const listener = jest.spyOn(controller.signal, 'addEventListener');
  fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
  await fetchCrosswordsPage({ page: 2, pageSize: 5, signal: controller.signal });
  expect(String(fetchMock.mock.calls[1][0])).toBe('http://qz.test/api/v1/crosswords?page=2&pageSize=5');
  expect(listener).toHaveBeenCalledWith('abort', expect.any(Function));
});

it('keeps a supplied id inside one URL segment and forwards cancellation', async () => {
  const controller = new AbortController();
  const listener = jest.spyOn(controller.signal, 'addEventListener');
  fetchMock.mockResolvedValueOnce(jsonResponse({ archived: true }));
  const result = await fetchCrosswordDetail('puzzle/other?query=1', controller.signal);
  expect(result.archived).toBe(true);
  expect(String(fetchMock.mock.calls[0][0])).toBe('http://qz.test/api/v1/crosswords/puzzle%2Fother%3Fquery%3D1');
  expect(listener).toHaveBeenCalledWith('abort', expect.any(Function));
});

it('checks or reveals a selection with optional bearer identity and no solution in its request', async () => {
  const selection = { scope: 'entry' as const, number: 1, direction: 'across' as const };
  fetchMock.mockResolvedValueOnce(jsonResponse({ cells: [], explanations: [], complete: false }));
  await checkCrossword('id/one', playVersion, 'BRIAN', selection);
  expect(String(fetchMock.mock.calls[0][0])).toBe('http://qz.test/api/v1/crosswords/id%2Fone/check');
  expect(fetchMock.mock.calls[0][1]).toMatchObject({ method: 'POST', body: JSON.stringify({ letters: 'BRIAN', selection, autoCheck: false, playVersion }) });
  fetchMock.mockResolvedValueOnce(jsonResponse({ cells: [], explanations: [], complete: false }));
  await checkCrossword('one', playVersion, 'BRIAN', selection, true, { accessToken: 'member-token' });
  expect(fetchMock.mock.calls[1][1]?.headers).toMatchObject({ Authorization: 'Bearer member-token' });
  fetchMock.mockResolvedValueOnce(jsonResponse({ cells: [], explanations: [], clean: false }));
  await revealCrossword('one', playVersion, { scope: 'cell', cell: 2 });
  expect(fetchMock.mock.calls[2][1]?.body).toBe(JSON.stringify({ selection: { scope: 'cell', cell: 2 }, playVersion }));
  fetchMock.mockResolvedValueOnce(jsonResponse({ cells: [], explanations: [], clean: false }));
  await revealCrossword('one', playVersion, { scope: 'grid' }, { accessToken: 'member-token' });
  expect(fetchMock.mock.calls[3][1]?.headers).toMatchObject({ Authorization: 'Bearer member-token' });
});

it('loads, saves and completes private progress through the shared authenticated client', async () => {
  const controller = new AbortController();
  const progress = { playVersion, letters: 'BRIAN', elapsedSeconds: 123, revealedCells: [], autoCheckUsed: false, updatedAt: '2026-10-04T00:00:00Z' };
  fetchMock.mockResolvedValueOnce(jsonResponse({ ...progress, startedAt: progress.updatedAt }));
  await fetchCrosswordProgress('one', 'member-token', controller.signal);
  expect(fetchMock.mock.calls[0][1]).toMatchObject({ method: 'GET', headers: { Authorization: 'Bearer member-token' } });
  fetchMock.mockResolvedValueOnce(jsonResponse({ ...progress, startedAt: progress.updatedAt }));
  await saveCrosswordProgress('one', progress, 'member-token', controller.signal);
  expect(fetchMock.mock.calls[1][1]).toMatchObject({ method: 'PUT', body: JSON.stringify(progress), headers: { Authorization: 'Bearer member-token' } });
  fetchMock.mockResolvedValueOnce(jsonResponse({ playVersion, correct: true, completion: null, review: [] }));
  await completeCrossword('one', progress, 'member-token', controller.signal);
  expect(String(fetchMock.mock.calls[2][0])).toBe('http://qz.test/api/v1/crosswords/one/complete');
  expect(fetchMock.mock.calls[2][1]).toMatchObject({ method: 'POST', body: JSON.stringify(progress) });
});

it('forwards bearer identity when requesting personal list status', async () => {
  fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
  await fetchCrosswordsPage({ accessToken: 'member-token' });
  expect(fetchMock.mock.calls[0][1]?.headers).toMatchObject({ Authorization: 'Bearer member-token' });
});
