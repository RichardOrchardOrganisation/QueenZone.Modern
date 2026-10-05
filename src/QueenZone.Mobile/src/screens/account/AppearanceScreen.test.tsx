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

describe('AppearanceScreen', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
    jest.mocked(sendJson).mockReset();
    mockSession.accessToken = null;
    mockSession.refreshProfile.mockReset();
  });

  it('starts with the system setting and lets the user choose light', async () => {
    renderScreen();

    expect(screen.getByRole('radio', { name: 'Use system setting' }).props.accessibilityState).toEqual({
      selected: true,
    });

    await userEvent.setup().press(screen.getByRole('radio', { name: 'Light' }));

    expect(screen.getByRole('radio', { name: 'Light' }).props.accessibilityState).toEqual({ selected: true });
    await waitFor(() => expect(AsyncStorage.getItem(themePreferenceStorageKey)).resolves.toBe('light'));
    expect(sendJson).not.toHaveBeenCalled();
  });

  it('saves the choice to the account when signed in', async () => {
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
    expect(screen.getByRole('radio', { name: 'Dark' }).props.accessibilityState).toEqual({ selected: true });
  });

  it('keeps the device choice and explains when the account save fails', async () => {
    mockSession.accessToken = 'token';
    jest.mocked(sendJson).mockRejectedValue(new Error('offline'));
    renderScreen();

    await userEvent.setup().press(screen.getByRole('radio', { name: 'Light' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/Saved on this device only/);
    expect(screen.getByRole('radio', { name: 'Light' }).props.accessibilityState).toEqual({ selected: true });
  });
});
