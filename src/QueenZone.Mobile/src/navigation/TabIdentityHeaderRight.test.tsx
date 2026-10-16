import { screen, userEvent } from '@testing-library/react-native';
import { createMockSession } from '../test/mockSession';
import { nestedTabParams } from './nestedTab';
import { renderWithProviders } from '../test/render';
import { testIds } from '../test/testIds';
import { TabIdentityHeaderRight, tabIdentityHandlers } from './TabIdentityHeaderRight';

const mockSession = createMockSession();
const mockUnread = { count: 0 };

jest.mock('../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../screens/messages/useUnreadConversationCount', () => ({
  useUnreadConversationCount: () => mockUnread.count,
}));

async function renderIdentity(
  props: { onProfilePress?: () => void; onMessagesPress?: () => void } = {},
) {
  const onProfilePress = props.onProfilePress ?? jest.fn();
  const onMessagesPress = props.onMessagesPress ?? jest.fn();
  return {
    onProfilePress,
    onMessagesPress,
    ...(await renderWithProviders(
      <TabIdentityHeaderRight onProfilePress={onProfilePress} onMessagesPress={onMessagesPress} />,
      { navigation: false },
    )),
  };
}

describe('TabIdentityHeaderRight', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.displayName = null;
    mockUnread.count = 0;
  });

  it('shows the signed-out affordance and no messages icon', async () => {
    await renderIdentity();

    expect(screen.getByTestId(testIds.tabIdentityHeader)).toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.tabMasthead)).not.toBeOnTheScreen();
    expect(screen.getByLabelText('Profile')).toBeOnTheScreen();
    expect(screen.getByText('·')).toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.homeMessages)).not.toBeOnTheScreen();
  });

  it('shows the messages icon without a badge when signed in with zero unread', async () => {
    mockSession.isSignedIn = true;
    mockSession.displayName = 'Contract Member';
    mockUnread.count = 0;
    await renderIdentity();

    expect(screen.getByLabelText('Profile')).toBeOnTheScreen();
    expect(screen.getByLabelText('Messages')).toBeOnTheScreen();
    expect(screen.getByText('CM')).toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.homeMessagesUnread)).not.toBeOnTheScreen();
  });

  it('badges the messages icon, not the avatar, when signed in with waiting PMs', async () => {
    mockSession.isSignedIn = true;
    mockSession.displayName = 'Contract Member';
    mockUnread.count = 3;
    await renderIdentity();

    expect(screen.getByLabelText('Profile')).toBeOnTheScreen();
    expect(screen.getByLabelText('Messages, 3 unread conversations')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.homeMessagesUnread, { includeHiddenElements: true })).toBeOnTheScreen();
    expect(screen.getByText('3', { includeHiddenElements: true })).toBeOnTheScreen();
  });

  it('caps the messages badge at 99+', async () => {
    mockSession.isSignedIn = true;
    mockSession.displayName = 'Contract Member';
    mockUnread.count = 100;
    await renderIdentity();

    expect(screen.getByLabelText('Messages, 100 unread conversations')).toBeOnTheScreen();
    expect(screen.getByText('99+', { includeHiddenElements: true })).toBeOnTheScreen();
  });

  it('hides the messages icon when signed out even if a count leaked', async () => {
    mockSession.isSignedIn = false;
    mockUnread.count = 4;
    await renderIdentity();

    expect(screen.getByLabelText('Profile')).toBeOnTheScreen();
    expect(screen.getByText('·')).toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.homeMessages)).not.toBeOnTheScreen();
    expect(screen.queryByText('4')).not.toBeOnTheScreen();
  });

  it('opens messages and profile from the cluster', async () => {
    mockSession.isSignedIn = true;
    mockSession.displayName = 'Contract Member';
    const { onProfilePress, onMessagesPress } = await renderIdentity();
    const user = userEvent.setup();

    await user.press(screen.getByTestId(testIds.homeMessages));
    await user.press(screen.getByTestId(testIds.homeProfile));

    expect(onMessagesPress).toHaveBeenCalledTimes(1);
    expect(onProfilePress).toHaveBeenCalledTimes(1);
  });

  it('opens messages when the unread badge is pressed', async () => {
    mockSession.isSignedIn = true;
    mockSession.displayName = 'Contract Member';
    mockUnread.count = 3;
    const { onMessagesPress } = await renderIdentity();
    const user = userEvent.setup();

    await user.press(screen.getByText('3', { includeHiddenElements: true }));

    expect(onMessagesPress).toHaveBeenCalledTimes(1);
  });

  it('routes mail and avatar to HomeTab Inbox and Profile', () => {
    const navigation = { navigate: jest.fn() };
    const { onMessagesPress, onProfilePress } = tabIdentityHandlers(navigation);

    onMessagesPress();
    onProfilePress();

    expect(navigation.navigate).toHaveBeenCalledWith('HomeTab', nestedTabParams('Inbox'));
    expect(navigation.navigate).toHaveBeenCalledWith('HomeTab', { screen: 'Profile' });
  });
});
