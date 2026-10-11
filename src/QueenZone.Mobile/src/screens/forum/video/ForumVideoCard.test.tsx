import { useLayoutEffect, useState } from 'react';
import { act, fireEvent, screen } from '@testing-library/react-native';
import { Alert, Linking } from 'react-native';
import { renderWithProviders } from '../../../test/render';
import { ForumVideoCard } from './ForumVideoCard';
import { ForumPlayersContext } from './ForumPlayerProvider';
import { PlayerPool, type PoolSnapshot } from './playerPool';
import { playerBaseUrl, playerTimeoutMs } from './playerDocument';
import { testIds } from '../../../test/testIds';
const mockPause = jest.fn();
type ContextValue = NonNullable<ReturnType<typeof import('./ForumPlayerProvider').useForumPlayers>>;
let mockChanged: (value: PoolSnapshot) => void = () => {};
let mockPool: PlayerPool;
const mockHandles = new Map<string, { pause: () => void; measure: (top: number, bottom: number, valid: () => boolean) => void }>();
jest.mock('react-native-webview', () => {
  const React = jest.requireActual<typeof import('react')>('react');
  const { View } = jest.requireActual<typeof import('react-native')>('react-native');
  return { __esModule: true, default: React.forwardRef(function MockPlayer(props: Record<string, unknown>, ref) {
    React.useImperativeHandle(ref, () => ({ injectJavaScript: mockPause }));
    return React.createElement(View, props);
  }) };
});
const video = { provider: 'youtube' as const, videoId: 'M7lc1UVf-VE', startSeconds: null, watchUrl: 'https://www.youtube.com/watch?v=M7lc1UVf-VE', anchorIndex: 0 };
const attach = (key: string, handle: Parameters<ContextValue['attach']>[1]) => { mockHandles.set(key, handle); return () => { mockHandles.delete(key); }; };
function Harness() {
  const [snapshot, setSnapshot] = useState<PoolSnapshot>({});
  useLayoutEffect(() => { mockChanged = setSnapshot; }, []);
  return <ForumPlayersContext.Provider value={{ pool: mockPool, snapshot, attach, online: true, measure: jest.fn() }}><ForumVideoCard cardKey="post:0" video={video} originalHtml='<a href="https://youtu.be/M7lc1UVf-VE">Original link</a>' /></ForumPlayersContext.Provider>;
}
function mountPlayer() {
  fireEvent(screen.getByText('Original link').parent!, 'layout', { nativeEvent: { layout: { width: 320, height: 200 } } });
  // Fire the card viewport's layout, which is outside the HTML renderer.
  const views = screen.UNSAFE_getAllByType(requireView());
  const viewport = views.find(v => v.props.collapsable === false && v.props.onLayout);
  fireEvent(viewport!, 'layout', { nativeEvent: { layout: { width: 320, height: 200 } } });
  act(() => mockPool.measure('post:0', mockPool.cards['post:0'].owner, { top: 100, bottom: 300, width: 320, viewportTop: 0, viewportBottom: 800 }));
}
function requireView() { return jest.requireActual<typeof import('react-native')>('react-native').View; }
function send(event: string, value?: unknown, generation = mockPool.cards['post:0'].generation!) {
  fireEvent(screen.getByTestId(testIds.forumYoutubePlayer), 'message', { nativeEvent: { data: JSON.stringify({ version: 1, generation, event, ...(value === undefined ? {} : { value }) }) } });
}
describe('native forum video card', () => {
  beforeEach(() => {
    jest.useFakeTimers(); mockHandles.clear(); mockChanged = () => {};
    mockPool = new PlayerPool(value => mockChanged(value), key => mockHandles.get(key)?.pause());
    mockPool.setEnabled(true);
    jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined); jest.spyOn(Alert, 'alert');
  });
  afterEach(() => { jest.useRealTimers(); });
  it('stays a first-party placeholder without geometry, preserving original and external links', () => {
    renderWithProviders(<Harness />, { navigation: false });
    expect(screen.getByText('Original link')).toBeOnTheScreen();
    expect(screen.getByText(/shares your IP address/)).toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.forumYoutubePlayer)).toBeNull();
    fireEvent.press(screen.getByTestId(testIds.forumYoutubeWatch));
    expect(Linking.openURL).toHaveBeenCalledWith(video.watchUrl);
  });
  it('loads the app-generated document near view with secure WebView props, never play injection', () => {
    renderWithProviders(<Harness />, { navigation: false }); mountPlayer();
    const player = screen.getByTestId(testIds.forumYoutubePlayer);
    expect(player.props.source.baseUrl).toBe(playerBaseUrl);
    expect(player.props.source.html).not.toContain('Original link');
    expect(player.props.source).not.toHaveProperty('headers');
    expect(player.props).toMatchObject({ incognito: true, sharedCookiesEnabled: false, thirdPartyCookiesEnabled: false, allowFileAccess: false, mixedContentMode: 'never', mediaPlaybackRequiresUserAction: true, allowsInlineMediaPlayback: true, allowsFullscreenVideo: true, javaScriptCanOpenWindowsAutomatically: false });
    send('ready'); expect(screen.getByText(/Press the YouTube Play/)).toBeOnTheScreen();
    send('state', 1); expect(mockPool.cards['post:0'].playing).toBe(true);
    send('fullscreen', true); expect(mockPool.cards['post:0'].fullscreen).toBe(true);
    const viewport = screen.UNSAFE_getAllByType(requireView()).find(v => v.props.collapsable === false && v.props.onLayout)!;
    fireEvent(viewport, 'layout', { nativeEvent: { layout: { width: 0, height: 0 } } });
    expect(screen.getByTestId(testIds.forumYoutubePlayer)).toBeOnTheScreen();
    expect(mockPause).not.toHaveBeenCalled();
  });
  it('keeps 153 failure sticky until explicit Retry and ignores malformed/stale messages', () => {
    renderWithProviders(<Harness />, { navigation: false }); mountPlayer();
    const old = mockPool.cards['post:0'].generation!;
    send('state', 1, old - 1); expect(mockPool.cards['post:0'].playing).toBe(false);
    send('openURL', 'https://evil.test'); expect(Linking.openURL).not.toHaveBeenCalled();
    send('error', 153); expect(screen.getByText(/identify this app/)).toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.forumYoutubePlayer)).toBeNull();
    act(() => jest.advanceTimersByTime(50000)); expect(screen.queryByTestId(testIds.forumYoutubePlayer)).toBeNull();
    fireEvent.press(screen.getByTestId(testIds.forumYoutubeRetry)); expect(screen.getByTestId(testIds.forumYoutubePlayer)).toBeOnTheScreen();
    expect(mockPool.cards['post:0'].generation).not.toBe(old);
    expect(mockPool.cards['post:0'].playing).toBe(false);
  });
  it('times out, releases resources on unmount and rejects dangerous navigation', () => {
    const rendered = renderWithProviders(<Harness />, { navigation: false }); mountPlayer();
    const nav = screen.getByTestId(testIds.forumYoutubePlayer).props.onShouldStartLoadWithRequest;
    expect(nav({ url: 'javascript:1', isTopFrame: true, navigationType: 'click' })).toBe(false);
    expect(nav({ url: video.watchUrl, isTopFrame: true, navigationType: 'click' })).toBe(false);
    expect(Linking.openURL).toHaveBeenCalledWith(video.watchUrl);
    act(() => jest.advanceTimersByTime(playerTimeoutMs)); expect(screen.getByTestId(testIds.forumYoutubeRetry)).toBeOnTheScreen();
    rendered.unmount(); expect(Object.keys(mockPool.cards)).toHaveLength(0);
  });
  it.each(['error', 'httpError', 'contentProcessDidTerminate', 'renderProcessGone'])('reports %s with external fallback and no automatic launch', event => {
    renderWithProviders(<Harness />, { navigation: false }); mountPlayer();
    fireEvent(screen.getByTestId(testIds.forumYoutubePlayer), event, { nativeEvent: {} });
    expect(screen.getByTestId(testIds.forumYoutubeRetry)).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.forumYoutubeWatch)).toBeOnTheScreen(); expect(Linking.openURL).not.toHaveBeenCalled();
  });
  it('routes native user popup link events through the safe opener and rejects dangerous schemes', () => {
    renderWithProviders(<Harness />, { navigation: false }); mountPlayer();
    const player = screen.getByTestId(testIds.forumYoutubePlayer);
    fireEvent(player, 'openWindow', { nativeEvent: { targetUrl: 'javascript:1' } });
    expect(Linking.openURL).not.toHaveBeenCalled();
    fireEvent(player, 'openWindow', { nativeEvent: { targetUrl: video.watchUrl } });
    expect(Linking.openURL).toHaveBeenCalledWith(video.watchUrl);
  });
  it('fallback open failure uses existing caught error handling', async () => {
    jest.mocked(Linking.openURL).mockRejectedValue(new Error('No browser'));
    renderWithProviders(<Harness />, { navigation: false });
    await act(async () => fireEvent.press(screen.getByTestId(testIds.forumYoutubeWatch)));
    expect(Alert.alert).toHaveBeenCalledWith('Could not open link', 'This link could not be opened on this device.');
  });
});
