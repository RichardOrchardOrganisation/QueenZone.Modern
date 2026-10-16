import { Text } from 'react-native';
import { screen, userEvent } from '@testing-library/react-native';
import { MemberGate } from './MemberGate';
import { createMockSession } from '../test/mockSession';
import { renderWithProviders } from '../test/render';

const mockSession = createMockSession();
const mockNavigate = jest.fn();
const mockDispatch = jest.fn();

jest.mock('./SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('@react-navigation/native', () => {
  const actual = jest.requireActual('@react-navigation/native');
  return {
    ...actual,
    useNavigation: () => ({ navigate: mockNavigate, dispatch: mockDispatch }),
  };
});

describe('MemberGate', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.isRestoring = false;
    mockNavigate.mockReset();
    mockDispatch.mockReset();
  });

  it('renders nothing while session restore is in flight', async () => {
    mockSession.isRestoring = true;
    await renderWithProviders(
      <MemberGate title="Messages">
        <Text>inbox</Text>
      </MemberGate>,
      { navigation: false },
    );
    expect(screen.queryByText('Messages')).toBeNull();
    expect(screen.queryByText('inbox')).toBeNull();
  });

  it('shows a sign-in gate and opens the root SignIn modal', async () => {
    const user = userEvent.setup();
    await renderWithProviders(
      <MemberGate title="Messages">
        <Text>inbox</Text>
      </MemberGate>,
      { navigation: false },
    );
    expect(screen.getByText('Messages')).toBeOnTheScreen();
    expect(screen.getByText(/member-only boundary/)).toBeOnTheScreen();
    expect(screen.queryByText('inbox')).toBeNull();
    await user.press(screen.getByRole('button', { name: 'Sign in' }));
    expect(mockDispatch).toHaveBeenCalledWith(
      expect.objectContaining({
        type: 'NAVIGATE',
        payload: expect.objectContaining({ name: 'SignIn' }),
      }),
    );
  });

  it('renders children when signed in', async () => {
    mockSession.isSignedIn = true;
    await renderWithProviders(
      <MemberGate title="Messages">
        <Text>inbox</Text>
      </MemberGate>,
      { navigation: false },
    );
    expect(screen.getByText('inbox')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Sign in' })).toBeNull();
  });

  it('renders children when a stored session is signed in even if restore is flagged', async () => {
    mockSession.isSignedIn = true;
    mockSession.isRestoring = true;
    await renderWithProviders(
      <MemberGate title="Messages">
        <Text>inbox</Text>
      </MemberGate>,
      { navigation: false },
    );
    expect(screen.getByText('inbox')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Sign in' })).toBeNull();
  });
});
