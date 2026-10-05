import type { ReactNode } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { archiveDisclaimer, radius, space, type, useTheme } from '../theme';

export type PlaceholderAction = {
  label: string;
  onPress: () => void;
  variant?: 'primary' | 'outline' | 'ghost';
};

type Props = {
  title: string;
  epic: string;
  access: 'public' | 'member';
  description: string;
  actions?: PlaceholderAction[];
  headerShown?: boolean;
  footer?: ReactNode;
};

/**
 * Themed placeholder used by the Epic 1–6 shell until real screens land.
 * Follows STYLE_GUIDE §2 anatomy: Eyebrow → Title → Meta → Body → actions.
 */
export function PlaceholderScreen({
  title,
  epic,
  access,
  description,
  actions = [],
  headerShown = true,
  footer,
}: Props) {
  const insets = useSafeAreaInsets();
  const { c } = useTheme();

  return (
    <ScrollView
      style={[styles.scroll, { backgroundColor: c.surfacePage }]}
      contentContainerStyle={[
        styles.content,
        {
          paddingTop: headerShown ? space.xl : insets.top + space.xl,
          paddingBottom: insets.bottom + space.xxl,
        },
      ]}
    >
      <Text style={[type.eyebrow, { color: c.accentPrimary }]}>{epic}</Text>
      <Text
        style={[type.pageTitle, { color: c.textPrimary }]}
        maxFontSizeMultiplier={1.4}
        allowFontScaling
      >
        {title}
      </Text>
      <Text style={[type.meta, { color: c.textMuted }]}>
        {access === 'public' ? 'Public' : 'Members'}
      </Text>
      <Text style={[type.body, { color: c.textSecondary, marginTop: space.sm }]} allowFontScaling>
        {description}
      </Text>
      {actions.length > 0 ? (
        <View style={styles.actions}>
          {actions.map((action) => {
            const isOutline = action.variant === 'outline' || action.variant === 'ghost';
            const isGhost = action.variant === 'ghost';
            const outlineBorderWidth = isOutline ? 1 : 0;
            return (
              <Pressable
                key={action.label}
                accessibilityRole="button"
                accessibilityLabel={action.label}
                onPress={action.onPress}
                style={({ pressed }) => [
                  styles.button,
                  {
                    backgroundColor: isOutline ? 'transparent' : c.accentPrimary,
                    borderColor: c.border,
                    borderWidth: isGhost ? 0 : outlineBorderWidth,
                  },
                  pressed ? styles.buttonPressed : null,
                ]}
              >
                <Text
                  style={[
                    type.button,
                    { color: isOutline ? c.accentPrimary : c.textOnAccent },
                  ]}
                >
                  {action.label}
                </Text>
              </Pressable>
            );
          })}
        </View>
      ) : null}
      <Text style={[type.caption, { color: c.textMuted, marginTop: space.xxl }]}>
        {archiveDisclaimer}
      </Text>
      {footer}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  scroll: {
    flex: 1,
  },
  content: {
    paddingHorizontal: space.xl,
    gap: space.md,
  },
  actions: {
    gap: 10,
    marginTop: space.base,
  },
  button: {
    minHeight: 48,
    justifyContent: 'center',
    alignItems: 'center',
    paddingHorizontal: space.base,
    borderRadius: radius.xs,
  },
  buttonPressed: {
    opacity: 0.85,
    transform: [{ translateY: 1 }],
  },
});
