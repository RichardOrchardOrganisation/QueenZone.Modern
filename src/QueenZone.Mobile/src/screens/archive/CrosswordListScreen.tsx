import { testIds } from '../../test/testIds';
import { useCallback, useState } from 'react';
import { Pressable, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { fetchCrosswordsPage } from '../../api/crosswords';
import type { CrosswordListItem } from '../../api/types';
import { usePagedContent } from '../../hooks/usePagedContent';
import { useSession } from '../../session/SessionContext';
import type { ArchiveStackParamList } from '../../navigation/types';
import { PagedListScreen } from '../../ui/PagedListScreen';
import { PageTitleBlock } from '../../ui/PageTitleBlock';
import { type, space, useTheme } from '../../theme';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'CrosswordList'>;
export function CrosswordListScreen({ navigation }: Props) {
  const { c } = useTheme();
  const { accessToken, profile } = useSession();
  const [difficulty, setDifficulty] = useState<'easy' | 'medium' | 'hard' | undefined>();
  const [size, setSize] = useState<'small' | 'large' | undefined>();
  const paged = usePagedContent<CrosswordListItem>(useCallback((page, signal) => fetchCrosswordsPage({ page, pageSize: 20, signal, accessToken, difficulty, size }),
    [accessToken, difficulty, size]), 20, `${profile?.memberId ?? 'guest'}:${difficulty}:${size}`);
  return <PagedListScreen testID={testIds.crosswordListScreen} paged={paged} keyExtractor={item => item.id}
    loadingLabel="Loading crosswords…" emptyMessage="No crosswords match these filters yet."
    ListHeaderComponent={<View><PageTitleBlock eyebrow="Queen crosswords" title="Crosswords" subtitle="Clues from the music, history and people of Queen." />
      <View style={{ paddingHorizontal: space.xl, flexDirection: 'row', flexWrap: 'wrap' }}>
        {(['all', 'easy', 'medium', 'hard'] as const).map(value => <Pressable key={value} accessibilityRole="button"
          accessibilityLabel={`${value} difficulties`} accessibilityState={{ selected: (difficulty ?? 'all') === value }}
          onPress={() => setDifficulty(value === 'all' ? undefined : value)} style={{ minHeight: 44, padding: 10 }}>
          <Text style={[type.meta, { color: (difficulty ?? 'all') === value ? c.accentPrimary : c.textSecondary }]}>{value}</Text>
        </Pressable>)}
        {(['all', 'small', 'large'] as const).map(value => <Pressable key={value} accessibilityRole="button" accessibilityLabel={`${value} grids`}
          accessibilityState={{ selected: (size ?? 'all') === value }} onPress={() => setSize(value === 'all' ? undefined : value)} style={{ minHeight: 44, padding: 10 }}>
          <Text style={[type.meta, { color: (size ?? 'all') === value ? c.accentPrimary : c.textSecondary }]}>{value}</Text>
        </Pressable>)}
      </View></View>}
    renderItem={({ item }) => <Pressable testID={`crossword-list-${item.slug}`} accessibilityRole="button" accessibilityLabel={`Open crossword ${item.title}`}
      onPress={() => navigation.navigate('CrosswordPlay', { slug: item.slug })} style={{ padding: space.xl, borderTopWidth: 0.5, borderColor: c.hairline }}>
      <Text style={[type.listTitle, { color: c.textPrimary }]}>{item.title}</Text>
      <Text style={[type.meta, { color: c.textSecondary }]}>{item.difficulty} · {item.width}×{item.height} · {item.progress === 'completed' ? 'Completed' : item.progress === 'inProgress' ? `In progress ${item.progressPercent ?? 0}%` : 'Not started'}</Text>
    </Pressable>} />;
}
