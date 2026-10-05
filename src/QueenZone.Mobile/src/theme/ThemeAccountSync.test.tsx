import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, render, screen } from '@testing-library/react-native';
import { Text } from 'react-native';
import { parseMemberProfile } from '../api/me';
import { memberProfilePayload } from '../test/fixtures';
import { createMockSession } from '../test/mockSession';
import { ThemeAccountSync } from './ThemeAccountSync';
import { ThemeProvider, themePreferenceStorageKey, useTheme } from './ThemeProvider';

const mockSession = createMockSession();

jest.mock('../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

function Probe() {
  const { preference, devicePreference, accountPreference } = useTheme();
  return <Text>{`effective:${preference} device:${devicePreference} account:${accountPreference}`}</Text>;
}

function renderSync() {
  return render(
    <ThemeProvider>
      <ThemeAccountSync />
      <Probe />
    </ThemeProvider>,
  );
}

describe('ThemeAccountSync', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
    mockSession.profile = null;
  });

  it('signed out: the system setting applies unless the device has an override', async () => {
    await AsyncStorage.setItem(themePreferenceStorageKey, 'light');
    renderSync();

    expect(await screen.findByText('effective:light device:light account:system')).toBeTruthy();
  });

  it('applies the account preference when the device has no override', async () => {
    mockSession.profile = parseMemberProfile({ ...memberProfilePayload(), themePreference: 'dark' });
    renderSync();

    expect(await screen.findByText('effective:dark device:system account:dark')).toBeTruthy();
    await act(async () => {});
  });

  it('lets a device override win over the account preference', async () => {
    await AsyncStorage.setItem(themePreferenceStorageKey, 'light');
    mockSession.profile = parseMemberProfile({ ...memberProfilePayload(), themePreference: 'dark' });
    renderSync();

    expect(await screen.findByText('effective:light device:light account:dark')).toBeTruthy();
  });
});
