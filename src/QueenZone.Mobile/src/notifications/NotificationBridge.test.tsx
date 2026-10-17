import { act, screen, userEvent, waitFor } from '@testing-library/react-native';
import * as Notifications from 'expo-notifications';
import { testIds } from '../test/testIds';
import { renderWithProviders } from '../test/render';
import { NotificationBridge } from './NotificationBridge';

const mockSession = {
  isSignedIn: true,
  isRestoring: false,
  displayName: 'Contract Member',
  accessToken: 'token',
  profile: null,
};

const mockNavigate = jest.fn();

jest.mock('../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('@react-navigation/native', () => {
  const actual = jest.requireActual('@react-navigation/native') as Record<string, unknown>;
  return {
    ...actual,
    useNavigation: () => ({ navigate: mockNavigate }),
  };
});

const addNotificationResponseReceivedListener =
  Notifications.addNotificationResponseReceivedListener as jest.MockedFunction<
    typeof Notifications.addNotificationResponseReceivedListener
  >;
const addNotificationReceivedListener = Notifications.addNotificationReceivedListener as jest.MockedFunction<
  typeof Notifications.addNotificationReceivedListener
>;
const getLastNotificationResponseAsync = Notifications.getLastNotificationResponseAsync as jest.MockedFunction<
  typeof Notifications.getLastNotificationResponseAsync
>;
const setNotificationHandler = Notifications.setNotificationHandler as jest.MockedFunction<
  typeof Notifications.setNotificationHandler
>;

const conversationId = '11111111-2222-3333-4444-555555555555';

function notification(data: Record<string, unknown>, identifier: string, title = 'Title', body = 'Body') {
  return {
    date: 1,
    request: {
      identifier,
      content: { title, body, data },
    },
  } as unknown as Notifications.Notification;
}

function response(data: Record<string, unknown>, identifier: string): Notifications.NotificationResponse {
  return {
    actionIdentifier: Notifications.DEFAULT_ACTION_IDENTIFIER,
    notification: notification(data, identifier),
  };
}

function iosNotification(contract: Record<string, unknown>, identifier: string, title = 'Title', body = 'Body') {
  return {
    date: 1,
    request: {
      identifier,
      content: { title, body, data: {} },
      trigger: {
        type: 'push',
        payload: {
          aps: { alert: { title, body }, sound: 'default' },
          ...contract,
        },
      },
    },
  } as unknown as Notifications.Notification;
}

function iosResponse(contract: Record<string, unknown>, identifier: string): Notifications.NotificationResponse {
  return {
    actionIdentifier: Notifications.DEFAULT_ACTION_IDENTIFIER,
    notification: iosNotification(contract, identifier),
  };
}

describe('NotificationBridge', () => {
  let responseListener: ((value: Notifications.NotificationResponse) => void) | undefined;
  let receivedListener: ((value: Notifications.Notification) => void) | undefined;

  beforeEach(() => {
    mockNavigate.mockReset();
    mockSession.isRestoring = false;
    mockSession.isSignedIn = true;
    responseListener = undefined;
    receivedListener = undefined;
    addNotificationResponseReceivedListener.mockImplementation((cb) => {
      responseListener = cb;
      return { remove: jest.fn() };
    });
    addNotificationReceivedListener.mockImplementation((cb) => {
      receivedListener = cb;
      return { remove: jest.fn() };
    });
    getLastNotificationResponseAsync.mockReset().mockResolvedValue(null);
    setNotificationHandler.mockClear();
  });

  it('opens a forum thread from a cold-start tap', async () => {
    getLastNotificationResponseAsync.mockResolvedValue(
      response({ category: 'forumReply', topicId: '1002', postId: '9' }, 'cold-forum'),
    );

    await renderWithProviders(<NotificationBridge />, { navigation: false });

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledTimes(1));
    expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'ForumTab',
      params: { screen: 'Thread', params: { id: 1002, postId: 9 }, initial: false },
    });
    expect(setNotificationHandler).toHaveBeenCalled();
  });

  it('opens a conversation from a background tap', async () => {
    await renderWithProviders(<NotificationBridge />, { navigation: false });
    await act(async () => {
      await Promise.resolve();
    });

    await act(async () => {
      responseListener?.(response({ category: 'privateMessage', conversationId }, 'warm-pm'));
    });

    expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'HomeTab',
      params: { screen: 'Conversation', params: { id: conversationId }, initial: false },
    });
  });

  it('opens a news article from an iOS APNs cold-start tap', async () => {
    getLastNotificationResponseAsync.mockResolvedValue(iosResponse({ category: 'news', articleId: '1003' }, 'ios-cold-news'));

    await renderWithProviders(<NotificationBridge />, { navigation: false });

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledTimes(1));
    expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'NewsTab',
      params: { screen: 'Story', params: { id: 1003 }, initial: false },
    });
  });

  it('opens a news article from an iOS APNs background tap', async () => {
    await renderWithProviders(<NotificationBridge />, { navigation: false });
    await act(async () => {
      await Promise.resolve();
    });

    await act(async () => {
      responseListener?.(iosResponse({ category: 'news', articleId: '1003' }, 'ios-warm-news'));
    });

    expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'NewsTab',
      params: { screen: 'Story', params: { id: 1003 }, initial: false },
    });
  });

  it('opens the news listing from an iOS APNs tap when articleId is missing', async () => {
    await renderWithProviders(<NotificationBridge />, { navigation: false });
    await act(async () => {
      await Promise.resolve();
    });

    await act(async () => {
      responseListener?.(iosResponse({ category: 'news' }, 'ios-news-list'));
    });

    expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'NewsTab',
      params: { screen: 'NewsIndex', params: { refreshAt: expect.any(Number) }, initial: false },
    });
  });

  it('opens a news article from the in-app foreground banner', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<NotificationBridge />, { navigation: false });
    await act(async () => {
      await Promise.resolve();
    });

    await act(async () => {
      receivedListener?.(
        notification({ category: 'news', articleId: '1003' }, 'fg-news', 'QueenZone modernisation begins', 'New article published.'),
      );
    });

    expect(screen.getByText('News')).toBeOnTheScreen();
    expect(screen.getByText('QueenZone modernisation begins')).toBeOnTheScreen();

    await user.press(screen.getByTestId(testIds.notificationBanner));

    expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'NewsTab',
      params: { screen: 'Story', params: { id: 1003 }, initial: false },
    });
  });

  it('opens a news article from an iOS APNs in-app foreground banner', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<NotificationBridge />, { navigation: false });
    await act(async () => {
      await Promise.resolve();
    });

    await act(async () => {
      receivedListener?.(
        iosNotification(
          { category: 'news', articleId: '1003' },
          'ios-fg-news',
          'QueenZone modernisation begins',
          'New article published.',
        ),
      );
    });

    expect(screen.getByText('News')).toBeOnTheScreen();
    expect(screen.getByText('QueenZone modernisation begins')).toBeOnTheScreen();

    await user.press(screen.getByTestId(testIds.notificationBanner));

    expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'NewsTab',
      params: { screen: 'Story', params: { id: 1003 }, initial: false },
    });
  });

  it('waits for session restore before applying a cold-start tap', async () => {
    mockSession.isRestoring = true;
    getLastNotificationResponseAsync.mockResolvedValue(response({ category: 'news', articleId: '88' }, 'cold-wait'));

    const view = await renderWithProviders(<NotificationBridge />, { navigation: false });
    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
    });
    expect(mockNavigate).not.toHaveBeenCalled();

    mockSession.isRestoring = false;
    await view.rerender(<NotificationBridge />);

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledTimes(1));
    expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'NewsTab',
      params: { screen: 'Story', params: { id: 88 }, initial: false },
    });
  });

  it('holds a foreground-banner tap until session restore finishes', async () => {
    mockSession.isRestoring = true;
    const user = userEvent.setup();
    const view = await renderWithProviders(<NotificationBridge />, { navigation: false });
    await act(async () => {
      await Promise.resolve();
    });

    await act(async () => {
      receivedListener?.(notification({ category: 'news', articleId: '88' }, 'fg-wait', 'Held article', 'New article published.'));
    });

    await user.press(screen.getByTestId(testIds.notificationBanner));
    expect(mockNavigate).not.toHaveBeenCalled();

    mockSession.isRestoring = false;
    await view.rerender(<NotificationBridge />);

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledTimes(1));
    expect(mockNavigate).toHaveBeenCalledWith('Tabs', {
      screen: 'NewsTab',
      params: { screen: 'Story', params: { id: 88 }, initial: false },
    });
  });
});
