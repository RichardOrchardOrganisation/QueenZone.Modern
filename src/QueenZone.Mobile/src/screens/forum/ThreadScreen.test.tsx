import type { ReactNode } from 'react';
import { Alert, RefreshControl } from 'react-native';
import { fireEvent, screen, userEvent, waitFor, within } from '@testing-library/react-native';
import {
  fetchForumTopic,
  fetchForumPostModerationState,
  fetchForumTopicPoll,
  fetchForumTopicPosts,
  fetchForumTopicPostsResult,
  fetchForumTopicResult,
  fetchForumTopicWatch,
  cacheForumAttachment,
  blockForumPostAuthor,
  openForumAttachmentFile,
  openForumAttachmentImage,
  saveForumAttachmentImage,
  unwatchForumTopic,
  voteForumTopicPoll,
  watchForumTopic,
} from '../../api';
import type { CachedResult } from '../../api';
import type { ApiPagedResponse, ForumPost } from '../../api/types';
import { ApiError } from '../../api/client';
import {
  forumAttachmentFixture,
  forumPostFixture,
  forumTopicDetailFixture,
  memberProfileFixture,
  pagedResponse,
} from '../../test/fixtures';
import { useOfflineQueue } from '../../offlineQueue';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { ThreadScreen } from './ThreadScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchForumTopic: jest.fn(),
    fetchForumPostModerationState: jest.fn(async () => ({ reportedPostIds: [], blockedMemberIds: [] })),
    fetchForumTopicResult: jest.fn(),
    fetchForumTopicPosts: jest.fn(),
    fetchForumTopicPostsResult: jest.fn(),
    fetchForumTopicPoll: jest.fn(),
    fetchForumTopicWatch: jest.fn(),
    watchForumTopic: jest.fn(),
    unwatchForumTopic: jest.fn(),
    voteForumTopicPoll: jest.fn(),
    closeForumTopicPoll: jest.fn(),
    openForumAttachmentFile: jest.fn(),
    openForumAttachmentImage: jest.fn(),
    cacheForumAttachment: jest.fn(),
    blockForumPostAuthor: jest.fn(),
    saveForumAttachmentImage: jest.fn(),
  };
});

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../../offlineQueue', () => ({
  useOfflineQueue: jest.fn(() => []),
  flushOfflineQueue: jest.fn(),
  removeOfflineItem: jest.fn(),
  updateOfflineItem: jest.fn(),
}));

const useOfflineQueueMock = useOfflineQueue as jest.MockedFunction<typeof useOfflineQueue>;

jest.mock('../../config', () => ({
  getAppConfig: () => ({ apiBaseUrl: 'http://qz.test', appEnv: 'development', version: '0.1.0' }),
}));

const fetchTopic = fetchForumTopic as jest.MockedFunction<typeof fetchForumTopic>;
const fetchModerationState = fetchForumPostModerationState as jest.MockedFunction<
  typeof fetchForumPostModerationState
>;
const fetchTopicResult = fetchForumTopicResult as jest.MockedFunction<typeof fetchForumTopicResult>;
const fetchPosts = fetchForumTopicPosts as jest.MockedFunction<typeof fetchForumTopicPosts>;
const fetchPostsResult = fetchForumTopicPostsResult as jest.MockedFunction<
  typeof fetchForumTopicPostsResult
>;

const NETWORK_CACHED_AT = '2024-06-01T10:00:00.000Z';

function asNetwork<T>(data: T): CachedResult<T> {
  return { data, source: 'network', cachedAt: NETWORK_CACHED_AT };
}

function asCache<T>(data: T, cachedAt = NETWORK_CACHED_AT): CachedResult<T> {
  return { data, source: 'cache', cachedAt };
}

const defaultTopic = forumTopicDetailFixture({
  id: 1002,
  title: 'Ranking every studio album',
  forumId: 1,
  forumName: 'The Music',
  categoryPath: '/forum/1/the-music',
  detailPath: '/forum/topic/1002/ranking-every-studio-album',
  postCount: 1,
  hasPoll: false,
  isLocked: false,
});

const defaultPosts = pagedResponse([forumPostFixture()], 1, 1);

function mockNetworkTopic(topic = defaultTopic, posts = defaultPosts) {
  fetchTopic.mockResolvedValue(topic);
  fetchPosts.mockResolvedValue(posts);
  fetchTopicResult.mockResolvedValue(asNetwork(topic));
  fetchPostsResult.mockResolvedValue(asNetwork(posts));
}

function mockNetworkPosts(posts: ApiPagedResponse<ForumPost>) {
  fetchPosts.mockResolvedValue(posts);
  fetchPostsResult.mockResolvedValue(asNetwork(posts));
}
const fetchPoll = fetchForumTopicPoll as jest.MockedFunction<typeof fetchForumTopicPoll>;
const fetchWatch = fetchForumTopicWatch as jest.MockedFunction<typeof fetchForumTopicWatch>;
const watchTopic = watchForumTopic as jest.MockedFunction<typeof watchForumTopic>;
const unwatchTopic = unwatchForumTopic as jest.MockedFunction<typeof unwatchForumTopic>;
const votePoll = voteForumTopicPoll as jest.MockedFunction<typeof voteForumTopicPoll>;
const openAttachment = openForumAttachmentFile as jest.MockedFunction<typeof openForumAttachmentFile>;
const openImage = openForumAttachmentImage as jest.MockedFunction<typeof openForumAttachmentImage>;
const cacheAttachment = cacheForumAttachment as jest.MockedFunction<typeof cacheForumAttachment>;
const saveImageAttachment = saveForumAttachmentImage as jest.MockedFunction<
  typeof saveForumAttachmentImage
>;
const blockPostAuthor = blockForumPostAuthor as jest.MockedFunction<typeof blockForumPostAuthor>;

function renderThread(navigation = fakeNavigation()) {
  return {
    navigation,
    ...renderWithProviders(
      <ThreadScreen
        navigation={navigation as never}
        route={
          {
            key: 'thread',
            name: 'Thread',
            params: { id: 1002, title: 'Ranking every studio album' },
          } as never
        }
      />,
      { navigation: false },
    ),
  };
}

describe('ThreadScreen watch control', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    mockNetworkTopic();
    fetchPoll.mockResolvedValue({} as never);
    fetchWatch.mockResolvedValue({ watching: false });
    watchTopic.mockResolvedValue({ watching: true });
    unwatchTopic.mockResolvedValue({ watching: false });
    votePoll.mockReset();
    openAttachment.mockReset();
    openAttachment.mockResolvedValue(undefined);
    openImage.mockReset();
    openImage.mockResolvedValue('data:image/jpeg;base64,dGVzdA==');
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('asks signed-out members to sign in to watch', async () => {
    renderThread();
    await waitFor(() => expect(screen.getByTestId(testIds.forumThreadWatch)).toBeTruthy(), {
      timeout: 5_000,
    });
    expect(screen.getByLabelText('Sign in to watch')).toBeTruthy();
    expect(fetchWatch).not.toHaveBeenCalled();
  });

  it('watches and unwatches a topic for a signed-in member', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    renderThread();

    await waitFor(() => expect(fetchWatch).toHaveBeenCalledWith(1002, 'tok', expect.any(AbortSignal)));
    const watchButton = await screen.findByLabelText('Watch topic');
    fireEvent.press(watchButton);

    await waitFor(() => expect(watchTopic).toHaveBeenCalledWith(1002, 'tok'));
    await screen.findByLabelText('Unwatch');
    fireEvent.press(screen.getByLabelText('Unwatch'));
    await waitFor(() => expect(unwatchTopic).toHaveBeenCalledWith(1002, 'tok'));
  });

  it('shows a topic error and retries', async () => {
    fetchTopicResult.mockRejectedValueOnce(new ApiError(500, 'The server had a problem.'));
    fetchTopicResult.mockResolvedValueOnce(asNetwork(defaultTopic));
    renderThread();
    await waitFor(() => expect(screen.getByText('The server had a problem.')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(fetchTopicResult).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.getByText('brightonrock')).toBeOnTheScreen());
  });

  it('hides Reply when the topic is locked', async () => {
    mockNetworkTopic({ ...defaultTopic, isLocked: true });
    renderThread();
    await waitFor(() => expect(screen.getByText('This topic is locked.')).toBeOnTheScreen());
    expect(screen.queryByRole('button', { name: 'Reply' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Sign in to reply' })).toBeNull();
  });

  it('opens the composer from the Reply CTA', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    const { navigation } = renderThread();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Reply' })).toBeOnTheScreen());
    expect(screen.getByTestId(testIds.forumThreadReply)).toBeOnTheScreen();

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Reply' }));
    expect(navigation.navigate).toHaveBeenCalledWith('Composer', {
      threadId: 1002,
      threadTitle: 'Ranking every studio album',
      isLocked: false,
    });
  });

  it('wires a poll vote to voteForumTopicPoll', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    mockNetworkTopic({ ...defaultTopic, hasPoll: true });
    const openPoll = {
      pollId: 'poll-1',
      topicId: 1002,
      question: 'Best studio album?',
      isMultiChoice: false,
      maxChoices: null,
      closesAt: null,
      closedAt: null,
      createdAt: '2024-01-01T00:00:00.000Z',
      totalVotes: 1,
      distinctVoters: 1,
      viewerHasVoted: true,
      isClosed: false,
      canViewerVote: false,
      canViewerClose: false,
      options: [
        {
          optionId: 'opt-a',
          optionText: 'A Night at the Opera',
          displayOrder: 1,
          voteCount: 1,
          percentage: 100,
          selectedByViewer: true,
        },
        {
          optionId: 'opt-b',
          optionText: 'Innuendo',
          displayOrder: 2,
          voteCount: 0,
          percentage: 0,
          selectedByViewer: false,
        },
      ],
    };
    fetchPoll.mockResolvedValue({
      ...openPoll,
      totalVotes: 0,
      distinctVoters: 0,
      viewerHasVoted: false,
      canViewerVote: true,
      options: openPoll.options.map((option) => ({
        ...option,
        voteCount: 0,
        percentage: 0,
        selectedByViewer: false,
      })),
    });
    votePoll.mockResolvedValue(openPoll);
    renderThread();
    await waitFor(() => expect(screen.getByText('Best studio album?')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('radio', { name: 'A Night at the Opera' }));
    await user.press(screen.getByRole('button', { name: 'Vote' }));
    await waitFor(() => expect(votePoll).toHaveBeenCalledWith(1002, ['opt-a'], 'tok'));
  });
});

function lastHeaderOptions(navigation: ReturnType<typeof fakeNavigation>) {
  const calls = navigation.setOptions.mock.calls;
  expect(calls.length).toBeGreaterThan(0);
  return calls[calls.length - 1]?.[0] as { headerLeft?: () => ReactNode; title?: string };
}

describe('ThreadScreen header back control', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    mockNetworkTopic();
    fetchPoll.mockResolvedValue({} as never);
    fetchWatch.mockResolvedValue({ watching: false });
  });

  it('pops the forum stack when back has somewhere to go', async () => {
    const navigation = fakeNavigation();
    navigation.canGoBack.mockReturnValue(true);
    renderThread(navigation);
    await waitFor(() => expect(screen.getByTestId(testIds.forumThreadScreen)).toBeOnTheScreen());

    const user = userEvent.setup();
    renderWithProviders(<>{lastHeaderOptions(navigation).headerLeft?.()}</>, { navigation: false });
    await user.press(screen.getByTestId(testIds.forumThreadBack));
    expect(navigation.goBack).toHaveBeenCalledTimes(1);
    expect(navigation.navigate).not.toHaveBeenCalled();
  });

  it('falls back to ForumIndex when the stack has no history', async () => {
    const navigation = fakeNavigation();
    navigation.canGoBack.mockReturnValue(false);
    renderThread(navigation);
    await waitFor(() => expect(screen.getByTestId(testIds.forumThreadScreen)).toBeOnTheScreen());

    const user = userEvent.setup();
    renderWithProviders(<>{lastHeaderOptions(navigation).headerLeft?.()}</>, { navigation: false });
    await user.press(screen.getByTestId(testIds.forumThreadBack));
    expect(navigation.goBack).not.toHaveBeenCalled();
    expect(navigation.navigate).toHaveBeenCalledWith('ForumIndex');
  });
});

describe('ThreadScreen attachments', () => {
  const imageNoThumb = forumAttachmentFixture();
  const pdf = forumAttachmentFixture({
    fileName: 'opera-side-two-notes.pdf',
    url: '/forum/attachment/legacy/1101',
    downloadUrl: '/api/v1/forum/attachments/legacy/1101',
    extension: 'PDF',
    formattedSize: '47.0 KB',
    isImage: false,
    thumbnailUrl: null,
  });
  const mp3 = forumAttachmentFixture({
    fileName: 'brighton-rock-solo.mp3',
    url: '/forum/attachment/legacy/1201',
    downloadUrl: '/api/v1/forum/attachments/legacy/1201',
    extension: 'MP3',
    formattedSize: '3.2 MB',
    isImage: false,
    thumbnailUrl: null,
  });

  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    mockNetworkTopic();
    fetchPoll.mockResolvedValue({} as never);
    fetchWatch.mockResolvedValue({ watching: false });
    openAttachment.mockReset();
    openAttachment.mockResolvedValue(undefined);
    openImage.mockReset();
    openImage.mockResolvedValue('data:image/jpeg;base64,dGVzdA==');
    cacheAttachment.mockReset();
    cacheAttachment.mockResolvedValue({
      fileUri: 'file:///cache/brighton-rock-solo.mp3',
      contentType: 'audio/mpeg',
      dataUri: 'data:audio/mpeg;base64,AA==',
    });
    saveImageAttachment.mockReset();
    saveImageAttachment.mockResolvedValue(undefined);
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('lets a signed-in member open an image with no thumbnail in the in-app viewer', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    mockNetworkPosts(
      pagedResponse(
        [
          {
            id: 1002,
            body: '<p>Hello</p>',
            postedAt: '2024-06-01T10:00:00.000Z',
            authorUsername: 'brightonrock',
            signature: null,
            authorMemberSince: null,
            authorMemberId: null,
            editedAt: null,
            editCount: 0,
            attachments: [imageNoThumb],
          },
        ],
        1,
        1,
      ),
    );
    renderThread();
    await waitFor(() => expect(screen.getByText('anoto-setlist-scan.jpg')).toBeOnTheScreen());
    expect(screen.queryByTestId(testIds.forumThreadAttachmentViewer)).toBeNull();
    expect(openImage).not.toHaveBeenCalled();

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /anoto-setlist-scan.jpg/ }));
    await waitFor(() =>
      expect(openImage).toHaveBeenCalledWith('/api/v1/forum/attachments/legacy/1002', 'tok'),
    );
    const viewer = screen.getByTestId(testIds.forumThreadAttachmentViewer);
    const viewerImage = within(viewer).getByLabelText('anoto-setlist-scan.jpg');
    expect(viewerImage.props.source).toEqual({
      uri: 'data:image/jpeg;base64,dGVzdA==',
    });
    expect(viewerImage.props.source.headers).toBeUndefined();
    expect(viewerImage.props.source.uri).not.toContain('/forum/attachment/');
    expect(viewerImage.props.source.uri).not.toMatch(/^https?:\/\//);
    expect(openAttachment).not.toHaveBeenCalled();
  });

  it('lets a signed-in member open a thumbed image in the in-app viewer', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    mockNetworkPosts(
      pagedResponse(
        [
          {
            id: 1002,
            body: '<p>Hello</p>',
            postedAt: '2024-06-01T10:00:00.000Z',
            authorUsername: 'brightonrock',
            signature: null,
            authorMemberSince: null,
            authorMemberId: null,
            editedAt: null,
            editCount: 0,
            attachments: [
              {
                ...imageNoThumb,
                fileName: 'tour-poster.jpg',
                thumbnailUrl: '/ugc/forum/tour-poster-thumb.webp',
              },
            ],
          },
        ],
        1,
        1,
      ),
    );
    renderThread();
    await waitFor(() => expect(screen.getByText('tour-poster.jpg')).toBeOnTheScreen());
    const preview = screen.getByLabelText('tour-poster.jpg');
    expect(preview.props.source.uri).toContain('/ugc/forum/tour-poster-thumb.webp');
    expect(preview.props.source.uri).not.toContain('/forum/attachment/');
    expect(preview.props.priority).toBe('low');
    expect(preview.props.recyclingKey).toBe('/api/v1/forum/attachments/legacy/1002');
    expect(screen.queryByTestId(testIds.forumThreadAttachmentViewer)).toBeNull();
    expect(openImage).not.toHaveBeenCalled();

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /tour-poster.jpg/ }));
    await waitFor(() =>
      expect(openImage).toHaveBeenCalledWith('/api/v1/forum/attachments/legacy/1002', 'tok'),
    );
    const viewer = screen.getByTestId(testIds.forumThreadAttachmentViewer);
    const viewerImage = within(viewer).getByLabelText('tour-poster.jpg');
    expect(viewerImage.props.source).toEqual({
      uri: 'data:image/jpeg;base64,dGVzdA==',
    });
    expect(viewerImage.props.contentFit).toBe('contain');
    expect(viewerImage.props.source.uri).not.toContain('/forum/attachment/');
    expect(openAttachment).not.toHaveBeenCalled();
  });

  it('does not pass a cookie-gated thumbnail to Image', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    mockNetworkPosts(
      pagedResponse(
        [
          {
            id: 1002,
            body: '<p>Hello</p>',
            postedAt: '2024-06-01T10:00:00.000Z',
            authorUsername: 'brightonrock',
            signature: null,
            authorMemberSince: null,
            authorMemberId: null,
            editedAt: null,
            editCount: 0,
            attachments: [
              {
                ...imageNoThumb,
                fileName: 'tour-poster.jpg',
                thumbnailUrl: '/forum/attachment/legacy/1002',
              },
            ],
          },
        ],
        1,
        1,
      ),
    );
    renderThread();
    await waitFor(() => expect(screen.getByText('tour-poster.jpg')).toBeOnTheScreen());
    expect(screen.queryByLabelText('tour-poster.jpg')).toBeNull();

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /tour-poster.jpg/ }));
    await waitFor(() =>
      expect(openImage).toHaveBeenCalledWith('/api/v1/forum/attachments/legacy/1002', 'tok'),
    );
    expect(screen.getByTestId(testIds.forumThreadAttachmentViewer)).toBeOnTheScreen();
  });

  it('shows an error when downloadUrl is cookie-gated', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    openImage.mockImplementationOnce(async (downloadUrl: string, accessToken: string) => {
      const actual = jest.requireActual<{
        openForumAttachmentImage: typeof openForumAttachmentImage;
      }>('../../api');
      return actual.openForumAttachmentImage(downloadUrl, accessToken);
    });
    mockNetworkPosts(
      pagedResponse(
        [
          {
            id: 1002,
            body: '<p>Hello</p>',
            postedAt: '2024-06-01T10:00:00.000Z',
            authorUsername: 'brightonrock',
            signature: null,
            authorMemberSince: null,
            authorMemberId: null,
            editedAt: null,
            editCount: 0,
            attachments: [
              {
                ...imageNoThumb,
                downloadUrl: '/forum/attachment/legacy/1002',
              },
            ],
          },
        ],
        1,
        1,
      ),
    );
    renderThread();
    await waitFor(() => expect(screen.getByText('anoto-setlist-scan.jpg')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /anoto-setlist-scan.jpg/ }));
    await waitFor(() =>
      expect(screen.getByText('This attachment cannot be opened from the app.')).toBeOnTheScreen(),
    );
    expect(screen.queryByTestId(testIds.forumThreadAttachmentViewer)).toBeNull();
  });

  it('shows a sign-in error when the session has no access token', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = null;
    mockNetworkPosts(
      pagedResponse(
        [
          {
            id: 1002,
            body: '<p>Hello</p>',
            postedAt: '2024-06-01T10:00:00.000Z',
            authorUsername: 'brightonrock',
            signature: null,
            authorMemberSince: null,
            authorMemberId: null,
            editedAt: null,
            editCount: 0,
            attachments: [imageNoThumb],
          },
        ],
        1,
        1,
      ),
    );
    renderThread();
    await waitFor(() => expect(screen.getByText('anoto-setlist-scan.jpg')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /anoto-setlist-scan.jpg/ }));
    await waitFor(() => expect(screen.getByText('Sign in to continue.')).toBeOnTheScreen());
    expect(openImage).not.toHaveBeenCalled();
    expect(screen.queryByTestId(testIds.forumThreadAttachmentViewer)).toBeNull();
  });

  it('shows an error when the image download fails', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    openImage.mockRejectedValueOnce(new ApiError(401, 'Sign in to continue.'));
    mockNetworkPosts(
      pagedResponse(
        [
          {
            id: 1002,
            body: '<p>Hello</p>',
            postedAt: '2024-06-01T10:00:00.000Z',
            authorUsername: 'brightonrock',
            signature: null,
            authorMemberSince: null,
            authorMemberId: null,
            editedAt: null,
            editCount: 0,
            attachments: [imageNoThumb],
          },
        ],
        1,
        1,
      ),
    );
    renderThread();
    await waitFor(() => expect(screen.getByText('anoto-setlist-scan.jpg')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /anoto-setlist-scan.jpg/ }));
    await waitFor(() => expect(screen.getByText('Sign in to continue.')).toBeOnTheScreen());
    expect(screen.queryByTestId(testIds.forumThreadAttachmentViewer)).toBeNull();
  });

  it('lets a signed-in member download a non-image attachment', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    mockNetworkPosts(
      pagedResponse(
        [
          {
            id: 1101,
            body: '<p>Notes</p>',
            postedAt: '2024-06-01T11:00:00.000Z',
            authorUsername: 'jazzfanz',
            signature: null,
            authorMemberSince: null,
            authorMemberId: null,
            editedAt: null,
            editCount: 0,
            attachments: [pdf],
          },
        ],
        1,
        1,
      ),
    );
    renderThread();
    await waitFor(() => expect(screen.getByText('opera-side-two-notes.pdf')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /opera-side-two-notes.pdf/ }));
    await waitFor(() =>
      expect(openAttachment).toHaveBeenCalledWith(
        '/api/v1/forum/attachments/legacy/1101',
        'tok',
        'opera-side-two-notes.pdf',
        { present: false },
      ),
    );
    expect(openAttachment.mock.calls[0]?.[0]).not.toContain('/forum/attachment/legacy/');
    expect(screen.getByTestId(testIds.forumThreadAttachmentOpened)).toBeOnTheScreen();
  });

  it('lets a signed-in member play a sound-file attachment', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    mockNetworkPosts(
      pagedResponse(
        [
          {
            id: 1201,
            body: '<p>Solo</p>',
            postedAt: '2024-06-01T12:00:00.000Z',
            authorUsername: 'jazzfanz',
            signature: null,
            authorMemberSince: null,
            authorMemberId: null,
            editedAt: null,
            editCount: 0,
            attachments: [mp3],
          },
        ],
        1,
        1,
      ),
    );
    renderThread();
    await waitFor(() => expect(screen.getByText('brighton-rock-solo.mp3')).toBeOnTheScreen());
    expect(screen.queryByLabelText('brighton-rock-solo.mp3')).toBeNull();

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /brighton-rock-solo.mp3/ }));
    await waitFor(() =>
      expect(cacheAttachment).toHaveBeenCalledWith(
        '/api/v1/forum/attachments/legacy/1201',
        'tok',
        'brighton-rock-solo.mp3',
      ),
    );
    expect(openImage).not.toHaveBeenCalled();
    expect(openAttachment).not.toHaveBeenCalled();
    expect(cacheAttachment.mock.calls[0]?.[0]).not.toContain('/forum/attachment/legacy/');
    expect(screen.getByTestId(testIds.forumThreadAttachmentAudio)).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.forumThreadAttachmentSaveFile)).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.forumThreadAttachmentOpened)).toBeOnTheScreen();
  });

  it('shows an error when a sound-file downloadUrl is cookie-gated', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    cacheAttachment.mockImplementationOnce(async (downloadUrl: string, accessToken: string, fileName: string) => {
      const actual = jest.requireActual<{
        cacheForumAttachment: typeof cacheForumAttachment;
      }>('../../api');
      return actual.cacheForumAttachment(downloadUrl, accessToken, fileName);
    });
    mockNetworkPosts(
      pagedResponse(
        [
          {
            id: 1201,
            body: '<p>Solo</p>',
            postedAt: '2024-06-01T12:00:00.000Z',
            authorUsername: 'jazzfanz',
            signature: null,
            authorMemberSince: null,
            authorMemberId: null,
            editedAt: null,
            editCount: 0,
            attachments: [
              {
                ...mp3,
                downloadUrl: '/forum/attachment/legacy/1201',
              },
            ],
          },
        ],
        1,
        1,
      ),
    );
    renderThread();
    await waitFor(() => expect(screen.getByText('brighton-rock-solo.mp3')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /brighton-rock-solo.mp3/ }));
    await waitFor(() =>
      expect(screen.getByText('This attachment cannot be opened from the app.')).toBeOnTheScreen(),
    );
    expect(screen.queryByTestId(testIds.forumThreadAttachmentOpened)).toBeNull();
  });

  it('shows signed-out metadata without opening bytes', async () => {
    mockNetworkPosts(
      pagedResponse(
        [
          {
            id: 1002,
            body: '<p>Hello</p>',
            postedAt: '2024-06-01T10:00:00.000Z',
            authorUsername: 'brightonrock',
            signature: null,
            authorMemberSince: null,
            authorMemberId: null,
            editedAt: null,
            editCount: 0,
            attachments: [imageNoThumb, pdf],
          },
        ],
        1,
        1,
      ),
    );
    renderThread();
    await waitFor(() => expect(screen.getByText('anoto-setlist-scan.jpg')).toBeOnTheScreen());
    expect(screen.getByText('opera-side-two-notes.pdf')).toBeOnTheScreen();
    expect(screen.getAllByText(/Members only/).length).toBe(2);
    expect(screen.queryByRole('button', { name: /anoto-setlist-scan.jpg/ })).toBeNull();
    expect(screen.queryByRole('button', { name: /opera-side-two-notes.pdf/ })).toBeNull();
    expect(screen.queryByTestId(testIds.forumThreadAttachmentViewer)).toBeNull();
    expect(openAttachment).not.toHaveBeenCalled();
    expect(openImage).not.toHaveBeenCalled();
    expect(saveImageAttachment).not.toHaveBeenCalled();
  });
});

const cachedTopic = forumTopicDetailFixture({
  id: 1002,
  title: 'Ranking every studio album',
  forumId: 1,
  forumName: 'The Music',
  categoryPath: '/forum/1/the-music',
  detailPath: '/forum/topic/1002/ranking-every-studio-album',
  postCount: 1,
  hasPoll: true,
  isLocked: false,
});

const cachedPosts = pagedResponse(
  [
    forumPostFixture({
      body: '<p>Hello from cache</p>',
      attachments: [forumAttachmentFixture()],
    }),
  ],
  1,
  1,
);

describe('ThreadScreen offline snapshot', () => {
  beforeEach(() => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    mockSession.profile = memberProfileFixture({ memberId: 'member-1' });
    useOfflineQueueMock.mockReturnValue([]);
    fetchTopicResult.mockResolvedValue(asCache(cachedTopic));
    fetchPostsResult.mockResolvedValue(asCache(cachedPosts));
    fetchPoll.mockResolvedValue({} as never);
    fetchWatch.mockResolvedValue({ watching: true });
    openAttachment.mockReset();
    openImage.mockReset();
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('shows the cached thread with an offline banner and disables reply, watch, poll, and attach', async () => {
    renderThread();
    await waitFor(() => expect(screen.getByText('brightonrock')).toBeOnTheScreen());
    expect(screen.getByTestId(testIds.offlineBanner)).toBeOnTheScreen();
    expect(screen.getByLabelText(/Offline · last updated/)).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Watch topic' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Reply' })).toBeEnabled();
    expect(screen.queryByText('Best studio album?')).toBeNull();
    expect(fetchWatch).not.toHaveBeenCalled();
    expect(fetchPoll).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: /anoto-setlist-scan.jpg/ })).toBeNull();
  });

  it('keeps the cached snapshot when pull-to-refresh fails offline', async () => {
    renderThread();
    await waitFor(() => expect(screen.getByText('brightonrock')).toBeOnTheScreen());

    fetchTopicResult.mockRejectedValueOnce(ApiError.offline());
    fetchPostsResult.mockRejectedValueOnce(ApiError.offline());
    fireEvent(screen.UNSAFE_getByType(RefreshControl), 'refresh');

    await waitFor(() => expect(fetchTopicResult).toHaveBeenCalledTimes(2));
    expect(screen.getByText('brightonrock')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.offlineBanner)).toBeOnTheScreen();
    expect(screen.queryByText('Unable to load')).toBeNull();
  });

  it('overlays a queued forum reply on the cached snapshot', async () => {
    useOfflineQueueMock.mockReturnValue([
      {
        schemaVersion: 1,
        operationId: 'op-forum',
        memberId: 'member-1',
        kind: 'forum.reply',
        target: { topicId: 1002 },
        payload: { body: 'Queued forum reply' },
        createdAt: '2024-06-01T10:05:00.000Z',
        updatedAt: '2024-06-01T10:05:00.000Z',
        attemptCount: 0,
        nextRetryAt: '2024-06-01T10:05:00.000Z',
        state: 'queued',
        lastError: null,
      },
    ]);

    renderThread();
    await waitFor(() => expect(screen.getByText('Hello from cache')).toBeOnTheScreen());
    expect(screen.getByText('Queued forum reply')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.pendingForumPost)).toBeOnTheScreen();
    expect(screen.getByLabelText('Queued')).toBeOnTheScreen();
  });
});

describe('ThreadScreen forum moderation', () => {
  beforeEach(() => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    mockSession.profile = memberProfileFixture({ memberId: 'viewer-1' });
    useOfflineQueueMock.mockReturnValue([]);
    mockNetworkTopic(
      defaultTopic,
      pagedResponse([forumPostFixture({ id: 42, authorMemberId: 'author-1', authorUsername: 'Reported author' })]),
    );
    fetchModerationState.mockReset();
    fetchModerationState.mockResolvedValue({ reportedPostIds: [], blockedMemberIds: [] });
    blockPostAuthor.mockReset();
    blockPostAuthor.mockResolvedValue(undefined);
    fetchPoll.mockResolvedValue({} as never);
    fetchWatch.mockResolvedValue({ watching: false });
  });

  afterEach(async () => {
    jest.restoreAllMocks();
    await flushVirtualizedList();
  });

  it('opens the report screen from the accessible post overflow menu', async () => {
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    const { navigation } = renderThread();

    await waitFor(() => expect(screen.getByLabelText("Actions for Reported author's post")).toBeOnTheScreen());
    fireEvent.press(screen.getByLabelText("Actions for Reported author's post"));
    alert.mock.calls[0]?.[2]?.find((action) => action.text === 'Report post')?.onPress?.();

    expect(navigation.navigate).toHaveBeenCalledWith('ForumReport', {
      postId: 42,
      authorUsername: 'Reported author',
      threadId: 1002,
      threadTitle: 'Ranking every studio album',
    });
  });

  it('confirms blocking and collapses the author immediately', async () => {
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    renderThread();

    await waitFor(() => expect(screen.getByLabelText("Actions for Reported author's post")).toBeOnTheScreen());
    fireEvent.press(screen.getByLabelText("Actions for Reported author's post"));
    alert.mock.calls[0]?.[2]?.find((action) => action.text === 'Block member')?.onPress?.();
    alert.mock.calls[1]?.[2]?.find((action) => action.text === 'Block')?.onPress?.();

    await waitFor(() => expect(blockPostAuthor).toHaveBeenCalledWith('tok', 42));
    expect(screen.getByLabelText('Show post from blocked member')).toBeOnTheScreen();
    expect(screen.queryByText('Hello')).toBeNull();
  });

  it('shows existing report state and prevents another submission', async () => {
    fetchModerationState.mockResolvedValue({ reportedPostIds: [42], blockedMemberIds: [] });
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    renderThread();

    await waitFor(() => expect(screen.getByText('Report submitted')).toBeOnTheScreen());
    fireEvent.press(screen.getByLabelText("Actions for Reported author's post"));
    expect(alert.mock.calls[0]?.[2]?.some((action) => action.text === 'Report post')).toBe(false);
  });
});
