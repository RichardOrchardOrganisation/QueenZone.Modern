import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Linking } from 'react-native';
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

  it('shows album and track Listen on links and opens them outside the app', async () => {
    const openUrl = jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
    fetchDetail.mockResolvedValue(
      albumDetailFixture({
        streamingLinks: [
          { provider: 'apple-music', url: 'https://music.apple.com/gb/album/a-night-at-the-opera/1' },
          { provider: 'spotify', url: 'https://open.spotify.com/album/abc' },
          { provider: 'youtube-music', url: 'https://music.youtube.com/playlist?list=x' },
        ],
        songs: [
          {
            songId: 1,
            title: 'Bohemian Rhapsody',
            isSingle: true,
            lyrics: null,
            notes: null,
            detailPath: '/songs/bohemian-rhapsody',
            streamingLinks: [{ provider: 'spotify', url: 'https://open.spotify.com/track/def' }],
          },
        ],
      }),
    );

    const navigation = renderAlbum();
    await waitFor(() => expect(screen.getByText('Bohemian Rhapsody')).toBeOnTheScreen());

    // Spotify first regardless of server order; unknown providers are ignored.
    expect(screen.getByText('Listen on Spotify')).toBeOnTheScreen();
    expect(screen.getByText('Listen on Apple Music')).toBeOnTheScreen();
    expect(screen.queryByText(/YouTube/i)).toBeNull();

    fireEvent.press(screen.getByLabelText('Listen on Apple Music: A Night at the Opera'));
    await waitFor(() =>
      expect(openUrl).toHaveBeenCalledWith('https://music.apple.com/gb/album/a-night-at-the-opera/1'),
    );

    fireEvent.press(screen.getByLabelText('Spotify: listen to Bohemian Rhapsody'));
    await waitFor(() => expect(openUrl).toHaveBeenCalledWith('https://open.spotify.com/track/def'));
    expect(navigation.navigate).not.toHaveBeenCalled();
    openUrl.mockRestore();
  });

  it('renders no Listen on links when the response has none or omits the field', async () => {
    renderAlbum();
    await waitFor(() => expect(screen.getByText('Bohemian Rhapsody')).toBeOnTheScreen());

    expect(screen.queryByText(/Listen on/)).toBeNull();
    expect(screen.queryByLabelText(/listen to/)).toBeNull();
  });
});
