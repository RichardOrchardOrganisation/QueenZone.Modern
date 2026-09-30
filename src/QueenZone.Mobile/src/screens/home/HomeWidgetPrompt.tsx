import AsyncStorage from '@react-native-async-storage/async-storage';
import { X } from 'lucide-react-native';
import { useEffect, useState } from 'react';
import { Modal, Pressable, StyleSheet, Text, View } from 'react-native';
import { Button } from '../../ui/Button';
import { fonts, radius, space, type, useTheme } from '../../theme';

export const widgetPromptDismissedKey = 'queenzone.mobile.widgetPromptDismissed';

export function HomeWidgetPrompt({ summary }: { summary: string }) {
  const { c } = useTheme();
  const [visible, setVisible] = useState(false);
  const [showInstructions, setShowInstructions] = useState(false);

  useEffect(() => {
    let active = true;
    void AsyncStorage.getItem(widgetPromptDismissedKey)
      .then((dismissed) => {
        if (active && dismissed !== 'true') setVisible(true);
      })
      .catch(() => {
        if (active) setVisible(true);
      });
    return () => { active = false; };
  }, []);

  const dismiss = () => {
    setVisible(false);
    setShowInstructions(false);
    void AsyncStorage.setItem(widgetPromptDismissedKey, 'true').catch(() => {
      // Keep the prompt dismissed for this session if storage is unavailable.
    });
  };

  if (!visible) return null;

  return (
    <View style={[styles.card, { backgroundColor: c.surfaceRaised, borderColor: c.borderStrong }]}>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel="Dismiss widget tip"
        hitSlop={12}
        onPress={dismiss}
        style={styles.dismiss}
      >
        <X size={18} color={c.textSecondary} accessibilityElementsHidden />
      </Pressable>

      <View style={[styles.preview, { backgroundColor: c.surfaceCard, borderColor: c.borderStrong }]}>
        <Text style={[styles.previewEyebrow, { color: c.accentSpecial }]}>ON THIS DAY</Text>
        <Text numberOfLines={2} style={[styles.previewBody, { color: c.textPrimary }]}>
          {summary}
        </Text>
      </View>

      <View style={styles.copy}>
        <Text style={[type.cardTitle, { color: c.textPrimary }]}>A little Queen on your Home Screen</Text>
        <Text style={[type.caption, { color: c.textSecondary }]}>
          See a Queen story, quote, or fact throughout the day with the QueenZone widget.
        </Text>
        <View style={styles.action}>
          <Button label="How to add it" size="sm" variant="outline" onPress={() => setShowInstructions(true)} />
        </View>
      </View>

      <Modal
        visible={showInstructions}
        transparent
        animationType="fade"
        onRequestClose={() => setShowInstructions(false)}
      >
        <View style={[styles.scrim, { backgroundColor: c.surfaceScrim }]}>
          <View style={[styles.instructions, { backgroundColor: c.surfaceSheet }]}>
            <Text style={[type.cardTitle, { color: c.textPrimary }]}>Add the QueenZone widget</Text>
            <Text style={[type.body, { color: c.textSecondary }]}>1. Touch and hold an empty area of your iPhone Home Screen.</Text>
            <Text style={[type.body, { color: c.textSecondary }]}>2. Tap Edit, then Add Widget.</Text>
            <Text style={[type.body, { color: c.textSecondary }]}>3. Search for QueenZone, choose a size, then tap Add Widget.</Text>
            <Button label="Done" onPress={() => setShowInstructions(false)} />
          </View>
        </View>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    marginTop: space.md,
    marginHorizontal: space.xl,
    borderWidth: 1,
    borderRadius: radius.sm,
    padding: space.base,
    gap: space.base,
  },
  dismiss: { position: 'absolute', right: space.base, top: space.base, zIndex: 1 },
  preview: {
    borderWidth: 1,
    borderRadius: radius.md,
    padding: space.md,
    width: 170,
    minHeight: 94,
    gap: space.sm,
  },
  previewEyebrow: { fontFamily: fonts.bodySemi, fontSize: 10, letterSpacing: 0.8 },
  previewBody: { fontFamily: fonts.body, fontSize: 13, lineHeight: 19 },
  copy: { gap: space.sm },
  action: { alignSelf: 'flex-start', marginTop: space.xs },
  scrim: { flex: 1, justifyContent: 'center', padding: space.xl },
  instructions: { borderRadius: radius.sheetIos, padding: space.xl, gap: space.base },
});
