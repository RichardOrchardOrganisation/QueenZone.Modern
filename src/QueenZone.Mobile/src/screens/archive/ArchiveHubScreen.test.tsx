import { archiveDestinations } from '../../content/archiveHub';
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

  it('opens the Quiz route from the Test yourself row', async () => {
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

    await user.press(screen.getByRole('button', { name: /Test yourself\. Quiz Sprint\./ }));
    expect(navigation.navigate).toHaveBeenCalledWith('QuizSprint');
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


describe('complete archive destination navigation', () => {
  it.each([
    ['stories', 'Articles'], ['timeline', 'Timeline'], ['biography', 'Biography'],
    ['discography', 'Discography'], ['tribute', 'FreddieTribute'],
    ['fan-performances', 'FanPerformances'], ['recently-restored', 'PhotosTab'],
    ['trivia', 'Trivia'], ['quiz', 'QuizSprint'], ['about', 'AboutArchive'],
  ])('opens %s through %s', async (id, route) => {
    const row = archiveDestinations.find((destination) => destination.id === id)!;
    const navigation = fakeNavigation();
    renderWithProviders(<ArchiveHubScreen navigation={navigation as never} route={{ key: 'archive', name: 'ArchiveHub' } as never} />, { navigation: false });
    await flushVirtualizedList();
    await userEvent.setup().press(screen.getByRole('button', { name: new RegExp(`${row.kicker}\\. ${row.title}\\.`) }));
    if (id === 'recently-restored') {
      expect(navigation.navigate).toHaveBeenCalledWith(route, { screen: 'PhotoIndex' });
    } else {
      expect(navigation.navigate).toHaveBeenCalledWith(route);
    }
    expect(navigation.navigate).toHaveBeenCalledTimes(1);
  });
});
