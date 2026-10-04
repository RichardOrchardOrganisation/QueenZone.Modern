import { useCallback } from 'react';
import { useNavigation } from '@react-navigation/native';
import { ScrollView, Text, View } from 'react-native';
import { fetchMyCrosswords } from '../../api/crosswords';
import { useDetailQuery } from '../../hooks/useDetailQuery';
import { MemberGate } from '../../session/MemberGate';
import { useSession } from '../../session/SessionContext';
import { type, useTheme } from '../../theme';
import { Button } from '../../ui/Button';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';
import { openCrosswordLink, type CrosswordNavigation } from '../../crosswords/deepLink';

export function MyCrosswordsScreen() {
  const { profile, accessToken } = useSession();
  return <MemberGate title="My crosswords"><History key={profile?.memberId ?? 'guest'} accessToken={accessToken} /></MemberGate>;
}
function History({ accessToken }: { accessToken: string | null }) {
  const { c } = useTheme();
  const navigation = useNavigation<CrosswordNavigation>();
  const load = useCallback((signal: AbortSignal) => accessToken ? fetchMyCrosswords(accessToken, signal) : Promise.reject(new Error('Sign in to see your completed crosswords.')), [accessToken]);
  const { data, loading, error, reload } = useDetailQuery(load);
  if (loading) return <LoadingBlock label="Loading your crosswords…" />;
  if (error || !data) return <ErrorBlock message={error ?? 'Your crosswords are unavailable.'} onRetry={reload} />;
  return <ScrollView style={{ backgroundColor: c.surfacePage }} contentContainerStyle={{ padding: 20, gap: 16 }}>
    <Text accessibilityRole="header" style={[type.pageTitle, { color: c.textPrimary }]}>My crosswords</Text>
    <Text style={[type.listTitle, { color: c.textPrimary }]}>{data.totalCompleted} completed · {data.weeklyStreak} consecutive weeks</Text>
    <Text style={[type.body, { color: c.textSecondary }]}>Weeks start Monday at midnight UTC. Your streak remains active while the current week is in progress.</Text>
    {!data.items.length ? <Text style={[type.body, { color: c.textSecondary }]}>No completed crosswords yet.</Text> : null}
    {data.items.map(puzzle => <View key={puzzle.id} style={{ gap: 8 }}><Text style={[type.body, { color: c.textPrimary }]}>
      {puzzle.title} · {puzzle.elapsedSeconds} seconds · {puzzle.clean ? 'Clean solve ✓' : 'Assisted solve'} · {new Date(puzzle.completedAt).toLocaleDateString()}
      </Text>{puzzle.playable && puzzle.slug ? <Button label={`Play ${puzzle.title}`} variant="ghost" onPress={() => openCrosswordLink(navigation, `queenzone://crosswords/${puzzle.slug!}`)} /> : <Text style={[type.meta, { color: c.textSecondary }]}>Currently unavailable</Text>}
    </View>)}
  </ScrollView>;
}
