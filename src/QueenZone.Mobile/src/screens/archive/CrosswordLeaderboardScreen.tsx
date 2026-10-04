import { useCallback } from 'react';
import { ScrollView, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { fetchCrosswordLeaderboard } from '../../api/crosswords';
import { useDetailQuery } from '../../hooks/useDetailQuery';
import type { ArchiveStackParamList } from '../../navigation/types';
import { useSession } from '../../session/SessionContext';
import { type, useTheme } from '../../theme';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'CrosswordLeaderboard'>;
export function CrosswordLeaderboardScreen({ route }: Props) {
  const { accessToken, profile } = useSession();
  return <Leaderboard key={`${route.params.id}:${profile?.memberId ?? 'guest'}`} {...route.params} accessToken={accessToken} />;
}
function Leaderboard({ id, title, accessToken }: { id: string; title: string; accessToken: string | null }) {
  const { c } = useTheme();
  const load = useCallback((signal: AbortSignal) => fetchCrosswordLeaderboard(id, signal, accessToken), [id, accessToken]);
  const { data, error, loading, reload } = useDetailQuery(load);
  if (loading) return <LoadingBlock label="Loading leaderboard…" />;
  if (error || !data) return <ErrorBlock message={error ?? 'Leaderboard unavailable.'} onRetry={reload} />;
  return <ScrollView style={{ backgroundColor: c.surfacePage }} contentContainerStyle={{ padding: 20, gap: 16 }}>
    <Text accessibilityRole="header" style={[type.pageTitle, { color: c.textPrimary }]}>{title} leaderboard</Text>
    <Text style={[type.body, { color: c.textSecondary }]}>Fastest 50 clean first solves. Reveals, auto-check and implausibly short times are excluded.</Text>
    {data.viewer ? <Text style={[type.listTitle, { color: c.textPrimary }]}>Your rank: {data.viewer.rank} of {data.totalMembers} · {data.viewer.elapsedSeconds} seconds</Text> : null}
    {!data.top.length ? <Text style={[type.body, { color: c.textSecondary }]}>No ranked solves yet. Be the first!</Text> : null}
    {data.top.map(solve => <View key={solve.rank} style={{ paddingVertical: 12, borderBottomWidth: .5, borderColor: c.hairline }}>
      <Text style={[type.listTitle, { color: c.textPrimary }]}>{solve.rank}. {solve.displayName} · {solve.elapsedSeconds} seconds</Text>
      <Text style={[type.meta, { color: c.textSecondary }]}>{new Date(solve.completedAt).toLocaleDateString()}</Text>
    </View>)}
  </ScrollView>;
}
