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
  preference: ThemePreference;
  setPreference: (preference: ThemePreference) => void;
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
  const changedByUser = useRef(false);
  const isControlled = props.preference !== undefined;
  const preference = isControlled ? preferenceProp : savedPreference;

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
    void AsyncStorage.setItem(themePreferenceStorageKey, next).catch(() => {
      // The in-memory choice still applies for this session.
    });
  }, []);

  const mode: ThemeMode =
    preference === 'system' ? (systemScheme === 'light' ? 'light' : 'dark') : preference;

  const value = useMemo<ThemeContextValue>(
    () => ({
      c: mode === 'light' ? light : dark,
      mode,
      preference,
      setPreference,
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
    [mode, preference, setPreference],
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
