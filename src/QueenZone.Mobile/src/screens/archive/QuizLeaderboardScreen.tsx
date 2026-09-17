import { useCallback, useState } from 'react';
import { FlatList, Pressable, StyleSheet, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { fetchQuizLeaderboard, type QuizLeaderboardEntry } from '../../api';
import { useDetailQuery } from '../../hooks/useDetailQuery';
import type { ArchiveStackParamList } from '../../navigation/types';
import { useSession } from '../../session/SessionContext';
import { testIds } from '../../test/testIds';
import { PageTitleBlock } from '../../ui/PageTitleBlock';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';
import { fonts, radius, space, type, useTheme } from '../../theme';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'QuizLeaderboard'>;
type Scope = 'week' | 'all';

export function QuizLeaderboardScreen(_props: Props) {
  const { c } = useTheme();
  const { accessToken } = useSession();
  const [scope, setScope] = useState<Scope>('week');
  const loadLeaderboard = useCallback(
    (signal: AbortSignal) => fetchQuizLeaderboard(scope, signal, accessToken),
    [scope, accessToken],
  );
  const { data: leaderboard, error, loading, reload } = useDetailQuery(loadLeaderboard);

  const isViewerRow = (entry: QuizLeaderboardEntry) =>
    leaderboard?.viewer != null &&
    entry.rank === leaderboard.viewer.rank &&
    entry.displayName === leaderboard.viewer.displayName;

  return (
    <View style={[styles.screen, { backgroundColor: c.surfacePage }]} testID={testIds.quizLeaderboardScreen}>
      <PageTitleBlock
        eyebrow="Standings"
        title="Quiz leaderboard"
        subtitle="Weekly and all-time quiz standings for the Queenzone community."
      />
      <View style={styles.tabs}>
        <Pressable
          testID={testIds.quizLeaderboardTabWeek}
          accessibilityRole="button"
          accessibilityLabel="This week"
          onPress={() => setScope('week')}
          style={[styles.tab, { borderColor: scope === 'week' ? c.accentPrimary : c.hairline }]}
        >
          <Text style={[type.button, { color: scope === 'week' ? c.accentPrimary : c.textSecondary }]}>
            This week
          </Text>
        </Pressable>
        <Pressable
          testID={testIds.quizLeaderboardTabAll}
          accessibilityRole="button"
          accessibilityLabel="All time"
          onPress={() => setScope('all')}
          style={[styles.tab, { borderColor: scope === 'all' ? c.accentPrimary : c.hairline }]}
        >
          <Text style={[type.button, { color: scope === 'all' ? c.accentPrimary : c.textSecondary }]}>
            All time
          </Text>
        </Pressable>
      </View>

      {loading ? (
        <LoadingBlock label="Loading leaderboard…" />
      ) : error || !leaderboard ? (
        <ErrorBlock message={error ?? 'Could not load the leaderboard.'} onRetry={reload} />
      ) : (
        <FlatList
          data={leaderboard.top}
          keyExtractor={(entry) => `${entry.rank}-${entry.displayName}`}
          ListEmptyComponent={
            <Text style={[type.body, { color: c.textSecondary, paddingHorizontal: space.xl }]}>
              No quiz results yet{scope === 'week' ? ' this week' : ''}.
            </Text>
          }
          renderItem={({ item }) => (
            <View
              style={[
                styles.row,
                { borderTopColor: c.hairline },
                isViewerRow(item) ? { backgroundColor: c.accentTintWeak } : null,
              ]}
            >
              <Text style={[type.listTitle, { color: c.textMuted, width: 32 }]}>{item.rank}</Text>
              <Text style={[type.listTitle, { color: c.textPrimary, flex: 1 }]}>{item.displayName}</Text>
              <Text style={[type.listTitle, { color: c.textPrimary, fontFamily: fonts.bodySemi }]}>
                {item.score}
              </Text>
            </View>
          )}
          ListFooterComponent={
            <View style={styles.footer}>
              {leaderboard.viewer && !leaderboard.top.some((entry) => isViewerRow(entry)) ? (
                <Text style={[type.body, { color: c.textSecondary }]}>
                  Your rank: #{leaderboard.viewer.rank} · {leaderboard.viewer.score} points
                </Text>
              ) : null}
              <Text style={[type.meta, { color: c.textMuted, marginTop: space.sm }]}>
                {leaderboard.totalMembers} member{leaderboard.totalMembers === 1 ? '' : 's'} ranked.
              </Text>
            </View>
          }
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1 },
  tabs: {
    flexDirection: 'row',
    gap: space.md,
    paddingHorizontal: space.xl,
    marginBottom: space.md,
  },
  tab: {
    paddingVertical: space.sm,
    paddingHorizontal: space.md,
    borderWidth: 1,
    borderRadius: radius.pill,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.md,
    paddingVertical: space.base,
    paddingHorizontal: space.xl,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
  footer: {
    paddingHorizontal: space.xl,
    paddingVertical: space.xl,
  },
});
