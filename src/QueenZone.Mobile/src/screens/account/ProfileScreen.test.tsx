import { screen, userEvent } from '@testing-library/react-native';
import { ProfileScreen } from './ProfileScreen';
import { memberProfileFixture } from '../../test/fixtures';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../messages/useUnreadConversationCount', () => ({
  useUnreadConversationCount: () => 2,
}));

function renderProfile() {
  return renderWithProviders(
    <ProfileScreen navigation={fakeNavigation() as never} route={{ key: 'profile', name: 'Profile' } as never} />,
    { navigation: false },
  );
}

describe('ProfileScreen', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.isRestoring = false;
    mockSession.displayName = null;
    mockSession.profile = null;
    mockSession.signOut.mockReset();
  });

  it('gates signed-out visitors behind Sign in', async () => {
    const navigation = fakeNavigation();
    const user = userEvent.setup();
    renderWithProviders(
      <ProfileScreen navigation={navigation as never} route={{ key: 'profile', name: 'Profile' } as never} />,
      { navigation: false },
    );
    expect(screen.getByText('Join the archive')).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Sign in' }));
    expect(navigation.dispatch).toHaveBeenCalledWith(
      expect.objectContaining({
        type: 'NAVIGATE',
        payload: expect.objectContaining({ name: 'SignIn' }),
      }),
    );
    await user.press(screen.getByRole('button', { name: 'Analytics preferences' }));
    expect(navigation.navigate).toHaveBeenCalledWith('AnalyticsSettings');
    await user.press(screen.getByRole('button', { name: 'Appearance' }));
    expect(navigation.navigate).toHaveBeenCalledWith('Appearance');
  });

  it('shows a restoring state instead of the signed-out gate', () => {
    mockSession.isRestoring = true;
    renderProfile();
    expect(screen.getByTestId('profile-restoring')).toBeOnTheScreen();
    expect(screen.queryByText('Join the archive')).toBeNull();
  });

  it('shows last-known identity instead of the restoring gate when a session exists', () => {
    mockSession.isSignedIn = true;
    mockSession.isRestoring = false;
    mockSession.displayName = 'Freddie';
    mockSession.profile = memberProfileFixture({ email: '' });
    mockSession.refreshProfile.mockResolvedValue(mockSession.profile);
    renderProfile();
    expect(screen.queryByTestId('profile-restoring')).toBeNull();
    expect(screen.queryByText('Restoring your session…')).toBeNull();
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
    expect(screen.getByText('FR')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeOnTheScreen();
  });

  it('does not full-screen restore when signed in even if isRestoring is still true', () => {
    mockSession.isSignedIn = true;
    mockSession.isRestoring = true;
    mockSession.displayName = 'Freddie';
    mockSession.profile = memberProfileFixture({ email: '' });
    mockSession.refreshProfile.mockResolvedValue(mockSession.profile);
    renderProfile();
    expect(screen.queryByTestId('profile-restoring')).toBeNull();
    expect(screen.queryByText('Restoring your session…')).toBeNull();
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
    expect(screen.getByText('FR')).toBeOnTheScreen();
  });

  it('shows member identity and sign out when signed in', async () => {
    mockSession.isSignedIn = true;
    mockSession.displayName = 'Freddie';
    mockSession.profile = memberProfileFixture();
    mockSession.refreshProfile.mockResolvedValue(mockSession.profile);
    renderProfile();
    expect(screen.getByText('Freddie')).toBeOnTheScreen();
    expect(screen.getByTestId('profile-messages')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Analytics preferences' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Appearance' })).toBeOnTheScreen();
  });

  it('opens My submissions from the member profile', async () => {
    const navigation = fakeNavigation();
    const user = userEvent.setup();
    mockSession.isSignedIn = true;
    mockSession.displayName = 'Freddie';
    mockSession.profile = memberProfileFixture();
    mockSession.refreshProfile.mockResolvedValue(mockSession.profile);
    renderWithProviders(
      <ProfileScreen navigation={navigation as never} route={{ key: 'profile', name: 'Profile' } as never} />,
      { navigation: false },
    );

    await user.press(screen.getByTestId('profile-my-submissions'));

    expect(navigation.navigate).toHaveBeenCalledWith('MySubmissions');
  });

  it('calls sign out and shows a busy control while it is pending', async () => {
    const user = userEvent.setup();
    mockSession.isSignedIn = true;
    mockSession.displayName = 'Freddie';
    mockSession.profile = memberProfileFixture();
    mockSession.refreshProfile.mockResolvedValue(mockSession.profile);
    mockSession.signOut.mockImplementation(() => new Promise(() => {}));
    renderProfile();

    await user.press(screen.getByRole('button', { name: 'Sign out' }));
    expect(mockSession.signOut).toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Sign out' }).props.accessibilityState).toEqual(
      expect.objectContaining({ busy: true }),
    );
  });
});
