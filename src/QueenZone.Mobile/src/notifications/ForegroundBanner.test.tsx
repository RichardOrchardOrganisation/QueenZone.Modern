import { screen, userEvent } from '@testing-library/react-native';
import { testIds } from '../test/testIds';
import { renderWithProviders } from '../test/render';
import { ForegroundBanner } from './ForegroundBanner';

describe('ForegroundBanner', () => {
  it('shows category, title, and body and invokes press / dismiss', async () => {
    const user = userEvent.setup();
    const onPress = jest.fn();
    const onDismiss = jest.fn();

    await renderWithProviders(
      <ForegroundBanner
        title="Ranking every studio album"
        body="New reply"
        destination={{ category: 'forumReply', topicId: 1002 }}
        onPress={onPress}
        onDismiss={onDismiss}
      />,
      { navigation: false },
    );

    expect(screen.getByText('Forum')).toBeOnTheScreen();
    expect(screen.getByText('Ranking every studio album')).toBeOnTheScreen();
    expect(screen.getByText('New reply')).toBeOnTheScreen();

    await user.press(screen.getByTestId(testIds.notificationBanner));
    expect(onPress).toHaveBeenCalledTimes(1);

    await user.press(screen.getByLabelText('Dismiss notification'));
    expect(onDismiss).toHaveBeenCalledTimes(1);
  });
});
