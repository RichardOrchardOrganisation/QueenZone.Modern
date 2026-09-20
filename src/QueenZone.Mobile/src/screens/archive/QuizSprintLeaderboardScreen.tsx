import { useCallback } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { fetchQuizSprintDaily } from '../../api';
import { useDetailQuery } from '../../hooks/useDetailQuery';
import type { ArchiveStackParamList } from '../../navigation/types';
import { useSession } from '../../session/SessionContext';
import { testIds } from '../../test/testIds';
import { fonts, palette, space } from '../../theme';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';
import { QuizSprintBoard } from './QuizSprintBoard';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'QuizSprintLeaderboard'>;

export function QuizSprintLeaderboardScreen(_props: Props) {
  const { accessToken } = useSession();
  const load = useCallback((signal: AbortSignal) => fetchQuizSprintDaily(signal, accessToken), [accessToken]);
  const { data: board, error, loading, reload } = useDetailQuery(load);

  return (
    <ScrollView
      testID={testIds.quizSprintLeaderboardScreen}
      style={styles.screen}
      contentContainerStyle={styles.content}
    >
      <Text style={styles.eyebrow}>QUIZ SPRINT</Text>
      <Text style={styles.title}>Today&apos;s leaderboard</Text>
      <Text style={styles.subtitle}>Each member&apos;s best sixty-second run today. The board resets at midnight UTC.</Text>
      {loading ? (
        <LoadingBlock label="Loading leaderboard…" />
      ) : error || !board ? (
        <ErrorBlock message={error ?? 'Could not load the leaderboard.'} onRetry={reload} />
      ) : (
        <View style={styles.board}>
          <QuizSprintBoard rows={board.top} viewer={board.viewer} emptyText="No scores yet today. Be the first on the board." />
          <Text style={styles.total}>
            {board.playersToday} member{board.playersToday === 1 ? '' : 's'} ranked today.
          </Text>
        </View>
      )}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: palette.black },
  content: { paddingHorizontal: space.xl, paddingTop: space.xl, paddingBottom: 40, gap: 10 },
  eyebrow: { fontFamily: fonts.titling, fontSize: 10, letterSpacing: 2.2, color: palette.gold },
  title: { fontFamily: fonts.display, fontSize: 34, lineHeight: 36, color: palette.white },
  subtitle: { fontFamily: fonts.body, fontSize: 15, lineHeight: 23, color: 'rgba(255,255,255,0.72)', marginBottom: space.md },
  board: { marginTop: space.sm },
  total: { fontFamily: fonts.body, fontSize: 13, color: 'rgba(255,255,255,0.6)', marginTop: space.md },
});
