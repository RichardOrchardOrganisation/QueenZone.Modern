import AsyncStorage from '@react-native-async-storage/async-storage';
import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { themePreferenceStorageKey } from '../../theme/ThemeProvider';
import { AppearanceScreen } from './AppearanceScreen';

describe('AppearanceScreen', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
  });

  it('starts with the system setting and lets the user choose light', async () => {
    renderWithProviders(
      <AppearanceScreen
        navigation={fakeNavigation() as never}
        route={{ key: 'appearance', name: 'Appearance' } as never}
      />,
      { navigation: false, themePreference: null },
    );

    expect(screen.getByRole('radio', { name: 'Use system setting' }).props.accessibilityState).toEqual({
      selected: true,
    });

    await userEvent.setup().press(screen.getByRole('radio', { name: 'Light' }));

    expect(screen.getByRole('radio', { name: 'Light' }).props.accessibilityState).toEqual({ selected: true });
    await waitFor(() => expect(AsyncStorage.getItem(themePreferenceStorageKey)).resolves.toBe('light'));
  });
});
