import { renderWithProviders } from '../test/render';
import { ArchiveStack, ForumStack, HomeStack, NewsStack, PhotosStack } from './stacks';

jest.mock('@react-navigation/native-stack', () => {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- Jest CJS mock factory.
  const React = require('react');
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- Jest CJS mock factory.
  const { View } = require('react-native');
  return {
    createNativeStackNavigator: () => ({
      Navigator: ({ children }: { children: import('react').ReactNode }) =>
        React.createElement(View, { testID: 'stack-navigator' }, children),
      Screen: () => null,
    }),
  };
});

describe('stack navigators', () => {
  it('each stack reads the active scheme for screen options', async () => {
    for (const Stack of [HomeStack, NewsStack, PhotosStack, ArchiveStack, ForumStack]) {
      const rendered = await renderWithProviders(<Stack />, { navigation: false });
      expect(rendered.getByTestId('stack-navigator')).toBeOnTheScreen();
      await rendered.unmount();
    }
  });
});
