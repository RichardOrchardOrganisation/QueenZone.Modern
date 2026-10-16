import { screen, userEvent } from '@testing-library/react-native';
import { createMockSession } from '../test/mockSession';
import { nestedTabParams } from './nestedTab';
import {
  ForumIndexHeaderRight,
  HeaderBackButton,
  HeaderCloseButton,
  NewsIndexHeaderRight,
  SearchIdentityHeaderRight,
} from './headerButtons';
import { testIds } from '../test/testIds';
import { renderWithProviders } from '../test/render';

const mockOpenSuggestNews = jest.fn();
const mockSession = createMockSession();

jest.mock('../share/news/NewsShare', () => ({
  openSuggestNews: (...args: unknown[]) => mockOpenSuggestNews(...args),
}));

jest.mock('../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../screens/messages/useUnreadConversationCount', () => ({
  useUnreadConversationCount: () => 0,
}));

describe('header dismiss controls', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    mockOpenSuggestNews.mockReset();
  });

  it('exposes a tappable Profile back control', async () => {
    const onPress = jest.fn();
    const user = userEvent.setup();
    await renderWithProviders(<HeaderBackButton testID={testIds.profileBack} onPress={onPress} />, {
      navigation: false,
    });
    await user.press(screen.getByTestId(testIds.profileBack));
    expect(onPress).toHaveBeenCalledTimes(1);
    expect(screen.getByLabelText('Back')).toBeOnTheScreen();
  });

  it('exposes a tappable Sign in close control', async () => {
    const onPress = jest.fn();
    const user = userEvent.setup();
    await renderWithProviders(<HeaderCloseButton testID={testIds.signInClose} onPress={onPress} />, {
      navigation: false,
    });
    await user.press(screen.getByTestId(testIds.signInClose));
    expect(onPress).toHaveBeenCalledTimes(1);
    expect(screen.getByLabelText('Close')).toBeOnTheScreen();
  });

  it('opens Suggest news from the News header', async () => {
    const navigation = { navigate: jest.fn() };
    const user = userEvent.setup();
    await renderWithProviders(<NewsIndexHeaderRight navigation={navigation as never} />, {
      navigation: false,
    });
    await user.press(screen.getByTestId(testIds.newsSuggest));
    expect(mockOpenSuggestNews).toHaveBeenCalledWith(navigation);
  });

  it('appends identity after News suggest and search', async () => {
    mockSession.isSignedIn = false;
    const navigation = { navigate: jest.fn() };
    const user = userEvent.setup();
    await renderWithProviders(<NewsIndexHeaderRight navigation={navigation as never} />, {
      navigation: false,
    });

    expect(screen.getByTestId(testIds.newsSuggest)).toBeOnTheScreen();
    expect(screen.getByLabelText('Search')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.tabIdentityHeader)).toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.tabMasthead)).not.toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.homeMessages)).not.toBeOnTheScreen();

    await user.press(screen.getByLabelText('Search'));
    await user.press(screen.getByTestId(testIds.homeProfile));
    expect(navigation.navigate).toHaveBeenCalledWith('Search');
    expect(navigation.navigate).toHaveBeenCalledWith('HomeTab', { screen: 'Profile' });
  });

  it('appends identity after Forum New and Search', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    const navigation = { navigate: jest.fn() };
    const user = userEvent.setup();
    await renderWithProviders(<ForumIndexHeaderRight navigation={navigation as never} />, {
      navigation: false,
    });

    expect(screen.getByTestId(testIds.forumNewThread)).toBeOnTheScreen();
    expect(screen.getByLabelText('Search')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.tabIdentityHeader)).toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.tabMasthead)).not.toBeOnTheScreen();

    await user.press(screen.getByTestId(testIds.forumNewThread));
    await user.press(screen.getByTestId(testIds.homeMessages));
    expect(navigation.navigate).toHaveBeenCalledWith('Composer', {});
    expect(navigation.navigate).toHaveBeenCalledWith('HomeTab', nestedTabParams('Inbox'));
  });

  it('wraps Photos and Archive search with identity', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    const navigation = { navigate: jest.fn() };
    const user = userEvent.setup();
    await renderWithProviders(
      <SearchIdentityHeaderRight
        navigation={navigation}
        onSearch={() => navigation.navigate('Search')}
      />,
      { navigation: false },
    );

    expect(screen.getByLabelText('Search')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.tabIdentityHeader)).toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.tabMasthead)).not.toBeOnTheScreen();

    await user.press(screen.getByLabelText('Search'));
    await user.press(screen.getByTestId(testIds.homeMessages));
    await user.press(screen.getByTestId(testIds.homeProfile));
    expect(navigation.navigate).toHaveBeenCalledWith('Search');
    expect(navigation.navigate).toHaveBeenCalledWith('HomeTab', nestedTabParams('Inbox'));
    expect(navigation.navigate).toHaveBeenCalledWith('HomeTab', { screen: 'Profile' });
  });
});
