import { useCallback, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { fetchQuizSprintLeaderboard } from '../../api';
import { useDetailQuery } from '../../hooks/useDetailQuery';
import type { ArchiveStackParamList } from '../../navigation/types';
import { useSession } from '../../session/SessionContext';
import { testIds } from '../../test/testIds';
import { fonts, palette, radius, space } from '../../theme';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';
import { QuizSprintBoard } from './QuizSprintBoard';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'QuizSprintLeaderboard'>;
type Scope = 'daily' | 'all';

export function QuizSprintLeaderboardScreen(_props: Props) {
  const { accessToken } = useSession();
  const [scope, setScope] = useState<Scope>('daily');
  const load = useCallback(
    (signal: AbortSignal) => fetchQuizSprintLeaderboard(scope, signal, accessToken),
    [scope, accessToken],
  );
  const { data: board, error, loading, reload } = useDetailQuery(load);
  const allTime = scope === 'all';

  return (
    <ScrollView
      testID={testIds.quizSprintLeaderboardScreen}
      style={styles.screen}
      contentContainerStyle={styles.content}
    >
      <Text style={styles.eyebrow}>QUIZ SPRINT</Text>
      <Text style={styles.title}>{allTime ? 'All-time leaderboard' : "Today's leaderboard"}</Text>
      <Text style={styles.subtitle}>
        {allTime
          ? "Each member's single best sixty-second run, ever."
          : "Each member's best sixty-second run today. The board resets at midnight UTC."}
      </Text>
      <View style={styles.tabs}>
        <Tab
          testID={testIds.quizSprintLeaderboardTabDaily}
          label="Today"
          active={!allTime}
          onPress={() => setScope('daily')}
        />
        <Tab
          testID={testIds.quizSprintLeaderboardTabAll}
          label="All time"
          active={allTime}
          onPress={() => setScope('all')}
        />
      </View>
      {loading ? (
        <LoadingBlock label="Loading leaderboard…" />
      ) : error || !board ? (
        <ErrorBlock message={error ?? 'Could not load the leaderboard.'} onRetry={reload} />
      ) : (
        <View style={styles.board}>
          <QuizSprintBoard
            rows={board.top}
            viewer={board.viewer}
            emptyText={allTime ? 'No Sprint scores yet. Be the first on the board.' : 'No scores yet today. Be the first on the board.'}
          />
          <Text style={styles.total}>
            {board.players} member{board.players === 1 ? '' : 's'} ranked{allTime ? '' : ' today'}.
          </Text>
        </View>
      )}
    </ScrollView>
  );
}

type TabProps = { label: string; active: boolean; onPress: () => void; testID: string };

function Tab({ label, active, onPress, testID }: TabProps) {
  return (
    <Pressable
      testID={testID}
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ selected: active }}
      onPress={onPress}
      style={[styles.tab, active && styles.tabActive]}
    >
      <Text style={[styles.tabLabel, active && styles.tabLabelActive]}>{label.toUpperCase()}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: palette.black },
  content: { paddingHorizontal: space.xl, paddingTop: space.xl, paddingBottom: 40, gap: 10 },
  eyebrow: { fontFamily: fonts.titling, fontSize: 10, letterSpacing: 2.2, color: palette.gold },
  title: { fontFamily: fonts.display, fontSize: 34, lineHeight: 36, color: palette.white },
  subtitle: { fontFamily: fonts.body, fontSize: 15, lineHeight: 23, color: 'rgba(255,255,255,0.72)', marginBottom: space.sm },
  tabs: { flexDirection: 'row', gap: space.md, marginBottom: space.sm },
  tab: {
    paddingVertical: 10,
    paddingHorizontal: 20,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.3)',
    borderRadius: radius.sm,
  },
  tabActive: { borderColor: palette.gold, backgroundColor: 'rgba(184,154,74,0.16)' },
  tabLabel: { fontFamily: fonts.bodyMedium, fontSize: 12, letterSpacing: 1, color: 'rgba(255,255,255,0.72)' },
  tabLabelActive: { color: palette.gold },
  board: { marginTop: space.sm },
  total: { fontFamily: fonts.body, fontSize: 13, color: 'rgba(255,255,255,0.6)', marginTop: space.md },
});
