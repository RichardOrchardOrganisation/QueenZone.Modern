import { screen, userEvent, waitFor, within } from '@testing-library/react-native';
import { KeyboardAvoidingView, Platform, ScrollView } from 'react-native';
import { contactApiUrl } from '../../api/contact';
import { jsonResponse } from '../../test/fixtures';
import { renderWithProviders, testHeaderHeight } from '../../test/render';
import { ContactScreen } from './ContactScreen';

jest.mock('../../config/appConfig', () => ({
  getAppConfig: () => ({ apiBaseUrl: 'http://qz.test', appEnv: 'development', version: '0.1.0' }),
}));

const contactUrl = contactApiUrl('http://qz.test');
const fetchMock = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit?]>();

const contactForm = {
  signedIn: false,
  signedInDisplayName: null,
  requiresContactDetails: true,
  formStamp: 'stamp-1',
  intro: 'Send a private message to the Queenzone admin.',
  confirmationTitle: 'Thank you',
  confirmationMessage: 'We have your message.',
  topics: [
    { value: 'Other', label: 'Other' },
    { value: 'Technical', label: 'Technical problem' },
  ],
  limits: {
    minSubjectLength: 5,
    maxSubjectLength: 200,
    minMessageLength: 20,
    maxMessageLength: 4000,
    maxNameLength: 100,
    maxEmailLength: 256,
  },
};

function renderContact() {
  return renderWithProviders(<ContactScreen />);
}

describe('ContactScreen', () => {
  beforeEach(() => {
    fetchMock.mockReset();
    global.fetch = fetchMock as unknown as typeof fetch;
    fetchMock.mockResolvedValue(jsonResponse(contactForm));
  });

  it('loads the public contact form', async () => {
    renderContact();
    await waitFor(() => expect(screen.getByLabelText('Subject')).toBeOnTheScreen());
    expect(screen.getByText('Send a private message to the Queenzone admin.')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Technical problem' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Send message' })).toBeEnabled();
    expect(fetchMock).toHaveBeenCalledWith(contactUrl, { headers: { Accept: 'application/json' } });
  });

  it('keeps the send action outside the scrolling fields and offsets the keyboard by the header', async () => {
    renderContact();
    await waitFor(() => expect(screen.getByLabelText('Your message')).toBeOnTheScreen());

    const avoiding = screen.UNSAFE_getByType(KeyboardAvoidingView);
    expect(avoiding.props.keyboardVerticalOffset).toBe(Platform.OS === 'ios' ? testHeaderHeight : 0);
    expect(within(screen.UNSAFE_getByType(ScrollView)).queryByRole('button', { name: 'Send message' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Send message' })).toBeOnTheScreen();
  });

  it('submits the form and shows confirmation', async () => {
    fetchMock
      .mockResolvedValueOnce(jsonResponse(contactForm))
      .mockResolvedValueOnce(
        jsonResponse({
          submitted: true,
          confirmationTitle: 'Thank you',
          confirmationMessage: 'We have your message.',
        }),
      );
    renderContact();
    await waitFor(() => expect(screen.getByLabelText('Subject')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Technical problem' }));
    await user.type(screen.getByLabelText('Subject'), 'Login help');
    await user.type(screen.getByLabelText('Your name'), 'Freddie');
    await user.type(screen.getByLabelText('Email address'), 'freddie@qz.test');
    await user.type(screen.getByLabelText('Your message'), 'I cannot sign in on this phone.');
    await user.press(screen.getByRole('button', { name: 'Send message' }));

    await waitFor(() => expect(screen.getByText('We have your message.')).toBeOnTheScreen());
    expect(screen.getByText('Thank you')).toBeOnTheScreen();
    const post = fetchMock.mock.calls[1];
    expect(String(post?.[0])).toBe(contactUrl);
    expect(post?.[1]).toEqual(
      expect.objectContaining({
        method: 'POST',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/json',
        },
      }),
    );
    expect(JSON.parse(String(post?.[1]?.body))).toEqual({
      topic: 'Technical',
      subject: 'Login help',
      message: 'I cannot sign in on this phone.',
      name: 'Freddie',
      email: 'freddie@qz.test',
      formStamp: 'stamp-1',
    });
  });

  it('shows a load error and retries', async () => {
    fetchMock.mockRejectedValueOnce(new Error('Could not load the contact form.')).mockResolvedValueOnce(
      jsonResponse(contactForm),
    );
    renderContact();
    await waitFor(() => expect(screen.getByText('Could not load the contact form.')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Retry loading the contact form' }));
    await waitFor(() => expect(screen.getByLabelText('Subject')).toBeOnTheScreen());
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
