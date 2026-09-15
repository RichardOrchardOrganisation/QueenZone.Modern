import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, render, screen, waitFor } from '@testing-library/react-native';
import { Text } from 'react-native';
import { ThemeProvider, themePreferenceStorageKey, useTheme } from './ThemeProvider';

function ThemeProbe() {
  const { mode, preference, setPreference, c } = useTheme();
  return (
    <>
      <Text testID="mode">{mode}</Text>
      <Text testID="preference">{preference}</Text>
      <Text testID="accent" onPress={() => setPreference('light')}>
        {c.accentPrimary}
      </Text>
    </>
  );
}

describe('ThemeProvider', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
  });

  it('defaults to the system setting when no choice has been saved', async () => {
    render(
      <ThemeProvider>
        <ThemeProbe />
      </ThemeProvider>,
    );

    expect(screen.getByTestId('mode')).toHaveTextContent('light');
    expect(screen.getByTestId('preference')).toHaveTextContent('system');
    expect(screen.getByTestId('accent')).toHaveTextContent('#244A8F');
    await waitFor(() => expect(AsyncStorage.getItem).toHaveBeenCalledWith(themePreferenceStorageKey));
  });

  it('restores a saved light choice', async () => {
    await AsyncStorage.setItem(themePreferenceStorageKey, 'light');

    render(
      <ThemeProvider>
        <ThemeProbe />
      </ThemeProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('mode')).toHaveTextContent('light'));
    expect(screen.getByTestId('accent')).toHaveTextContent('#244A8F');
  });

  it('applies and persists a new choice', async () => {
    render(
      <ThemeProvider>
        <ThemeProbe />
      </ThemeProvider>,
    );

    await act(async () => {
      screen.getByTestId('accent').props.onPress();
    });

    expect(screen.getByTestId('mode')).toHaveTextContent('light');
    expect(screen.getByTestId('preference')).toHaveTextContent('light');
    await waitFor(() =>
      expect(AsyncStorage.setItem).toHaveBeenCalledWith(themePreferenceStorageKey, 'light'),
    );
  });
});
