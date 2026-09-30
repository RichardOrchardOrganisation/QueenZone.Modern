import { useEffect, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { fetchQuizSprintDaily } from '../../api';
import { testIds } from '../../test/testIds';
import { fonts, palette, radius, useTheme } from '../../theme';

type Props = {
  onPlay: () => void;
};

/** The score to beat today, or null when nobody has played yet (or the board could not load). */
export function sprintCardLine(bestScore: number | null): string {
  return bestScore != null && bestScore > 0
    ? `Sixty seconds of Queen history. Beat ${bestScore} to top today's board.`
    : "Sixty seconds of Queen history. Be first on today's board.";
}

/** Daily Challenge card: the first interactive element under the masthead on the home screen. */
export function HomeSprintCard({ onPlay }: Props) {
  const { c } = useTheme();
  const [bestScore, setBestScore] = useState<number | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    fetchQuizSprintDaily(controller.signal)
      .then((board) => setBestScore(board.top[0]?.score ?? null))
      .catch(() => {
        // The card still works without today's best score.
      });
    return () => controller.abort();
  }, []);

  return (
    <View style={[styles.frame, { borderColor: c.hairline }]}>
      <View testID={testIds.homeSprintCard} style={styles.card}>
        <View style={styles.copy}>
          <Text style={styles.eyebrow}>DAILY CHALLENGE</Text>
          <Text style={styles.title}>Quiz Sprint</Text>
          <Text style={styles.line}>{sprintCardLine(bestScore)}</Text>
        </View>
        <View style={styles.dial} accessibilityElementsHidden importantForAccessibility="no-hide-descendants">
          <Text style={styles.dialNumber}>60</Text>
          <Text style={styles.dialLabel}>SEC</Text>
        </View>
        <Pressable
          testID={testIds.homeSprintStart}
          accessibilityRole="button"
          accessibilityLabel="Start the Quiz Sprint"
          onPress={onPlay}
          style={({ pressed }) => [styles.start, pressed && styles.startPressed]}
        >
          <Text style={styles.startLabel}>START</Text>
        </Pressable>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  /** Hairline only — the inner card stays the black/gold/white stage in both schemes. */
  frame: {
    marginHorizontal: 18,
    marginTop: 12,
    marginBottom: 8,
    padding: 1,
    borderWidth: StyleSheet.hairlineWidth,
    borderRadius: radius.md + 1,
  },
  card: {
    paddingVertical: 24,
    paddingHorizontal: 22,
    backgroundColor: palette.black,
    borderWidth: 1,
    borderColor: palette.gold,
    borderRadius: radius.md,
    flexDirection: 'row',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    rowGap: 18,
  },
  copy: { flex: 1, paddingRight: 12 },
  eyebrow: { fontFamily: fonts.titling, fontSize: 10, letterSpacing: 2.2, color: palette.gold },
  title: { fontFamily: fonts.display, fontSize: 30, lineHeight: 33, color: palette.white, marginTop: 6 },
  line: { fontFamily: fonts.body, fontSize: 14, lineHeight: 21, color: 'rgba(255,255,255,0.65)', marginTop: 8, maxWidth: 190 },
  dial: {
    width: 78,
    height: 78,
    borderRadius: 39,
    borderWidth: 1,
    borderColor: palette.gold,
    alignItems: 'center',
    justifyContent: 'center',
  },
  dialNumber: { fontFamily: fonts.display, fontSize: 30, lineHeight: 32, color: palette.white },
  dialLabel: { fontFamily: fonts.titling, fontSize: 8, letterSpacing: 1.6, color: palette.gold },
  start: {
    width: '100%',
    minHeight: 48,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: radius.sm,
    backgroundColor: palette.gold,
  },
  startPressed: { backgroundColor: '#C9AA55' },
  startLabel: { fontFamily: fonts.bodyMedium, fontSize: 14, letterSpacing: 1.2, color: palette.black },
});
