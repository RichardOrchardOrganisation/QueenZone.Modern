import { Pressable, Text } from 'react-native';
import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { setAudioModeAsync } from 'expo-audio';
import { createMockSession } from '../test/mockSession';
import { fanPerformanceFixture } from '../test/fixtures';
import { renderWithProviders } from '../test/render';
import { lockScreenAlbumTitle, lockScreenMetadata, lockScreenOptions } from './lockScreen';
import { resolveLockScreenArtworkUrl } from './lockScreenArtwork';
import { createMemoryStorage } from '../cache/storage';
import { createMemoryDownloadHost, setDownloadFileHostForTests } from '../downloads/files';
import { getCompletedDownload, setDownloadManifestStorageForTests, upsertCompletedDownload } from '../downloads/manifest';
import { OFFLINE_PLAYBACK_MESSAGE } from '../downloads/messages';
import { resetDownloadUiForTests } from '../downloads/uiState';
import { FanPerformancePlayerProvider, useFanPerformancePlayer } from './FanPerformancePlayer';

const bundledArtworkUrl = 'file:///app/assets/icon.png';

jest.mock('./lockScreenArtwork', () => ({
  resolveLockScreenArtworkUrl: jest.fn(async () => 'file:///app/assets/icon.png'),
}));

const mockPlayer = {
  replace: jest.fn(),
  play: jest.fn(),
  pause: jest.fn(),
  seekTo: jest.fn(),
  setActiveForLockScreen: jest.fn(),
  clearLockScreenControls: jest.fn(),
};

const mockStatus = {
  playing: false,
  currentTime: 0,
  duration: 0,
  isLoaded: true,
  isBuffering: false,
  didJustFinish: false,
  error: null as string | null,
};

const mockSession = createMockSession();

jest.mock('expo-audio', () => ({
  setAudioModeAsync: jest.fn(() => Promise.resolve()),
  useAudioPlayer: () => mockPlayer,
  useAudioPlayerStatus: () => ({ ...mockStatus }),
}));

jest.mock('../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../config', () => ({
  apiV1Url: (path: string) => `http://qz.test/api/v1${path.startsWith('/') ? path : `/${path}`}`,
}));

const trackA = fanPerformanceFixture();
const trackB = fanPerformanceFixture({
  id: 188,
  title: 'Radio Ga Ga',
  performedBy: 'Sam',
  detailPath: '/fan-performances/188',
  audioPath: '/api/v1/content/fan-performances/188/audio',
});

function Probe() {
  const player = useFanPerformancePlayer();
  return (
    <>
      <Text testID="current">{player.current?.title ?? 'none'}</Text>
      <Text testID="error">{player.error ?? 'no-error'}</Text>
      <Text testID="queue">{String(player.queue.length)}</Text>
      <Pressable testID="play-a" onPress={() => player.play(trackA, [trackA, trackB])}>
        <Text>play-a</Text>
      </Pressable>
      <Pressable testID="play-last" onPress={() => player.play(trackB, [trackA, trackB])}>
        <Text>play-last</Text>
      </Pressable>
      <Pressable testID="toggle" onPress={() => player.toggle()}>
        <Text>toggle</Text>
      </Pressable>
      <Pressable testID="next" onPress={() => player.playNext()}>
        <Text>next</Text>
      </Pressable>
      <Pressable testID="previous" onPress={() => player.playPrevious()}>
        <Text>previous</Text>
      </Pressable>
      <Pressable testID="seek" onPress={() => player.seekTo(12)}>
        <Text>seek</Text>
      </Pressable>
      <Pressable testID="seek-low" onPress={() => player.seekTo(-8)}>
        <Text>seek-low</Text>
      </Pressable>
      <Pressable testID="seek-high" onPress={() => player.seekTo(999)}>
        <Text>seek-high</Text>
      </Pressable>
      <Pressable testID="skip" onPress={() => player.skip(15)}>
        <Text>skip</Text>
      </Pressable>
    </>
  );
}

async function renderPlayer() {
  return await renderWithProviders(
    <FanPerformancePlayerProvider>
      <Probe />
    </FanPerformancePlayerProvider>,
    { navigation: false },
  );
}

describe('FanPerformancePlayerProvider', () => {
  beforeEach(() => {
    mockSession.accessToken = 'member-token';
    mockSession.profile = { memberId: 'member-1' } as never;
    mockSession.ensureAccessToken.mockImplementation(async () => mockSession.accessToken);
    setDownloadManifestStorageForTests(createMemoryStorage());
    setDownloadFileHostForTests(createMemoryDownloadHost());
    resetDownloadUiForTests();
    mockStatus.playing = false;
    mockStatus.didJustFinish = false;
    mockStatus.currentTime = 0;
    mockStatus.duration = 0;
    mockStatus.error = null;
    mockPlayer.replace.mockReset();
    mockPlayer.play.mockReset();
    mockPlayer.pause.mockReset();
    mockPlayer.setActiveForLockScreen.mockReset();
    mockPlayer.clearLockScreenControls.mockReset();
  });

  it('configures the native session for background playback', async () => {
    await renderPlayer();
    expect(setAudioModeAsync).toHaveBeenCalledWith(
      expect.objectContaining({
        playsInSilentMode: true,
        shouldPlayInBackground: true,
        interruptionMode: 'doNotMix',
      }),
    );
  });

  it('activates lock-screen metadata before play', async () => {
    const user = userEvent.setup();
    const order: string[] = [];
    mockPlayer.setActiveForLockScreen.mockImplementation(() => {
      order.push('lock');
    });
    mockPlayer.play.mockImplementation(() => {
      order.push('play');
    });
    await renderPlayer();
    await user.press(screen.getByTestId('play-a'));

    await waitFor(() => expect(mockPlayer.replace).toHaveBeenCalledWith({
      uri: 'http://qz.test/api/v1/content/fan-performances/187/audio',
      headers: { Authorization: 'Bearer member-token' },
      name: trackA.title,
    }));
    expect(mockPlayer.setActiveForLockScreen).toHaveBeenCalledWith(
      true,
      lockScreenMetadata(trackA, bundledArtworkUrl),
      { ...lockScreenOptions },
    );
    expect(order).toEqual(['lock', 'play']);
    expect(screen.getByTestId('current')).toHaveTextContent(trackA.title);
    expect(JSON.stringify(mockPlayer.setActiveForLockScreen.mock.calls[0])).not.toContain('member-token');
    expect(JSON.stringify(mockPlayer.setActiveForLockScreen.mock.calls[0])).not.toContain('http');
    expect(JSON.stringify(mockPlayer.setActiveForLockScreen.mock.calls[0])).not.toContain('blob:');
    expect(lockScreenMetadata(trackA, bundledArtworkUrl).albumTitle).toBe(lockScreenAlbumTitle);
    expect(lockScreenMetadata(trackA, bundledArtworkUrl).artworkUrl).toBe(bundledArtworkUrl);
  });

  it('plays without artwork when the bundled icon cannot be resolved', async () => {
    (resolveLockScreenArtworkUrl as jest.Mock).mockRejectedValueOnce(new Error('asset missing'));
    const user = userEvent.setup();
    await renderPlayer();
    await user.press(screen.getByTestId('play-a'));

    await waitFor(() =>
      expect(mockPlayer.setActiveForLockScreen).toHaveBeenCalledWith(
        true,
        lockScreenMetadata(trackA),
        { ...lockScreenOptions },
      ),
    );
    expect(mockPlayer.play).toHaveBeenCalled();
    expect(JSON.stringify(mockPlayer.setActiveForLockScreen.mock.calls[0])).not.toContain('artworkUrl');
  });

  it('refuses to load without a member token', async () => {
    mockSession.accessToken = null;
    const user = userEvent.setup();
    await renderPlayer();
    await user.press(screen.getByTestId('play-a'));
    await waitFor(() =>
      expect(screen.getByTestId('error')).toHaveTextContent(
        'Sign in with a member account to stream recordings.',
      ),
    );
    expect(mockSession.ensureAccessToken).toHaveBeenCalled();
    expect(mockPlayer.replace).not.toHaveBeenCalled();
    expect(mockPlayer.setActiveForLockScreen).not.toHaveBeenCalled();
  });

  it('plays with a token refreshed after the stored access token expired', async () => {
    mockSession.accessToken = 'expired-token';
    mockSession.ensureAccessToken.mockResolvedValue('fresh-token');
    const user = userEvent.setup();
    await renderPlayer();
    await user.press(screen.getByTestId('play-a'));
    await waitFor(() =>
      expect(mockPlayer.replace).toHaveBeenCalledWith(
        expect.objectContaining({
          headers: { Authorization: 'Bearer fresh-token' },
        }),
      ),
    );
  });

  it('advances lock-screen metadata to the next queued track', async () => {
    const user = userEvent.setup();
    const { rerender } = await renderPlayer();
    await user.press(screen.getByTestId('play-a'));
    await waitFor(() => expect(screen.getByTestId('current')).toHaveTextContent(trackA.title));
    mockPlayer.setActiveForLockScreen.mockClear();
    mockStatus.didJustFinish = true;
    await rerender(
      <FanPerformancePlayerProvider>
        <Probe />
      </FanPerformancePlayerProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('current')).toHaveTextContent(trackB.title));
    expect(mockPlayer.setActiveForLockScreen).toHaveBeenCalledWith(
      true,
      lockScreenMetadata(trackB, bundledArtworkUrl),
      { ...lockScreenOptions },
    );
    expect(mockPlayer.clearLockScreenControls).not.toHaveBeenCalled();
  });

  it('clears lock-screen controls when the queue ends', async () => {
    const user = userEvent.setup();
    const { rerender } = await renderPlayer();
    await user.press(screen.getByTestId('play-last'));
    await waitFor(() => expect(screen.getByTestId('current')).toHaveTextContent(trackB.title));
    mockStatus.didJustFinish = true;
    await rerender(
      <FanPerformancePlayerProvider>
        <Probe />
      </FanPerformancePlayerProvider>,
    );
    expect(mockPlayer.pause).toHaveBeenCalled();
    expect(mockPlayer.clearLockScreenControls).toHaveBeenCalled();
    expect(screen.getByTestId('current')).toHaveTextContent('none');
  });

  it('stops playback and clears controls on sign-out', async () => {
    const user = userEvent.setup();
    const { rerender } = await renderPlayer();
    await user.press(screen.getByTestId('play-a'));
    await waitFor(() => expect(screen.getByTestId('current')).toHaveTextContent(trackA.title));
    mockSession.accessToken = null;
    await rerender(
      <FanPerformancePlayerProvider>
        <Probe />
      </FanPerformancePlayerProvider>,
    );
    expect(mockPlayer.pause).toHaveBeenCalled();
    expect(mockPlayer.clearLockScreenControls).toHaveBeenCalled();
    expect(screen.getByTestId('current')).toHaveTextContent('none');
    expect(screen.getByTestId('queue')).toHaveTextContent('0');
  });

  it('pauses the same player from in-app toggle', async () => {
    const user = userEvent.setup();
    mockStatus.playing = true;
    const { rerender } = await renderPlayer();
    await user.press(screen.getByTestId('play-a'));
    await waitFor(() => expect(screen.getByTestId('current')).toHaveTextContent(trackA.title));
    await rerender(
      <FanPerformancePlayerProvider>
        <Probe />
      </FanPerformancePlayerProvider>,
    );
    await user.press(screen.getByTestId('toggle'));
    expect(mockPlayer.pause).toHaveBeenCalled();
  });

  it('resumes, seeks, skips, and steps the in-app queue', async () => {
    const user = userEvent.setup();
    mockStatus.playing = false;
    mockStatus.currentTime = 40;
    mockStatus.duration = 320;
    const { rerender } = await renderPlayer();
    await user.press(screen.getByTestId('play-last'));
    await waitFor(() => expect(screen.getByTestId('current')).toHaveTextContent(trackB.title));
    await rerender(
      <FanPerformancePlayerProvider>
        <Probe />
      </FanPerformancePlayerProvider>,
    );
    await user.press(screen.getByTestId('toggle'));
    await user.press(screen.getByTestId('seek'));
    await user.press(screen.getByTestId('skip'));
    await user.press(screen.getByTestId('previous'));

    expect(mockPlayer.play).toHaveBeenCalled();
    expect(mockPlayer.seekTo).toHaveBeenCalledWith(12);
    expect(mockPlayer.seekTo).toHaveBeenCalledWith(55);
    await waitFor(() => expect(screen.getByTestId('current')).toHaveTextContent(trackA.title));
  });

  it('clamps seek to the loaded duration', async () => {
    const user = userEvent.setup();
    mockStatus.currentTime = 40;
    mockStatus.duration = 320;
    const { rerender } = await renderPlayer();
    await user.press(screen.getByTestId('play-a'));
    await waitFor(() => expect(screen.getByTestId('current')).toHaveTextContent(trackA.title));
    await rerender(
      <FanPerformancePlayerProvider>
        <Probe />
      </FanPerformancePlayerProvider>,
    );
    await user.press(screen.getByTestId('seek-low'));
    await user.press(screen.getByTestId('seek-high'));
    expect(mockPlayer.seekTo).toHaveBeenCalledWith(0);
    expect(mockPlayer.seekTo).toHaveBeenCalledWith(320);
  });

  it('clears lock-screen controls on unmount', async () => {
    const user = userEvent.setup();
    const view = await renderPlayer();
    await user.press(screen.getByTestId('play-a'));
    await waitFor(() => expect(screen.getByTestId('current')).toHaveTextContent(trackA.title));
    mockPlayer.clearLockScreenControls.mockClear();
    await view.unmount();
    expect(mockPlayer.clearLockScreenControls).toHaveBeenCalled();
  });

  it('plays a same-member local file without a token refresh', async () => {
    const host = createMemoryDownloadHost();
    setDownloadFileHostForTests(host);
    host.files.set('file:///documents/fan-performances/187', new Uint8Array([1, 2, 3, 4]));
    await upsertCompletedDownload({
      performanceId: '187',
      localUri: 'file:///documents/fan-performances/187',
      title: trackA.title,
      performedBy: trackA.performedBy,
      byteSize: 4,
      sourceRevision: '"etag-1"',
      completedAt: '2026-09-05T00:00:00.000Z',
      memberId: 'member-1',
    });
    mockSession.ensureAccessToken.mockClear();
    const user = userEvent.setup();
    await renderPlayer();
    await user.press(screen.getByTestId('play-a'));
    await waitFor(() =>
      expect(mockPlayer.replace).toHaveBeenCalledWith({
        uri: 'file:///documents/fan-performances/187',
        name: trackA.title,
      }),
    );
    expect(mockSession.ensureAccessToken).not.toHaveBeenCalled();
    expect(JSON.stringify(mockPlayer.replace.mock.calls[0])).not.toContain('Bearer');
  });

  it('shows a playback error when a stream replace fails', async () => {
    mockPlayer.replace.mockImplementationOnce(() => {
      throw new Error('decoder failed');
    });
    const user = userEvent.setup();
    await renderPlayer();
    await user.press(screen.getByTestId('play-a'));
    await waitFor(() => expect(screen.getByTestId('error')).toHaveTextContent('Could not start playback.'));
  });

  it('streams the offline error after a corrupt local file is discarded', async () => {
    const host = createMemoryDownloadHost();
    setDownloadFileHostForTests(host);
    host.files.set('file:///documents/fan-performances/187', new Uint8Array([1, 2, 3, 4]));
    await upsertCompletedDownload({
      performanceId: '187',
      localUri: 'file:///documents/fan-performances/187',
      title: trackA.title,
      performedBy: trackA.performedBy,
      byteSize: 4,
      sourceRevision: '"etag-1"',
      completedAt: '2026-09-05T00:00:00.000Z',
      memberId: 'member-1',
    });
    mockPlayer.replace.mockImplementationOnce(() => {
      throw new Error('corrupt local file');
    });
    const { getNetworkStateAsync } = jest.requireMock('expo-network') as {
      getNetworkStateAsync: jest.Mock;
    };
    getNetworkStateAsync.mockResolvedValueOnce({ isConnected: true, isInternetReachable: true });
    getNetworkStateAsync.mockResolvedValueOnce({ isConnected: false, isInternetReachable: false });
    const user = userEvent.setup();
    await renderPlayer();
    await user.press(screen.getByTestId('play-a'));
    await waitFor(() => expect(screen.getByTestId('error')).toHaveTextContent(OFFLINE_PLAYBACK_MESSAGE));
  });

  it('falls back to streaming when local replace fails and clears the bad file', async () => {
    const host = createMemoryDownloadHost();
    setDownloadFileHostForTests(host);
    host.files.set('file:///documents/fan-performances/187', new Uint8Array([1, 2, 3, 4]));
    await upsertCompletedDownload({
      performanceId: '187',
      localUri: 'file:///documents/fan-performances/187',
      title: trackA.title,
      performedBy: trackA.performedBy,
      byteSize: 4,
      sourceRevision: '"etag-1"',
      completedAt: '2026-09-05T00:00:00.000Z',
      memberId: 'member-1',
    });
    mockPlayer.replace.mockImplementationOnce(() => {
      throw new Error('corrupt local file');
    });
    const user = userEvent.setup();
    await renderPlayer();
    await user.press(screen.getByTestId('play-a'));
    await waitFor(() =>
      expect(mockPlayer.replace).toHaveBeenCalledWith({
        uri: 'http://qz.test/api/v1/content/fan-performances/187/audio',
        headers: { Authorization: 'Bearer member-token' },
        name: trackA.title,
      }),
    );
    expect(await getCompletedDownload('member-1', '187')).toBeNull();
    expect(host.exists('file:///documents/fan-performances/187')).toBe(false);
  });

  it('falls back to streaming when iOS reports an asynchronous local playback error', async () => {
    const host = createMemoryDownloadHost();
    setDownloadFileHostForTests(host);
    host.files.set('file:///documents/fan-performances/187.mp3', new Uint8Array([1, 2, 3, 4]));
    await upsertCompletedDownload({
      performanceId: '187',
      localUri: 'file:///documents/fan-performances/187.mp3',
      title: trackA.title,
      performedBy: trackA.performedBy,
      byteSize: 4,
      sourceRevision: '"etag-1"',
      completedAt: '2026-09-05T00:00:00.000Z',
      memberId: 'member-1',
    });
    const user = userEvent.setup();
    const view = await renderPlayer();
    await user.press(screen.getByTestId('play-a'));
    await waitFor(() =>
      expect(mockPlayer.replace).toHaveBeenCalledWith({
        uri: 'file:///documents/fan-performances/187.mp3',
        name: trackA.title,
      }),
    );

    mockPlayer.replace.mockClear();
    mockStatus.error = 'The operation could not be completed';
    await view.rerender(
      <FanPerformancePlayerProvider>
        <Probe />
      </FanPerformancePlayerProvider>,
    );

    await waitFor(() =>
      expect(mockPlayer.replace).toHaveBeenCalledWith({
        uri: 'http://qz.test/api/v1/content/fan-performances/187/audio',
        headers: { Authorization: 'Bearer member-token' },
        name: trackA.title,
      }),
    );
    expect(await getCompletedDownload('member-1', '187')).toBeNull();
    expect(host.exists('file:///documents/fan-performances/187.mp3')).toBe(false);
  });

  it('shows an offline-specific error when the recording is not downloaded', async () => {
    const { getNetworkStateAsync } = jest.requireMock('expo-network') as {
      getNetworkStateAsync: jest.Mock;
    };
    getNetworkStateAsync.mockResolvedValueOnce({ isConnected: false, isInternetReachable: false });
    const user = userEvent.setup();
    await renderPlayer();
    await user.press(screen.getByTestId('play-a'));
    await waitFor(() => expect(screen.getByTestId('error')).toHaveTextContent(OFFLINE_PLAYBACK_MESSAGE));
    expect(mockPlayer.replace).not.toHaveBeenCalled();
  });

  it('throws outside the provider', async () => {
    await expect(renderWithProviders(<Probe />, { navigation: false })).rejects.toThrow(
      'useFanPerformancePlayer must be used inside FanPerformancePlayerProvider',
    );
  });
});
