import { fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { Keyboard, KeyboardAvoidingView, Platform, ScrollView } from 'react-native';
import { fetchJson, sendJson } from '../../api/client';
import { ApiError } from '../../api/errors';
import { memberProfilePayload } from '../../test/fixtures';
import { createMockSession } from '../../test/mockSession';
import { renderWithProviders } from '../../test/render';
import { DeleteAccountScreen } from './DeleteAccountScreen';

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../../api/client', () => ({
  fetchJson: jest.fn(),
  sendJson: jest.fn(),
}));

const fetchJsonMock = fetchJson as jest.MockedFunction<typeof fetchJson>;
const sendJsonMock = sendJson as jest.MockedFunction<typeof sendJson>;

const deletionProfile = memberProfilePayload({
  deletion: {
    confirmationPhrase: 'DELETE',
    confirmationHint: 'Type DELETE to schedule deletion of the account.',
    requestedTitle: 'Account deletion scheduled',
    requestedMessage: 'You have been signed out.',
    whatHappens: ['Your public posts stay in the archive.'],
  },
});

function renderDeleteAccount() {
  return renderWithProviders(<DeleteAccountScreen />);
}

describe('DeleteAccountScreen', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    mockSession.signOut.mockReset();
    mockSession.refreshProfile.mockReset();
    mockSession.signOut.mockResolvedValue(undefined);
    mockSession.refreshProfile.mockResolvedValue(undefined);
    fetchJsonMock.mockReset();
    sendJsonMock.mockReset();
    fetchJsonMock.mockResolvedValue(deletionProfile);
  });

  it('does not POST when the confirmation phrase is wrong', async () => {
    renderDeleteAccount();
    await waitFor(() => expect(screen.getByLabelText('Type DELETE to confirm')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Type DELETE to confirm'), 'NOPE');
    await user.press(screen.getByRole('button', { name: 'Delete my account now' }));

    await waitFor(() =>
      expect(screen.getByText('Type DELETE to schedule deletion of the account.')).toBeOnTheScreen(),
    );
    expect(sendJsonMock).not.toHaveBeenCalled();
    expect(mockSession.signOut).not.toHaveBeenCalled();
  });

  it('keeps the confirmation field visible when the keyboard opens', async () => {
    const scrollToEnd = jest.spyOn(ScrollView.prototype, 'scrollToEnd').mockImplementation(() => {});
    const addKeyboardListener = jest.spyOn(Keyboard, 'addListener');
    renderDeleteAccount();
    const confirmation = await screen.findByLabelText('Type DELETE to confirm');

    expect(screen.UNSAFE_getByType(KeyboardAvoidingView).props.behavior).toBe(
      Platform.OS === 'ios' ? 'padding' : undefined,
    );
    fireEvent(confirmation, 'focus');
    expect(scrollToEnd).toHaveBeenCalledWith({ animated: true });

    scrollToEnd.mockClear();
    const keyboardShown = addKeyboardListener.mock.calls.find(([event]) => event === 'keyboardDidShow')?.[1];
    keyboardShown?.({} as never);
    expect(scrollToEnd).toHaveBeenCalledWith({ animated: true });

    scrollToEnd.mockClear();
    fireEvent(confirmation, 'blur');
    keyboardShown?.({} as never);
    expect(scrollToEnd).not.toHaveBeenCalled();
  });

  it('deletes the account and signs out', async () => {
    sendJsonMock.mockResolvedValueOnce({
      requested: true,
      scheduledDeletionAt: null,
      title: 'Account deletion scheduled',
      message: 'You have been signed out.',
      statusReceipt: 'opaque-test-receipt',
    });
    renderDeleteAccount();
    await waitFor(() => expect(screen.getByLabelText('Type DELETE to confirm')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Type DELETE to confirm'), 'DELETE');
    await user.press(screen.getByRole('button', { name: 'Delete my account now' }));

    await waitFor(() => expect(screen.getByText('Account deletion scheduled')).toBeOnTheScreen());
    expect(sendJsonMock).toHaveBeenCalledWith('/me/deletion-request', {
      accessToken: 'tok',
      body: { confirmation: 'DELETE', immediate: true },
    });
    expect(mockSession.signOut).toHaveBeenCalled();
    expect(screen.getByText('You have been signed out.')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Refresh deletion status' })).toBeOnTheScreen();
    expect(await AsyncStorage.getItem('queenzone.accountDeletionReceipt')).toContain('opaque-test-receipt');
    await waitFor(() => expect(fetchJsonMock).toHaveBeenCalledWith('/account-deletion-status', {
      query: { receipt: 'opaque-test-receipt' },
    }));
    fetchJsonMock.mockResolvedValueOnce({ status: 'complete' });
    await user.press(screen.getByRole('button', { name: 'Refresh deletion status' }));
    await waitFor(() => expect(screen.getByText('Account deletion complete')).toBeOnTheScreen());
  });

  it('cancels a scheduled deletion and refreshes the profile', async () => {
    fetchJsonMock.mockResolvedValueOnce(
      memberProfilePayload({
        scheduledDeletionAt: '2026-09-26T00:00:00.000Z',
      }),
    );
    sendJsonMock.mockResolvedValueOnce(deletionProfile);
    renderDeleteAccount();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Cancel account deletion' })).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Cancel account deletion' }));

    await waitFor(() =>
      expect(sendJsonMock).toHaveBeenCalledWith('/me/deletion-request/cancel', { accessToken: 'tok' }),
    );
    expect(mockSession.refreshProfile).toHaveBeenCalled();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Delete my account now' })).toBeOnTheScreen());
  });

  it('shows an API error when deletion fails', async () => {
    sendJsonMock.mockRejectedValueOnce(new ApiError(500, 'The server had a problem.'));
    renderDeleteAccount();
    await waitFor(() => expect(screen.getByLabelText('Type DELETE to confirm')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Type DELETE to confirm'), 'DELETE');
    await user.press(screen.getByRole('button', { name: 'Delete my account now' }));

    await waitFor(() => expect(screen.getByText('The server had a problem.')).toBeOnTheScreen());
    expect(mockSession.signOut).not.toHaveBeenCalled();
  });
});
