import { fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import * as ImagePicker from 'expo-image-picker';
import { fetchJson, sendJson } from '../../api/client';
import { ApiError } from '../../api/errors';
import { uploadMemberAvatar } from '../../api/memberAvatar';
import { fallbackProfileLimits } from '../../api/me';
import { fetchNotificationPreferences, patchNotificationPreferences } from '../../api/notificationPreferences';
import { memberProfilePayload } from '../../test/fixtures';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { SettingsScreen } from './SettingsScreen';

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('@react-navigation/native', () => {
  const actual = jest.requireActual('@react-navigation/native');
  return {
    ...actual,
    useNavigation: () => ({ navigate: jest.fn(), dispatch: jest.fn() }),
  };
});

jest.mock('../../config/appConfig', () => ({
  getAppConfig: () => ({ apiBaseUrl: 'http://qz.test', appEnv: 'development', version: '0.1.0' }),
}));

jest.mock('../../api/client', () => ({
  fetchJson: jest.fn(),
  sendJson: jest.fn(),
  sendMultipart: jest.fn(),
}));

jest.mock('../../api/memberAvatar', () => ({
  uploadMemberAvatar: jest.fn(),
}));

jest.mock('../../api/notificationPreferences', () => ({
  fetchNotificationPreferences: jest.fn(),
  patchNotificationPreferences: jest.fn(),
}));

jest.mock('expo-image-picker', () => ({
  requestMediaLibraryPermissionsAsync: jest.fn(),
  requestCameraPermissionsAsync: jest.fn(),
  launchImageLibraryAsync: jest.fn(),
  launchCameraAsync: jest.fn(),
}));

const fetchJsonMock = fetchJson as jest.MockedFunction<typeof fetchJson>;
const sendJsonMock = sendJson as jest.MockedFunction<typeof sendJson>;
const uploadMemberAvatarMock = uploadMemberAvatar as jest.MockedFunction<typeof uploadMemberAvatar>;
const fetchNotificationPreferencesMock = fetchNotificationPreferences as jest.MockedFunction<
  typeof fetchNotificationPreferences
>;
const patchNotificationPreferencesMock = patchNotificationPreferences as jest.MockedFunction<
  typeof patchNotificationPreferences
>;

const profilePayload = memberProfilePayload({
  memberId: '11111111-1111-1111-1111-111111111111',
  email: 'fan@example.com',
  displayName: 'Roger',
});

const defaultPreferences = { forumReply: true, privateMessage: true, news: false };

function mockSettingsLoad(
  preferences = defaultPreferences,
  profile: Record<string, unknown> = profilePayload,
) {
  fetchJsonMock.mockImplementation(async (path: string) => {
    if (path === '/me') {
      return profile;
    }
    throw new Error(`unexpected GET ${path}`);
  });
  fetchNotificationPreferencesMock.mockResolvedValue(preferences);
}

function renderSettings(navigation = fakeNavigation()) {
  return {
    navigation,
    ...renderWithProviders(
      <SettingsScreen navigation={navigation as never} route={{ key: 'settings', name: 'Settings' } as never} />,
    ),
  };
}

describe('SettingsScreen notifications', () => {
  beforeEach(() => {
    mockSession.isSignedIn = true;
    mockSession.isRestoring = false;
    mockSession.accessToken = 'tok';
    mockSession.refreshProfile.mockResolvedValue(undefined);
    mockSettingsLoad();
    sendJsonMock.mockReset();
    uploadMemberAvatarMock.mockReset();
    patchNotificationPreferencesMock.mockReset();
  });

  it('gates unsigned visitors', () => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    renderSettings();
    expect(screen.getByText('Settings')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeOnTheScreen();
  });

  it('loads independent toggles from the preferences endpoint', async () => {
    mockSettingsLoad({ forumReply: true, privateMessage: false, news: false });
    renderSettings();

    await waitFor(() => expect(screen.getByTestId(testIds.settingsNotifyNews)).toBeOnTheScreen());
    expect(screen.getByText('Notifications')).toBeOnTheScreen();
    expect(screen.getByRole('switch', { name: 'Forum replies' })).toBeOnTheScreen();
    expect(screen.getByRole('switch', { name: 'Private messages' })).toHaveProp('value', false);
    expect(screen.getByRole('switch', { name: 'News' })).toHaveProp('value', false);
    expect(screen.getByText('You still need to Watch a topic to get forum reply pushes.')).toBeOnTheScreen();
    expect(fetchNotificationPreferencesMock).toHaveBeenCalledWith('tok');
  });

  it('persists a toggle immediately and reports status', async () => {
    patchNotificationPreferencesMock.mockResolvedValue({ forumReply: true, privateMessage: true, news: true });
    renderSettings();
    await waitFor(() => expect(screen.getByRole('switch', { name: 'News' })).toBeOnTheScreen());

    fireEvent(screen.getByRole('switch', { name: 'News' }), 'valueChange', true);

    await waitFor(() => expect(patchNotificationPreferencesMock).toHaveBeenCalledWith('tok', { news: true }));
    expect(screen.getByText('Notification preferences updated.')).toBeOnTheScreen();
    expect(screen.getByRole('switch', { name: 'News' })).toHaveProp('value', true);
  });

  it('reverts the toggle when save fails', async () => {
    patchNotificationPreferencesMock.mockRejectedValue(
      new ApiError(500, 'The server had a problem. Try again shortly.'),
    );
    renderSettings();
    await waitFor(() => expect(screen.getByRole('switch', { name: 'News' })).toHaveProp('value', false));

    fireEvent(screen.getByRole('switch', { name: 'News' }), 'valueChange', true);

    await waitFor(() =>
      expect(screen.getByText('The server had a problem. Try again shortly.')).toBeOnTheScreen(),
    );
    expect(screen.getByRole('switch', { name: 'News' })).toHaveProp('value', false);
  });

  it('loads the profile', async () => {
    renderSettings();
    await waitFor(() => expect(screen.getByDisplayValue('Roger')).toBeOnTheScreen());
    expect(screen.getByText('fan@example.com')).toBeOnTheScreen();
    expect(fetchJsonMock).toHaveBeenCalledWith('/me', { accessToken: 'tok' });
    expect(fetchNotificationPreferencesMock).toHaveBeenCalledWith('tok');
  });

  it('shows a load error when settings cannot be fetched', async () => {
    fetchJsonMock.mockRejectedValueOnce(new ApiError(500, 'The server had a problem.'));
    renderSettings();
    await waitFor(() => expect(screen.getByText('The server had a problem.')).toBeOnTheScreen());
    expect(screen.queryByLabelText('Display name')).toBeOnTheScreen();
  });

  it('rejects an invalid display name without PATCHing', async () => {
    renderSettings();
    await waitFor(() => expect(screen.getByLabelText('Display name')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.clear(screen.getByLabelText('Display name'));
    await user.type(screen.getByLabelText('Display name'), 'R');
    await user.press(screen.getByRole('button', { name: 'Save display name' }));

    await waitFor(() => expect(screen.getByText('Display name must be at least 2 characters.')).toBeOnTheScreen());
    expect(sendJsonMock).not.toHaveBeenCalled();
  });

  it('saves the display name and messaging privacy', async () => {
    sendJsonMock
      .mockResolvedValueOnce(
        memberProfilePayload({
          memberId: profilePayload.memberId,
          email: profilePayload.email,
          displayName: 'Brian',
        }),
      )
      .mockResolvedValueOnce(
        memberProfilePayload({
          memberId: profilePayload.memberId,
          email: profilePayload.email,
          displayName: 'Brian',
          messagePrivacy: 'nobody',
        }),
      );
    renderSettings();
    await waitFor(() => expect(screen.getByLabelText('Display name')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.clear(screen.getByLabelText('Display name'));
    await user.type(screen.getByLabelText('Display name'), 'Brian');
    await user.press(screen.getByRole('button', { name: 'Save display name' }));

    await waitFor(() => expect(screen.getByText('Display name updated.')).toBeOnTheScreen());
    expect(sendJsonMock).toHaveBeenCalledWith('/me', {
      method: 'PATCH',
      accessToken: 'tok',
      body: { displayName: 'Brian' },
    });

    await user.press(screen.getByRole('radio', { name: 'Nobody' }));
    await waitFor(() => expect(screen.getByText('Messaging privacy updated.')).toBeOnTheScreen());
    expect(sendJsonMock).toHaveBeenCalledWith('/me', {
      method: 'PATCH',
      accessToken: 'tok',
      body: { messagePrivacy: 'nobody' },
    });
  });

  it('navigates to DeleteAccount', async () => {
    const { navigation } = renderSettings();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Delete my account' })).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Delete my account' }));
    expect(navigation.navigate).toHaveBeenCalledWith('DeleteAccount');
  });

  it('claims a matching legacy account', async () => {
    mockSettingsLoad(defaultPreferences, {
      ...profilePayload,
      legacyLink: {
        kind: 'claimable',
        match: null,
        claimableMatches: [{ userId: 42, username: 'classic-roger' }],
        unavailableMatches: [],
      },
    });
    sendJsonMock.mockResolvedValueOnce(
      memberProfilePayload({
        ...profilePayload,
        legacyLink: {
          kind: 'linked',
          match: { userId: 42, username: 'classic-roger' },
          claimableMatches: [],
          unavailableMatches: [],
        },
      }),
    );
    renderSettings();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Claim legacy account' })).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Claim legacy account' }));
    await waitFor(() =>
      expect(sendJsonMock).toHaveBeenCalledWith('/me/legacy-link', {
        accessToken: 'tok',
        body: { legacyUserId: 42, adoptDisplayName: true },
      }),
    );
  });

  it('unlinks a linked legacy account', async () => {
    mockSettingsLoad(defaultPreferences, {
      ...profilePayload,
      legacyLink: {
        kind: 'linked',
        match: { userId: 42, username: 'classic-roger' },
        claimableMatches: [],
        unavailableMatches: [],
      },
    });
    sendJsonMock.mockResolvedValueOnce(
      memberProfilePayload({
        ...profilePayload,
        legacyLink: { kind: 'none', match: null, claimableMatches: [], unavailableMatches: [] },
      }),
    );
    renderSettings();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Unlink legacy account' })).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Unlink legacy account' }));
    await waitFor(() =>
      expect(sendJsonMock).toHaveBeenCalledWith('/me/legacy-link', { method: 'DELETE', accessToken: 'tok' }),
    );
  });

  it('uploads the chosen avatar through the member avatar API', async () => {
    uploadMemberAvatarMock.mockResolvedValueOnce({
      memberId: profilePayload.memberId,
      email: profilePayload.email,
      displayName: 'Roger',
      createdAt: '',
      lastLoginAt: null,
      hasAvatar: true,
      avatarPath: '/account/avatar/11111111-1111-1111-1111-111111111111',
      avatarThumbPath: null,
      messagePrivacy: 'members',
      themePreference: 'system',
      linkedProviders: [],
      legacyLink: { kind: 'none', match: null, claimableMatches: [], unavailableMatches: [] },
      scheduledDeletionAt: null,
      limits: fallbackProfileLimits,
      deletion: {
        confirmationPhrase: 'DELETE',
        confirmationHint: 'Type DELETE to schedule deletion of the account.',
        requestedTitle: 'Account deletion scheduled',
        requestedMessage: 'You have been signed out.',
        whatHappens: [],
      },
    });
    (ImagePicker.requestMediaLibraryPermissionsAsync as jest.Mock).mockResolvedValue({ granted: true });
    (ImagePicker.launchImageLibraryAsync as jest.Mock).mockResolvedValue({
      canceled: false,
      assets: [{ uri: 'file:///tmp/avatar.jpg', fileName: 'avatar.jpg', mimeType: 'image/jpeg' }],
    });

    renderSettings();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Choose photo' })).toBeOnTheScreen());
    fireEvent.press(screen.getByRole('button', { name: 'Choose photo' }));

    await waitFor(() => expect(screen.getByText('Avatar updated.')).toBeOnTheScreen());
    expect(uploadMemberAvatarMock).toHaveBeenCalledWith(
      { uri: 'file:///tmp/avatar.jpg', name: 'avatar.jpg', type: 'image/jpeg' },
      'tok',
    );
  });

  it('shows a local-file avatar failure instead of a fake offline message', async () => {
    const cause = new TypeError('Network request failed');
    uploadMemberAvatarMock.mockRejectedValueOnce(ApiError.localFile(cause));
    (ImagePicker.requestMediaLibraryPermissionsAsync as jest.Mock).mockResolvedValue({ granted: true });
    (ImagePicker.launchImageLibraryAsync as jest.Mock).mockResolvedValue({
      canceled: false,
      assets: [{ uri: 'file:///tmp/avatar.jpg', fileName: 'avatar.jpg', mimeType: 'image/jpeg' }],
    });

    renderSettings();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Choose photo' })).toBeOnTheScreen());
    fireEvent.press(screen.getByRole('button', { name: 'Choose photo' }));

    await waitFor(() =>
      expect(screen.getByText('Could not read the selected photo. Try choosing it again.')).toBeOnTheScreen(),
    );
    expect(screen.queryByText('Unable to reach QueenZone. Check your connection and try again.')).toBeNull();
  });
});
