import type { ReactNode } from 'react';
import { useHeaderHeight } from '@react-navigation/elements';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, Text, View } from 'react-native';
import { space, type } from '../theme';

export function FormScreenLayout({
  children,
  backgroundColor,
  bottomInset,
  footer,
  testID,
}: {
  children: ReactNode;
  backgroundColor: string;
  bottomInset: number;
  footer?: ReactNode;
  testID?: string;
}) {
  const headerHeight = useHeaderHeight();
  return (
    <KeyboardAvoidingView
      testID={testID}
      style={[styles.flex, { backgroundColor }]}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      keyboardVerticalOffset={Platform.OS === 'ios' ? headerHeight : 0}
    >
      <ScrollView
        style={styles.flex}
        keyboardShouldPersistTaps="handled"
        keyboardDismissMode="on-drag"
        contentContainerStyle={[styles.content, { paddingBottom: bottomInset + space.xxl }]}
      >
        {children}
      </ScrollView>
      {footer ? (
        <View style={[styles.footer, { backgroundColor, paddingBottom: bottomInset + space.sm }]}>
          {footer}
        </View>
      ) : null}
    </KeyboardAvoidingView>
  );
}

export function FormFieldLabel({ color, children }: { color: string; children: string }) {
  return <Text style={[type.listTitle, { color }]}>{children}</Text>;
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  content: {
    paddingHorizontal: space.xl,
    paddingTop: space.base,
    gap: space.md,
  },
  footer: {
    paddingHorizontal: space.xl,
    paddingTop: space.sm,
  },
});
