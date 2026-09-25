import type { ReactNode } from 'react';
import { Alert, Linking } from 'react-native';
import { act, screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchNewsDetail } from '../../api';
import { ApiError } from '../../api/client';
import { deferred, newsDetailFixture } from '../../test/fixtures';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { openExternalUrlCopy } from '../../ui/openExternalUrl';
import { NewsStoryScreen } from './NewsStoryScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchNewsDetail: jest.fn(),
  };
});

const fetchDetail = fetchNewsDetail as jest.MockedFunction<typeof fetchNewsDetail>;

function lastHeaderOptions(navigation: ReturnType<typeof fakeNavigation>) {
  const calls = navigation.setOptions.mock.calls;
  expect(calls.length).toBeGreaterThan(0);
  return calls[calls.length - 1]?.[0] as { headerLeft?: () => ReactNode; title?: string };
}

function renderStory(navigation = fakeNavigation(), id = 42) {
  return {
    navigation,
    ...renderWithProviders(
      <NewsStoryScreen
        navigation={navigation as never}
        route={{ key: 'story', name: 'Story', params: { id } } as never}
      />,
      { navigation: false },
    ),
  };
}

describe('NewsStoryScreen', () => {
  beforeEach(() => {
    fetchDetail.mockReset();
    fetchDetail.mockResolvedValue(
      newsDetailFixture({
        id: 7,
        title: "Roger Taylor Releases New Single and Video 'I See You Now'",
      }),
    );
    jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
    jest.spyOn(Alert, 'alert');
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('installs a back control that returns to Home when opened from the Home stack', async () => {
    const navigation = fakeNavigation();
    navigation.canGoBack.mockReturnValue(false);
    navigation.getState.mockReturnValue({ routeNames: ['Home', 'Search', 'Story'] });
    renderStory(navigation, 7);
    await waitFor(() => expect(screen.getByTestId(testIds.newsStoryScreen)).toBeOnTheScreen());

    const user = userEvent.setup();
    renderWithProviders(<>{lastHeaderOptions(navigation).headerLeft?.()}</>, { navigation: false });
    await user.press(screen.getByTestId(testIds.newsStoryBack));
    expect(navigation.goBack).not.toHaveBeenCalled();
    expect(navigation.navigate).toHaveBeenCalledWith('Home');
  });

  it('installs a back control that returns to NewsIndex when the stack has no history', async () => {
    const navigation = fakeNavigation();
    navigation.canGoBack.mockReturnValue(false);
    renderStory(navigation, 7);
    await waitFor(() => expect(screen.getByTestId(testIds.newsStoryScreen)).toBeOnTheScreen());

    const options = lastHeaderOptions(navigation);
    expect(options.title).toBe("Roger Taylor Releases New Single and Video 'I See You Now'");
    expect(options.headerLeft).toEqual(expect.any(Function));

    const user = userEvent.setup();
    renderWithProviders(<>{options.headerLeft?.()}</>, { navigation: false });
    await user.press(screen.getByTestId(testIds.newsStoryBack));
    expect(navigation.goBack).not.toHaveBeenCalled();
    expect(navigation.navigate).toHaveBeenCalledWith('NewsIndex');
  });

  it('pops the news stack when back has somewhere to go', async () => {
    const navigation = fakeNavigation();
    navigation.canGoBack.mockReturnValue(true);
    renderStory(navigation, 7);
    await waitFor(() => expect(screen.getByTestId(testIds.newsStoryScreen)).toBeOnTheScreen());

    const user = userEvent.setup();
    renderWithProviders(<>{lastHeaderOptions(navigation).headerLeft?.()}</>, { navigation: false });
    await user.press(screen.getByLabelText('Back'));
    expect(navigation.goBack).toHaveBeenCalledTimes(1);
    expect(navigation.navigate).not.toHaveBeenCalled();
  });

  it('shows an error and retries', async () => {
    fetchDetail
      .mockRejectedValueOnce(new ApiError(0, 'Unable to reach QueenZone. Check your connection and try again.'))
      .mockResolvedValueOnce(newsDetailFixture({ title: 'Retried headline' }));
    renderStory();
    await waitFor(() => expect(screen.getByText('Unable to load')).toBeOnTheScreen());
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(screen.getByText('Retried headline')).toBeOnTheScreen());
  });

  it('keeps the loaded article when the aborted first fetch later fails as offline', async () => {
    const first = deferred<ReturnType<typeof newsDetailFixture>>();
    fetchDetail
      .mockReturnValueOnce(first.promise)
      .mockResolvedValueOnce(newsDetailFixture({ title: 'Kept headline' }));
    const navigation = fakeNavigation();
    const { rerender } = renderWithProviders(
      <NewsStoryScreen
        navigation={navigation as never}
        route={{ key: 'story', name: 'Story', params: { id: 1 } } as never}
      />,
      { navigation: false },
    );
    rerender(
      <NewsStoryScreen
        navigation={navigation as never}
        route={{ key: 'story', name: 'Story', params: { id: 2 } } as never}
      />,
    );
    await waitFor(() => expect(screen.getByText('Kept headline')).toBeOnTheScreen());

    await act(async () => {
      first.reject(new ApiError(0, 'Unable to reach QueenZone. Check your connection and try again.'));
    });
    expect(screen.getByText('Kept headline')).toBeOnTheScreen();
    expect(screen.queryByText('Unable to load')).toBeNull();
  });

  it('omits the discussion block when topicId is missing', async () => {
    fetchDetail.mockResolvedValue(
      newsDetailFixture({
        title: 'Legacy article without topic',
        discussionReplyCount: 2,
        discussionPreview: [
          { authorDisplayName: 'Should not render', postedAt: '2026-08-02T09:00:00.000Z', excerpt: 'Hidden excerpt' },
        ],
      }),
    );
    renderStory();
    await waitFor(() => expect(screen.getByText('Legacy article without topic')).toBeOnTheScreen());

    expect(screen.queryByTestId(testIds.newsStoryDiscussion)).toBeNull();
    expect(screen.queryByText('Start the discussion')).toBeNull();
    expect(screen.queryByText('Join the discussion')).toBeNull();
    expect(screen.queryByText('Should not render')).toBeNull();
    expect(screen.queryByText('Hidden excerpt')).toBeNull();
  });

  it('renders Start the discussion when the linked topic has no replies', async () => {
    fetchDetail.mockResolvedValue(
      newsDetailFixture({
        title: 'Article awaiting replies',
        topicId: 1002,
        discussionReplyCount: 0,
        discussionPreview: [],
      }),
    );
    const { navigation } = renderStory();
    await waitFor(() => expect(screen.getByText('Start the discussion')).toBeOnTheScreen());

    expect(screen.getByTestId(testIds.newsStoryDiscussion)).toBeOnTheScreen();
    expect(screen.queryByText('Join the discussion')).toBeNull();
    expect(screen.queryByText('1 reply')).toBeNull();

    const user = userEvent.setup();
    await user.press(screen.getByTestId(testIds.newsStoryDiscussionCta));
    expect(navigation.navigate).toHaveBeenCalledWith('ForumTab', {
      screen: 'Thread',
      params: { id: 1002, title: 'Article awaiting replies' },
      initial: false,
    });
  });

  it('renders preview and Join the discussion for a signed-out reader', async () => {
    fetchDetail.mockResolvedValue(
      newsDetailFixture({
        title: 'Article with replies',
        topicId: 1002,
        discussionReplyCount: 3,
        discussionPreview: [
          {
            authorDisplayName: 'Alice',
            postedAt: '2026-08-01T10:30:00.000Z',
            excerpt: 'First preview excerpt',
          },
          {
            authorDisplayName: 'Bob',
            postedAt: '2026-08-01T11:30:00.000Z',
            excerpt: 'Latest preview excerpt',
          },
        ],
      }),
    );
    const { navigation } = renderStory();
    await waitFor(() => expect(screen.getByText('Join the discussion')).toBeOnTheScreen());

    expect(screen.getByText('3 replies')).toBeOnTheScreen();
    expect(screen.getByText('Alice')).toBeOnTheScreen();
    expect(screen.getByText('First preview excerpt')).toBeOnTheScreen();
    expect(screen.getByText('Bob')).toBeOnTheScreen();
    expect(screen.getByText('Latest preview excerpt')).toBeOnTheScreen();
    expect(screen.queryByText('Start the discussion')).toBeNull();

    const user = userEvent.setup();
    await user.press(screen.getByLabelText('Open discussion from Alice'));
    expect(navigation.navigate).toHaveBeenCalledWith('ForumTab', {
      screen: 'Thread',
      params: { id: 1002, title: 'Article with replies' },
      initial: false,
    });
    expect(navigation.navigate).not.toHaveBeenCalledWith('Composer', expect.anything());
  });

  it('renders a single-reply preview with Join the discussion', async () => {
    fetchDetail.mockResolvedValue(
      newsDetailFixture({
        title: 'Article with one reply',
        topicId: 1002,
        discussionReplyCount: 1,
        discussionPreview: [
          {
            authorDisplayName: 'Only',
            postedAt: '2026-08-02T09:00:00.000Z',
            excerpt: 'Sole reply excerpt',
          },
        ],
      }),
    );
    renderStory();
    await waitFor(() => expect(screen.getByText('Join the discussion')).toBeOnTheScreen());

    expect(screen.getByText('1 reply')).toBeOnTheScreen();
    expect(screen.getByText('Only')).toBeOnTheScreen();
    expect(screen.getByText('Sole reply excerpt')).toBeOnTheScreen();
    expect(screen.queryByText('Start the discussion')).toBeNull();
  });

  it('opens a source URL from the news story', async () => {
    const youtube = 'https://www.youtube.com/watch?v=1GfZoSuG8WY';
    fetchDetail.mockResolvedValue(
      newsDetailFixture({
        title: 'Story with source',
        sourceUrl: youtube,
      }),
    );
    renderStory();
    await waitFor(() => expect(screen.getByLabelText('Open source')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByLabelText('Open source'));
    expect(Linking.openURL).toHaveBeenCalledWith(youtube);
    expect(Alert.alert).not.toHaveBeenCalled();
  });

  it('alerts when the news source URL cannot be opened and does not throw', async () => {
    const youtube = 'https://www.youtube.com/watch?v=1GfZoSuG8WY';
    fetchDetail.mockResolvedValue(
      newsDetailFixture({
        title: 'Story with source',
        sourceUrl: youtube,
      }),
    );
    jest.spyOn(Linking, 'openURL').mockRejectedValue(new Error(`Unable to open URL: ${youtube}`));
    renderStory();
    await waitFor(() => expect(screen.getByLabelText('Open source')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByLabelText('Open source'));
    await waitFor(() =>
      expect(Alert.alert).toHaveBeenCalledWith(openExternalUrlCopy.title, openExternalUrlCopy.body),
    );
  });
});
