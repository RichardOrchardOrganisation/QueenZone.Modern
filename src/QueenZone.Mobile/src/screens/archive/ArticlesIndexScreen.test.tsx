import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchArticlesPage, fetchNewsPage } from '../../api';
import { articleItemFixture, pagedResponse } from '../../test/fixtures';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { ArticlesIndexScreen } from './ArticlesIndexScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchArticlesPage: jest.fn(),
    fetchNewsPage: jest.fn(),
  };
});

const fetchPage = fetchArticlesPage as jest.MockedFunction<typeof fetchArticlesPage>;
const fetchNews = fetchNewsPage as jest.MockedFunction<typeof fetchNewsPage>;

async function renderArticles(navigation = fakeNavigation()) {
  return {
    navigation,
    ...(await renderWithProviders(
      <ArticlesIndexScreen
        navigation={navigation as never}
        route={{ key: 'articles', name: 'Articles' } as never}
      />,
      { navigation: false },
    )),
  };
}

describe('ArticlesIndexScreen', () => {
  beforeEach(() => {
    fetchPage.mockReset();
    fetchNews.mockReset();
    fetchPage.mockResolvedValue(pagedResponse([articleItemFixture()]));
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('requests the archive page size and lists articles', async () => {
    await renderArticles();

    await waitFor(() =>
      expect(screen.getByText('Inside the Making of Bohemian Rhapsody')).toBeOnTheScreen(),
    );
    expect(screen.getByText('Articles')).toBeOnTheScreen();
    expect(screen.getByText('1 articles')).toBeOnTheScreen();
    expect(screen.getByText(/Recording/)).toBeOnTheScreen();
    expect(screen.queryByText('104 features · Editorial')).toBeNull();
    expect(screen.queryByText('104 features')).toBeNull();
    expect(fetchPage).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 20 }));
    expect(fetchNews).not.toHaveBeenCalled();
  });

  it('opens Story with the real article id', async () => {
    const { navigation } = await renderArticles();
    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: 'Open article Inside the Making of Bohemian Rhapsody' }),
      ).toBeOnTheScreen(),
    );

    const user = userEvent.setup();
    await user.press(
      screen.getByRole('button', { name: 'Open article Inside the Making of Bohemian Rhapsody' }),
    );
    expect(navigation.navigate).toHaveBeenCalledWith('Story', { id: 101 });
    expect(navigation.navigate).not.toHaveBeenCalledWith('Story', { id: 0 });
    expect(fetchNews).not.toHaveBeenCalled();
  });

  it('titles the archive articles index Articles and shows the empty list', async () => {
    fetchPage.mockResolvedValue(pagedResponse([], 1, 0));
    await renderArticles();

    await waitFor(() => expect(screen.getByText('No articles yet.')).toBeOnTheScreen());
    expect(screen.getByText('Articles')).toBeOnTheScreen();
    expect(screen.queryByText('104 features · Editorial')).toBeNull();
    expect(screen.queryByText('Stories')).toBeNull();
    expect(screen.queryByText('The day Queen stole Live Aid')).toBeNull();
    expect(screen.queryByText('Inside the making of Bohemian Rhapsody')).toBeNull();
  });
});
