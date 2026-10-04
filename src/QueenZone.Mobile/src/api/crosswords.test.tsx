import { fetchCrosswordDetail, fetchCrosswordsPage } from './crosswords';
import { jsonResponse } from '../test/fixtures';

jest.mock('../config', () => ({
  apiV1Url: (path: string) => `http://qz.test/api/v1${path}`,
}));

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
