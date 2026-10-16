import { act, screen, userEvent, waitFor } from '@testing-library/react-native';
import { createMemoryStorage } from '../cache/storage';
import { resetExternalStoreForTests } from '../cache/externalStore';
import { fanPerformanceFixture, memberProfileFixture } from '../test/fixtures';
import { createMockSession } from '../test/mockSession';
import { renderWithProviders } from '../test/render';
import { testIds } from '../test/testIds';
import { FanPerformanceDownloadsList } from './FanPerformanceDownloadsList';
import { setDownloadManifestStorageForTests } from './manifest';
import { resetDownloadManagerForTests } from './manager';
import { resetDownloadUiForTests, setDownloadUiSnapshot, snapshotFromEntry, transientSnapshot } from './uiState';
import { createMemoryDownloadHost, setDownloadFileHostForTests } from './files';

const mockSession = createMockSession();
const mockPlayer = {
  current: null as { id: number } | null,
  playing: false,
  play: jest.fn(),
  toggle: jest.fn(),
};
const track = fanPerformanceFixture();

jest.mock('../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../audio/FanPerformancePlayer', () => ({
  useFanPerformancePlayer: () => mockPlayer,
}));

describe('FanPerformanceDownloadsList', () => {
  beforeEach(() => {
    resetDownloadManagerForTests();
    resetDownloadUiForTests();
    resetExternalStoreForTests();
    setDownloadManifestStorageForTests(createMemoryStorage());
    setDownloadFileHostForTests(createMemoryDownloadHost());
    mockSession.profile = memberProfileFixture({ memberId: 'member-1' });
    mockSession.accessToken = 'member-token';
    mockPlayer.current = null;
    mockPlayer.playing = false;
    mockPlayer.play.mockReset();
    mockPlayer.toggle.mockReset();
    setDownloadUiSnapshot(
      'member-1',
      snapshotFromEntry({
        performanceId: String(track.id),
        localUri: 'file:///documents/fan-performances/187',
        title: track.title,
        performedBy: track.performedBy,
        byteSize: 4096,
        sourceRevision: '"etag-1"',
        completedAt: '2026-09-05T00:00:00.000Z',
        memberId: 'member-1',
      }),
    );
  });

  it('plays and removes a downloaded recording', async () => {
    await renderWithProviders(<FanPerformanceDownloadsList />, { navigation: false });
    expect(screen.getByText(track.title)).toBeOnTheScreen();
    expect(screen.getByText(/4.0 KB/)).toBeOnTheScreen();

    const user = userEvent.setup();
    await user.press(screen.getByTestId(`${testIds.fanPerformanceDownloadPlayPrefix}${track.id}`));
    expect(mockPlayer.play).toHaveBeenCalled();

    await user.press(screen.getByTestId(`${testIds.fanPerformanceDownloadRemovePrefix}${track.id}`));
    await waitFor(() => expect(screen.getByText('No downloaded recordings yet.')).toBeOnTheScreen());
  });

  it('lists in-progress and failed downloads with their status', async () => {
    setDownloadUiSnapshot(
      'member-1',
      transientSnapshot('190', 'queued', {
        title: 'Now I\'m Here',
        performedBy: 'Sam',
      }),
    );
    setDownloadUiSnapshot(
      'member-1',
      transientSnapshot('188', 'downloading', {
        title: 'Liar',
        performedBy: 'Sam',
        byteSize: 512,
        expectedBytes: 2048,
      }),
    );
    setDownloadUiSnapshot(
      'member-1',
      transientSnapshot('189', 'failed', {
        title: 'Father To Son',
        performedBy: 'Sam',
        error: 'The download timed out. Try again.',
      }),
    );

    await renderWithProviders(<FanPerformanceDownloadsList />, { navigation: false });
    expect(screen.getByText('Now I\'m Here')).toBeOnTheScreen();
    expect(screen.getByText(/Queued/)).toBeOnTheScreen();
    expect(screen.getByText('Liar')).toBeOnTheScreen();
    expect(screen.getByText(/Downloading · 25%/)).toBeOnTheScreen();
    expect(screen.getByText('Father To Son')).toBeOnTheScreen();
    expect(screen.getByTestId(`${testIds.fanPerformanceDownloadErrorPrefix}189`)).toHaveTextContent(
      'The download timed out. Try again.',
    );
  });

  it('keeps progress on the retried row and shows the full rate-limit error', async () => {
    setDownloadUiSnapshot(
      'member-1',
      transientSnapshot('191', 'downloaded', {
        title: 'Aaa First',
        performedBy: 'Ann',
        byteSize: 4096,
      }),
    );
    setDownloadUiSnapshot(
      'member-1',
      transientSnapshot('192', 'downloading', {
        title: 'Mmm Middle',
        performedBy: 'Mel',
        byteSize: 512,
        expectedBytes: 1024,
      }),
    );
    setDownloadUiSnapshot(
      'member-1',
      transientSnapshot('193', 'failed', {
        title: 'Zzz Last',
        performedBy: 'Zoe',
        error: 'Too many audio requests. Wait 5 minutes and try again.',
      }),
    );

    await renderWithProviders(<FanPerformanceDownloadsList />, { navigation: false });
    expect(screen.getByTestId(`${testIds.fanPerformanceDownloadProgressPrefix}191`)).toHaveTextContent(
      /Downloaded/,
    );
    expect(screen.getByTestId(`${testIds.fanPerformanceDownloadProgressPrefix}192`)).toHaveTextContent(
      'Performed by Mel · Downloading · 50%',
    );
    expect(screen.getByTestId(`${testIds.fanPerformanceDownloadErrorPrefix}193`)).toHaveTextContent(
      'Too many audio requests. Wait 5 minutes and try again.',
    );
    expect(screen.getByTestId(`${testIds.fanPerformanceDownloadProgressPrefix}191`)).not.toHaveTextContent(
      /Downloading/,
    );
  });

  it('binds live progress to the retried performance id, not the first row', async () => {
    setDownloadUiSnapshot(
      'member-1',
      transientSnapshot('191', 'failed', {
        title: 'Aaa First',
        performedBy: 'Ann',
        error: 'Could not download this recording. Try again.',
      }),
    );
    setDownloadUiSnapshot(
      'member-1',
      transientSnapshot('192', 'failed', {
        title: 'Mmm Middle',
        performedBy: 'Mel',
        error: 'Could not download this recording. Try again.',
      }),
    );
    setDownloadUiSnapshot(
      'member-1',
      transientSnapshot('193', 'failed', {
        title: 'Zzz Last',
        performedBy: 'Zoe',
        error: 'Could not download this recording. Try again.',
      }),
    );

    const view = await renderWithProviders(<FanPerformanceDownloadsList />, { navigation: false });

    await act(() => {
      setDownloadUiSnapshot(
        'member-1',
        transientSnapshot('193', 'downloading', {
          title: 'Zzz Last',
          performedBy: 'Zoe',
          byteSize: 400,
          expectedBytes: 1000,
        }),
      );
    });
    await view.rerender(<FanPerformanceDownloadsList />);

    await waitFor(() =>
      expect(screen.getByTestId(`${testIds.fanPerformanceDownloadProgressPrefix}193`)).toHaveTextContent(
        'Performed by Zoe · Downloading · 40%',
      ),
    );
    expect(screen.getByTestId(`${testIds.fanPerformanceDownloadProgressPrefix}191`)).not.toHaveTextContent(
      /Downloading|40%/,
    );
    expect(screen.getByTestId(`${testIds.fanPerformanceDownloadProgressPrefix}192`)).not.toHaveTextContent(
      /Downloading|40%/,
    );
  });
});
