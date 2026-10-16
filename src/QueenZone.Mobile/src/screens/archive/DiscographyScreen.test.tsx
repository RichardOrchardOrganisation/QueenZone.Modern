import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchDiscographyPage } from '../../api';
import type { AlbumListItem } from '../../api/types';
import { pagedResponse } from '../../test/fixtures';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { DiscographyScreen } from './DiscographyScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchDiscographyPage: jest.fn(),
  };
});

const fetchPage = fetchDiscographyPage as jest.MockedFunction<typeof fetchDiscographyPage>;

function albumFixture(overrides: Partial<AlbumListItem> = {}): AlbumListItem {
  return {
    albumId: 7,
    name: 'A Night at the Opera',
    releaseYear: 1975,
    thumbnailUrl: 'https://cdn.queenzone.org/discography/7-thumb.jpg',
    detailPath: '/discography/7',
    ...overrides,
  };
}

async function renderDiscography() {
  const navigation = fakeNavigation();
  return {
    navigation,
    ...(await renderWithProviders(
      <DiscographyScreen
        navigation={navigation as never}
        route={{ key: 'discography', name: 'Discography' } as never}
      />,
      { navigation: false },
    )),
  };
}

describe('DiscographyScreen', () => {
  beforeEach(() => {
    fetchPage.mockReset();
    fetchPage.mockResolvedValue(
      pagedResponse([
        albumFixture(),
        albumFixture({
          albumId: 8,
          name: 'News of the World',
          releaseYear: 1977,
          thumbnailUrl: null,
          detailPath: '/discography/8',
        }),
      ]),
    );
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('renders list thumbs through ArchiveImage with low priority and album recycling keys', async () => {
    const { navigation } = await renderDiscography();
    await waitFor(() => expect(screen.getByText('A Night at the Opera')).toBeOnTheScreen());
    expect(fetchPage).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 20 }));

    const thumb = screen.getByLabelText('A Night at the Opera');
    expect(thumb.props.source).toEqual({
      uri: 'https://cdn.queenzone.org/discography/7-thumb.jpg',
    });
    expect(thumb.props.priority).toBe('low');
    expect(thumb.props.recyclingKey).toBe('7');
    expect(thumb.props.accessibilityIgnoresInvertColors).toBe(true);

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Open album A Night at the Opera' }));
    expect(navigation.navigate).toHaveBeenCalledWith('Album', { id: 7 });
  });
});
