import type { ReactNode } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, Text } from 'react-native';
import { space, type } from '../theme';

export function FormScreenLayout({
  children,
  backgroundColor,
  bottomInset,
  testID,
}: {
  children: ReactNode;
  backgroundColor: string;
  bottomInset: number;
  testID?: string;
}) {
  return (
    <KeyboardAvoidingView
      testID={testID}
      style={[styles.flex, { backgroundColor }]}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
    >
      <ScrollView
        style={styles.flex}
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={[styles.content, { paddingBottom: bottomInset + space.xxl }]}
      >
        {children}
      </ScrollView>
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
});
