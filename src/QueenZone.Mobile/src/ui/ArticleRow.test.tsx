import { Pressable, Text } from 'react-native';
import { screen, userEvent } from '@testing-library/react-native';
import { renderWithProviders } from '../test/render';
import { ArticleRow } from './ArticleRow';

describe('ArticleRow', () => {
  it('keeps the default row press on the whole tile', async () => {
    const onPress = jest.fn();
    await renderWithProviders(
      <ArticleRow title="Queen headline" subtitle="Excerpt" meta="15 Jan 2024" onPress={onPress} />,
      { navigation: false },
    );

    expect(screen.getByText('Queen headline')).toBeOnTheScreen();
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Queen headline' }));
    expect(onPress).toHaveBeenCalledTimes(1);
  });

  it('leaves a leading control outside the title press target', async () => {
    const onOpen = jest.fn();
    const onPlay = jest.fn();
    await renderWithProviders(
      <ArticleRow
        title="Somebody to Love"
        subtitle="Performed by Jane"
        meta="15 Jan 2024 · 5:20"
        hint="Sign in to play"
        leading={
          <Pressable accessibilityRole="button" accessibilityLabel="Play Somebody to Love" onPress={onPlay}>
            <Text>Play</Text>
          </Pressable>
        }
        onPress={onOpen}
        accessibilityLabel="Open Somebody to Love"
      />,
      { navigation: false },
    );

    expect(screen.getByText('Sign in to play')).toBeOnTheScreen();
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Play Somebody to Love' }));
    expect(onPlay).toHaveBeenCalledTimes(1);
    expect(onOpen).not.toHaveBeenCalled();

    await user.press(screen.getByRole('button', { name: 'Open Somebody to Love' }));
    expect(onOpen).toHaveBeenCalledTimes(1);
    expect(onPlay).toHaveBeenCalledTimes(1);
  });

  it('includes decorative leading in the row press target', async () => {
    const onPress = jest.fn();
    await renderWithProviders(
      <ArticleRow
        title="Queen headline"
        subtitle="Excerpt"
        meta="15 Jan 2024"
        leading={<Text testID="news-thumb">Thumb</Text>}
        leadingInteractive={false}
        onPress={onPress}
        accessibilityLabel="Open Queen headline"
      />,
      { navigation: false },
    );

    const user = userEvent.setup();
    await user.press(screen.getByTestId('news-thumb'));
    expect(onPress).toHaveBeenCalledTimes(1);
  });
});
