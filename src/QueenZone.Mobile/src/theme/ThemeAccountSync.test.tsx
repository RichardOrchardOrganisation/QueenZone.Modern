import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, render, screen } from '@testing-library/react-native';
import { Text } from 'react-native';
import { memberProfilePayload } from '../test/fixtures';
import { parseMemberProfile } from '../api/me';
import { createMockSession } from '../test/mockSession';
import { ThemeAccountSync } from './ThemeAccountSync';
import { ThemeProvider, useTheme } from './ThemeProvider';

const mockSession = createMockSession();

jest.mock('../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

function Probe() {
  const { preference } = useTheme();
  return <Text>{`preference:${preference}`}</Text>;
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

  it('leaves the device preference alone when signed out', async () => {
    await AsyncStorage.setItem('queenzone.mobile.themePreference', 'light');
    renderSync();

    expect(await screen.findByText('preference:light')).toBeTruthy();
  });

  it('applies the account preference once the profile loads', async () => {
    mockSession.profile = parseMemberProfile({ ...memberProfilePayload(), themePreference: 'dark' });
    renderSync();

    expect(await screen.findByText('preference:dark')).toBeTruthy();
    await act(async () => {});
  });
});
