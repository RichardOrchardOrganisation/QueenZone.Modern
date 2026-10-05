import type { ReactNode } from 'react';
import { useCallback, useEffect, useLayoutEffect, useState } from 'react';
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { useHeaderHeight } from '@react-navigation/elements';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { Pause, Play, SkipBack, SkipForward } from 'lucide-react-native';
import { ApiError, fetchFanPerformanceDetail, fetchFanPerformancesPage, toPlainText, type FanPerformance } from '../../api';
import { reportFanPerformance } from '../../api/fanPerformanceSubmissions';
import { useFanPerformancePlayer } from '../../audio/FanPerformancePlayer';
import { formatTrackDuration } from '../../audio/formatDuration';
import { DownloadAction } from '../../downloads/DownloadAction';
import type { ArchiveStackParamList } from '../../navigation/types';
import { useSession } from '../../session/SessionContext';
import { openSignIn } from '../../session/signInNavigation';
import { testIds } from '../../test/testIds';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';
import { Button } from '../../ui/Button';
import { IconButton } from '../../ui/IconButton';
import { space, type, useTheme } from '../../theme';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'FanPerformanceDetail'>;

export function FanPerformanceDetailScreen({ navigation, route }: Props) {
  return <FanPerformancePlayerPanel navigation={navigation} route={route} />;
}

function FanPerformancePlayerPanel({ navigation, route }: Props) {
  const headerHeight = useHeaderHeight();
  const { c } = useTheme();
  const { id } = route.params;
  const { accessToken, isRestoring, isSignedIn } = useSession();
  const player = useFanPerformancePlayer();
  const [track, setTrack] = useState<FanPerformance | null>(null);
  const [queue, setQueue] = useState<FanPerformance[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadToken, setReloadToken] = useState(0);
  const [barWidth, setBarWidth] = useState(0);
  const [reportReason, setReportReason] = useState('');
  const [reportError, setReportError] = useState<string | null>(null);
  const [reportStatus, setReportStatus] = useState<string | null>(null);
  const [reporting, setReporting] = useState(false);

  useLayoutEffect(() => {
    navigation.setOptions({ title: track?.title ?? 'Fan performance' });
  }, [navigation, track?.title]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    Promise.all([
      fetchFanPerformanceDetail(id, controller.signal),
      fetchFanPerformancesPage({ page: 1, pageSize: 100, signal: controller.signal }),
    ])
      .then(([detail, page]) => {
        setTrack(detail);
        setQueue(page.items);
        setLoading(false);
      })
      .catch((err: unknown) => {
        if (err instanceof Error && err.name === 'AbortError') {
          return;
        }
        setTrack(null);
        setError(err instanceof ApiError ? err.message : 'Something went wrong.');
        setLoading(false);
      });
    return () => controller.abort();
  }, [id, reloadToken]);

  const retry = useCallback(() => setReloadToken((n) => n + 1), []);

  if (loading) {
    return <LoadingBlock label="Loading recording…" />;
  }

  if (error || !track) {
    return <ErrorBlock message={error ?? 'Recording not found.'} onRetry={retry} />;
  }

  const active = player.current?.id === track.id;
  const duration = active && player.duration > 0 ? player.duration : (track.durationSeconds ?? 0);
  const currentTime = active ? player.currentTime : 0;
  const progress = duration > 0 ? Math.min(1, currentTime / duration) : 0;
  const description = toPlainText(track.description);

  let playerContent: ReactNode;
  if (accessToken || isSignedIn) {
    playerContent = <View style={styles.player}>
      <Pressable
        accessibilityRole="adjustable"
        accessibilityLabel="Seek"
        accessibilityValue={{
          min: 0,
          max: Math.round(duration),
          now: Math.round(currentTime),
        }}
        onLayout={(event) => setBarWidth(event.nativeEvent.layout.width)}
        onPress={(event) => {
          if (barWidth <= 0 || duration <= 0) {
            return;
          }
          if (!active) {
            player.play(track, queue);
          }
          player.seekTo((event.nativeEvent.locationX / barWidth) * duration);
        }}
        style={[styles.seekTrack, { backgroundColor: c.hairline }]}
      >
        <View
          style={[
            styles.seekFill,
            { width: `${progress * 100}%`, backgroundColor: c.accentPrimary },
          ]}
        />
      </Pressable>
      <View style={styles.times}>
        <Text style={[type.meta, { color: c.textMuted }]}>{formatTrackDuration(currentTime)}</Text>
        <Text style={[type.meta, { color: c.textMuted }]}>{formatTrackDuration(duration)}</Text>
      </View>
      {player.error && active ? (
        <Text style={[type.caption, { color: c.danger, marginTop: space.sm }]}>{player.error}</Text>
      ) : null}
      <View style={styles.controls}>
        <IconButton
          icon={SkipBack}
          accessibilityLabel="Skip back 15 seconds"
          onPress={() => {
            if (!active) {
              player.play(track, queue);
            }
            player.skip(-15);
          }}
        />
        <IconButton
          icon={active && player.playing ? Pause : Play}
          accessibilityLabel={active && player.playing ? 'Pause' : 'Play'}
          tone="accent"
          size={24}
          onPress={() => {
            if (!active) {
              player.play(track, queue);
              return;
            }
            player.toggle();
          }}
        />
        <IconButton
          icon={SkipForward}
          accessibilityLabel="Skip forward 15 seconds"
          onPress={() => {
            if (!active) {
              player.play(track, queue);
            }
            player.skip(15);
          }}
        />
      </View>
      <View style={styles.download}>
        <DownloadAction
          track={track}
          onNeedSignIn={() =>
            openSignIn(navigation, {
              tab: 'ArchiveTab',
              screen: 'FanPerformanceDetail',
              params: { id },
            })
          }
        />
      </View>
    </View>;
  } else if (isRestoring) {
    playerContent = <View style={styles.player} testID={testIds.fanPerformanceSessionRestoring}>
      <Text style={[type.body, { color: c.textSecondary }]}>Restoring your session…</Text>
    </View>;
  } else {
    playerContent = <View style={styles.player}>
      <Text style={[type.body, { color: c.textSecondary }]}>
        Sign in to play this recording. Streaming uses the same member-gated audio path as the
        website.
      </Text>
      <View style={{ marginTop: space.base }}>
        <Button
          label="Sign in"
          onPress={() =>
            openSignIn(navigation, {
              tab: 'ArchiveTab',
              screen: 'FanPerformanceDetail',
              params: { id },
            })
          }
        />
      </View>
    </View>;
  }

  return (
    <KeyboardAvoidingView
      style={[styles.scroll, { backgroundColor: c.surfacePage }]}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      keyboardVerticalOffset={Platform.OS === 'ios' ? headerHeight : 0}
    >
    <ScrollView
      style={styles.scroll}
      contentContainerStyle={styles.content}
      keyboardShouldPersistTaps="handled"
    >
      <Text style={[type.eyebrow, { color: c.accentArchive }]}>Fan performances</Text>
      <Text
        style={[type.articleTitle, { color: c.textPrimary, marginTop: space.lg }]}
        allowFontScaling
        maxFontSizeMultiplier={1.4}
      >
        {track.title}
      </Text>
      <Text style={[type.meta, { color: c.textMuted, marginTop: space.md }]}>
        {[`Performed by ${track.performedBy}`, formatTrackDuration(track.durationSeconds)]
          .filter(Boolean)
          .join(' · ')}
      </Text>
      {track.contributorDisplayName ? (
        <Text style={[type.meta, { color: c.textMuted, marginTop: space.sm }]}>
          Submitted by {track.contributorDisplayName}
        </Text>
      ) : null}
      {description ? (
        <Text style={[type.body, { color: c.textSecondary, marginTop: space.xl }]}>{description}</Text>
      ) : null}

      {playerContent}

      {accessToken || isSignedIn ? (
        <View style={styles.report} testID={testIds.fanPerformanceReport}>
          <Text style={[type.cardTitle, { color: c.textPrimary }]}>Report this performance</Text>
          {reportStatus ? (
            <Text style={[type.body, { color: c.textSecondary }]}>{reportStatus}</Text>
          ) : (
            <>
              <TextInput
                value={reportReason}
                onChangeText={setReportReason}
                accessibilityLabel="Report reason"
                placeholder="Why should this recording be hidden?"
                placeholderTextColor={c.textMuted}
                multiline
                style={[
                  styles.reportInput,
                  { color: c.textPrimary, borderColor: c.border, backgroundColor: c.surfaceCard },
                ]}
              />
              {reportError ? <Text style={[type.body, { color: c.danger }]}>{reportError}</Text> : null}
              <Button
                label="Send report"
                loading={reporting}
                testID={testIds.fanPerformanceReportSend}
                onPress={() => {
                  void (async () => {
                    if (!accessToken) {
                      openSignIn(navigation, {
                        tab: 'ArchiveTab',
                        screen: 'FanPerformanceDetail',
                        params: { id },
                      });
                      return;
                    }
                    if (!reportReason.trim()) {
                      setReportError('A reason is required.');
                      return;
                    }
                    setReporting(true);
                    setReportError(null);
                    try {
                      const created = await reportFanPerformance(id, reportReason.trim(), accessToken);
                      setReportStatus(
                        created.alreadyReported
                          ? 'You have already reported this performance.'
                          : 'Thanks. The admin team will review this performance.',
                      );
                    } catch (err: unknown) {
                      setReportError(err instanceof ApiError ? err.message : 'Could not send the report.');
                    } finally {
                      setReporting(false);
                    }
                  })();
                }}
              />
            </>
          )}
        </View>
      ) : null}
    </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  scroll: { flex: 1 },
  content: {
    paddingHorizontal: space.xl,
    paddingTop: space.xl,
    paddingBottom: space.section,
  },
  player: {
    marginTop: space.xxl,
  },
  report: {
    marginTop: space.section,
    gap: space.sm,
  },
  reportInput: {
    borderWidth: 1,
    borderRadius: 8,
    minHeight: 88,
    paddingHorizontal: space.md,
    paddingVertical: space.sm,
  },
  seekTrack: {
    height: 8,
    borderRadius: 4,
    overflow: 'hidden',
  },
  seekFill: {
    height: 8,
  },
  times: {
    marginTop: space.sm,
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  controls: {
    marginTop: space.base,
    flexDirection: 'row',
    justifyContent: 'center',
    gap: space.xl,
  },
  download: {
    marginTop: space.base,
    alignItems: 'flex-start',
  },
});
