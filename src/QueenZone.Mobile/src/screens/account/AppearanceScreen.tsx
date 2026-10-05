import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { useState } from 'react';
import { Pressable, ScrollView, Text, View } from 'react-native';
import { sendJson } from '../../api/client';
import { ApiError } from '../../api/errors';
import type { HomeStackParamList } from '../../navigation/types';
import { radius, space, type, useTheme, type ThemePreference } from '../../theme';
import { useSession } from '../../session/SessionContext';
import { Eyebrow } from '../../ui/Eyebrow';

type Props = NativeStackScreenProps<HomeStackParamList, 'Appearance'>;

const options: readonly { value: ThemePreference; title: string; description: string }[] = [
  {
    value: 'system',
    title: 'Use system setting',
    description: 'Automatically match your phone’s Light or Dark appearance.',
  },
  { value: 'dark', title: 'Dark', description: 'Gold accents on a dark background.' },
  { value: 'light', title: 'Light', description: 'Blue accents on a light background.' },
];

export function AppearanceScreen(_: Props) {
  const { c, preference, setPreference } = useTheme();
  const { accessToken, refreshProfile } = useSession();
  const [accountError, setAccountError] = useState<string | null>(null);

  // The choice always applies on this device straight away; when signed in it is also saved to the
  // account so the website and other devices follow it.
  async function choose(next: ThemePreference) {
    setPreference(next);
    setAccountError(null);
    if (!accessToken) {
      return;
    }

    try {
      await sendJson('/me', { method: 'PATCH', accessToken, body: { themePreference: next } });
      await refreshProfile();
    } catch (err) {
      setAccountError(
        err instanceof ApiError
          ? err.message
          : 'Saved on this device only. We could not update your account just now.',
      );
    }
  }

  return (
    <ScrollView
      style={{ flex: 1, backgroundColor: c.surfacePage }}
      contentContainerStyle={{ paddingBottom: space.section }}
    >
      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xl, paddingBottom: space.md, gap: space.sm }}>
        <Eyebrow tone="muted">Colour scheme</Eyebrow>
        <Text style={[type.body, { color: c.textSecondary }]}>
          {accessToken
            ? 'Choose how QueenZone looks. Your choice is saved to your account and also applies on the website.'
            : 'Choose how QueenZone looks on this device. Sign in to save your choice to your account.'}
        </Text>
        {accountError ? (
          <Text accessibilityRole="alert" style={[type.caption, { color: c.danger }]}>
            {accountError}
          </Text>
        ) : null}
      </View>
      <View style={{ paddingHorizontal: space.xl, gap: space.md }}>
        {options.map((option) => {
          const selected = preference === option.value;
          return (
            <Pressable
              key={option.value}
              accessibilityRole="radio"
              accessibilityState={{ selected }}
              accessibilityLabel={option.title}
              onPress={() => void choose(option.value)}
              style={{
                minHeight: 72,
                borderWidth: 1,
                borderColor: selected ? c.accentPrimary : c.border,
                borderRadius: radius.sm,
                backgroundColor: c.surfaceCard,
                paddingHorizontal: space.base,
                paddingVertical: space.md,
                justifyContent: 'center',
                gap: space.xs,
              }}
            >
              <Text style={[type.listTitle, { color: c.textPrimary }]}>{option.title}</Text>
              <Text style={[type.caption, { color: c.textMuted }]}>{option.description}</Text>
            </Pressable>
          );
        })}
      </View>
    </ScrollView>
  );
}
