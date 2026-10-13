import { useCallback, useState } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';
import { ApiError, fetchRandomTrivia, fetchTriviaById, type RandomTrivia } from '../../api';
import type { RouteProp } from '@react-navigation/native';
import type { ArchiveStackParamList } from '../../navigation/types';
import { useHomeSection } from '../../hooks/useHomeSection';
import { testIds } from '../../test/testIds';
import { space, type, useTheme } from '../../theme';
import { ArchiveFooter } from '../../ui/ArchiveFooter';
import { Button } from '../../ui/Button';
import { FeatureBlock } from '../../ui/FeatureBlock';
import { PageTitleBlock } from '../../ui/PageTitleBlock';
import { EmptyBlock, ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';

function trimmedMetaPart(value: string | null | undefined): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

export function triviaMetaLine(fact: RandomTrivia): string | null {
  const parts = [fact.category, fact.difficulty, fact.source]
    .map(trimmedMetaPart)
    .filter((part): part is string => part !== null);
  return parts.length > 0 ? parts.join(' · ') : null;
}

/** Every widget tap gets a fresh session, even when its id matches the previous tap. */
export function TriviaScreen({ route }: { route?: RouteProp<ArchiveStackParamList, 'Trivia'> }) {
  const id = route?.params?.id;
  const requestId = route?.params?.requestId ?? 0;
  return <TriviaSession key={`${id ?? 'random'}:${requestId}`} id={id} />;
}

function TriviaSession({ id }: { id?: number }) {
  const [random, setRandom] = useState(false);
  return <TriviaContent key={random ? 'random' : 'entry'} id={random ? undefined : id} onRandom={() => setRandom(true)} />;
}

function TriviaContent({ id, onRandom }: { id?: number; onRandom: () => void }) {
  const { c } = useTheme();
  const { view, reload, refresh } = useHomeSection(
    useCallback((signal) => {
      if (id === undefined) {
        return fetchRandomTrivia(signal);
      }
      if (!Number.isSafeInteger(id) || id <= 0) {
        return Promise.reject(new ApiError(404, 'This trivia fact is unavailable.'));
      }
      return fetchTriviaById(id, signal);
    }, [id]),
  );
  const [nexting, setNexting] = useState(false);

  const onNext = useCallback(async () => {
    if (id !== undefined) {
      onRandom();
      return;
    }
    setNexting(true);
    try {
      await refresh();
    } finally {
      setNexting(false);
    }
  }, [id, onRandom, refresh]);

  if (view.kind === 'skeleton') {
    return <LoadingBlock label="Loading trivia…" />;
  }

  if (view.kind === 'error') {
    return <ErrorBlock message={view.message} onRetry={reload} />;
  }

  const fact = view.data;
  if (!fact) {
    return (
      <ScrollView
        testID={testIds.triviaScreen}
        style={[styles.scroll, { backgroundColor: c.surfacePage }]}
        contentContainerStyle={styles.empty}
      >
        <PageTitleBlock
          eyebrow="Queen Facts"
          title="Trivia"
          subtitle={id === undefined ? "A random published fact from the Queenzone archive." : "The published fact from your widget."}
        />
        <EmptyBlock message="No trivia facts have been published yet." />
      </ScrollView>
    );
  }

  const meta = triviaMetaLine(fact);

  return (
    <ScrollView
      testID={testIds.triviaScreen}
      style={[styles.scroll, { backgroundColor: c.surfacePage }]}
      contentContainerStyle={styles.content}
    >
      <PageTitleBlock
        eyebrow="Queen Facts"
        title="Trivia"
        subtitle={id === undefined ? "A random published fact from the Queenzone archive." : "The published fact from your widget."}
      />
      <FeatureBlock eyebrow="Queen Trivia" body={fact.text} />
      {meta ? (
        <Text
          testID={testIds.triviaMeta}
          style={[type.meta, { color: c.textMuted, marginHorizontal: space.xl, marginTop: space.md }]}
        >
          {meta}
        </Text>
      ) : null}
      <View style={styles.next}>
        <Button
          testID={testIds.triviaNext}
          label="Next fact"
          onPress={() => {
            void onNext();
          }}
          loading={nexting}
        />
      </View>
      <ArchiveFooter />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  scroll: { flex: 1 },
  content: {
    paddingBottom: space.section,
  },
  empty: {
    flexGrow: 1,
  },
  next: {
    alignSelf: 'flex-start',
    marginTop: space.xl,
    marginHorizontal: space.xl,
  },
});
