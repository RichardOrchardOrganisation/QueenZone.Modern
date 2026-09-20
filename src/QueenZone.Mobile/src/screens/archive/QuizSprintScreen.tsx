import { Image } from 'expo-image';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Pressable, ScrollView, Share, StyleSheet, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import {
  checkQuizSprintAnswer,
  fetchQuizSprintDaily,
  finishQuizSprint,
  startQuizSprint,
  type QuizAnswerSubmission,
  type QuizSprintDailyBoard,
  type QuizSprintResult,
  type QuizSprintRound,
} from '../../api';
import { media } from '../../content/media';
import type { ArchiveStackParamList } from '../../navigation/types';
import { openSignIn } from '../../session/signInNavigation';
import { useSession } from '../../session/SessionContext';
import { testIds } from '../../test/testIds';
import { fonts, palette, radius, space } from '../../theme';
import { ErrorBlock } from '../../ui/ScreenStates';
import { QuizSprintBoard } from './QuizSprintBoard';
import {
  FEEDBACK_MS,
  URGENT_SECONDS,
  nextStreak,
  optionLetter,
  pointsForAnswer,
  secondsLeft,
  streakLabel,
  verdictFor,
} from './quizSprintMeta';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'QuizSprint'>;

type Phase = 'intro' | 'starting' | 'play' | 'finishing' | 'done';

type Feedback = { pickedId: string; correctId: string | null; isCorrect: boolean | null };

const CORRECT_TEXT = '#4F6B4A';
const CORRECT_BG = '#EEF3EC';

export function QuizSprintScreen({ navigation }: Props) {
  const { isSignedIn, accessToken } = useSession();
  const [phase, setPhase] = useState<Phase>('intro');
  const [error, setError] = useState<string | null>(null);
  const [board, setBoard] = useState<QuizSprintDailyBoard | null>(null);
  const [round, setRound] = useState<QuizSprintRound | null>(null);
  const [result, setResult] = useState<QuizSprintResult | null>(null);

  const loadBoard = useCallback(
    (signal?: AbortSignal) =>
      fetchQuizSprintDaily(signal, accessToken)
        .then(setBoard)
        .catch(() => {
          // The board is decoration on the intro; the sprint still works without it.
        }),
    [accessToken],
  );

  useEffect(() => {
    const controller = new AbortController();
    void loadBoard(controller.signal);
    return () => controller.abort();
  }, [loadBoard]);

  const begin = useCallback(async () => {
    setPhase('starting');
    setError(null);
    try {
      setRound(await startQuizSprint());
      setPhase('play');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not start a sprint.');
      setPhase('intro');
    }
  }, []);

  const finish = useCallback(
    async (activeRound: QuizSprintRound, answers: QuizAnswerSubmission[]) => {
      setPhase('finishing');
      try {
        const finished = await finishQuizSprint(activeRound.ticket, answers, accessToken);
        setResult(finished);
        await loadBoard();
        setPhase('done');
      } catch (err: unknown) {
        setError(err instanceof Error ? err.message : 'Could not score that sprint.');
        setPhase('intro');
      }
    },
    [accessToken, loadBoard],
  );

  const signIn = useCallback(
    () => openSignIn(navigation, { tab: 'ArchiveTab', screen: 'QuizSprint' }),
    [navigation],
  );
  const openLeaderboard = useCallback(() => navigation.navigate('QuizSprintLeaderboard'), [navigation]);

  if (phase === 'play' && round) {
    return <SprintPlay round={round} onFinish={(answers) => void finish(round, answers)} />;
  }

  if (phase === 'done' && result) {
    return (
      <SprintResults
        result={result}
        board={board}
        isSignedIn={isSignedIn}
        onAgain={() => {
          setResult(null);
          setRound(null);
          setPhase('intro');
        }}
        onSignIn={signIn}
      />
    );
  }

  const busy = phase === 'starting' || phase === 'finishing';
  const best = board?.top[0] ?? null;

  return (
    <ScrollView
      testID={testIds.quizSprintScreen}
      style={styles.dark}
      contentContainerStyle={styles.introContent}
    >
      <Image source={media.crestWhite} style={styles.introCrest} contentFit="contain" accessibilityElementsHidden />
      <Text style={styles.eyebrow}>THE DAILY CHALLENGE</Text>
      <Text style={styles.introTitle}>Sixty seconds on the clock.</Text>
      <Text style={styles.standfirst}>
        One question at a time, drawn from the Queen archive. Answer fast, build a streak, and take your place on
        today&apos;s leaderboard.
      </Text>

      <View style={styles.dial} accessibilityElementsHidden>
        <View style={styles.dialRing} />
        <Text style={styles.dialNumber}>60</Text>
        <Text style={styles.dialLabel}>SECONDS</Text>
      </View>

      <Text style={styles.rules}>60 SECONDS · UNLIMITED RUNS · NO SKIPS</Text>

      {error ? <ErrorBlock message={error} onRetry={() => void begin()} /> : null}

      <Pressable
        testID={testIds.quizSprintBegin}
        accessibilityRole="button"
        accessibilityLabel="Begin the sprint"
        disabled={busy}
        onPress={() => void begin()}
        style={({ pressed }) => [styles.cta, busy && styles.ctaBusy, pressed && styles.ctaPressed]}
      >
        <Text style={styles.ctaLabel}>{busy ? 'LOADING…' : 'BEGIN THE SPRINT'}</Text>
      </Pressable>

      <Pressable accessibilityRole="button" accessibilityLabel="View the leaderboard" onPress={openLeaderboard}>
        <Text style={styles.link}>VIEW THE LEADERBOARD</Text>
      </Pressable>

      {!isSignedIn ? (
        <View style={styles.signIn}>
          <Text style={styles.note}>Scores are only added to the leaderboard for signed-in members.</Text>
          <Pressable
            testID={testIds.quizSprintSignIn}
            accessibilityRole="button"
            accessibilityLabel="Sign in to be ranked"
            onPress={signIn}
          >
            <Text style={styles.noteLink}>Sign in to be ranked</Text>
          </Pressable>
        </View>
      ) : null}

      {best ? (
        <Text style={styles.best}>
          BEST TODAY · {best.displayName.toUpperCase()} · {best.score}
        </Text>
      ) : null}
    </ScrollView>
  );
}

type PlayProps = {
  round: QuizSprintRound;
  onFinish: (answers: QuizAnswerSubmission[]) => void;
};

function SprintPlay({ round, onFinish }: PlayProps) {
  // Derive remaining time from a stored end timestamp, not a decrementing counter.
  const [endsAt] = useState(() => Date.now() + Math.max(0, round.expiresAtUnixMilliseconds - round.serverNowUnixMilliseconds));
  const [now, setNow] = useState(() => Date.now());
  const [index, setIndex] = useState(0);
  const [score, setScore] = useState(0);
  const [streak, setStreak] = useState(0);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const answersRef = useRef<QuizAnswerSubmission[]>([]);
  const finishedRef = useRef(false);
  const advanceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const complete = useCallback(() => {
    if (finishedRef.current) {
      return;
    }
    finishedRef.current = true;
    if (advanceRef.current) {
      clearTimeout(advanceRef.current);
    }
    onFinish(answersRef.current);
  }, [onFinish]);

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 200);
    return () => clearInterval(timer);
  }, []);

  useEffect(() => () => {
    if (advanceRef.current) {
      clearTimeout(advanceRef.current);
    }
  }, []);

  const remainingMs = Math.max(0, endsAt - now);
  const seconds = secondsLeft(endsAt, now);

  useEffect(() => {
    if (remainingMs <= 0) {
      complete();
    }
  }, [remainingMs, complete]);

  const question = round.questions[index];

  const pick = useCallback(
    async (optionId: string) => {
      if (feedback || finishedRef.current || !question) {
        return;
      }
      setFeedback({ pickedId: optionId, correctId: null, isCorrect: null });
      answersRef.current = [...answersRef.current, { questionId: question.id, selectedOptionId: optionId }];

      let check: Awaited<ReturnType<typeof checkQuizSprintAnswer>> | null = null;
      try {
        check = await checkQuizSprintAnswer(round.ticket, question.id, optionId);
      } catch {
        // No feedback if the check fails; the server still scores the finished run.
      }
      if (finishedRef.current) {
        return;
      }
      if (check) {
        setFeedback({ pickedId: optionId, correctId: check.correctOptionId, isCorrect: check.isCorrect });
        setScore((value) => value + pointsForAnswer(check.isCorrect, streak));
        setStreak(nextStreak(check.isCorrect, streak));
      }
      advanceRef.current = setTimeout(() => {
        setFeedback(null);
        if (index + 1 >= round.questions.length) {
          complete();
        } else {
          setIndex(index + 1);
        }
      }, FEEDBACK_MS);
    },
    [complete, feedback, index, question, round, streak],
  );

  if (!question) {
    return null;
  }

  const urgent = seconds <= URGENT_SECONDS;

  return (
    <View testID={testIds.quizSprintPlay} style={styles.light}>
      <View style={styles.track}>
        <View style={[styles.fill, { width: `${Math.min(100, (remainingMs / (round.durationSeconds * 1000)) * 100)}%` }]} />
      </View>
      <View style={styles.status} accessibilityRole="timer">
        <View style={styles.statusLeft}>
          <Text style={[styles.clock, urgent && styles.clockUrgent]}>{seconds}</Text>
          <Text style={styles.statusLabel}>SECONDS LEFT</Text>
        </View>
        <View style={styles.statusRight}>
          <Text style={[styles.streak, streak >= 3 && styles.streakHot]}>{streakLabel(streak)}</Text>
          <View style={styles.scoreRow}>
            <Text style={styles.scoreValue}>{score}</Text>
            <Text style={styles.statusLabel}>POINTS</Text>
          </View>
        </View>
      </View>

      <ScrollView contentContainerStyle={styles.questionArea}>
        <Text style={styles.questionNumber}>QUESTION {String(index + 1).padStart(2, '0')}</Text>
        <Text accessibilityRole="header" style={styles.prompt}>
          {question.text}
        </Text>
        {question.options.map((option, optionIndex) => {
          const picked = feedback?.pickedId === option.id;
          const revealedCorrect = feedback?.correctId === option.id;
          const wrong = picked && feedback?.isCorrect === false;
          return (
            <Pressable
              key={option.id}
              testID={testIds.quizSprintOption}
              accessibilityRole="button"
              accessibilityLabel={`${optionLetter(optionIndex)}. ${option.text}`}
              disabled={feedback != null}
              onPress={() => void pick(option.id)}
              style={[
                styles.answer,
                revealedCorrect && styles.answerCorrect,
                wrong && styles.answerWrong,
              ]}
            >
              <Text style={[styles.letter, revealedCorrect && { color: CORRECT_TEXT }, wrong && { color: palette.burgundy }]}>
                {optionLetter(optionIndex)}
              </Text>
              <Text style={[styles.answerText, wrong && { color: palette.burgundy }]}>{option.text}</Text>
            </Pressable>
          );
        })}
      </ScrollView>
    </View>
  );
}

type ResultsProps = {
  result: QuizSprintResult;
  board: QuizSprintDailyBoard | null;
  isSignedIn: boolean;
  onAgain: () => void;
  onSignIn: () => void;
};

function SprintResults({ result, board, isSignedIn, onAgain, onSignIn }: ResultsProps) {
  const share = useCallback(() => {
    void Share.share({ message: `I scored ${result.points} in the Queenzone Quiz Sprint. Can you beat it?` });
  }, [result.points]);

  return (
    <ScrollView testID={testIds.quizSprintResult} style={styles.dark} contentContainerStyle={styles.resultsContent}>
      <Text style={styles.eyebrow}>TIME</Text>
      <Text style={styles.resultScore}>{result.points}</Text>
      <Text style={styles.resultUnit}>POINTS</Text>
      <Text style={styles.verdict}>{verdictFor(result.points)}</Text>
      <Text style={styles.detail}>
        {result.correct} correct from {result.attempted} answered · best streak {result.bestStreak}
      </Text>
      {result.recorded ? (
        <Text style={styles.note}>
          Your score is on today&apos;s leaderboard{result.rank != null ? ` (rank #${result.rank})` : ''}.
        </Text>
      ) : !isSignedIn ? (
        <View style={styles.signIn}>
          <Text style={styles.note}>Your score is only added to the leaderboard if you are signed in.</Text>
          <Pressable accessibilityRole="button" accessibilityLabel="Sign in to be ranked" onPress={onSignIn}>
            <Text style={styles.noteLink}>Sign in to be ranked</Text>
          </Pressable>
        </View>
      ) : null}

      <Text style={[styles.eyebrow, styles.boardTitle]}>DAILY LEADERBOARD</Text>
      <QuizSprintBoard
        rows={board?.top.slice(0, 8) ?? []}
        viewer={board?.viewer ?? null}
        emptyText="No scores on the board yet today."
      />

      <Pressable
        testID={testIds.quizSprintAgain}
        accessibilityRole="button"
        accessibilityLabel="Run it again"
        onPress={onAgain}
        style={({ pressed }) => [styles.cta, styles.resultCta, pressed && styles.ctaPressed]}
      >
        <Text style={styles.ctaLabel}>RUN IT AGAIN</Text>
      </Pressable>
      <Pressable accessibilityRole="button" accessibilityLabel="Share your score" onPress={share} style={styles.ghost}>
        <Text style={styles.ghostLabel}>SHARE YOUR SCORE</Text>
      </Pressable>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  dark: { flex: 1, backgroundColor: palette.black },
  light: { flex: 1, backgroundColor: palette.white },
  introContent: { alignItems: 'center', paddingHorizontal: space.xl, paddingTop: 32, paddingBottom: 40, gap: 20 },
  introCrest: { position: 'absolute', top: 120, width: 330, height: 330, opacity: 0.07 },
  eyebrow: { fontFamily: fonts.titling, fontSize: 10, letterSpacing: 2.2, color: palette.gold },
  introTitle: {
    fontFamily: fonts.display,
    fontSize: 46,
    lineHeight: 47,
    letterSpacing: -0.7,
    color: palette.white,
    textAlign: 'center',
  },
  standfirst: {
    fontFamily: fonts.body,
    fontSize: 16,
    lineHeight: 26,
    color: 'rgba(255,255,255,0.72)',
    textAlign: 'center',
    maxWidth: 300,
  },
  dial: { width: 196, height: 196, borderRadius: 98, borderWidth: 1, borderColor: 'rgba(255,255,255,0.18)', alignItems: 'center', justifyContent: 'center' },
  dialRing: { position: 'absolute', width: 168, height: 168, borderRadius: 84, borderWidth: 1, borderColor: 'rgba(184,154,74,0.5)' },
  dialNumber: { fontFamily: fonts.display, fontSize: 82, lineHeight: 86, color: palette.white },
  dialLabel: { fontFamily: fonts.titling, fontSize: 10, letterSpacing: 2.8, color: palette.gold },
  rules: { fontFamily: fonts.titling, fontSize: 10, letterSpacing: 2, color: 'rgba(255,255,255,0.5)', textAlign: 'center' },
  cta: {
    alignSelf: 'stretch',
    minHeight: 56,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: radius.sm,
    backgroundColor: palette.gold,
  },
  ctaBusy: { opacity: 0.6 },
  ctaPressed: { backgroundColor: '#C9AA55', transform: [{ translateY: 1 }] },
  ctaLabel: { fontFamily: fonts.bodyMedium, fontSize: 15, letterSpacing: 1.2, color: palette.black },
  link: { fontFamily: fonts.bodyMedium, fontSize: 13, letterSpacing: 1.1, color: 'rgba(255,255,255,0.72)', paddingVertical: space.sm },
  signIn: { alignItems: 'center', gap: 4 },
  note: { fontFamily: fonts.body, fontSize: 14, lineHeight: 21, color: 'rgba(255,255,255,0.72)', textAlign: 'center' },
  noteLink: { fontFamily: fonts.bodyMedium, fontSize: 14, color: palette.gold, paddingVertical: space.xs },
  best: { fontFamily: fonts.bodyMedium, fontSize: 11, letterSpacing: 1, color: 'rgba(255,255,255,0.45)', textAlign: 'center' },

  track: { height: 5, backgroundColor: palette.greyLight },
  fill: { height: 5, backgroundColor: palette.gold },
  status: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: space.xl,
    paddingVertical: 16,
    borderBottomWidth: 1,
    borderBottomColor: palette.greyLight,
    backgroundColor: palette.white,
  },
  statusLeft: { flexDirection: 'row', alignItems: 'baseline', gap: 8 },
  statusRight: { alignItems: 'flex-end', gap: 2 },
  clock: { fontFamily: fonts.display, fontSize: 34, color: palette.black },
  clockUrgent: { color: palette.burgundy },
  statusLabel: { fontFamily: fonts.titling, fontSize: 10, letterSpacing: 1.6, color: palette.grey500 },
  streak: { fontFamily: fonts.bodyMedium, fontSize: 11, letterSpacing: 0.9, color: palette.grey500 },
  streakHot: { color: palette.gold },
  scoreRow: { flexDirection: 'row', alignItems: 'baseline', gap: 6 },
  scoreValue: { fontFamily: fonts.display, fontSize: 30, color: palette.black },
  questionArea: { paddingHorizontal: space.xl, paddingTop: 32, paddingBottom: 40, gap: 10 },
  questionNumber: { fontFamily: fonts.titling, fontSize: 11, letterSpacing: 2.4, color: palette.purple, marginBottom: 4 },
  prompt: { fontFamily: fonts.display, fontSize: 28, lineHeight: 33, color: palette.black, marginBottom: 14 },
  answer: {
    flexDirection: 'row',
    alignItems: 'baseline',
    gap: 14,
    minHeight: 56,
    paddingVertical: 16,
    paddingHorizontal: 20,
    borderWidth: 1,
    borderColor: palette.grey300,
    borderRadius: radius.sm,
    backgroundColor: palette.white,
  },
  answerCorrect: { backgroundColor: CORRECT_BG, borderColor: CORRECT_TEXT },
  answerWrong: { backgroundColor: palette.burgundyTint, borderColor: palette.burgundy },
  letter: { fontFamily: fonts.titling, fontSize: 13, color: palette.grey400 },
  answerText: { flex: 1, fontFamily: fonts.body, fontSize: 16, lineHeight: 22, color: palette.black },

  resultsContent: { paddingHorizontal: space.xl, paddingTop: 40, paddingBottom: 40, gap: 10 },
  resultScore: { fontFamily: fonts.display, fontSize: 82, lineHeight: 76, color: palette.white },
  resultUnit: { fontFamily: fonts.titling, fontSize: 12, letterSpacing: 2.4, color: 'rgba(255,255,255,0.6)', marginBottom: 6 },
  verdict: { fontFamily: fonts.body, fontSize: 17, lineHeight: 27, color: 'rgba(255,255,255,0.72)' },
  detail: { fontFamily: fonts.body, fontSize: 13, color: 'rgba(255,255,255,0.6)' },
  boardTitle: { color: 'rgba(255,255,255,0.55)', marginTop: 22 },
  resultCta: { marginTop: 22 },
  ghost: {
    alignSelf: 'stretch',
    minHeight: 56,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.3)',
    borderRadius: radius.sm,
  },
  ghostLabel: { fontFamily: fonts.bodyMedium, fontSize: 13, letterSpacing: 1.1, color: palette.white },
});
