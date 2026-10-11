import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { AppState, type View } from 'react-native';
import { addNetworkStateListener, getNetworkStateAsync, type NetworkState } from 'expo-network';
import { PlayerPool, type PoolSnapshot } from './playerPool';

type Handle = { measure: (top: number, bottom: number, valid: () => boolean) => void; pause: () => void };
type PlayerContext = {
  pool: PlayerPool; snapshot: PoolSnapshot; online: boolean;
  attach: (key: string, handle: Handle) => () => void; measure: () => void;
};
export const ForumPlayersContext = createContext<PlayerContext | null>(null);
export function useForumPlayers(): PlayerContext | null { return useContext(ForumPlayersContext); }
type FocusNavigation = { isFocused: () => boolean; addListener: (event: 'focus' | 'blur', callback: () => void) => () => void };
export function ForumPlayerProvider({ children, viewport, navigation }: { children: ReactNode; viewport: React.RefObject<View | null>; navigation: FocusNavigation }) {
  const [focused, setFocused] = useState(() => navigation.isFocused());
  const [online, setOnline] = useState(false);
  const [active, setActive] = useState(AppState.currentState === 'active');
  const [snapshot, setSnapshot] = useState<PoolSnapshot>({});
  const handles = useRef(new Map<string, Handle>());
  const lastPublished = useRef('');
  const [pool] = useState(() => new PlayerPool(next => {
    const signature = JSON.stringify(Object.values(next).map(c => [c.key, c.owner, c.generation, c.failed]));
    if (signature !== lastPublished.current) { lastPublished.current = signature; setSnapshot(next); }
  }, key => handles.current.get(key)?.pause()));
  const pending = useRef<number | null>(null);
  const measurement = useRef({ epoch: 0, frame: 0 });
  const measure = useCallback(() => {
    if (pending.current != null) return;
    const epoch = measurement.current.epoch;
    pending.current = requestAnimationFrame(() => {
      pending.current = null;
      const frame = ++measurement.current.frame;
      viewport.current?.measureInWindow((_x, y, _width, height) => {
        if (epoch !== measurement.current.epoch || frame !== measurement.current.frame || height <= 0) return;
        for (const handle of handles.current.values()) handle.measure(y, y + height, () => epoch === measurement.current.epoch && frame === measurement.current.frame);
      });
    });
  }, [measurement, viewport]);
  const attach = useCallback((key: string, handle: Handle) => {
    handles.current.set(key, handle); measure();
    return () => { if (handles.current.get(key) === handle) handles.current.delete(key); };
  }, [measure]);
  useEffect(() => {
    const focus = navigation.addListener('focus', () => setFocused(true));
    const blur = navigation.addListener('blur', () => { measurement.current.epoch++; pool.setEnabled(false); setFocused(false); });
    return () => { focus(); blur(); };
  }, [measurement, navigation, pool]);
  useEffect(() => {
    let alive = true;
    let observed = false;
    const update = (state: NetworkState) => {
      if (!alive) return;
      const available = state.isConnected === true && state.isInternetReachable !== false;
      if (!available) { measurement.current.epoch++; pool.setEnabled(false); }
      setOnline(available);
    };
    const sub = addNetworkStateListener(state => { observed = true; update(state); });
    void getNetworkStateAsync().then(state => { if (!observed) update(state); }).catch(() => { if (alive && !observed) setOnline(false); });
    return () => { alive = false; sub.remove(); };
  }, [measurement, pool]);
  useEffect(() => {
    const sub = AppState.addEventListener('change', state => {
      // Native fullscreen/system presentation may enter inactive without
      // backgrounding. Background always stops and releases every player.
      if (state === 'background') { measurement.current.epoch++; pool.setEnabled(false); setActive(false); }
      else if (state === 'active') setActive(true);
    });
    return () => sub.remove();
  }, [measurement, pool]);
  useEffect(() => {
    const cycle = measurement.current;
    cycle.epoch++;
    if (pending.current != null) { cancelAnimationFrame(pending.current); pending.current = null; }
    pool.setEnabled(focused && active && online);
    if (focused && active && online) measure();
    return () => { cycle.epoch++; pool.setEnabled(false); };
  }, [active, focused, measure, measurement, online, pool]);
  useEffect(() => {
    const timer = setInterval(() => pool.tick(), 100);
    return () => {
      clearInterval(timer); pool.setEnabled(false);
      if (pending.current != null) cancelAnimationFrame(pending.current);
    };
  }, [pool]);
  return <ForumPlayersContext.Provider value={{ pool, snapshot, online, attach, measure }}>{children}</ForumPlayersContext.Provider>;
}
