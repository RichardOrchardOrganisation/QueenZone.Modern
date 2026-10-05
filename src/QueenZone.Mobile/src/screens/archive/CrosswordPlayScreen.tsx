import type { ReactNode } from 'react';
import { testIds } from '../../test/testIds';
import { useCallback, useLayoutEffect, useState } from 'react';
import { Alert, Modal, Pressable, ScrollView, Share, Switch, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { CrosswordDetail, CrosswordSelection } from '../../api/types';
import { fetchJsonWithOfflineCache } from '../../cache';
import { useDetailQuery } from '../../hooks/useDetailQuery';
import type { ArchiveStackParamList } from '../../navigation/types';
import { useSession } from '../../session/SessionContext';
import { useCrosswordPlay } from '../../crosswords/useCrosswordPlay';
import { CrosswordGrid } from '../../crosswords/CrosswordGrid';
import { CrosswordKeyboard } from '../../crosswords/CrosswordKeyboard';
import * as core from '../../crosswords/core';
import { Button } from '../../ui/Button';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';
import { type, useTheme } from '../../theme';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'CrosswordPlay'>;
export function CrosswordPlayScreen({ route, navigation }: Props) {
  const { slug } = route.params;
  const { accessToken, profile } = useSession();
  const load = useCallback((signal: AbortSignal) => fetchJsonWithOfflineCache<CrosswordDetail>(`/crosswords/by-slug/${encodeURIComponent(slug)}`,
    { cacheKey: `crosswords:detail:${slug}`, signal, invalidateOn: [404] }), [slug]);
  const { data: puzzle, error, loading, reload } = useDetailQuery(load);
  useLayoutEffect(() => { navigation.setOptions({ title: puzzle?.title ?? 'Crossword', headerRight: puzzle ? () => <Button label="Leaderboard" size="sm" variant="ghost" onPress={() => navigation.navigate('CrosswordLeaderboard', { id: puzzle.id, title: puzzle.title })} /> : undefined }); }, [navigation, puzzle]);
  if (loading) return <LoadingBlock label="Loading crossword…" />;
  if (error || !puzzle) return <ErrorBlock message={error ?? 'This crossword is unavailable on this server.'} onRetry={reload} />;
  return <CrosswordSolver key={`${puzzle.id}:${puzzle.playVersion}:${profile?.memberId ?? 'guest'}`} puzzle={puzzle}
    memberId={profile?.memberId ?? null} accessToken={accessToken} onReload={reload} onNext={() => navigation.navigate('CrosswordList')} />;
}

type SolverProps = { puzzle: CrosswordDetail; memberId: string | null; accessToken: string | null; onReload: () => void; onNext: () => void };
export function CrosswordSolver({ puzzle, memberId, accessToken, onReload, onNext }: SolverProps) {
  const { c } = useTheme();
  const play = useCrosswordPlay(puzzle, memberId, accessToken);
  const [showClues, setShowClues] = useState(false);
  const [showReview, setShowReview] = useState(false);
  const [actionMenu, setActionMenu] = useState<'check' | 'reveal' | null>(null);
  const entry = play.model.entries[play.state.entry];
  const disabled = !play.ready || play.busy || play.timer.paused || !!play.completion;
  const connected = play.online && !!puzzle.playVersion && !disabled;
  function confirmReveal(scope: CrosswordSelection['scope']) {
    if (scope === 'cell') { void play.reveal(scope); return; }
    Alert.alert('Reveal this selection?', "This solve won't count for the leaderboard.", [
      { text: 'Cancel', style: 'cancel' }, { text: 'Reveal', style: 'destructive', onPress: () => { void play.reveal(scope); } },
    ]);
  }
  function menu(kind: 'check' | 'reveal') { setActionMenu(kind); }
  let solverContent: ReactNode;
  if (play.completion) {
    solverContent = <CompletionPanel puzzle={puzzle} memberId={memberId} play={play} onNext={onNext} onReview={() => setShowReview(true)} />;
  } else if (play.timer.paused) {
    solverContent = <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center' }}>
      <Text style={[type.pageTitle, { color: c.textPrimary }]}>Paused</Text><Button label="Resume crossword" onPress={play.pause} />
    </View>;
  } else {
    solverContent = <CrosswordGrid puzzle={puzzle} model={play.model} state={play.state} disabled={disabled} onCell={cell => play.select(core.selectCell(play.model, play.state, cell))} />;
  }

  return <View testID={testIds.crosswordPlayScreen} style={{ flex: 1, backgroundColor: c.surfacePage }}>
    {puzzle.archived ? <Text style={[type.meta, { color: c.textSecondary, paddingHorizontal: 12 }]}>Archived crossword · still playable</Text> : null}
    <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-around' }}>
      <Text testID={testIds.crosswordTimer} style={[type.meta, { color: c.textPrimary }]}>{Math.floor(play.seconds / 60)}:{String(play.seconds % 60).padStart(2, '0')}</Text>
      <Button label={play.timer.paused ? 'Resume' : 'Pause'} size="sm" variant="ghost" disabled={!play.ready || !!play.completion} onPress={play.pause} />
      <Button label="Check" size="sm" variant="ghost" disabled={!connected} onPress={() => menu('check')} />
      <Button label="Reveal" size="sm" variant="ghost" disabled={!connected} onPress={() => menu('reveal')} />
      <Button label="Clues" size="sm" variant="ghost" disabled={!play.ready} onPress={() => setShowClues(true)} />
    </View>
    {solverContent}
    <View accessibilityLiveRegion="polite" style={{ paddingHorizontal: 12 }}>
      <Text testID={testIds.crosswordStatus} style={[type.meta, { color: c.textSecondary }]}>{play.status || (!play.online ? 'Offline · letters saved on this device; checks need a connection' : play.pending ? 'Progress waiting to sync' : ' ')}</Text>
      {play.needsAttention ? <Button label="Crossword changed — reload" size="sm" variant="ghost" onPress={onReload} /> : null}
      {play.guest ? <Button label="Keep progress from this device?" size="sm" variant="ghost" onPress={play.keepGuest} /> : null}
    </View>
    {!play.completion ? <>
      <View style={{ flexDirection: 'row', alignItems: 'center', borderTopWidth: 0.5, borderColor: c.hairline }}>
        <Pressable accessibilityRole="button" accessibilityLabel="Previous clue" disabled={disabled} onPress={() => play.select(core.nextEntry(play.model, play.state, -1))} style={{ padding: 12, minWidth: 44 }}><Text style={{ color: c.accentPrimary }}>‹</Text></Pressable>
        <Pressable accessibilityRole="button" accessibilityLabel="Toggle clue direction" disabled={disabled} onPress={() => play.select(core.toggleDirection(play.model, play.state))} style={{ flex: 1, minHeight: 48, justifyContent: 'center' }}>
          <Text testID={testIds.crosswordActiveClue} numberOfLines={3} style={[type.meta, { color: c.textPrimary }]}>{entry.number} {entry.direction}: {entry.clue} {entry.enumeration}</Text>
        </Pressable>
        <Pressable accessibilityRole="button" accessibilityLabel="Next clue" disabled={disabled} onPress={() => play.select(core.nextEntry(play.model, play.state))} style={{ padding: 12, minWidth: 44 }}><Text style={{ color: c.accentPrimary }}>›</Text></Pressable>
      </View>
      <CrosswordKeyboard disabled={disabled} onLetter={letter => play.change(core.typeLetter(play.model, play.state, letter))} onBackspace={() => play.change(core.deleteLetter(play.model, play.state))} />
    </> : null}
    <Modal visible={actionMenu !== null} transparent onRequestClose={() => setActionMenu(null)}>
      <View style={{ flex: 1, justifyContent: 'center', backgroundColor: 'rgba(0,0,0,0.5)', padding: 24 }}>
        <View style={{ backgroundColor: c.surfaceCard, padding: 20, gap: 12 }} accessibilityViewIsModal>
          <Text style={[type.listTitle, { color: c.textPrimary }]}>{actionMenu === 'check' ? 'Check letters' : 'Reveal letters'}</Text>
          {(['cell', 'entry', 'grid'] as const).map(scope => <Button key={scope} testID={`crossword-${actionMenu}-${scope}`} label={`${actionMenu === 'check' ? 'Check' : 'Reveal'} ${scope === 'cell' ? 'letter' : scope === 'entry' ? 'word' : 'grid'}`}
            onPress={() => { const action = actionMenu; setActionMenu(null); if (action === 'check') void play.check(scope); else confirmReveal(scope); }} />)}
          <Button label="Cancel" variant="ghost" onPress={() => setActionMenu(null)} />
        </View>
      </View>
    </Modal>
    <Modal visible={showClues || showReview} animationType="slide" onRequestClose={() => { setShowClues(false); setShowReview(false); }}>
      <View style={{ flex: 1, backgroundColor: c.surfacePage, paddingTop: 44 }}>
        <Button label="Close" onPress={() => { setShowClues(false); setShowReview(false); }} variant="ghost" />
        <ScrollView contentContainerStyle={{ padding: 16 }}>
          <Text style={[type.pageTitle, { color: c.textPrimary }]}>{showReview ? 'Review clues' : 'Clues'}</Text>
          {showReview ? play.review.map(item => <View key={`${item.number}-${item.direction}`} style={{ paddingVertical: 12 }}>
            <Text style={[type.listTitle, { color: c.textPrimary }]}>{item.number} {item.direction}{item.answer ? `: ${item.answer}` : ''}</Text>
            {item.explanation ? <Text style={[type.body, { color: c.textSecondary }]}>{item.explanation}</Text> : null}
          </View>) : play.model.entries.map((clue, index) => <Pressable key={clue.key} accessibilityRole="button"
            accessibilityLabel={`${clue.number} ${clue.direction} ${clue.clue} ${clue.enumeration}`} onPress={() => { play.select(core.jumpToEntry(play.model, play.state, index)); setShowClues(false); }} style={{ paddingVertical: 12, minHeight: 44 }}>
            <Text style={[type.body, { color: c.textPrimary }]}>{clue.number} {clue.direction} · {clue.clue} {clue.enumeration}{core.entryFilled(play.model, play.state, index) ? ' ✓' : ''}</Text>
          </Pressable>)}
          {!showReview ? <View style={{ flexDirection: 'row', alignItems: 'center', gap: 12 }}><Text style={[type.body, { color: c.textPrimary }]}>Auto check</Text><Switch accessibilityLabel="Auto check" value={play.state.autoCheck} disabled={!connected} onValueChange={play.setAutoCheck} /></View> : null}
          {!showReview && connected ? <Button label="Check grid" onPress={() => { void play.check('grid'); setShowClues(false); }} variant="outline" /> : null}
          {play.review.length && !showReview ? <Button label="Review explanations" onPress={() => { setShowClues(false); setShowReview(true); }} variant="ghost" /> : null}
          {core.isFilled(play.model, play.state) && !play.completion ? <Button label="Try finish again" onPress={play.retryFinish} /> : null}
        </ScrollView>
      </View>
    </Modal>
  </View>;
}

function CompletionPanel({ puzzle, memberId, play, onNext, onReview }: Pick<SolverProps, 'puzzle' | 'memberId' | 'onNext'> & {
  play: ReturnType<typeof useCrosswordPlay>; onReview: () => void;
}) {
  const { c } = useTheme();
  if (!play.completion) return null;
  return <View style={{ flex: 1, justifyContent: 'center', padding: 20, gap: 12 }}>
      <Text style={[type.pageTitle, { color: c.textPrimary }]}>Crossword complete</Text>
      <Text style={[type.body, { color: c.textPrimary }]}>{play.completion.elapsedSeconds} seconds · {play.completion.clean ? 'Clean solve ✓' : 'Assisted solve'}</Text>
      {!memberId ? <Text style={[type.body, { color: c.textSecondary }]}>Guest progress is saved on this device. Sign in to keep future solves across devices.</Text> : null}
      <Button label="Share result" onPress={() => { void Share.share({ message: `I finished ${puzzle.title} on QueenZone: ${play.completion?.elapsedSeconds} seconds · ${play.completion?.clean ? 'clean solve' : 'assisted solve'}. https://www.queenzone.org/crosswords/${puzzle.slug}` }).catch(() => {}); }} />
      <Button label="Next crossword" onPress={onNext} />
      {play.review.length ? <Button label="Review clues" variant="outline" onPress={onReview} /> : null}
    </View>;
}
