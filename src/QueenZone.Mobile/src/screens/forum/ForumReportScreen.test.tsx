import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { reportForumPost } from '../../api';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { ForumReportScreen } from './ForumReportScreen';

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return { ...actual, reportForumPost: jest.fn() };
});

const reportForumPostMock = reportForumPost as jest.MockedFunction<typeof reportForumPost>;

function renderReport(navigation = fakeNavigation()) {
  return {
    navigation,
    ...renderWithProviders(
      <ForumReportScreen
        navigation={navigation as never}
        route={{
          key: 'report',
          name: 'ForumReport',
          params: { postId: 42, authorUsername: 'abusive-user', threadId: 1002, threadTitle: 'Thread title' },
        } as never}
      />,
    ),
  };
}

describe('ForumReportScreen', () => {
  beforeEach(() => {
    mockSession.isSignedIn = true;
    mockSession.isRestoring = false;
    mockSession.accessToken = 'token';
    reportForumPostMock.mockReset();
  });

  it('requires a reason and enforces the details limit in the native form', () => {
    renderReport();

    expect(screen.getByRole('button', { name: 'Submit report' })).toBeDisabled();
    expect(screen.getByLabelText('Supporting details').props.maxLength).toBe(1000);
    fireEvent.press(screen.getByRole('radio', { name: /Spam or scams/ }));
    expect(screen.getByRole('button', { name: 'Submit report' })).toBeEnabled();
  });

  it('submits once, confirms success, and returns to the reported post', async () => {
    reportForumPostMock.mockResolvedValue({ reportId: 'report-1', status: 'Open', alreadyReported: false });
    const { navigation } = renderReport();

    fireEvent.press(screen.getByRole('radio', { name: /Harassment or bullying/ }));
    fireEvent.changeText(screen.getByLabelText('Supporting details'), '  Supporting details  ');
    fireEvent.press(screen.getByRole('button', { name: 'Submit report' }));

    await waitFor(() => expect(screen.getByText('Report submitted')).toBeOnTheScreen());
    expect(reportForumPostMock).toHaveBeenCalledTimes(1);
    expect(reportForumPostMock).toHaveBeenCalledWith(
      'token', 42, 'Harassment or bullying', '  Supporting details  ',
    );

    fireEvent.press(screen.getByRole('button', { name: 'Return to thread' }));
    expect(navigation.navigate).toHaveBeenCalledWith('Thread', {
      id: 1002,
      title: 'Thread title',
      postId: 42,
      reportedPostId: 42,
    });
  });

  it('shows an API failure and allows retry', async () => {
    reportForumPostMock.mockRejectedValueOnce(new Error('offline'));
    renderReport();
    fireEvent.press(screen.getByRole('radio', { name: /Other/ }));
    fireEvent.press(screen.getByRole('button', { name: 'Submit report' }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Could not submit this report.'));
    expect(screen.getByRole('button', { name: 'Submit report' })).toBeEnabled();
  });
});
