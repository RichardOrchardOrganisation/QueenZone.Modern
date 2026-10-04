import { useCallback, useEffect, useLayoutEffect, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { ApiError, fetchSongDetail, toPlainText, type SongDetail } from '../../api';
import type { ArchiveStackParamList } from '../../navigation/types';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';
import { space, type, useTheme } from '../../theme';
import { testIds } from '../../test/testIds';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'Song'>;

function slugFromDetailPath(path: string | null | undefined): string | null {
  if (!path) {
    return null;
  }
  const prefix = '/songs/';
  if (!path.startsWith(prefix)) {
    return null;
  }
  const slug = path.slice(prefix.length);
  return slug.length > 0 ? slug : null;
}

export function songSlugFromAlbumTrack(detailPath: string | null | undefined): string | null {
  return slugFromDetailPath(detailPath);
}

export function SongScreen({ navigation, route }: Props) {
  const { c } = useTheme();
  const { slug } = route.params;
  const [song, setSong] = useState<SongDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadToken, setReloadToken] = useState(0);

  useLayoutEffect(() => {
    navigation.setOptions({ title: song?.title ?? 'Song' });
  }, [navigation, song?.title]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    fetchSongDetail(slug, controller.signal)
      .then((detail) => {
        setSong(detail);
        setLoading(false);
      })
      .catch((err: unknown) => {
        if (err instanceof Error && err.name === 'AbortError') {
          return;
        }
        setSong(null);
        setError(err instanceof ApiError ? err.message : 'Something went wrong.');
        setLoading(false);
      });
    return () => controller.abort();
  }, [slug, reloadToken]);

  const retry = useCallback(() => setReloadToken((n) => n + 1), []);

  if (loading) {
    return <LoadingBlock label="Loading song…" />;
  }

  if (error || !song) {
    return <ErrorBlock message={error ?? 'Song not found.'} onRetry={retry} />;
  }

  const lyrics = song.lyrics ? toPlainText(song.lyrics) : '';

  return (
    <ScrollView
      testID={testIds.songScreen}
      style={[styles.scroll, { backgroundColor: c.surfacePage }]}
      contentContainerStyle={styles.content}
    >
      <Text style={[type.eyebrow, { color: c.accentArchive }]}>Song</Text>
      <Text
        style={[type.articleTitle, { color: c.textPrimary, marginTop: space.lg }]}
        allowFontScaling
        maxFontSizeMultiplier={1.4}
      >
        {song.title}
      </Text>
      <Text style={[type.meta, { color: c.textMuted, marginTop: space.md }]}>
        {song.appearances.length} {song.appearances.length === 1 ? 'appearance' : 'appearances'}
      </Text>
      <Text style={[type.eyebrow, { color: c.textSecondary, marginTop: space.xxl }]}>Appearances</Text>
      <View style={{ marginTop: space.md }}>
        {song.appearances.map((appearance) => (
          <Pressable
            key={`${appearance.albumId}-${appearance.albumName}`}
            onPress={() => navigation.navigate('Album', { id: appearance.albumId })}
            style={[styles.row, { borderTopColor: c.hairline }]}
          >
            <Text style={[type.listTitle, { color: c.textPrimary }]}>{appearance.albumName}</Text>
            {appearance.releaseYear != null ? (
              <Text style={[type.meta, { color: c.textMuted, marginTop: space.xs }]}>
                {String(appearance.releaseYear)}
              </Text>
            ) : null}
            {appearance.isSingle ? (
              <Text style={[type.meta, { color: c.accentPrimary, marginTop: space.xs }]}>Single</Text>
            ) : null}
            {appearance.notes ? (
              <Text style={[type.caption, { color: c.textSecondary, marginTop: space.xs }]}>
                {toPlainText(appearance.notes)}
              </Text>
            ) : null}
          </Pressable>
        ))}
      </View>
      {lyrics ? (
        <>
          <Text style={[type.eyebrow, { color: c.textSecondary, marginTop: space.xxl }]}>Lyrics</Text>
          <Text style={[type.body, { color: c.textSecondary, marginTop: space.md }]}>{lyrics}</Text>
        </>
      ) : null}
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
  row: {
    paddingVertical: space.base,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
});
