import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Linking } from 'react-native';
import { fetchSongDetail } from '../../api';
import type { SongDetail } from '../../api/types';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { SongScreen, songSlugFromAlbumTrack } from './SongScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchSongDetail: jest.fn(),
  };
});

const fetchDetail = fetchSongDetail as jest.MockedFunction<typeof fetchSongDetail>;

function songDetailFixture(overrides: Partial<SongDetail> = {}): SongDetail {
  return {
    slug: 'bohemian-rhapsody',
    title: 'Bohemian Rhapsody',
    lyrics: 'Is this the real life<br>Is this just fantasy',
    detailPath: '/songs/bohemian-rhapsody',
    appearances: [
      {
        albumId: 4,
        albumName: 'A Night at the Opera',
        releaseYear: 1975,
        isSingle: true,
        notes: 'Lead single',
        albumPath: '/discography/albums/4/a-night-at-the-opera',
      },
    ],
    related: [],
    ...overrides,
  };
}

function renderSong(slug = 'bohemian-rhapsody') {
  const navigation = fakeNavigation();
  renderWithProviders(
    <SongScreen
      navigation={navigation as never}
      route={{ key: 'song', name: 'Song', params: { slug } } as never}
    />,
    { navigation: false },
  );
  return navigation;
}

describe('SongScreen', () => {
  beforeEach(() => {
    fetchDetail.mockReset();
    fetchDetail.mockResolvedValue(songDetailFixture());
  });

  it('renders title, lyrics, and album appearances', async () => {
    const navigation = renderSong();
    await waitFor(() => expect(screen.getByTestId(testIds.songScreen)).toBeOnTheScreen());
    expect(screen.getByText('Bohemian Rhapsody')).toBeOnTheScreen();
    expect(screen.getByText('A Night at the Opera')).toBeOnTheScreen();
    expect(screen.getByText('Is this the real life\nIs this just fantasy')).toBeOnTheScreen();

    fireEvent.press(screen.getByText('A Night at the Opera'));
    expect(navigation.navigate).toHaveBeenCalledWith('Album', { id: 4 });
  });

  it('shows the resolved header links and each appearance link', async () => {
    const openUrl = jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
    fetchDetail.mockResolvedValue(
      songDetailFixture({
        streamingLinks: [{ provider: 'spotify', url: 'https://open.spotify.com/track/rhapsody' }],
        appearances: [
          {
            albumId: 4,
            albumName: 'A Night at the Opera',
            releaseYear: 1975,
            isSingle: true,
            notes: null,
            albumPath: '/discography/albums/4/a-night-at-the-opera',
            streamingLinks: [{ provider: 'spotify', url: 'https://open.spotify.com/track/rhapsody' }],
          },
          {
            albumId: 9,
            albumName: 'Live Killers',
            releaseYear: 1979,
            isSingle: false,
            notes: null,
            albumPath: '/discography/albums/9/live-killers',
          },
        ],
      }),
    );

    const navigation = renderSong();
    await waitFor(() => expect(screen.getByTestId(testIds.songListenOn)).toBeOnTheScreen());

    expect(screen.getByText('Listen on Spotify')).toBeOnTheScreen();
    expect(screen.queryByText('Listen on Apple Music')).toBeNull();
    expect(screen.getByTestId(`${testIds.songAppearanceListenOn}-4-spotify`)).toBeOnTheScreen();
    expect(screen.queryByTestId(`${testIds.songAppearanceListenOn}-9`)).toBeNull();

    fireEvent.press(screen.getByLabelText('Spotify: listen to Bohemian Rhapsody on A Night at the Opera'));
    await waitFor(() => expect(openUrl).toHaveBeenCalledWith('https://open.spotify.com/track/rhapsody'));
    expect(navigation.navigate).not.toHaveBeenCalled();
    openUrl.mockRestore();
  });
});

describe('songSlugFromAlbumTrack', () => {
  it('reads the slug from an additive album-track detailPath', () => {
    expect(songSlugFromAlbumTrack('/songs/bohemian-rhapsody')).toBe('bohemian-rhapsody');
    expect(songSlugFromAlbumTrack('/discography/albums/4/a-night-at-the-opera')).toBeNull();
    expect(songSlugFromAlbumTrack(null)).toBeNull();
  });
});
