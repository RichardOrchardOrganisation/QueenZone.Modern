import { screen, waitFor } from '@testing-library/react-native';
import { fetchBiographyPage } from '../../api';
import type { BiographyChapterListItem } from '../../api/types';
import { pagedResponse } from '../../test/fixtures';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { BiographyScreen } from './BiographyScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchBiographyPage: jest.fn(),
  };
});

const fetchPage = fetchBiographyPage as jest.MockedFunction<typeof fetchBiographyPage>;

function chapterFixture(overrides: Partial<BiographyChapterListItem> = {}): BiographyChapterListItem {
  return {
    id: 1,
    title: 'Early years',
    summary: 'Smile becomes Queen.',
    displaySequence: 1,
    detailPath: '/biography/1',
    ...overrides,
  };
}

describe('BiographyScreen', () => {
  beforeEach(() => {
    fetchPage.mockReset();
    fetchPage.mockResolvedValue(pagedResponse([chapterFixture()]));
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('requests the hook-default page size and shows the chapter summary on the row', async () => {
    await renderWithProviders(
      <BiographyScreen
        navigation={fakeNavigation() as never}
        route={{ key: 'biography', name: 'Biography' } as never}
      />,
      { navigation: false },
    );

    await waitFor(() => expect(screen.getByText('Early years')).toBeOnTheScreen());
    expect(screen.getByText('Smile becomes Queen.')).toBeOnTheScreen();
    expect(screen.getByText('Chapter 1')).toBeOnTheScreen();
    expect(fetchPage).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 20 }));
  });
});
