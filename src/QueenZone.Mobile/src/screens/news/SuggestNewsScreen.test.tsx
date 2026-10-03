import { Button } from '../../ui/Button';
import { act, screen, userEvent, waitFor } from '@testing-library/react-native';
import type { NewsShareView } from '../../share/news/session';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { SuggestNewsScreen } from './SuggestNewsScreen';

const mockSession = createMockSession();
const mockFlush = jest.fn(async () => undefined);
let mockShare: NewsShareView = { kind: 'idle' };

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../../share/news/NewsShare', () => ({
  useNewsShare: () => mockShare,
  getNewsShareController: () => ({ flush: mockFlush }),
  openSuggestNews: jest.fn(),
}));

function renderSuggest(navigation = fakeNavigation()) {
  return {
    navigation,
    ...renderWithProviders(
      <SuggestNewsScreen
        navigation={navigation as never}
        route={{ key: 'suggest', name: 'SuggestNews' } as never}
      />,
      { navigation: false },
    ),
  };
}

describe('SuggestNewsScreen', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    mockFlush.mockClear();
    mockShare = {
      kind: 'form',
      draft: {
        url: 'https://www.bbc.co.uk/news/example',
        title: 'Queen announce dates',
        notes: '',
        origin: 'share',
      },
      patch: jest.fn(),
      cancel: jest.fn(),
      submit: jest.fn(),
    };
  });

  it('shows a signed-out form and signs in with SuggestNews returnTo', async () => {
    const { navigation } = renderSuggest();
    expect(screen.getByTestId(testIds.suggestNewsScreen)).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.suggestNewsUrl)).toBeOnTheScreen();
    expect(screen.getByDisplayValue('https://www.bbc.co.uk/news/example')).toBeOnTheScreen();
    expect(screen.getByText('www.bbc.co.uk')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.suggestNewsSubmit)).toBeDisabled();

    const user = userEvent.setup();
    await user.press(screen.getByTestId(testIds.suggestNewsSignIn));
    await waitFor(() => expect(mockFlush).toHaveBeenCalled());
    expect(navigation.dispatch).toHaveBeenCalledWith(
      expect.objectContaining({
        type: 'NAVIGATE',
        payload: expect.objectContaining({
          name: 'SignIn',
          params: { returnTo: { tab: 'HomeTab', screen: 'SuggestNews' } },
        }),
      }),
    );
  });

  it('dismisses a rejected share', async () => {
    const cancel = jest.fn();
    mockShare = {
      kind: 'rejected',
      reason: 'file',
      detail: 'Share a link, not a file or photo.',
      cancel,
    };
    const navigation = fakeNavigation();
    navigation.canGoBack.mockReturnValue(true);
    renderSuggest(navigation);
    const user = userEvent.setup();
    await user.press(screen.getByTestId(testIds.suggestNewsCancel));
    expect(cancel).toHaveBeenCalled();
    expect(navigation.goBack).toHaveBeenCalled();
  });

  it('cancels a chooser back to Home when the stack is empty', async () => {
    const cancel = jest.fn();
    mockShare = {
      kind: 'choose',
      candidates: ['https://www.bbc.co.uk/one', 'http://example.com/old'],
      choose: jest.fn(),
      cancel,
    };
    const navigation = fakeNavigation();
    renderSuggest(navigation);
    expect(screen.getByText('Needs an https:// link')).toBeOnTheScreen();
    const user = userEvent.setup();
    await user.press(screen.getByTestId(testIds.suggestNewsCancel));
    expect(cancel).toHaveBeenCalled();
    expect(navigation.navigate).toHaveBeenCalledWith('Home');
  });

  it('closes the idle empty state', async () => {
    mockShare = { kind: 'idle' };
    const navigation = fakeNavigation();
    renderSuggest(navigation);
    expect(screen.getByText('No story is waiting to be suggested.')).toBeOnTheScreen();
    const user = userEvent.setup();
    await user.press(screen.getByTestId(testIds.suggestNewsCancel));
    expect(navigation.navigate).toHaveBeenCalledWith('Home');
  });

  it('cancels a form and pops the stack', async () => {
    const cancel = jest.fn();
    mockShare = {
      kind: 'form',
      draft: {
        url: 'https://www.bbc.co.uk/news/example',
        title: '',
        notes: '',
        origin: 'inApp',
      },
      patch: jest.fn(),
      cancel,
      submit: jest.fn(),
    };
    const navigation = fakeNavigation();
    navigation.canGoBack.mockReturnValue(true);
    renderSuggest(navigation);
    const user = userEvent.setup();
    await user.press(screen.getByTestId(testIds.suggestNewsCancel));
    expect(cancel).toHaveBeenCalled();
    expect(navigation.goBack).toHaveBeenCalled();
  });

  it('lets the member pick from the chooser', async () => {
    const choose = jest.fn();
    mockShare = {
      kind: 'choose',
      candidates: ['https://www.bbc.co.uk/one', 'https://www.bbc.co.uk/two'],
      choose,
      cancel: jest.fn(),
    };
    renderSuggest();
    expect(screen.getByTestId(testIds.suggestNewsChooser)).toBeOnTheScreen();
    const user = userEvent.setup();
    await user.press(screen.getByLabelText('Use https://www.bbc.co.uk/two'));
    expect(choose).toHaveBeenCalledWith('https://www.bbc.co.uk/two');
  });

  it('routes success to My submissions', async () => {
    const acknowledge = jest.fn();
    mockShare = {
      kind: 'submitted',
      created: {
        id: '11111111-1111-1111-1111-111111111111',
        status: 'Pending',
        url: 'https://www.bbc.co.uk/news/example',
        title: 'Queen announce dates',
        submittedAt: '2026-08-26T10:00:00Z',
      },
      acknowledge,
    };
    const { navigation } = renderSuggest();
    expect(screen.getByTestId(testIds.suggestNewsSuccess)).toBeOnTheScreen();
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'View my submissions' }));
    expect(acknowledge).toHaveBeenCalled();
    expect(navigation.navigate).toHaveBeenCalledWith('MySubmissions');
  });

  it('retries a failed network submit', async () => {
    const submit = jest.fn();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    mockShare = {
      kind: 'failed',
      draft: {
        url: 'https://www.bbc.co.uk/news/example',
        title: '',
        notes: '',
        origin: 'share',
      },
      error: {
        code: 'network',
        message: 'Unable to reach QueenZone. Check your connection and try again.',
        retryable: true,
      },
      patch: jest.fn(),
      cancel: jest.fn(),
      submit,
    };
    renderSuggest();
    const user = userEvent.setup();
    await user.press(screen.getByTestId(testIds.suggestNewsRetry));
    expect(submit).toHaveBeenCalledWith('tok');
  });
});

describe('suggestion submit and retry guards', () => {
  const draft = { url: 'https://www.bbc.co.uk/news/example', title: 'Queen announce dates', notes: '', origin: 'share' as const };
  beforeEach(() => { mockSession.isSignedIn = true; mockSession.accessToken = 'test-token'; });

  it.each(['form', 'failed'] as const)('submits from %s with the current token', async (kind) => {
    const submit = jest.fn(async () => undefined);
    const base = { draft, patch: jest.fn(), cancel: jest.fn(), submit };
    mockShare = kind === 'form' ? { kind, ...base } : { kind, ...base, error: { code: 'network', message: 'Network unavailable', retryable: true } };
    renderSuggest();
    const user = userEvent.setup();
    expect(screen.getByTestId(testIds.suggestNewsSubmit)).toBeEnabled();
    await user.press(screen.getByTestId(testIds.suggestNewsSubmit));
    expect(submit).toHaveBeenCalledWith('test-token');
    if (kind === 'failed') {
      await user.press(screen.getByTestId(testIds.suggestNewsRetry));
      expect(submit).toHaveBeenCalledTimes(2);
    }
  });

  it.each(['submitting', 'missing-token'] as const)('does not submit while %s even if its handler is invoked', async (state) => {
    const submit = jest.fn(async () => undefined);
    mockSession.accessToken = state === 'missing-token' ? null : 'test-token';
    mockShare = state === 'submitting'
      ? { kind: 'submitting', draft }
      : { kind: 'failed', draft, submit, patch: jest.fn(), cancel: jest.fn(), error: { code: 'unauthorized', message: 'Sign in again', retryable: true } };
    renderSuggest();
    expect(screen.getByTestId(testIds.suggestNewsSubmit)).toBeDisabled();
    await userEvent.setup().press(screen.getByTestId(testIds.suggestNewsSubmit));
    for (const button of screen.UNSAFE_getAllByType(Button).filter((item) => ['Submit', 'Retry'].includes(item.props.label))) {
      await act(async () => button.props.onPress());
    }
    expect(submit).not.toHaveBeenCalled();
    if (state === 'missing-token') expect(screen.getByTestId(testIds.suggestNewsRetry)).toBeDisabled();
  });
});
