import AsyncStorage from '@react-native-async-storage/async-storage';
import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { sendJson } from '../../api/client';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { themePreferenceStorageKey } from '../../theme/ThemeProvider';
import { AppearanceScreen } from './AppearanceScreen';

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../../api/client', () => ({
  sendJson: jest.fn(),
}));

function renderScreen() {
  return renderWithProviders(
    <AppearanceScreen
      navigation={fakeNavigation() as never}
      route={{ key: 'appearance', name: 'Appearance' } as never}
    />,
    { navigation: false, themePreference: null },
  );
}

function selected(name: string) {
  return screen.getByRole('radio', { name }).props.accessibilityState;
}

describe('AppearanceScreen', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
    jest.mocked(sendJson).mockReset();
    mockSession.accessToken = null;
    mockSession.refreshProfile.mockReset();
  });

  it('signed out: starts with the system setting and saves the choice on the device only', async () => {
    renderScreen();

    expect(selected('Use system setting')).toEqual({ selected: true });

    await userEvent.setup().press(screen.getByRole('radio', { name: 'Light' }));

    expect(selected('Light')).toEqual({ selected: true });
    await waitFor(() => expect(AsyncStorage.getItem(themePreferenceStorageKey)).resolves.toBe('light'));
    expect(sendJson).not.toHaveBeenCalled();
    expect(screen.queryByText('This device only')).toBeNull();
  });

  it('signed in: saves the account choice to the profile and leaves the device alone', async () => {
    mockSession.accessToken = 'token';
    jest.mocked(sendJson).mockResolvedValue({});
    renderScreen();

    await userEvent.setup().press(screen.getByRole('radio', { name: 'Dark' }));

    await waitFor(() =>
      expect(sendJson).toHaveBeenCalledWith('/me', {
        method: 'PATCH',
        accessToken: 'token',
        body: { themePreference: 'dark' },
      }),
    );
    expect(mockSession.refreshProfile).toHaveBeenCalled();
    await expect(AsyncStorage.getItem(themePreferenceStorageKey)).resolves.toBeNull();
  });

  it('signed in: a device override is stored locally without touching the account', async () => {
    mockSession.accessToken = 'token';
    renderScreen();

    expect(selected('This device: Same as account')).toEqual({ selected: true });

    await userEvent.setup().press(screen.getByRole('radio', { name: 'This device: Light' }));

    expect(selected('This device: Light')).toEqual({ selected: true });
    await waitFor(() => expect(AsyncStorage.getItem(themePreferenceStorageKey)).resolves.toBe('light'));
    expect(sendJson).not.toHaveBeenCalled();

    await userEvent.setup().press(screen.getByRole('radio', { name: 'This device: Same as account' }));
    await waitFor(() => expect(AsyncStorage.getItem(themePreferenceStorageKey)).resolves.toBeNull());
  });

  it('signed in: explains when the account save fails', async () => {
    mockSession.accessToken = 'token';
    jest.mocked(sendJson).mockRejectedValue(new Error('offline'));
    renderScreen();

    await userEvent.setup().press(screen.getByRole('radio', { name: 'Light' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not update your account/);
  });
});
