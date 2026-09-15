import { HeaderHeightContext } from '@react-navigation/elements';
import { NavigationContainer } from '@react-navigation/native';
import { act, render, type RenderOptions } from '@testing-library/react-native';
import type { ReactElement } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { ThemeProvider, type ThemePreference } from '../theme';

const safeAreaMetrics = {
  frame: { x: 0, y: 0, width: 390, height: 844 },
  insets: { top: 47, left: 0, right: 0, bottom: 34 },
};

/** Native-stack header stand-in for screens that call `useHeaderHeight()`. */
export const testHeaderHeight = 96;

type Options = RenderOptions & {
  navigation?: boolean;
  /** Defaults to dark for deterministic tests; null exercises persisted app behaviour. */
  themePreference?: ThemePreference | null;
};

export function renderWithProviders(ui: ReactElement, options: Options = {}) {
  const { navigation = true, themePreference = 'dark', ...renderOptions } = options;
  const content = navigation ? <NavigationContainer>{ui}</NavigationContainer> : ui;

  return render(content, {
    wrapper: ({ children }) => (
      <SafeAreaProvider initialMetrics={safeAreaMetrics}>
        <HeaderHeightContext.Provider value={testHeaderHeight}>
          <ThemeProvider preference={themePreference ?? undefined}>{children}</ThemeProvider>
        </HeaderHeightContext.Provider>
      </SafeAreaProvider>
    ),
    ...renderOptions,
  });
}

export function fakeNavigation() {
  return {
    navigate: jest.fn(),
    replace: jest.fn(),
    goBack: jest.fn(),
    setParams: jest.fn(),
    setOptions: jest.fn(),
    addListener: jest.fn(() => jest.fn()),
    isFocused: jest.fn(() => true),
    dispatch: jest.fn(),
    reset: jest.fn(),
    canGoBack: jest.fn(() => false),
    getParent: jest.fn(),
    getState: jest.fn(),
    getId: jest.fn(),
  };
}

/**
 * Drain VirtualizedList's deferred `_updateCellsToRender` timeout so it cannot
 * leak an `act(...)` warning into a later Jest file.
 */
export async function flushVirtualizedList(): Promise<void> {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });
}
