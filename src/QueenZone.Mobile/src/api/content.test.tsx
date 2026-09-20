import {
  fetchAlbumDetail,
  fetchArticleDetail,
  fetchArticlesPage,
  fetchBiographyChapter,
  fetchBiographyPage,
  fetchDiscographyPage,
  fetchAllFanPerformances,
  fetchFanPerformanceDetail,
  fetchFanPerformancesPage,
  fetchFreddieTributePage,
  fetchLiveActivity,
  fetchNewsDetail,
  fetchNewsPage,
  fetchOnThisDay,
  fetchTimelineEventById,
  fetchPhotoCategories,
  fetchPhotoCategory,
  fetchPhotoCategoryItems,
  fetchPhotoDetail,
  fetchHomePoll,
  fetchQuoteById,
  startQuizSprint,
  checkQuizSprintAnswer,
  finishQuizSprint,
  fetchQuizSprintDaily,
  fetchQuizSprintLeaderboard,
  fetchRandomQuote,
  fetchRandomTrivia,
  voteHomePoll,
  fetchTimelinePage,
} from './content';
import { fanPerformanceFixture, jsonResponse } from '../test/fixtures';
import { ContentCache, createMemoryStorage, setContentCacheForTests } from '../cache';

jest.mock('../config', () => ({
  apiV1Url: (path: string) => `http://qz.test/api/v1${path.startsWith('/') ? path : `/${path}`}`,
}));

const fetchMock = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit?]>();

beforeEach(() => {
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
});

afterEach(() => {
  setContentCacheForTests(null);
});

function lastUrl() {
  const call = fetchMock.mock.calls.at(-1);
  if (!call) {
    throw new Error('fetch was not called');
  }
  return String(call[0]);
}

describe('fetchNewsPage', () => {
  it('paginates and forwards the decade filter', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
    await fetchNewsPage({ page: 2, pageSize: 10, decade: 2010 });
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/news?page=2&pageSize=10&decade=2010');
  });

  it('omits decade when not given', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
    await fetchNewsPage();
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/news');
  });

  it('serves a fresh-enough cached page without waiting on the network when cacheHint is set (issue #1477)', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    setContentCacheForTests(cache);
    const cached = { items: [{ id: 1 }], totalCount: 1 };
    await cache.put('home:news', cached);
    fetchMock.mockResolvedValueOnce(jsonResponse(null)); // background revalidation, result unused by this test

    const page = await fetchNewsPage({
      page: 1,
      pageSize: 4,
      cacheHint: { cacheKey: 'home:news', ttlMs: 60_000 },
    });

    expect(page).toEqual(cached);
  });
});

describe('fetchNewsDetail', () => {
  it('requests the numeric id path and returns the parsed body', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 42, title: 'Story' }));
    const detail = await fetchNewsDetail(42);
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/news/42');
    expect(detail).toMatchObject({ id: 42, title: 'Story' });
  });
});

describe('fetchArticlesPage and fetchArticleDetail', () => {
  it('builds the list and article-detail URLs', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
    await fetchArticlesPage({ page: 2, pageSize: 20 });
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/articles?page=2&pageSize=20');

    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 101, title: 'Inside the Making of Bohemian Rhapsody' }));
    const detail = await fetchArticleDetail(101);
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/articles/101');
    expect(detail).toMatchObject({ id: 101, title: 'Inside the Making of Bohemian Rhapsody' });
  });
});

describe('fetchBiographyPage and fetchBiographyChapter', () => {
  it('builds the list and chapter-detail URLs', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
    await fetchBiographyPage({ page: 3 });
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/biography?page=3');

    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 7 }));
    await fetchBiographyChapter(7);
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/biography/7');
  });
});

describe('fetchDiscographyPage and fetchAlbumDetail', () => {
  it('builds the list and album-detail URLs', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
    await fetchDiscographyPage();
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/discography');

    fetchMock.mockResolvedValueOnce(jsonResponse({ albumId: 5 }));
    await fetchAlbumDetail(5);
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/discography/5');
  });
});

describe('fetchTimelinePage and fetchOnThisDay', () => {
  it('requests the timeline list and the single on-this-day event', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
    await fetchTimelinePage({ page: 1, pageSize: 5 });
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/timeline?page=1&pageSize=5');

    fetchMock.mockResolvedValueOnce(jsonResponse(null));
    const event = await fetchOnThisDay();
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/on-this-day');
    expect(event).toBeNull();
  });

  it('serves a fresh-enough cached on-this-day event without waiting on the network (issue #1477)', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    setContentCacheForTests(cache);
    await cache.put('home:on-this-day', { id: 1, title: 'cached event' });
    fetchMock.mockResolvedValueOnce(jsonResponse(null)); // background revalidation, result unused by this test

    const event = await fetchOnThisDay(undefined, { cacheKey: 'home:on-this-day', ttlMs: 60_000 });

    expect(event).toEqual({ id: 1, title: 'cached event' });
  });
});

describe('fetchTimelineEventById', () => {
  it('requests a published event by id, including deep off-page ids', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({
        id: 9999,
        title: 'Deep off-page event',
        summary: 'Would sit many pages down the chronological list.',
        eventDate: '1985-07-13T00:00:00Z',
        formattedDate: '13 July 1985',
        category: 'live',
        categoryLabel: 'Live',
        sourceUrl: null,
      }),
    );
    const event = await fetchTimelineEventById(9999);
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/timeline/9999');
    expect(event).toMatchObject({ id: 9999, title: 'Deep off-page event' });
  });
});

describe('fetchLiveActivity', () => {
  it('requests the live-activity summary', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ newForumRepliesToday: 3 }));
    const summary = await fetchLiveActivity();
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/live-activity');
    expect(summary.newForumRepliesToday).toBe(3);
  });

  it('fetches over the network once a cached entry ages past ttlMs (issue #1477)', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    setContentCacheForTests(cache);
    await cache.put('home:live-activity', { newForumRepliesToday: 1 });
    fetchMock.mockResolvedValueOnce(jsonResponse({ newForumRepliesToday: 9 }));

    const summary = await fetchLiveActivity(undefined, { cacheKey: 'home:live-activity', ttlMs: -1 });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(summary.newForumRepliesToday).toBe(9);
  });
});

describe('fetchRandomQuote', () => {
  it('requests the random quote and returns null when none are published', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse(null));
    const quote = await fetchRandomQuote();
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/quotes/random');
    expect(quote).toBeNull();
  });

  it('returns the parsed quote body', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 5, text: 'A kind of magic', whoSaid: 'Freddie Mercury' }));
    const quote = await fetchRandomQuote();
    expect(quote).toMatchObject({ id: 5, text: 'A kind of magic', whoSaid: 'Freddie Mercury' });
  });

  it('always hits the network when fallback is forced off (pull-to-refresh, issue #1477)', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    setContentCacheForTests(cache);
    await cache.put('home:quote', { id: 1, text: 'cached', whoSaid: 'Freddie Mercury' });
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 2, text: 'fresh', whoSaid: 'Freddie Mercury' }));

    const quote = await fetchRandomQuote(undefined, {
      cacheKey: 'home:quote',
      ttlMs: 60_000,
      fallback: false,
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(quote).toMatchObject({ id: 2, text: 'fresh' });
  });
});

describe('fetchRandomTrivia', () => {
  it('requests the random trivia fact and returns null when none are published', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse(null));
    const fact = await fetchRandomTrivia();
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/trivia/random');
    expect(fact).toBeNull();
  });

  it('returns the parsed trivia body', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({
        id: 12,
        text: 'The first Queen album was recorded in 1972.',
        category: 'Studio',
        difficulty: 'Easy',
        source: 'Queen archive',
      }),
    );
    const fact = await fetchRandomTrivia();
    expect(fact).toMatchObject({
      id: 12,
      text: 'The first Queen album was recorded in 1972.',
      category: 'Studio',
      difficulty: 'Easy',
      source: 'Queen archive',
    });
  });
});

describe('fetchHomePoll', () => {
  it('requests the current home poll and returns null when none is live', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse(null));
    const poll = await fetchHomePoll();
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/home-poll');
    expect(poll).toBeNull();
  });

  it('forwards a bearer token so the viewer choice can be marked', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({
        id: 'poll-1',
        question: 'Best album?',
        options: [],
        totalVotes: 0,
        isClosed: false,
        viewerHasVoted: false,
        selectedOptionId: null,
      }),
    );
    await fetchHomePoll(undefined, 'member-token');
    const init = fetchMock.mock.calls.at(-1)?.[1] as RequestInit;
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer member-token');
  });
});

describe('voteHomePoll', () => {
  it('posts the option id to the home-poll votes path', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({
        id: 'poll-1',
        question: 'Best album?',
        options: [],
        totalVotes: 1,
        isClosed: false,
        viewerHasVoted: true,
        selectedOptionId: 'opt-1',
      }),
    );
    await voteHomePoll('opt-1', 'member-token');
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/home-poll/votes');
    const init = fetchMock.mock.calls.at(-1)?.[1] as RequestInit;
    expect(init.method).toBe('POST');
    expect(init.body).toBe(JSON.stringify({ optionId: 'opt-1' }));
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer member-token');
  });
});

describe('Quiz Sprint api', () => {
  it('starts a round with a POST to the sprint start path', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ ticket: 't', serverNowUnixMilliseconds: 1, expiresAtUnixMilliseconds: 2, durationSeconds: 60, questions: [] }),
    );
    const round = await startQuizSprint();
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/quizzes/sprint/start');
    expect((fetchMock.mock.calls.at(-1)?.[1] as RequestInit).method).toBe('POST');
    expect(round.ticket).toBe('t');
  });

  it('checks one answer against the ticket', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ isCorrect: true, correctOptionId: 'o1' }));
    await checkQuizSprintAnswer('t', 'q1', 'o1');
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/quizzes/sprint/answer');
    const init = fetchMock.mock.calls.at(-1)?.[1] as RequestInit;
    expect(init.body).toBe(JSON.stringify({ ticket: 't', questionId: 'q1', optionId: 'o1' }));
  });

  it('finishes with the ticket and answers, sending Bearer only when signed in', async () => {
    fetchMock.mockImplementation(async () =>
      jsonResponse({ attempted: 1, correct: 1, points: 1, bestStreak: 1, recorded: true, rank: 1, answers: [] }),
    );
    await finishQuizSprint('t', [{ questionId: 'q1', selectedOptionId: 'o1' }], 'member-token');
    let init = fetchMock.mock.calls.at(-1)?.[1] as RequestInit;
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/quizzes/sprint/finish');
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer member-token');
    expect(init.body).toBe(JSON.stringify({ ticket: 't', answers: [{ questionId: 'q1', selectedOptionId: 'o1' }] }));

    await finishQuizSprint('t', []);
    init = fetchMock.mock.calls.at(-1)?.[1] as RequestInit;
    expect((init.headers as Record<string, string>).Authorization).toBeUndefined();
  });

  it('reads the leaderboard for a scope', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ scope: 'all', top: [], viewer: null, players: 3 }));
    const board = await fetchQuizSprintLeaderboard('all', undefined, 'member-token');
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/quizzes/sprint/leaderboard?scope=all');
    expect(board.players).toBe(3);
    const init = fetchMock.mock.calls.at(-1)?.[1] as RequestInit;
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer member-token');
  });

  it('reads the daily board', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ top: [], viewer: null, playersToday: 0 }));
    const board = await fetchQuizSprintDaily();
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/quizzes/sprint/daily');
    expect(board.playersToday).toBe(0);
  });
});

describe('fetchQuoteById', () => {
  it('requests the published quote by id', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ id: 5, text: 'A kind of magic', whoSaid: 'Freddie Mercury', context: 'Live Aid' }),
    );
    const quote = await fetchQuoteById(5);
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/quotes/5');
    expect(quote).toMatchObject({
      id: 5,
      text: 'A kind of magic',
      whoSaid: 'Freddie Mercury',
      context: 'Live Aid',
    });
  });
});

describe('fetchFreddieTributePage and fetchFanPerformancesPage/Detail', () => {
  it('builds tribute and fan-performance URLs', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
    await fetchFreddieTributePage({ page: 1 });
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/freddietribute?page=1');

    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
    await fetchFanPerformancesPage();
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/fan-performances');

    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 9 }));
    await fetchFanPerformanceDetail(9);
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/fan-performances/9');
  });

  it('fetchAllFanPerformances walks every page at pageSize 100', async () => {
    const first = fanPerformanceFixture({ id: 1, title: 'First' });
    const second = fanPerformanceFixture({ id: 2, title: 'Second' });
    fetchMock
      .mockResolvedValueOnce(
        jsonResponse({
          items: [first],
          page: 1,
          pageSize: 100,
          totalCount: 2,
          totalPages: 2,
        }),
      )
      .mockResolvedValueOnce(
        jsonResponse({
          items: [second],
          page: 2,
          pageSize: 100,
          totalCount: 2,
          totalPages: 2,
        }),
      );

    const catalog = await fetchAllFanPerformances();
    expect(catalog).toEqual([first, second]);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(String(fetchMock.mock.calls[0]?.[0])).toBe(
      'http://qz.test/api/v1/content/fan-performances?page=1&pageSize=100',
    );
    expect(String(fetchMock.mock.calls[1]?.[0])).toBe(
      'http://qz.test/api/v1/content/fan-performances?page=2&pageSize=100',
    );
  });
});

describe('photo content endpoints', () => {
  it('builds category list, single-category, item-list, and detail URLs', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
    await fetchPhotoCategories({ page: 2 });
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/photos/categories?page=2');

    fetchMock.mockResolvedValueOnce(jsonResponse({ catId: 1 }));
    await fetchPhotoCategory('live shots');
    expect(lastUrl()).toBe('http://qz.test/api/v1/content/photos/categories/live%20shots');

    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
    await fetchPhotoCategoryItems('live-shots', { size: 'thumb' });
    expect(lastUrl()).toBe(
      'http://qz.test/api/v1/content/photos/categories/live-shots/items?size=thumb',
    );

    fetchMock.mockResolvedValueOnce(jsonResponse({ picId: 3 }));
    await fetchPhotoDetail('live-shots', 3, { size: 'full' });
    expect(lastUrl()).toBe(
      'http://qz.test/api/v1/content/photos/categories/live-shots/items/3?size=full',
    );
  });

  it('serves a fresh-enough cached category page without waiting on the network (issue #1477)', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    setContentCacheForTests(cache);
    const cached = { items: [{ slug: 'live-shots' }], totalCount: 1 };
    await cache.put('home:photo-categories', cached);
    fetchMock.mockResolvedValueOnce(jsonResponse(null)); // background revalidation, result unused by this test

    const page = await fetchPhotoCategories({
      page: 1,
      pageSize: 3,
      cacheHint: { cacheKey: 'home:photo-categories', ttlMs: 60_000 },
    });

    expect(page).toEqual(cached);
  });
});
