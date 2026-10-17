import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchFanPerformanceDetail, fetchFanPerformancesPage } from '../../api';
import { ApiError } from '../../api/errors';
import { reportFanPerformance } from '../../api/fanPerformanceSubmissions';
import { fanPerformanceFixture, pagedResponse } from '../../test/fixtures';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { FanPerformanceDetailScreen } from './FanPerformanceDetailScreen';

const mockSession = createMockSession();
const mockPlayer = {
  current: null,
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
    fetchFanPerformanceDetail: jest.fn(),
    fetchFanPerformancesPage: jest.fn(),
  };
});

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../../api/fanPerformanceSubmissions', () => ({
  reportFanPerformance: jest.fn(),
}));

jest.mock('../../audio/FanPerformancePlayer', () => ({
  useFanPerformancePlayer: () => mockPlayer,
}));

const fetchDetail = fetchFanPerformanceDetail as jest.MockedFunction<typeof fetchFanPerformanceDetail>;
const fetchPage = fetchFanPerformancesPage as jest.MockedFunction<typeof fetchFanPerformancesPage>;
const track = fanPerformanceFixture();

async function renderDetail() {
  return await renderWithProviders(
    <FanPerformanceDetailScreen
      navigation={fakeNavigation() as never}
      route={{ key: 'detail', name: 'FanPerformanceDetail', params: { id: track.id } } as never}
    />,
    { navigation: false },
  );
}

describe('FanPerformanceDetailScreen', () => {
  beforeEach(() => {
    mockSession.accessToken = null;
    mockSession.isSignedIn = false;
    mockSession.isRestoring = false;
    fetchDetail.mockResolvedValue(track);
    fetchPage.mockResolvedValue(pagedResponse([track]));
    (reportFanPerformance as jest.Mock).mockReset();
  });

  it('does not ask for sign-in while a previous session is still restoring', async () => {
    mockSession.isRestoring = true;
    await renderDetail();
    await waitFor(() => expect(screen.getByText(track.title)).toBeOnTheScreen());
    expect(screen.getByTestId('fan-performance-session-restoring')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Sign in' })).toBeNull();
  });

  it('shows the sign-in prompt when restore finished without a member', async () => {
    await renderDetail();
    await waitFor(() => expect(screen.getByText(track.title)).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeOnTheScreen();
  });

  it('shows playback controls when a member token is present', async () => {
    mockSession.accessToken = 'member-token';
    mockSession.isSignedIn = true;
    await renderDetail();
    await waitFor(() => expect(screen.getByLabelText('Play')).toBeOnTheScreen());
    expect(screen.queryByRole('button', { name: 'Sign in' })).toBeNull();
    expect(screen.getByTestId(`${testIds.fanPerformanceDownloadPrefix}${track.id}`)).toBeOnTheScreen();
  });

  it('shows contributor credit and sends a report when signed in', async () => {
    mockSession.accessToken = 'member-token';
    mockSession.isSignedIn = true;
    fetchDetail.mockResolvedValue(
      fanPerformanceFixture({
        contributorMemberId: 'member-1',
        contributorDisplayName: 'Stage Fan',
      }),
    );
    (reportFanPerformance as jest.Mock).mockResolvedValue({ reportId: 'rep-1', alreadyReported: false });
    await renderDetail();
    await waitFor(() => expect(screen.getByText('Submitted by Stage Fan')).toBeOnTheScreen());
    expect(screen.getByTestId(testIds.fanPerformanceReport)).toBeOnTheScreen();

    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Report reason'), 'Not the performer.');
    await user.press(screen.getByTestId(testIds.fanPerformanceReportSend));
    await waitFor(() =>
      expect(screen.getByText('Thanks. The admin team will review this performance.')).toBeOnTheScreen(),
    );
    expect(reportFanPerformance).toHaveBeenCalledWith(track.id, 'Not the performer.', 'member-token');
  });

  it('requires a report reason and shows the already-reported confirmation', async () => {
    mockSession.accessToken = 'member-token';
    mockSession.isSignedIn = true;
    (reportFanPerformance as jest.Mock).mockResolvedValue({ reportId: 'rep-2', alreadyReported: true });
    await renderDetail();
    await waitFor(() => expect(screen.getByTestId(testIds.fanPerformanceReport)).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByTestId(testIds.fanPerformanceReportSend));
    expect(screen.getByText('A reason is required.')).toBeOnTheScreen();
    expect(reportFanPerformance).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText('Report reason'), 'Duplicate upload');
    await user.press(screen.getByTestId(testIds.fanPerformanceReportSend));
    await waitFor(() =>
      expect(screen.getByText('You have already reported this performance.')).toBeOnTheScreen(),
    );
  });

  it('shows a report API failure', async () => {
    mockSession.accessToken = 'member-token';
    mockSession.isSignedIn = true;
    (reportFanPerformance as jest.Mock).mockRejectedValue(ApiError.http(429, 'Too many reports.'));
    await renderDetail();
    await waitFor(() => expect(screen.getByTestId(testIds.fanPerformanceReport)).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Report reason'), 'Spam');
    await user.press(screen.getByTestId(testIds.fanPerformanceReportSend));
    await waitFor(() => expect(screen.getByText('Too many reports.')).toBeOnTheScreen());
  });

  it('does not show restoring copy when a stored session is already signed in', async () => {
    mockSession.isSignedIn = true;
    mockSession.isRestoring = false;
    mockSession.accessToken = 'expired-access';
    await renderDetail();
    await waitFor(() => expect(screen.getByLabelText('Play')).toBeOnTheScreen());
    expect(screen.queryByTestId('fan-performance-session-restoring')).toBeNull();
    expect(screen.queryByText('Restoring your session…')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Sign in' })).toBeNull();
  });
});
