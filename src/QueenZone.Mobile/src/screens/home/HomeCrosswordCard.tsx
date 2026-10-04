import { useCallback } from 'react';
import { Pressable, Text, View } from 'react-native';
import { fetchCrosswordsPage } from '../../api/crosswords';
import { useHomeSection } from '../../hooks/useHomeSection';
import { useSession } from '../../session/SessionContext';
import { space, type, useTheme } from '../../theme';
import { testIds } from '../../test/testIds';
export function HomeCrosswordCard({ onPlay }: { onPlay: (slug: string) => void }) {
  const { accessToken } = useSession();
  const { c } = useTheme();
  const { view } = useHomeSection(useCallback(async (signal: AbortSignal) => (await fetchCrosswordsPage({ pageSize: 1, signal, accessToken })).items, [accessToken]));
  const puzzle = view.kind === 'content' ? view.data[0] : null;
  if (!puzzle) return null;
  return <View testID={testIds.homeCrosswordCard} style={{ marginHorizontal: space.xl, paddingVertical: space.base, borderBottomWidth: 0.5, borderColor: c.hairline }}>
    <Text style={[type.eyebrow, { color: c.accentPrimary }]}>Queen crossword</Text>
    <Text style={[type.listTitle, { color: c.textPrimary }]}>{puzzle.title}</Text>
    <Text style={[type.meta, { color: c.textSecondary }]}>{puzzle.difficulty} · {puzzle.width}×{puzzle.height}</Text>
    <Pressable accessibilityRole="button" accessibilityLabel={puzzle.progress === 'inProgress' ? 'Continue crossword' : 'Play crossword'} onPress={() => onPlay(puzzle.slug)} style={{ minHeight: 44, justifyContent: 'center' }}>
      <Text style={[type.button, { color: c.accentPrimary }]}>{puzzle.progress === 'inProgress' ? 'Continue crossword' : 'Play crossword'}</Text>
    </Pressable>
  </View>;
}
