import { act, fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchNewsPage, fetchNewsYearRange } from '../../api';
import { ApiError } from '../../api/client';
import { invalidate } from '../../cache/externalStore';
import { NEWS_LIST_CACHE_KEY } from '../../cache/keys';
import { newsArticlePlaceholder } from '../../content/newsArticleImage';
import { deferred, newsItemFixture, pagedResponse } from '../../test/fixtures';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { NewsIndexScreen, newsListResetKey } from './NewsIndexScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchNewsPage: jest.fn(),
    fetchNewsYearRange: jest.fn(),
  };
});

jest.mock('../../config', () => {
  const actual = jest.requireActual('../../config');
  return {
    ...actual,
    getAppConfig: () => ({ apiBaseUrl: 'http://qz.test', appEnv: 'development', version: '0.1.0' }),
  };
});

const fetchNews = fetchNewsPage as jest.MockedFunction<typeof fetchNewsPage>;
const fetchYearRange = fetchNewsYearRange as jest.MockedFunction<typeof fetchNewsYearRange>;

async function renderNews(navigation = fakeNavigation(), refreshAt?: number) {
  return {
    navigation,
    ...(await renderWithProviders(
      <NewsIndexScreen
        navigation={navigation as never}
        route={{ key: 'news', name: 'NewsIndex', params: refreshAt === undefined ? undefined : { refreshAt } } as never}
      />,
      { navigation: false },
    )),
  };
}

describe('NewsIndexScreen', () => {
  beforeEach(() => {
    fetchNews.mockReset();
    fetchYearRange.mockReset();
    fetchYearRange.mockResolvedValue({ minYear: 2006, maxYear: 2026 });
  });

  it('shows loading then a labelled article that opens Story with the item id', async () => {
    const pending = deferred<ReturnType<typeof pagedResponse<ReturnType<typeof newsItemFixture>>>>();
    fetchNews.mockReturnValueOnce(pending.promise);
    const { navigation } = await renderNews();
    expect(screen.getByLabelText('Loading news…')).toBeOnTheScreen();
    pending.resolve(pagedResponse([newsItemFixture({ id: 7, title: 'Live Aid' })], 1, 1));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Open Live Aid' })).toBeOnTheScreen());
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Open Live Aid' }));
    expect(navigation.navigate).toHaveBeenCalledWith('Story', { id: 7 });
    expect(screen.queryByText('Start the discussion')).toBeNull();
    expect(screen.queryByText('Join the discussion')).toBeNull();
  });

  it('shows empty copy when the list has no items', async () => {
    fetchNews.mockResolvedValue(pagedResponse([], 1, 0));
    await renderNews();
    await waitFor(() => expect(screen.getByText('No news articles yet.')).toBeOnTheScreen());
  });

  it('shows an error and retries', async () => {
    fetchNews
      .mockRejectedValueOnce(new ApiError(0, 'Unable to reach QueenZone. Check your connection and try again.'))
      .mockResolvedValueOnce(pagedResponse([newsItemFixture()], 1, 1));
    await renderNews();
    await waitFor(() => expect(screen.getByText('Unable to load')).toBeOnTheScreen());
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Open Queen headline' })).toBeOnTheScreen());
  });

  it('picking a decade re-queries the server instead of filtering the loaded page', async () => {
    // Regression for #838: an older decade's first matching article can be well past whatever
    // page happens to be loaded, so filtering must be a fresh server request, not client-side.
    fetchNews.mockResolvedValueOnce(pagedResponse([newsItemFixture({ id: 1, title: 'Recent article' })], 1, 1));
    await renderNews();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Open Recent article' })).toBeOnTheScreen());
    expect(fetchNews).toHaveBeenCalledWith(expect.objectContaining({ page: 1, decade: undefined }));

    fetchNews.mockResolvedValueOnce(
      pagedResponse([newsItemFixture({ id: 9999, title: 'Old article from the 2000s' })], 1, 1),
    );
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: '2000s' }));

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Open Old article from the 2000s' })).toBeOnTheScreen(),
    );
    expect(screen.queryByRole('button', { name: 'Open Recent article' })).not.toBeOnTheScreen();
    expect(fetchNews).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, decade: 2000 }));
  });

  it('shows decade-specific empty copy when the server returns no matches', async () => {
    fetchNews.mockResolvedValueOnce(pagedResponse([newsItemFixture()], 1, 1));
    await renderNews();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Open Queen headline' })).toBeOnTheScreen());

    fetchNews.mockResolvedValueOnce(pagedResponse([], 1, 0));
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: '2000s' }));

    await waitFor(() => expect(screen.getByText('No articles for this decade yet.')).toBeOnTheScreen());
  });

  it('stepping the year rail re-queries the server with the exact year, not a decade', async () => {
    // Years run 2006-2026 (mocked fetchNewsYearRange), newest first, so one decrement from the
    // default (max year) moves to 2025.
    fetchNews.mockResolvedValueOnce(pagedResponse([newsItemFixture({ id: 1, title: 'Recent article' })], 1, 1));
    await renderNews();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Open Recent article' })).toBeOnTheScreen());
    expect(fetchNews).toHaveBeenCalledWith(expect.objectContaining({ page: 1, decade: undefined, year: undefined }));

    fetchNews.mockResolvedValueOnce(pagedResponse([newsItemFixture({ id: 9999, title: 'Article from 2025' })], 1, 1));
    const rail = screen.getByLabelText('Jump to year');
    await fireEvent(rail, 'accessibilityAction', { nativeEvent: { actionName: 'decrement' } });

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Open Article from 2025' })).toBeOnTheScreen(),
    );
    expect(fetchNews).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, decade: undefined, year: 2025 }));
  });

  it('shows year-specific empty copy when the server returns no matches for that year', async () => {
    fetchNews.mockResolvedValueOnce(pagedResponse([newsItemFixture()], 1, 1));
    await renderNews();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Open Queen headline' })).toBeOnTheScreen());

    fetchNews.mockResolvedValueOnce(pagedResponse([], 1, 0));
    const rail = screen.getByLabelText('Jump to year');
    await fireEvent(rail, 'accessibilityAction', { nativeEvent: { actionName: 'decrement' } });

    await waitFor(() => expect(screen.getByText('No articles for 2025 yet.')).toBeOnTheScreen());
  });

  it('picking a decade chip after a year jump clears the year filter', async () => {
    fetchNews.mockResolvedValueOnce(pagedResponse([newsItemFixture({ id: 1, title: 'Recent article' })], 1, 1));
    await renderNews();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Open Recent article' })).toBeOnTheScreen());

    fetchNews.mockResolvedValueOnce(pagedResponse([newsItemFixture({ id: 2, title: '2025 article' })], 1, 1));
    const rail = screen.getByLabelText('Jump to year');
    await fireEvent(rail, 'accessibilityAction', { nativeEvent: { actionName: 'decrement' } });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Open 2025 article' })).toBeOnTheScreen());

    fetchNews.mockResolvedValueOnce(pagedResponse([newsItemFixture({ id: 3, title: 'ALL article' })], 1, 1));
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'ALL' }));

    await waitFor(() => expect(screen.getByRole('button', { name: 'Open ALL article' })).toBeOnTheScreen());
    expect(fetchNews).toHaveBeenLastCalledWith(expect.objectContaining({ decade: undefined, year: undefined }));
  });

  it('refreshes the mounted list when a news push invalidates the list', async () => {
    fetchNews.mockResolvedValueOnce(pagedResponse([newsItemFixture({ id: 1, title: 'Stale headline' })], 1, 1));
    await renderNews();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Open Stale headline' })).toBeOnTheScreen());
    const newsCalls = fetchNews.mock.calls.length;

    fetchNews.mockResolvedValueOnce(
      pagedResponse([newsItemFixture({ id: 1003, title: 'QueenZone modernisation begins' })], 1, 1),
    );
    await act(async () => {
      invalidate(NEWS_LIST_CACHE_KEY);
    });

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Open QueenZone modernisation begins' })).toBeOnTheScreen(),
    );
    expect(fetchNews).toHaveBeenCalledTimes(newsCalls + 1);
    expect(fetchNews).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1 }));
  });

  it('does not refetch after unmount when a news push arrives', async () => {
    fetchNews.mockResolvedValueOnce(pagedResponse([newsItemFixture({ id: 1, title: 'Stale headline' })], 1, 1));
    const { unmount } = await renderNews();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Open Stale headline' })).toBeOnTheScreen());
    const newsCalls = fetchNews.mock.calls.length;
    await unmount();

    await act(async () => {
      invalidate(NEWS_LIST_CACHE_KEY);
    });

    expect(fetchNews).toHaveBeenCalledTimes(newsCalls);
  });

  it('reloads the list when a notification tap passes refreshAt', async () => {
    fetchNews.mockResolvedValueOnce(pagedResponse([newsItemFixture({ id: 1, title: 'Stale headline' })], 1, 1));
    const view = await renderNews();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Open Stale headline' })).toBeOnTheScreen());

    fetchNews.mockResolvedValueOnce(pagedResponse([newsItemFixture({ id: 1003, title: 'QueenZone modernisation begins' })], 1, 1));
    await view.rerender(
      <NewsIndexScreen
        navigation={fakeNavigation() as never}
        route={{ key: 'news', name: 'NewsIndex', params: { refreshAt: 99 } } as never}
      />,
    );

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Open QueenZone modernisation begins' })).toBeOnTheScreen(),
    );
    expect(fetchNews).toHaveBeenCalledTimes(2);
  });

  it('renders the bundled placeholder when the article has no image', async () => {
    fetchNews.mockResolvedValue(
      pagedResponse([newsItemFixture({ id: 7, title: 'Live Aid', imageUrl: null, thumbnailUrl: null })], 1, 1),
    );
    const { navigation } = await renderNews();
    await waitFor(() => expect(screen.getByTestId('news-story-7-thumb')).toBeOnTheScreen());
    expect(screen.getByTestId('news-story-7-thumb').props.source).toBe(newsArticlePlaceholder);
    expect(screen.getByTestId('news-story-7-thumb').props.placeholder).toBeUndefined();

    const user = userEvent.setup();
    await user.press(screen.getByTestId('news-story-7-thumb'));
    expect(navigation.navigate).toHaveBeenCalledWith('Story', { id: 7 });
  });

  it('renders the resolved /ugc thumbnail and falls back after a failed load', async () => {
    fetchNews.mockResolvedValue(
      pagedResponse(
        [
          newsItemFixture({
            id: 7,
            title: 'Live Aid',
            imageUrl: '/ugc/articles/editors/me/hero.webp',
            thumbnailUrl: '/ugc/articles/editors/me/hero.webp?size=thumb',
          }),
        ],
        1,
        1,
      ),
    );
    await renderNews();
    await waitFor(() => expect(screen.getByTestId('news-story-7-thumb')).toBeOnTheScreen());
    const thumb = screen.getByTestId('news-story-7-thumb');
    expect(thumb.props.source).toEqual({
      uri: 'http://qz.test/ugc/articles/editors/me/hero.webp?size=thumb',
    });
    expect(thumb.props.placeholder).toBe(newsArticlePlaceholder);

    await fireEvent(thumb, 'error');
    expect(screen.getByTestId('news-story-7-thumb').props.source).toBe(newsArticlePlaceholder);
  });

  it('uses the bundled placeholder when gallery picks expose null API urls', async () => {
    fetchNews.mockResolvedValue(
      pagedResponse(
        [newsItemFixture({ id: 9, title: 'Gallery pick', imageUrl: null, thumbnailUrl: null })],
        1,
        1,
      ),
    );
    await renderNews();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Open Gallery pick' })).toBeOnTheScreen());
    expect(screen.getByTestId('news-story-9-thumb').props.source).toBe(newsArticlePlaceholder);
  });
});

describe('newsListResetKey', () => {
  it('keeps the decade/year key until a notification refresh arrives', () => {
    expect(newsListResetKey('ALL', undefined)).toBe('ALL');
    expect(newsListResetKey('ALL', 99)).toBe('ALL:99');
  });
});
