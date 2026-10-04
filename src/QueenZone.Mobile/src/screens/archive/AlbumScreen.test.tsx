import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { fetchAlbumDetail } from '../../api';
import type { AlbumDetail } from '../../api/types';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { AlbumScreen } from './AlbumScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchAlbumDetail: jest.fn(),
  };
});

const fetchDetail = fetchAlbumDetail as jest.MockedFunction<typeof fetchAlbumDetail>;

function albumDetailFixture(overrides: Partial<AlbumDetail> = {}): AlbumDetail {
  return {
    albumId: 7,
    name: 'A Night at the Opera',
    releaseYear: 1975,
    artistName: 'Queen',
    generalNotes: 'Studio album.',
    coverUrl: 'https://cdn.queenzone.org/discography/7-cover.jpg',
    detailPath: '/discography/7',
    songs: [
      {
        songId: 1,
        title: 'Bohemian Rhapsody',
        isSingle: true,
        lyrics: null,
        notes: null,
        detailPath: '/songs/bohemian-rhapsody',
      },
    ],
    ...overrides,
  };
}

function renderAlbum(id = 7) {
  const navigation = fakeNavigation();
  renderWithProviders(
    <AlbumScreen
      navigation={navigation as never}
      route={{ key: 'album', name: 'Album', params: { id } } as never}
    />,
    { navigation: false },
  );
  return navigation;
}

describe('AlbumScreen', () => {
  beforeEach(() => {
    fetchDetail.mockReset();
    fetchDetail.mockResolvedValue(albumDetailFixture());
  });

  it('renders the cover through ArchiveImage at normal priority', async () => {
    renderAlbum();
    await waitFor(() => expect(screen.getByText('Bohemian Rhapsody')).toBeOnTheScreen());

    const cover = screen.getByLabelText('A Night at the Opera cover');
    expect(cover.props.source).toEqual({
      uri: 'https://cdn.queenzone.org/discography/7-cover.jpg',
    });
    expect(cover.props.priority).toBe('normal');
    expect(cover.props.recyclingKey).toBe('7');
    expect(cover.props.accessibilityIgnoresInvertColors).toBe(true);
  });

  it('opens the song screen from a track with detailPath', async () => {
    const navigation = renderAlbum();
    await waitFor(() => expect(screen.getByText('Bohemian Rhapsody')).toBeOnTheScreen());
    fireEvent.press(screen.getByLabelText('Bohemian Rhapsody'));
    expect(navigation.navigate).toHaveBeenCalledWith('Song', { slug: 'bohemian-rhapsody' });
  });
});
