import { createRef, useLayoutEffect } from 'react';
import { AppState, View } from 'react-native';
import { act, fireEvent, waitFor } from '@testing-library/react-native';
import { addNetworkStateListener, getNetworkStateAsync, type NetworkState } from 'expo-network';
import { renderWithProviders, fakeNavigation } from '../../../test/render';
import { ForumPlayerProvider, useForumPlayers } from './ForumPlayerProvider';
let context: ReturnType<typeof useForumPlayers>;
function Probe() { const value = useForumPlayers(); useLayoutEffect(() => { context = value; }, [value]); return null; }
describe('forum screen player lifecycle', () => {
  it('pauses/releases on network loss, blur and background, and invalidates pending measurements', async () => {
    const listeners = new Map<string, () => void>();
    const navigation = { ...fakeNavigation(), addListener: jest.fn((event: 'focus' | 'blur', callback: () => void) => { listeners.set(event, callback); return jest.fn(); }) };
    let appListener: (state: 'active' | 'inactive' | 'background') => void = () => {};
    jest.spyOn(AppState, 'addEventListener').mockImplementation((_event, callback) => { appListener = callback; return { remove: jest.fn() }; });
    let networkListener: (state: NetworkState) => void = () => {};
    jest.mocked(addNetworkStateListener).mockImplementation(callback => { networkListener = callback; return { remove: jest.fn() }; });
    const viewport = createRef<React.ComponentRef<typeof View>>();
    Object.assign(viewport, { current: { measureInWindow: (callback: (x: number, y: number, w: number, h: number) => void) => callback(0, 0, 320, 800) } });
    const rendered = await renderWithProviders(<ForumPlayerProvider viewport={viewport} navigation={navigation}><Probe /></ForumPlayerProvider>, { navigation: false });
    await act(() => appListener('active'));
    await waitFor(() => expect(context!.online).toBe(true));
    const pool = context!.pool; let owner: number;
    const geometry = { top: 0, bottom: 200, width: 320, viewportTop: 0, viewportBottom: 800 };
    const pause = jest.fn();
    await act(() => { owner = pool.register('a'); context!.attach('a', { pause, measure: () => {} }); pool.measure('a', owner, geometry); });
    expect(pool.cards.a.generation).not.toBeNull();
    await act(() => appListener('inactive')); expect(pool.cards.a.generation).not.toBeNull();
    await act(() => networkListener({ isConnected: false })); expect(pool.cards.a.generation).toBeNull(); expect(pause).toHaveBeenCalled();
    for (const barrier of rendered.getAllByTestId('forum-player-release-barrier', { includeHiddenElements: true })) {
      await fireEvent(barrier, 'layout', { nativeEvent: { layout: { width: 0, height: 0 } } });
      expect(pool.pendingReleases.length).toBeGreaterThan(0);
      // A duplicate initial-layout event cannot acknowledge the second commit.
      await fireEvent(barrier, 'layout', { nativeEvent: { layout: { width: 0, height: 0 } } });
      expect(pool.pendingReleases.length).toBeGreaterThan(0);
      await fireEvent(barrier, 'layout', { nativeEvent: { layout: { width: 1.1428572, height: 0 } } });
      expect(pool.pendingReleases).toEqual([]);
    }
    await act(() => networkListener({ isConnected: true, isInternetReachable: true }));
    await act(() => pool.measure('a', owner!, geometry)); expect(pool.cards.a.generation).not.toBeNull();
    await act(() => listeners.get('blur')!()); expect(pool.cards.a.generation).toBeNull();
    for (const barrier of rendered.getAllByTestId('forum-player-release-barrier', { includeHiddenElements: true })) {
      await fireEvent(barrier, 'layout', { nativeEvent: { layout: { width: 0, height: 0 } } });
      await fireEvent(barrier, 'layout', { nativeEvent: { layout: { width: 1.1428572, height: 0 } } });
    }
    await act(() => listeners.get('focus')!()); await act(() => pool.measure('a', owner!, geometry));
    await act(() => appListener('background')); expect(pool.cards.a.generation).toBeNull();
    await act(() => appListener('active')); expect(pool.cards.a.generation).toBeNull();
    await rendered.unmount(); expect(pool.enabled).toBe(false);
  });
  it('unknown or failed initial network detection stays offline with no player requests', async () => {
    jest.mocked(getNetworkStateAsync).mockRejectedValueOnce(new Error('Unknown network'));
    const rendered = await renderWithProviders(<ForumPlayerProvider viewport={createRef<React.ComponentRef<typeof View>>()} navigation={fakeNavigation()}><Probe /></ForumPlayerProvider>, { navigation: false });
    await act(async () => {});
    expect(context!.online).toBe(false); expect(context!.pool.enabled).toBe(false);
    await rendered.unmount();
  });
});
