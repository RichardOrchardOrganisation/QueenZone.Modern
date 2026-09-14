import AsyncStorage from '@react-native-async-storage/async-storage';
import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { themePreferenceStorageKey } from '../../theme/ThemeProvider';
import { AppearanceScreen } from './AppearanceScreen';

describe('AppearanceScreen', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
  });

  it('starts dark and lets the user choose light', async () => {
    renderWithProviders(
      <AppearanceScreen
        navigation={fakeNavigation() as never}
        route={{ key: 'appearance', name: 'Appearance' } as never}
      />,
      { navigation: false },
    );

    expect(screen.getByRole('radio', { name: 'Dark' }).props.accessibilityState).toEqual({ selected: true });

    await userEvent.setup().press(screen.getByRole('radio', { name: 'Light' }));

    expect(screen.getByRole('radio', { name: 'Light' }).props.accessibilityState).toEqual({ selected: true });
    await waitFor(() => expect(AsyncStorage.getItem(themePreferenceStorageKey)).resolves.toBe('light'));
  });
});
