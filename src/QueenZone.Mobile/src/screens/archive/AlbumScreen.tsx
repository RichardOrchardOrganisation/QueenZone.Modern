import { useCallback, useEffect, useLayoutEffect, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { ApiError, fetchAlbumDetail, toPlainText, type AlbumDetail } from '../../api';
import type { ArchiveStackParamList } from '../../navigation/types';
import { ArchiveImage } from '../../ui/ArchiveImage';
import { ListenOnLinks } from '../../ui/ListenOnLinks';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';
import { radius, space, type, useTheme } from '../../theme';
import { testIds } from '../../test/testIds';
import { songSlugFromAlbumTrack } from './SongScreen';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'Album'>;

export function AlbumScreen({ navigation, route }: Props) {
  const { c } = useTheme();
  const { id } = route.params;
  const [album, setAlbum] = useState<AlbumDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadToken, setReloadToken] = useState(0);

  useLayoutEffect(() => {
    navigation.setOptions({ title: album?.name ?? 'Album' });
  }, [navigation, album?.name]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    fetchAlbumDetail(id, controller.signal)
      .then((detail) => {
        setAlbum(detail);
        setLoading(false);
      })
      .catch((err: unknown) => {
        if (err instanceof Error && err.name === 'AbortError') {
          return;
        }
        setAlbum(null);
        setError(err instanceof ApiError ? err.message : 'Something went wrong.');
        setLoading(false);
      });
    return () => controller.abort();
  }, [id, reloadToken]);

  const retry = useCallback(() => setReloadToken((n) => n + 1), []);

  if (loading) {
    return <LoadingBlock label="Loading album…" />;
  }

  if (error || !album) {
    return <ErrorBlock message={error ?? 'Album not found.'} onRetry={retry} />;
  }

  const notes = toPlainText(album.generalNotes);
  const yearMeta = [album.artistName, album.releaseYear != null ? String(album.releaseYear) : null]
    .filter(Boolean)
    .join(' · ');

  return (
    <ScrollView
      style={[styles.scroll, { backgroundColor: c.surfacePage }]}
      contentContainerStyle={styles.content}
    >
      <Text style={[type.eyebrow, { color: c.accentArchive }]}>Discography</Text>
      {album.coverUrl ? (
        <ArchiveImage
          source={{ uri: album.coverUrl }}
          style={styles.cover}
          priority="normal"
          recyclingKey={String(id)}
          label={`${album.name} cover`}
          accessibilityIgnoresInvertColors
        />
      ) : null}
      <Text
        style={[type.articleTitle, { color: c.textPrimary, marginTop: space.lg }]}
        allowFontScaling
        maxFontSizeMultiplier={1.4}
      >
        {album.name}
      </Text>
      {yearMeta ? (
        <Text style={[type.meta, { color: c.textMuted, marginTop: space.md }]}>{yearMeta}</Text>
      ) : null}
      {notes ? (
        <Text style={[type.body, { color: c.textSecondary, marginTop: space.xl }]}>{notes}</Text>
      ) : null}
      <ListenOnLinks links={album.streamingLinks} subject={album.name} testID={testIds.albumListenOn} />
      <Text style={[type.eyebrow, { color: c.textSecondary, marginTop: space.xxl }]}>Track list</Text>
      <View style={{ marginTop: space.md }}>
        {album.songs.map((song, index) => {
          const songSlug = songSlugFromAlbumTrack(song.detailPath);
          const row = (
            <>
              <Text style={[type.meta, { color: c.textMuted, width: 28 }]}>{index + 1}</Text>
              <View style={styles.trackBody}>
                <Text style={[type.listTitle, { color: c.textPrimary }]}>{song.title}</Text>
                {song.isSingle ? (
                  <Text style={[type.meta, { color: c.accentPrimary, marginTop: space.xs }]}>Single</Text>
                ) : null}
                {song.notes ? (
                  <Text style={[type.caption, { color: c.textSecondary, marginTop: space.xs }]}>
                    {toPlainText(song.notes)}
                  </Text>
                ) : null}
              </View>
            </>
          );

          // Track links sit beside the row, not inside its Pressable, so screen readers can reach them.
          const links = (
            <ListenOnLinks
              links={song.streamingLinks}
              subject={song.title}
              compact
              testID={`${testIds.albumTrackListenOn}-${song.songId}`}
            />
          );

          if (!songSlug) {
            return (
              <View key={song.songId} style={[styles.trackItem, { borderTopColor: c.hairline }]}>
                <View style={styles.track}>{row}</View>
                <View style={styles.trackLinks}>{links}</View>
              </View>
            );
          }

          return (
            <View key={song.songId} style={[styles.trackItem, { borderTopColor: c.hairline }]}>
              <Pressable
                onPress={() => navigation.navigate('Song', { slug: songSlug })}
                style={styles.track}
                accessibilityRole="button"
                accessibilityLabel={song.title}
              >
                {row}
              </Pressable>
              <View style={styles.trackLinks}>{links}</View>
            </View>
          );
        })}
      </View>
      <View style={{ height: space.section }} />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  scroll: { flex: 1 },
  content: {
    paddingHorizontal: space.xl,
    paddingTop: space.xl,
    paddingBottom: space.section,
  },
  cover: {
    width: '100%',
    aspectRatio: 1,
    marginTop: space.base,
    borderRadius: radius.xs,
  },
  trackItem: {
    borderTopWidth: StyleSheet.hairlineWidth,
  },
  track: {
    flexDirection: 'row',
    gap: space.sm,
    paddingVertical: space.base,
  },
  trackLinks: {
    // Line up with the title column (28px number + gap) and keep the row's bottom padding.
    marginLeft: 28 + space.sm,
    marginTop: -space.sm,
    marginBottom: space.sm,
  },
  trackBody: {
    flex: 1,
  },
});
