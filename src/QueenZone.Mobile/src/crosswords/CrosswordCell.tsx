import { memo } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { fonts, useTheme } from '../theme';

export type CrosswordCellProps = {
  cell: number;
  letter: string;
  number: string | number;
  size: number;
  selected: boolean;
  inWord: boolean;
  incorrect: boolean;
  revealed: boolean;
  disabled: boolean;
  label: string;
  onPress: (cell: number) => void;
};

export function CrosswordCellView({
  cell, letter, number, size, selected, inWord, incorrect, revealed, disabled, label, onPress,
}: CrosswordCellProps) {
  const { c } = useTheme();
  return <Pressable testID={`crossword-cell-${cell}`} accessibilityRole="button"
    accessibilityLabel={label} accessibilityState={{ selected, disabled }} disabled={disabled}
    onPress={() => onPress(cell)} style={[styles.cell, { width: size, height: size,
      backgroundColor: inWord ? c.surfaceCard : c.surfacePage,
      borderColor: selected ? c.accentPrimary : c.borderStrong, borderWidth: selected ? 3 : 0.5 }]}>
    <Text maxFontSizeMultiplier={1.2} style={[styles.number, { color: c.textSecondary, fontSize: Math.max(8, size * 0.22) }]}>{number || ''}</Text>
    <Text maxFontSizeMultiplier={1.2} style={{ fontFamily: fonts.bodyMedium, fontSize: size * 0.5, color: c.textPrimary,
      textDecorationLine: incorrect ? 'line-through' : 'none' }}>{letter === '.' ? '' : letter}</Text>
    <Text accessibilityElementsHidden style={[styles.marker, { fontSize: Math.max(9, size * 0.22), color: incorrect ? c.danger : c.accentPrimary }]}>{revealed ? '▲' : incorrect ? '×' : ''}</Text>
  </Pressable>;
}

export const CrosswordCell = memo(CrosswordCellView);

export const CrosswordBlock = memo(function CrosswordBlock({ size, color }: { size: number; color: string }) {
  return <View style={{ width: size, height: size, backgroundColor: color }} accessible={false} />;
});

const styles = StyleSheet.create({
  cell: { alignItems: 'center', justifyContent: 'center' },
  number: { position: 'absolute', top: 0, left: 2, fontFamily: fonts.body },
  marker: { position: 'absolute', bottom: 0, right: 1 },
});
