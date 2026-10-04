import { testIds } from '../test/testIds';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import * as Haptics from 'expo-haptics';
import { fonts, useTheme } from '../theme';

type Props = { onLetter: (letter: string) => void; onBackspace: () => void; disabled: boolean };
export function CrosswordKeyboard({ onLetter, onBackspace, disabled }: Props) {
  const { c } = useTheme();
  function press(letter: string) {
    void Haptics.selectionAsync().catch(() => {});
    if (letter === '⌫') onBackspace(); else onLetter(letter);
  }
  return <View testID={testIds.crosswordKeyboard} style={[styles.keyboard, { backgroundColor: c.surfacePage }]}>
    {['QWERTYUIOP', 'ASDFGHJKL', 'ZXCVBNM⌫'].map(row => <View key={row} style={styles.row}>
      {[...row].map(letter => <Pressable key={letter} testID={`crossword-key-${letter === '⌫' ? 'backspace' : letter}`}
        accessibilityRole="button" accessibilityLabel={letter === '⌫' ? 'Backspace' : letter}
        accessibilityState={{ disabled }} disabled={disabled} onPress={() => press(letter)}
        style={({ pressed }) => [styles.key, { backgroundColor: c.surfaceCard, borderColor: c.borderStrong, opacity: disabled ? 0.4 : pressed ? 0.7 : 1 }]}>
        <Text style={[styles.letter, { color: c.textPrimary }]}>{letter}</Text>
      </Pressable>)}
    </View>)}
  </View>;
}
const styles = StyleSheet.create({
  keyboard: { paddingHorizontal: 6, paddingVertical: 4, gap: 4 },
  row: { flexDirection: 'row', justifyContent: 'center', gap: 3 },
  key: { flex: 1, maxWidth: 44, height: 44, borderRadius: 5, borderWidth: StyleSheet.hairlineWidth, alignItems: 'center', justifyContent: 'center' },
  letter: { fontFamily: fonts.bodyMedium, fontSize: 18 },
});
