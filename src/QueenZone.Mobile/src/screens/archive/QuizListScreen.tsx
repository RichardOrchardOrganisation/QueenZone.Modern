import { useCallback } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { fetchQuizzesPage, type QuizListItem } from '../../api';
import { usePagedContent } from '../../hooks/usePagedContent';
import type { ArchiveStackParamList } from '../../navigation/types';
import { PageTitleBlock } from '../../ui/PageTitleBlock';
import { PagedListScreen } from '../../ui/PagedListScreen';
import { testIds } from '../../test/testIds';
import { radius, space, type, useTheme } from '../../theme';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'QuizList'>;

export function QuizListScreen({ navigation }: Props) {
  const { c } = useTheme();
  const paged = usePagedContent<QuizListItem>(
    useCallback((page, signal) => fetchQuizzesPage({ page, pageSize: 20, signal }), []),
  );

  return (
    <PagedListScreen
      testID={testIds.quizListScreen}
      paged={paged}
      keyExtractor={(item) => item.id}
      loadingLabel="Loading quizzes…"
      emptyMessage="No quizzes have been published yet."
      ListHeaderComponent={
        <View>
          <PageTitleBlock
            eyebrow="Test yourself"
            title="Quiz"
            subtitle="Multiple-choice quizzes drawn from the Queenzone archive."
          />
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="View the leaderboard"
            onPress={() => navigation.navigate('QuizLeaderboard')}
            style={styles.leaderboardLink}
          >
            <Text style={[type.button, { color: c.accentPrimary }]}>View the leaderboard</Text>
          </Pressable>
        </View>
      }
      renderItem={({ item }) => (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={`Open quiz ${item.title}`}
          onPress={() => navigation.navigate('QuizPlay', { id: item.id })}
          style={({ pressed }) => [styles.row, { borderTopColor: c.hairline, opacity: pressed ? 0.72 : 1 }]}
        >
          <View style={styles.text}>
            <Text style={[type.listTitle, { color: c.textPrimary }]}>{item.title}</Text>
            {item.description ? (
              <Text style={[type.body, { color: c.textSecondary, marginTop: space.xs }]} numberOfLines={2}>
                {item.description}
              </Text>
            ) : null}
            <Text style={[type.meta, { color: c.textMuted, marginTop: space.sm }]}>
              {item.questionCount} question{item.questionCount === 1 ? '' : 's'}
            </Text>
          </View>
        </Pressable>
      )}
    />
  );
}

const styles = StyleSheet.create({
  leaderboardLink: {
    marginHorizontal: space.xl,
    marginBottom: space.md,
    alignSelf: 'flex-start',
  },
  row: {
    paddingVertical: space.base,
    paddingHorizontal: space.xl,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderRadius: radius.xs,
  },
  text: {
    flex: 1,
  },
});
