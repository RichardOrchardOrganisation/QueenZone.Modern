import { knownStreamingLinks } from './ListenOnLinks';

describe('knownStreamingLinks', () => {
  it('keeps known providers in display order and drops unknown keys', () => {
    expect(
      knownStreamingLinks([
        { provider: 'apple-music', url: 'https://music.apple.com/gb/album/x/1' },
        { provider: 'deezer', url: 'https://www.deezer.com/album/1' },
        { provider: 'spotify', url: 'https://open.spotify.com/album/abc' },
      ]),
    ).toEqual([
      { key: 'spotify', name: 'Spotify', url: 'https://open.spotify.com/album/abc' },
      { key: 'apple-music', name: 'Apple Music', url: 'https://music.apple.com/gb/album/x/1' },
    ]);
  });

  it('drops links whose URL is not on the provider host', () => {
    expect(
      knownStreamingLinks([
        { provider: 'spotify', url: 'https://evil.example/album/abc' },
        { provider: 'apple-music', url: 'http://music.apple.com/gb/album/x/1' },
      ]),
    ).toEqual([]);
  });

  it('treats a missing or empty list as no links', () => {
    expect(knownStreamingLinks(undefined)).toEqual([]);
    expect(knownStreamingLinks(null)).toEqual([]);
    expect(knownStreamingLinks([])).toEqual([]);
  });
});
