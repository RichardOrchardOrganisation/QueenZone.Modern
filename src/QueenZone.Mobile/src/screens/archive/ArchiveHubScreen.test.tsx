import { screen, userEvent } from '@testing-library/react-native';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { ArchiveHubScreen } from './ArchiveHubScreen';

describe('ArchiveHubScreen', () => {
  it('shows the updated archive summary and photographs destination', async () => {
    const navigation = fakeNavigation();
    renderWithProviders(
      <ArchiveHubScreen
        navigation={navigation as never}
        route={{ key: 'archive', name: 'ArchiveHub' } as never}
      />,
      { navigation: false },
    );
    await flushVirtualizedList();

    expect(
      screen.getByText(
        "Four thousand news articles going back to 2004, a hundred long-form features, tens of thousands of photographs and the community's own history — preserved and catalogued.",
      ),
    ).toBeOnTheScreen();
    expect(screen.getByText('Photographs')).toBeOnTheScreen();
    expect(screen.queryByText('Recently restored')).toBeNull();
  });

  it('labels the articles destination Articles and opens the Articles route', async () => {
    const user = userEvent.setup();
    const navigation = fakeNavigation();
    renderWithProviders(
      <ArchiveHubScreen
        navigation={navigation as never}
        route={{ key: 'archive', name: 'ArchiveHub' } as never}
      />,
      { navigation: false },
    );
    await flushVirtualizedList();
    expect(screen.getByText('Articles')).toBeOnTheScreen();
    expect(screen.queryByText('Stories')).toBeNull();

    await user.press(screen.getByRole('button', { name: /Long-form\. Articles\./ }));
    expect(navigation.navigate).toHaveBeenCalledWith('Articles');
  });

  it('opens the Trivia route from the Queen facts row', async () => {
    const user = userEvent.setup();
    const navigation = fakeNavigation();
    renderWithProviders(
      <ArchiveHubScreen
        navigation={navigation as never}
        route={{ key: 'archive', name: 'ArchiveHub' } as never}
      />,
      { navigation: false },
    );
    await flushVirtualizedList();

    await user.press(screen.getByRole('button', { name: /Queen facts\. Trivia\./ }));
    expect(navigation.navigate).toHaveBeenCalledWith('Trivia');
  });

  it('opens Timeline in-stack from the listing row', async () => {
    const user = userEvent.setup();
    const navigation = fakeNavigation();
    renderWithProviders(
      <ArchiveHubScreen
        navigation={navigation as never}
        route={{ key: 'archive', name: 'ArchiveHub' } as never}
      />,
      { navigation: false },
    );
    await flushVirtualizedList();

    await user.press(screen.getByRole('button', { name: /History\. Timeline\./ }));
    expect(navigation.navigate).toHaveBeenCalledWith('Timeline');
    expect(navigation.navigate).not.toHaveBeenCalledWith(
      'ArchiveTab',
      expect.objectContaining({ screen: 'Timeline' }),
    );
  });
});
