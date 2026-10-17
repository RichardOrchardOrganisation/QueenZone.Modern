import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchAllFanPerformances, fetchFanPerformancesPage } from '../../api';
import { fanPerformanceFixture, pagedResponse } from '../../test/fixtures';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { FanPerformancesScreen, shuffleFanPerformances } from './FanPerformancesScreen';

const mockSession = createMockSession();
const mockPlayer = {
  current: null as { id: number } | null,
  queue: [],
  error: null,
  playing: false,
  currentTime: 0,
  duration: 0,
  isLoaded: false,
  isBuffering: false,
  play: jest.fn(),
  toggle: jest.fn(),
  seekTo: jest.fn(),
  skip: jest.fn(),
  playNext: jest.fn(),
  playPrevious: jest.fn(),
};

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchFanPerformancesPage: jest.fn(),
    fetchAllFanPerformances: jest.fn(),
  };
});

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../../audio/FanPerformancePlayer', () => ({
  useFanPerformancePlayer: () => mockPlayer,
}));

const fetchPage = fetchFanPerformancesPage as jest.MockedFunction<typeof fetchFanPerformancesPage>;
const fetchAll = fetchAllFanPerformances as jest.MockedFunction<typeof fetchAllFanPerformances>;
const track = fanPerformanceFixture();
const catalogSignIn = expect.objectContaining({
  type: 'NAVIGATE',
  payload: expect.objectContaining({
    name: 'SignIn',
    params: { returnTo: { tab: 'ArchiveTab', screen: 'FanPerformances' } },
  }),
});

async function renderList() {
  const navigation = fakeNavigation();
  return {
    navigation,
    ...(await renderWithProviders(
      <FanPerformancesScreen
        navigation={navigation as never}
        route={{ key: 'list', name: 'FanPerformances' } as never}
      />,
      { navigation: false },
    )),
  };
}

describe('FanPerformancesScreen', () => {
  beforeEach(() => {
    mockSession.accessToken = null;
    mockSession.isRestoring = false;
    mockPlayer.current = null;
    mockPlayer.playing = false;
    mockPlayer.play.mockReset();
    mockPlayer.toggle.mockReset();
    mockPlayer.playNext.mockReset();
    fetchPage.mockResolvedValue(pagedResponse([track]));
    fetchAll.mockReset();
    fetchAll.mockResolvedValue([track]);
  });

  afterEach(async () => {
    jest.restoreAllMocks();
    await flushVirtualizedList();
  });

  it('shows submitted-by credit and opens the pick-file submit screen when signed in', async () => {
    mockSession.accessToken = 'member-token';
    fetchPage.mockResolvedValue(
      pagedResponse([fanPerformanceFixture({ contributorDisplayName: 'Stage Fan' })]),
    );
    const { navigation } = await renderList();
    await waitFor(() => expect(screen.getByText(/Submitted by Stage Fan/)).toBeOnTheScreen());
    await userEvent.setup().press(screen.getByTestId(testIds.fanPerformanceSubmit));
    expect(navigation.navigate).toHaveBeenCalledWith('FanPerformanceSubmit');
  });

  it('opens detail from the title and sends unsigned visitors to sign in from play', async () => {
    const { navigation } = await renderList();
    await waitFor(() => expect(screen.getByText(track.title)).toBeOnTheScreen());

    expect(screen.getByText('Sign in to play')).toBeOnTheScreen();
    const user = userEvent.setup();
    await user.press(screen.getByTestId(`${testIds.fanPerformanceRowPrefix}${track.id}`));
    expect(navigation.navigate).toHaveBeenCalledWith('FanPerformanceDetail', { id: track.id });
    expect(mockPlayer.play).not.toHaveBeenCalled();

    await user.press(screen.getByRole('button', { name: `Sign in to play ${track.title}` }));
    expect(navigation.dispatch).toHaveBeenCalledWith(catalogSignIn);
    expect(navigation.navigate).toHaveBeenCalledTimes(1);
  });

  it('sends unsigned visitors to sign in from Play All and Shuffle Play All', async () => {
    const { navigation } = await renderList();
    await waitFor(() => expect(screen.getByTestId(testIds.fanPerformancesPlayAll)).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByTestId(testIds.fanPerformancesPlayAll));
    expect(navigation.dispatch).toHaveBeenCalledWith(catalogSignIn);
    expect(fetchAll).not.toHaveBeenCalled();
    expect(mockPlayer.play).not.toHaveBeenCalled();

    await user.press(screen.getByTestId(testIds.fanPerformancesShufflePlayAll));
    expect(navigation.dispatch).toHaveBeenCalledTimes(2);
    expect(fetchAll).not.toHaveBeenCalled();
    expect(mockPlayer.play).not.toHaveBeenCalled();
  });

  it('does not open sign-in from play while a session is restoring', async () => {
    mockSession.isRestoring = true;
    const { navigation } = await renderList();
    await waitFor(() => expect(screen.getByText(track.title)).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByTestId(`${testIds.fanPerformancePlayPrefix}${track.id}`));
    expect(navigation.dispatch).not.toHaveBeenCalled();
    expect(mockPlayer.play).not.toHaveBeenCalled();
  });

  it('plays from the list when a member is signed in and still opens detail from the title', async () => {
    mockSession.accessToken = 'member-token';
    const { navigation } = await renderList();
    await waitFor(() => expect(screen.getByRole('button', { name: `Play ${track.title}` })).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: `Play ${track.title}` }));
    await waitFor(() => expect(mockPlayer.play).toHaveBeenCalledWith(track, [track]));
    expect(navigation.navigate).not.toHaveBeenCalled();

    await user.press(screen.getByRole('button', { name: `Open ${track.title}` }));
    expect(navigation.navigate).toHaveBeenCalledWith('FanPerformanceDetail', { id: track.id });
  });

  it('Play All queues the full catalog, not the loaded FlatList page', async () => {
    mockSession.accessToken = 'member-token';
    const first = fanPerformanceFixture({ id: 1, title: 'First' });
    const second = fanPerformanceFixture({ id: 2, title: 'Second' });
    const third = fanPerformanceFixture({ id: 3, title: 'Third' });
    fetchPage.mockResolvedValue(pagedResponse([first], 1, 2));
    fetchAll.mockResolvedValue([first, second, third]);
    await renderList();
    await waitFor(() => expect(screen.getByTestId(testIds.fanPerformancesPlayAll)).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByTestId(testIds.fanPerformancesPlayAll));
    await waitFor(() => expect(mockPlayer.play).toHaveBeenCalledTimes(1));
    expect(mockPlayer.play).toHaveBeenCalledWith(first, [first, second, third]);
    expect(fetchAll).toHaveBeenCalledTimes(1);
  });

  it('Shuffle Play All randomizes once per tap and passes that order as the queue', async () => {
    mockSession.accessToken = 'member-token';
    const first = fanPerformanceFixture({ id: 1, title: 'First' });
    const second = fanPerformanceFixture({ id: 2, title: 'Second' });
    const third = fanPerformanceFixture({ id: 3, title: 'Third' });
    const catalog = [first, second, third];
    fetchPage.mockResolvedValue(pagedResponse([first], 1, 2));
    fetchAll.mockResolvedValue(catalog);
    const random = jest.spyOn(Math, 'random').mockReturnValue(0);
    await renderList();
    await waitFor(() => expect(screen.getByTestId(testIds.fanPerformancesShufflePlayAll)).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByTestId(testIds.fanPerformancesShufflePlayAll));
    const shuffledOnce = shuffleFanPerformances(catalog, () => 0);
    await waitFor(() => expect(mockPlayer.play).toHaveBeenCalledTimes(1));
    expect(mockPlayer.play).toHaveBeenCalledWith(shuffledOnce[0], shuffledOnce);
    expect(mockPlayer.playNext).not.toHaveBeenCalled();

    random.mockReturnValue(0.99);
    await user.press(screen.getByTestId(testIds.fanPerformancesShufflePlayAll));
    const shuffledAgain = shuffleFanPerformances(catalog, () => 0.99);
    await waitFor(() => expect(mockPlayer.play).toHaveBeenCalledTimes(2));
    expect(mockPlayer.play.mock.calls[1]?.[1]).toEqual(shuffledAgain);
    expect(shuffledAgain.map((item) => item.id)).not.toEqual(shuffledOnce.map((item) => item.id));
    random.mockRestore();
  });

  it('opens Downloads from the listing when signed in', async () => {
    mockSession.accessToken = 'member-token';
    const { navigation } = await renderList();
    await waitFor(() => expect(screen.getByTestId(testIds.fanPerformanceDownloads)).toBeOnTheScreen());
    await userEvent.setup().press(screen.getByTestId(testIds.fanPerformanceDownloads));
    expect(navigation.navigate).toHaveBeenCalledWith('FanPerformanceDownloads');
  });

  it('toggles pause when the listed track is already playing', async () => {
    mockSession.accessToken = 'member-token';
    mockPlayer.current = { id: track.id };
    mockPlayer.playing = true;
    await renderList();
    await waitFor(() => expect(screen.getByRole('button', { name: `Pause ${track.title}` })).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: `Pause ${track.title}` }));
    expect(mockPlayer.toggle).toHaveBeenCalledTimes(1);
    expect(mockPlayer.play).not.toHaveBeenCalled();
  });
});
