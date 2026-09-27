import { useCallback, useLayoutEffect, useMemo, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { fetchQuizDetail, submitQuizAttempt, type QuizAnswerSubmission, type QuizResult } from '../../api';
import { useDetailQuery } from '../../hooks/useDetailQuery';
import type { ArchiveStackParamList } from '../../navigation/types';
import { openSignIn } from '../../session/signInNavigation';
import { useSession } from '../../session/SessionContext';
import { testIds } from '../../test/testIds';
import { Button } from '../../ui/Button';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';
import { fonts, radius, space, type, useTheme } from '../../theme';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'QuizPlay'>;

export function QuizPlayScreen({ navigation, route }: Props) {
  const { c } = useTheme();
  const { id } = route.params;
  const { isSignedIn, accessToken } = useSession();
  const loadQuiz = useCallback((signal: AbortSignal) => fetchQuizDetail(id, signal), [id]);
  const { data: quiz, error, loading, reload } = useDetailQuery(loadQuiz);

  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [result, setResult] = useState<QuizResult | null>(null);

  useLayoutEffect(() => {
    navigation.setOptions({ title: quiz?.title ?? 'Quiz' });
  }, [navigation, quiz?.title]);

  const allAnswered = useMemo(
    () => (quiz ? quiz.questions.every((question) => answers[question.id] != null) : false),
    [quiz, answers],
  );

  const submit = useCallback(async () => {
    if (!quiz || !accessToken) {
      return;
    }

    setSubmitting(true);
    setSubmitError(null);
    try {
      const payload: QuizAnswerSubmission[] = quiz.questions.map((question) => ({
        questionId: question.id,
        selectedOptionId: answers[question.id] ?? null,
      }));
      const submitted = await submitQuizAttempt(quiz.id, payload, accessToken);
      setResult(submitted);
    } catch (err: unknown) {
      setSubmitError(err instanceof Error ? err.message : 'Could not submit that quiz.');
    } finally {
      setSubmitting(false);
    }
  }, [accessToken, answers, quiz]);

  if (loading) {
    return <LoadingBlock label="Loading quiz…" />;
  }

  if (error || !quiz) {
    return <ErrorBlock message={error ?? 'Quiz not found.'} onRetry={reload} />;
  }

  if (result) {
    return (
      <ScrollView
        testID={testIds.quizResult}
        style={[styles.scroll, { backgroundColor: c.surfacePage }]}
        contentContainerStyle={styles.content}
      >
        <Text style={[type.eyebrow, { color: c.accentArchive }]}>{result.quizTitle}</Text>
        <Text style={[type.pageTitle, { color: c.textPrimary, marginTop: space.sm }]}>
          {result.score} / {result.maxScore} points
        </Text>
        <Text style={[type.body, { color: c.textSecondary, marginTop: space.xs }]}>
          {result.correctCount} / {result.questionCount} correct
        </Text>

        {result.answers.map((answer) => (
          <View
            key={answer.questionId}
            style={[
              styles.resultRow,
              { borderLeftColor: answer.isCorrect ? c.accentSpecial : c.danger },
            ]}
          >
            <Text style={[type.listTitle, { color: c.textPrimary }]}>{answer.questionText}</Text>
            <Text style={[type.body, { color: c.textSecondary, marginTop: space.xs }]}>
              {answer.selectedOptionText
                ? `Your answer: ${answer.selectedOptionText}`
                : 'You did not answer this question.'}
            </Text>
            {!answer.isCorrect ? (
              <Text style={[type.body, { color: c.textSecondary, marginTop: space.xs }]}>
                Correct answer: {answer.correctOptionText}
              </Text>
            ) : null}
          </View>
        ))}

        <View style={styles.resultActions}>
          <Button label="More quizzes" variant="outline" onPress={() => navigation.navigate('QuizList')} />
          <Button label="View leaderboard" onPress={() => navigation.navigate('QuizLeaderboard')} />
        </View>
      </ScrollView>
    );
  }

  return (
    <ScrollView
      testID={testIds.quizPlayScreen}
      style={[styles.scroll, { backgroundColor: c.surfacePage }]}
      contentContainerStyle={styles.content}
    >
      <Text style={[type.eyebrow, { color: c.accentArchive }]}>Test yourself</Text>
      <Text style={[type.pageTitle, { color: c.textPrimary, marginTop: space.sm }]}>{quiz.title}</Text>
      {quiz.description ? (
        <Text style={[type.body, { color: c.textSecondary, marginTop: space.sm }]}>{quiz.description}</Text>
      ) : null}

      {quiz.questions.map((question) => (
        <View key={question.id} style={styles.question}>
          <Text style={[type.listTitle, { color: c.textPrimary, marginBottom: space.sm }]}>
            {question.text}
          </Text>
          {question.options.map((option) => {
            const selected = answers[question.id] === option.id;
            return (
              <Pressable
                key={option.id}
                testID={`${testIds.quizOption}-${option.id}`}
                accessibilityRole="radio"
                accessibilityState={{ checked: selected }}
                accessibilityLabel={option.text}
                onPress={() => setAnswers((prev) => ({ ...prev, [question.id]: option.id }))}
                style={[
                  styles.option,
                  { borderColor: selected ? c.accentPrimary : c.hairline, backgroundColor: c.surfaceCard },
                ]}
              >
                <View
                  style={[
                    styles.radio,
                    { borderColor: selected ? c.accentPrimary : c.borderStrong, backgroundColor: selected ? c.accentPrimary : 'transparent' },
                  ]}
                >
                  {selected ? (
                    <Text style={{ fontSize: 11, fontFamily: fonts.bodySemi, lineHeight: 12, color: c.textOnAccent }}>
                      ✓
                    </Text>
                  ) : null}
                </View>
                <Text style={[type.body, { color: c.textPrimary, flex: 1 }]}>{option.text}</Text>
              </Pressable>
            );
          })}
        </View>
      ))}

      {submitError ? (
        <Text style={[type.caption, { color: c.danger, marginTop: space.md }]}>{submitError}</Text>
      ) : null}

      <View style={styles.submit}>
        {isSignedIn ? (
          <Button
            testID={testIds.quizSubmit}
            label="Submit answers"
            disabled={!allAnswered}
            loading={submitting}
            onPress={() => {
              void submit();
            }}
          />
        ) : (
          <Button
            testID={testIds.quizResultSignIn}
            label="Sign in to submit"
            onPress={() => openSignIn(navigation, { tab: 'ArchiveTab', screen: 'QuizPlay', params: { id } })}
          />
        )}
      </View>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  scroll: { flex: 1 },
  content: {
    paddingHorizontal: space.xl,
    paddingTop: space.xl,
    paddingBottom: space.section,
  },
  question: {
    marginTop: space.xl,
  },
  option: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.md,
    minHeight: 48,
    paddingVertical: 12,
    paddingHorizontal: 12,
    borderWidth: 1,
    borderRadius: radius.xs,
    marginBottom: space.sm,
  },
  radio: {
    width: 18,
    height: 18,
    borderRadius: radius.pill,
    borderWidth: 1.5,
    alignItems: 'center',
    justifyContent: 'center',
  },
  submit: {
    marginTop: space.xl,
    alignSelf: 'flex-start',
  },
  resultRow: {
    marginTop: space.lg,
    paddingLeft: space.md,
    borderLeftWidth: 3,
  },
  resultActions: {
    marginTop: space.xxl,
    gap: space.md,
    alignSelf: 'flex-start',
  },
});
