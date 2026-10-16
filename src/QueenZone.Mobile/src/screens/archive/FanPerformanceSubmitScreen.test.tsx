import { fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import * as DocumentPicker from 'expo-document-picker';
import { ApiError } from '../../api/errors';
import { createFanPerformanceSubmission } from '../../api/fanPerformanceSubmissions';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { FanPerformanceSubmitScreen } from './FanPerformanceSubmitScreen';

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('@react-navigation/native', () => {
  const actual = jest.requireActual('@react-navigation/native');
  return {
    ...actual,
    useNavigation: () => ({ navigate: jest.fn() }),
  };
});

jest.mock('../../api/fanPerformanceSubmissions', () => {
  const actual = jest.requireActual('../../api/fanPerformanceSubmissions');
  return {
    ...actual,
    createFanPerformanceSubmission: jest.fn(),
  };
});

jest.mock('expo-document-picker', () => ({
  getDocumentAsync: jest.fn(),
}));

const submit = createFanPerformanceSubmission as jest.MockedFunction<typeof createFanPerformanceSubmission>;

async function renderSubmit() {
  return await renderWithProviders(
    <FanPerformanceSubmitScreen
      navigation={fakeNavigation() as never}
      route={{ key: 'submit', name: 'FanPerformanceSubmit' } as never}
    />,
  );
}

async function fillRequiredFields(user: ReturnType<typeof userEvent.setup>) {
  await waitFor(() => expect(screen.getByLabelText('Title')).toBeOnTheScreen());
  await user.type(screen.getByLabelText('Title'), 'Reaching Out cover');
  await user.type(screen.getByLabelText('Queen song covered'), 'Reaching Out');
  await user.type(screen.getByLabelText('Performed by'), 'Stage Fan');
}

describe('FanPerformanceSubmitScreen', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    submit.mockReset();
    (DocumentPicker.getDocumentAsync as jest.Mock).mockReset();
  });

  it('gates unsigned visitors', async () => {
    await renderSubmit();
    expect(screen.getByText('Submit a fan performance')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeOnTheScreen();
  });

  it('requires the rights declaration before calling the shared API', async () => {
    const user = userEvent.setup();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    (DocumentPicker.getDocumentAsync as jest.Mock).mockResolvedValue({
      canceled: false,
      assets: [{ uri: 'file://cover.mp3', name: 'cover.mp3', mimeType: 'audio/mpeg', size: 2048 }],
    });

    await renderSubmit();
    await fillRequiredFields(user);
    await user.press(screen.getByTestId(testIds.fanPerformanceSubmitPick));
    await waitFor(() => expect(screen.getByText('cover.mp3')).toBeOnTheScreen());
    await user.press(screen.getByTestId(testIds.fanPerformanceSubmitSend));

    expect(submit).not.toHaveBeenCalled();
    expect(
      screen.getByText('You must confirm this is your own performance and agree to publication.'),
    ).toBeOnTheScreen();
  });

  it('keeps the form when the picker is canceled', async () => {
    const user = userEvent.setup();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    (DocumentPicker.getDocumentAsync as jest.Mock).mockResolvedValue({ canceled: true, assets: [] });

    await renderSubmit();
    await fillRequiredFields(user);
    await user.press(screen.getByTestId(testIds.fanPerformanceSubmitPick));
    await waitFor(() => expect(DocumentPicker.getDocumentAsync).toHaveBeenCalled());
    expect(screen.getByText('Choose an existing audio file')).toBeOnTheScreen();
  });

  it('shows a picker error when the document picker throws', async () => {
    const user = userEvent.setup();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    (DocumentPicker.getDocumentAsync as jest.Mock).mockRejectedValue(new Error('denied'));

    await renderSubmit();
    await waitFor(() => expect(screen.getByTestId(testIds.fanPerformanceSubmitPick)).toBeOnTheScreen());
    await user.press(screen.getByTestId(testIds.fanPerformanceSubmitPick));
    await waitFor(() => expect(screen.getByText('Could not open the file picker.')).toBeOnTheScreen());
  });

  it('picks an existing audio file and submits through the shared API', async () => {
    const user = userEvent.setup();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    (DocumentPicker.getDocumentAsync as jest.Mock).mockResolvedValue({
      canceled: false,
      assets: [{ uri: 'file://cover.mp3', name: 'cover.mp3', mimeType: 'audio/mpeg', size: 2048 }],
    });
    submit.mockResolvedValue({
      id: 'sub-1',
      status: 'Pending',
      title: 'Reaching Out cover',
      submittedAt: '2026-09-04T00:15:00.000Z',
    });

    await renderSubmit();
    await fillRequiredFields(user);
    await user.press(screen.getByTestId(testIds.fanPerformanceSubmitPick));
    await waitFor(() => expect(screen.getByText('cover.mp3')).toBeOnTheScreen());
    await fireEvent(screen.getByLabelText('Rights declaration'), 'valueChange', true);
    await user.press(screen.getByTestId(testIds.fanPerformanceSubmitSend));

    await waitFor(() => expect(submit).toHaveBeenCalledTimes(1));
    expect(submit).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'Reaching Out cover',
        coveredSong: 'Reaching Out',
        performedBy: 'Stage Fan',
        rightsDeclarationAccepted: true,
        audio: { uri: 'file://cover.mp3', name: 'cover.mp3', type: 'audio/mpeg' },
      }),
      'tok',
    );
    expect(screen.getByText('Your fan performance is under review.')).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Submit another performance' }));
    await waitFor(() => expect(screen.getByText('Choose an existing audio file')).toBeOnTheScreen());
  });

  it('surfaces an API error from the shared submission service', async () => {
    const user = userEvent.setup();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    (DocumentPicker.getDocumentAsync as jest.Mock).mockResolvedValue({
      canceled: false,
      assets: [{ uri: 'file://cover.mp3', name: 'cover.mp3', mimeType: 'audio/mpeg', size: 2048 }],
    });
    submit.mockRejectedValue(ApiError.http(429, 'Quota exceeded.'));

    await renderSubmit();
    await fillRequiredFields(user);
    await user.press(screen.getByTestId(testIds.fanPerformanceSubmitPick));
    await waitFor(() => expect(screen.getByText('cover.mp3')).toBeOnTheScreen());
    await fireEvent(screen.getByLabelText('Rights declaration'), 'valueChange', true);
    await user.press(screen.getByTestId(testIds.fanPerformanceSubmitSend));

    await waitFor(() => expect(screen.getByText('Quota exceeded.')).toBeOnTheScreen());
    expect(screen.queryByText('Your fan performance is under review.')).toBeNull();
  });

  it('asks the signed-in member to sign in again when the token is missing', async () => {
    const user = userEvent.setup();
    mockSession.isSignedIn = true;
    mockSession.accessToken = null;
    (DocumentPicker.getDocumentAsync as jest.Mock).mockResolvedValue({
      canceled: false,
      assets: [{ uri: 'file://cover.mp3', name: 'cover.mp3', mimeType: 'audio/mpeg', size: 2048 }],
    });

    await renderSubmit();
    await fillRequiredFields(user);
    await user.press(screen.getByTestId(testIds.fanPerformanceSubmitPick));
    await waitFor(() => expect(screen.getByText('cover.mp3')).toBeOnTheScreen());
    await fireEvent(screen.getByLabelText('Rights declaration'), 'valueChange', true);
    await user.press(screen.getByTestId(testIds.fanPerformanceSubmitSend));

    await waitFor(() => expect(screen.getByText('Sign in to submit a fan performance.')).toBeOnTheScreen());
    expect(submit).not.toHaveBeenCalled();
  });
});
