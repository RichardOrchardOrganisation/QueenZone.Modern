import { StyleSheet, Text, View } from 'react-native';
import type { QuizSprintLeaderboardEntry } from '../../api';
import { fonts, palette, space } from '../../theme';

type Props = {
  rows: QuizSprintLeaderboardEntry[];
  viewer: QuizSprintLeaderboardEntry | null;
  emptyText: string;
};

function rankLabel(rank: number): string {
  return String(rank).padStart(2, '0');
}

/** Today's Quiz Sprint standings on a dark surface; the viewer's own row is highlighted gold. */
export function QuizSprintBoard({ rows, viewer, emptyText }: Props) {
  const shown = viewer && !rows.some((row) => row.rank === viewer.rank) ? [...rows, viewer] : rows;

  if (shown.length === 0) {
    return <Text style={styles.empty}>{emptyText}</Text>;
  }

  return (
    <View accessibilityRole="list">
      {shown.map((row) => {
        const isYou = viewer != null && row.rank === viewer.rank && row.displayName === viewer.displayName;
        return (
          <View key={`${row.rank}-${row.displayName}`} style={[styles.row, isYou && styles.rowYou]}>
            <Text style={styles.rank}>{rankLabel(row.rank)}</Text>
            <View style={styles.who}>
              <Text numberOfLines={1} style={[styles.name, isYou && styles.gold]}>
                {isYou ? 'You' : row.displayName}
              </Text>
              <Text style={[styles.meta, isYou && styles.goldMeta]}>
                60 SEC · {row.bestStreak} STREAK
              </Text>
            </View>
            <Text style={[styles.score, isYou && styles.gold]}>{row.score}</Text>
          </View>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  empty: { fontFamily: fonts.body, fontSize: 15, color: 'rgba(255,255,255,0.66)' },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: 13,
    paddingHorizontal: 12,
    borderTopWidth: 1,
    borderTopColor: 'rgba(255,255,255,0.16)',
    gap: space.md,
  },
  rowYou: { backgroundColor: 'rgba(184,154,74,0.16)' },
  rank: { width: 26, fontFamily: fonts.titling, fontSize: 13, color: 'rgba(255,255,255,0.4)' },
  who: { flex: 1 },
  name: { fontFamily: fonts.bodyMedium, fontSize: 15, color: palette.white },
  meta: {
    fontFamily: fonts.body,
    fontSize: 11,
    letterSpacing: 0.6,
    color: 'rgba(255,255,255,0.45)',
    marginTop: 2,
  },
  score: { fontFamily: fonts.display, fontSize: 24, color: palette.white },
  gold: { color: palette.gold },
  goldMeta: { color: 'rgba(184,154,74,0.8)' },
});
