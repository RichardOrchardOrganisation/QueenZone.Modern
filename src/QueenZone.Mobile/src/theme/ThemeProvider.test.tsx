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

  it('clears the device override when the choice returns to system', async () => {
    await AsyncStorage.setItem(themePreferenceStorageKey, 'light');
    function Reset() {
      const { setPreference, devicePreference } = useTheme();
      return (
        <Text testID="device" onPress={() => setPreference('system')}>
          {devicePreference}
        </Text>
      );
    }
    render(
      <ThemeProvider>
        <Reset />
      </ThemeProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('device')).toHaveTextContent('light'));

    await act(async () => {
      screen.getByTestId('device').props.onPress();
    });

    expect(screen.getByTestId('device')).toHaveTextContent('system');
    await waitFor(() => expect(AsyncStorage.getItem(themePreferenceStorageKey)).resolves.toBeNull());
  });

  it('serializes delayed storage writes so the latest repeated choice persists', async () => {
    let finishFirst!: () => void;
    const original = jest.mocked(AsyncStorage.setItem).getMockImplementation()!;
    const writes = jest.spyOn(AsyncStorage, 'setItem');
    writes.mockClear();
    writes.mockImplementationOnce((key, value) => new Promise<void>((resolve) => {
      finishFirst = () => { void original(key, value).then(resolve); };
    }));
    function RepeatedChoice() {
      const { setPreference } = useTheme();
      return <Text testID="repeat" onPress={() => {
        setPreference('light');
        setPreference('dark');
      }}>Choose</Text>;
    }
    render(<ThemeProvider><RepeatedChoice /></ThemeProvider>);
    await waitFor(() => expect(AsyncStorage.getItem).toHaveBeenCalledWith(themePreferenceStorageKey));
    await act(async () => { screen.getByTestId('repeat').props.onPress(); });
    expect(writes).toHaveBeenCalledTimes(1);
    await act(async () => { finishFirst(); });
    await waitFor(() => expect(AsyncStorage.getItem(themePreferenceStorageKey)).resolves.toBe('dark'));
    expect(writes).toHaveBeenCalledTimes(2);
    writes.mockRestore();
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
