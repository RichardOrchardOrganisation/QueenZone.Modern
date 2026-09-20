import { Alert, Linking } from 'react-native';
import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchArticleDetail, fetchNewsDetail } from '../../api';
import { ApiError } from '../../api/client';
import { articleDetailFixture } from '../../test/fixtures';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { openExternalUrlCopy } from '../../ui/openExternalUrl';
import { StoryScreen } from './StoryScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchArticleDetail: jest.fn(),
    fetchNewsDetail: jest.fn(),
  };
});

const fetchDetail = fetchArticleDetail as jest.MockedFunction<typeof fetchArticleDetail>;
const fetchNews = fetchNewsDetail as jest.MockedFunction<typeof fetchNewsDetail>;

function lastHeaderOptions(navigation: ReturnType<typeof fakeNavigation>) {
  const calls = navigation.setOptions.mock.calls;
  expect(calls.length).toBeGreaterThan(0);
  return calls[calls.length - 1]?.[0] as { title?: string };
}

function renderStory(navigation = fakeNavigation(), id = 101) {
  return {
    navigation,
    ...renderWithProviders(
      <StoryScreen
        navigation={navigation as never}
        route={{ key: 'story', name: 'Story', params: { id } } as never}
      />,
      { navigation: false },
    ),
  };
}

describe('StoryScreen', () => {
  beforeEach(() => {
    fetchDetail.mockReset();
    fetchNews.mockReset();
    fetchDetail.mockResolvedValue(articleDetailFixture());
    jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
    jest.spyOn(Alert, 'alert');
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('loads the article by id and does not call fetchNewsDetail', async () => {
    const { navigation } = renderStory(fakeNavigation(), 101);
    await waitFor(() => expect(screen.getByTestId(testIds.articleStoryScreen)).toBeOnTheScreen());

    expect(fetchDetail).toHaveBeenCalledWith(101, expect.any(AbortSignal));
    expect(fetchNews).not.toHaveBeenCalled();
    expect(screen.getByText('Inside the Making of Bohemian Rhapsody')).toBeOnTheScreen();
    expect(screen.getByText('Recording')).toBeOnTheScreen();
    expect(screen.getByText('Queenzone archive')).toBeOnTheScreen();
    expect(screen.queryByText('Discussion')).toBeNull();
    expect(lastHeaderOptions(navigation).title).toBe('Inside the Making of Bohemian Rhapsody');
  });

  it('opens a safe source URL and renders plain-text sources as text', async () => {
    fetchDetail.mockResolvedValue(
      articleDetailFixture({
        source: 'https://www.queenzone.org/articles/101/inside-the-making-of-bohemian-rhapsody',
      }),
    );
    renderStory();
    await waitFor(() => expect(screen.getByLabelText('Open source')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByLabelText('Open source'));
    expect(Linking.openURL).toHaveBeenCalledWith(
      'https://www.queenzone.org/articles/101/inside-the-making-of-bohemian-rhapsody',
    );
    expect(screen.queryByText('Queenzone archive')).toBeNull();
    expect(Alert.alert).not.toHaveBeenCalled();
  });

  it('alerts when a YouTube source URL cannot be opened and does not throw', async () => {
    const youtube = 'https://www.youtube.com/watch?v=1GfZoSuG8WY';
    fetchDetail.mockResolvedValue(articleDetailFixture({ source: youtube }));
    jest.spyOn(Linking, 'openURL').mockRejectedValue(new Error(`Unable to open URL: ${youtube}`));
    renderStory();
    await waitFor(() => expect(screen.getByLabelText('Open source')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByLabelText('Open source'));
    await waitFor(() =>
      expect(Alert.alert).toHaveBeenCalledWith(openExternalUrlCopy.title, openExternalUrlCopy.body),
    );
    expect(Linking.openURL).toHaveBeenCalledWith(youtube);
  });

  it('uses Articles as the eyebrow when category is missing', async () => {
    fetchDetail.mockResolvedValue(articleDetailFixture({ categoryName: null, source: null }));
    renderStory();
    await waitFor(() => expect(screen.getByTestId(testIds.articleStoryScreen)).toBeOnTheScreen());
    expect(screen.getByText('Articles')).toBeOnTheScreen();
  });

  it('shows an error and retries', async () => {
    fetchDetail
      .mockRejectedValueOnce(
        new ApiError(404, 'No published article with id \'424242\'.'),
      )
      .mockResolvedValueOnce(articleDetailFixture({ title: 'Retried archive feature' }));
    renderStory();
    await waitFor(() => expect(screen.getByText('Unable to load')).toBeOnTheScreen());
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(screen.getByText('Retried archive feature')).toBeOnTheScreen());
  });
});
