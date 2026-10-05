import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { useColorScheme } from 'react-native';
import {
  chrome,
  dark,
  fonts,
  imagery,
  light,
  motion,
  palette,
  radius,
  shadow,
  space,
  type,
  type ColorScheme,
  type ThemeMode,
} from './tokens';

export type ThemePreference = 'system' | ThemeMode;

export const themePreferenceStorageKey = 'queenzone.mobile.themePreference';

type ThemeContextValue = {
  /** Resolved colours for the active mode. */
  c: ColorScheme;
  mode: ThemeMode;
  /** Effective choice: this device's override, else the signed-in account's, else the system. */
  preference: ThemePreference;
  /** This device's own override. `system` clears it so the account setting (or the system) applies. */
  devicePreference: ThemePreference;
  setPreference: (preference: ThemePreference) => void;
  /** The signed-in member's saved choice. Held in memory only; the account is the source of truth. */
  accountPreference: ThemePreference;
  setAccountPreference: (preference: ThemePreference) => void;
  palette: typeof palette;
  type: typeof type;
  fonts: typeof fonts;
  space: typeof space;
  radius: typeof radius;
  shadow: typeof shadow;
  motion: typeof motion;
  chrome: typeof chrome;
  imagery: typeof imagery;
};

const ThemeContext = createContext<ThemeContextValue | null>(null);

type Props = {
  children: ReactNode;
  /** Follows the OS by default; pass a value to force a mode in previews or tests. */
  preference?: ThemePreference;
};

/**
 * Provides design tokens. The app follows the OS appearance until someone
 * chooses and persists an explicit preference.
 */
export function ThemeProvider(props: Props) {
  const { children, preference: preferenceProp = 'system' } = props;
  const systemScheme = useColorScheme();
  const [savedPreference, setSavedPreference] = useState<ThemePreference>(preferenceProp);
  const [accountPreference, setAccountPreference] = useState<ThemePreference>('system');
  const changedByUser = useRef(false);
  const storageWrites = useRef<Promise<void>>(Promise.resolve());
  const isControlled = props.preference !== undefined;
  const devicePreference = isControlled ? preferenceProp : savedPreference;
  const preference = devicePreference === 'system' ? accountPreference : devicePreference;

  useEffect(() => {
    if (isControlled) {
      return;
    }

    let active = true;
    void AsyncStorage.getItem(themePreferenceStorageKey).then((stored) => {
      if (active && !changedByUser.current && (stored === 'dark' || stored === 'light' || stored === 'system')) {
        setSavedPreference(stored);
      }
    }).catch(() => {
      // Keep following the system when local storage is unavailable.
    });
    return () => {
      active = false;
    };
  }, [isControlled]);

  const setPreference = useCallback((next: ThemePreference) => {
    changedByUser.current = true;
    setSavedPreference(next);
    // Keep repeated changes ordered even if native storage completes slowly.
    storageWrites.current = storageWrites.current.then(async () => {
      if (next === 'system') await AsyncStorage.removeItem(themePreferenceStorageKey);
      else await AsyncStorage.setItem(themePreferenceStorageKey, next);
    }).catch(() => {
      // The in-memory choice still applies for this session; later writes may retry.
    });
  }, []);

  const systemMode: ThemeMode = systemScheme === 'light' ? 'light' : 'dark';
  const mode: ThemeMode = preference === 'system' ? systemMode : preference;

  const value = useMemo<ThemeContextValue>(
    () => ({
      c: mode === 'light' ? light : dark,
      mode,
      preference,
      devicePreference,
      setPreference,
      accountPreference,
      setAccountPreference,
      palette,
      type,
      fonts,
      space,
      radius,
      shadow,
      motion,
      chrome,
      imagery,
    }),
    [mode, preference, devicePreference, setPreference, accountPreference],
  );

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) {
    throw new Error('useTheme must be used within ThemeProvider');
  }
  return ctx;
}
