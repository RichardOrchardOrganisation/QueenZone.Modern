import { fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import { Keyboard, ScrollView } from 'react-native';
import { fallbackAuthProviders } from '../../api/auth';
import { jsonResponse } from '../../test/fixtures';
import { createMockSession } from '../../test/mockSession';
import { testIds } from '../../test/testIds';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { SignInScreen } from './SignInScreen';

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

const fetchMock = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit?]>();

beforeEach(() => {
  mockSession.isSignedIn = false;
  mockSession.signIn.mockReset();
  mockSession.signInWithPassword.mockReset();
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
  fetchMock.mockResolvedValue(jsonResponse({ providers: fallbackAuthProviders }));
});

afterEach(() => {
  jest.restoreAllMocks();
});

function renderSignIn(returnTo?: { tab: 'ForumTab'; screen: 'Composer'; params: { threadId: number } }) {
  const navigation = fakeNavigation();
  renderWithProviders(
    <SignInScreen
      navigation={navigation as never}
      route={{ key: 'signin', name: 'SignIn', params: returnTo ? { returnTo } : undefined } as never}
    />,
    { navigation: false },
  );
  return navigation;
}

describe('SignInScreen', () => {
  it('lists providers and surfaces a sign-in failure', async () => {
    const user = userEvent.setup();
    mockSession.signIn.mockRejectedValue(new Error('Sign-in was cancelled.'));
    renderSignIn();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Continue with Google' })).toBeOnTheScreen());
    await user.press(screen.getByRole('button', { name: 'Continue with Google' }));
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Sign-in was cancelled.'));
  });

  it('uses the Apple-approved visual treatment for the Apple provider', async () => {
    renderSignIn();
    await waitFor(() => expect(screen.getByTestId('apple-sign-in-button')).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Continue with Apple' })).toBeOnTheScreen();
  });

  it('leaves for the prompting screen after a successful provider hop', async () => {
    const user = userEvent.setup();
    mockSession.signIn.mockResolvedValue(undefined);
    const navigation = renderSignIn({ tab: 'ForumTab', screen: 'Composer', params: { threadId: 9 } });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Continue with Google' })).toBeOnTheScreen());
    await user.press(screen.getByRole('button', { name: 'Continue with Google' }));
    await waitFor(() =>
      expect(navigation.navigate).toHaveBeenCalledWith('Tabs', {
        screen: 'ForumTab',
        params: { screen: 'Composer', params: { threadId: 9 } },
      }),
    );
  });

  it('expands other ways and completes password sign-in on the same session path', async () => {
    const user = userEvent.setup();
    mockSession.signInWithPassword.mockResolvedValue(undefined);
    const navigation = renderSignIn({ tab: 'ForumTab', screen: 'Composer', params: { threadId: 9 } });
    await waitFor(() => expect(screen.getByTestId(testIds.signInOtherWays)).toBeOnTheScreen());
    expect(screen.queryByTestId(testIds.signInEmail)).toBeNull();

    await user.press(screen.getByTestId(testIds.signInOtherWays));
    await waitFor(() => expect(screen.getByTestId(testIds.signInEmail)).toBeOnTheScreen());
    await user.type(screen.getByTestId(testIds.signInEmail), 'reviewer@example.com');
    await user.type(screen.getByTestId(testIds.signInPassword), 'correct horse battery staple');
    await user.press(screen.getByTestId(testIds.signInPasswordSubmit));

    await waitFor(() =>
      expect(mockSession.signInWithPassword).toHaveBeenCalledWith(
        'reviewer@example.com',
        'correct horse battery staple',
      ),
    );
    await waitFor(() =>
      expect(navigation.navigate).toHaveBeenCalledWith('Tabs', {
        screen: 'ForumTab',
        params: { screen: 'Composer', params: { threadId: 9 } },
      }),
    );
  });

  it('surfaces a password sign-in failure without leaving', async () => {
    const user = userEvent.setup();
    mockSession.signInWithPassword.mockRejectedValue(new Error('Incorrect email or password.'));
    const navigation = renderSignIn();
    await waitFor(() => expect(screen.getByTestId(testIds.signInOtherWays)).toBeOnTheScreen());
    await user.press(screen.getByTestId(testIds.signInOtherWays));
    await user.type(screen.getByLabelText('Email'), 'reviewer@example.com');
    await user.type(screen.getByLabelText('Password'), 'wrong');
    await user.press(screen.getByTestId(testIds.signInPasswordSubmit));
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Incorrect email or password.'));
    expect(navigation.navigate).not.toHaveBeenCalled();
  });

  it('submits the reviewer credentials from the password keyboard', async () => {
    const dismissKeyboard = jest.spyOn(Keyboard, 'dismiss').mockImplementation(() => {});
    mockSession.signInWithPassword.mockResolvedValue(undefined);
    renderSignIn();
    await waitFor(() => expect(screen.getByTestId(testIds.signInOtherWays)).toBeOnTheScreen());
    fireEvent.press(screen.getByTestId(testIds.signInOtherWays));

    const email = screen.getByTestId(testIds.signInEmail);
    const password = screen.getByTestId(testIds.signInPassword);
    expect(email.props.returnKeyType).toBe('next');
    expect(password.props.returnKeyType).toBe('go');

    fireEvent.changeText(email, 'reviewer@example.com');
    fireEvent(email, 'submitEditing');
    fireEvent.changeText(password, 'correct horse battery staple');
    fireEvent(password, 'submitEditing');

    await waitFor(() =>
      expect(mockSession.signInWithPassword).toHaveBeenCalledWith(
        'reviewer@example.com',
        'correct horse battery staple',
      ),
    );
    expect(dismissKeyboard).toHaveBeenCalledTimes(1);
  });

  it('scrolls the password fields into view when the form and keyboard open', async () => {
    const scrollToEnd = jest.spyOn(ScrollView.prototype, 'scrollToEnd').mockImplementation(() => {});
    const addKeyboardListener = jest.spyOn(Keyboard, 'addListener');
    renderSignIn();
    await waitFor(() => expect(screen.getByTestId(testIds.signInOtherWays)).toBeOnTheScreen());
    fireEvent.press(screen.getByTestId(testIds.signInOtherWays));

    fireEvent(screen.UNSAFE_getByType(ScrollView), 'contentSizeChange', 400, 900);
    expect(scrollToEnd).toHaveBeenCalledWith({ animated: true });

    scrollToEnd.mockClear();
    fireEvent(screen.getByTestId(testIds.signInEmail), 'focus');
    expect(scrollToEnd).toHaveBeenCalledWith({ animated: true });

    scrollToEnd.mockClear();
    const keyboardShown = addKeyboardListener.mock.calls.find(([event]) => event === 'keyboardDidShow')?.[1];
    keyboardShown?.({} as never);
    expect(scrollToEnd).toHaveBeenCalledWith({ animated: true });
  });

  it('describes OAuth secrets separately from the password fallback', async () => {
    renderSignIn();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Continue with Google' })).toBeOnTheScreen());
    expect(screen.getByText(/OAuth never sends a provider secret/)).toBeOnTheScreen();
    expect(screen.getByText(/operator-created accounts/)).toBeOnTheScreen();
    expect(screen.queryByText(/The app never sees a password/)).toBeNull();
  });
});
