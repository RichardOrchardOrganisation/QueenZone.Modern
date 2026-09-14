import { memo } from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { type, useTheme } from '../../theme';

export const DateDivider = memo(function DateDivider({ label }: { label: string }) {
  const { c } = useTheme();
  return (
    <View style={styles.dividerRow}>
      <View style={[styles.dividerRule, { backgroundColor: c.ruleSubtle }]} />
      <Text style={[type.eyebrow, { color: c.textMuted }]}>{label}</Text>
      <View style={[styles.dividerRule, { backgroundColor: c.ruleSubtle }]} />
    </View>
  );
});

const styles = StyleSheet.create({
  dividerRow: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  dividerRule: { flex: 1, height: 1 },
});
