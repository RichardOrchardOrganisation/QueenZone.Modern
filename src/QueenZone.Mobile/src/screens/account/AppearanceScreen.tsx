import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { useEffect, useRef, useState } from 'react';
import { Pressable, ScrollView, Text, View } from 'react-native';
import { sendJson } from '../../api/client';
import { ApiError } from '../../api/errors';
import type { HomeStackParamList } from '../../navigation/types';
import { radius, space, type, useTheme, type ThemePreference } from '../../theme';
import { useSession } from '../../session/SessionContext';
import { Eyebrow } from '../../ui/Eyebrow';

type Props = Readonly<NativeStackScreenProps<HomeStackParamList, 'Appearance'>>;

type Option = { value: ThemePreference; title: string; description: string };

const systemOptions: readonly Option[] = [
  {
    value: 'system',
    title: 'Use system setting',
    description: 'Automatically match your phone’s Light or Dark appearance.',
  },
  { value: 'dark', title: 'Dark', description: 'Gold accents on a dark background.' },
  { value: 'light', title: 'Light', description: 'Blue accents on a light background.' },
];

const deviceOptions: readonly Option[] = [
  {
    value: 'system',
    title: 'This device: Same as account',
    description: 'Use the choice saved to your account.',
  },
  { value: 'dark', title: 'This device: Dark', description: 'Always dark on this phone.' },
  { value: 'light', title: 'This device: Light', description: 'Always light on this phone.' },
];

type ChoiceListProps = Readonly<{
  options: readonly Option[];
  selected: ThemePreference;
  onChoose: (value: ThemePreference) => void;
  disabled?: boolean;
}>;

function ChoiceList({ options, selected, onChoose, disabled = false }: ChoiceListProps) {
  const { c } = useTheme();
  return (
    <View style={{ paddingHorizontal: space.xl, gap: space.md }}>
      {options.map((option) => {
        const isSelected = selected === option.value;
        return (
          <Pressable
            key={option.value}
            accessibilityRole="radio"
            accessibilityState={{ selected: isSelected, disabled }}
            disabled={disabled}
            accessibilityLabel={option.title}
            onPress={() => onChoose(option.value)}
            style={{
              minHeight: 72,
              borderWidth: 1,
              borderColor: isSelected ? c.accentPrimary : c.border,
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
  );
}

export function AppearanceScreen(_: Props) {
  const { c, devicePreference, accountPreference, setPreference } = useTheme();
  const { accessToken, profile, refreshProfile } = useSession();
  const [accountError, setAccountError] = useState<string | null>(null);
  const [pendingAccountChoice, setPendingAccountChoice] = useState<ThemePreference | null>(null);

  const saveInFlight = useRef(false);
  const mounted = useRef(true);
  const activeMember = useRef(profile?.memberId);
  useEffect(() => {
    activeMember.current = profile?.memberId;
    setAccountError(null);
    setPendingAccountChoice(null);
  }, [profile?.memberId]);
  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
  }, []);

  // Saved to the account so the website and other devices follow it. A device override still wins here.
  async function chooseForAccount(next: ThemePreference) {
    if (saveInFlight.current || !accessToken) return;
    saveInFlight.current = true;
    const member = activeMember.current;
    const stillActive = () => mounted.current && member === activeMember.current;
    setPendingAccountChoice(next);
    setAccountError(null);
    try {
      await sendJson('/me', { method: 'PATCH', accessToken, body: { themePreference: next } });
      if (stillActive()) await refreshProfile();
    } catch (err) {
      if (stillActive()) setAccountError(
        err instanceof ApiError ? err.message : 'We could not update your account just now. Try again.',
      );
    } finally {
      saveInFlight.current = false;
      if (mounted.current) setPendingAccountChoice(null);
    }
  }

  return (
    <ScrollView
      style={{ flex: 1, backgroundColor: c.surfacePage }}
      contentContainerStyle={{ paddingBottom: space.section }}
    >
      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xl, paddingBottom: space.md, gap: space.sm }}>
        <Eyebrow tone="muted">{accessToken ? 'Account colour scheme' : 'Colour scheme'}</Eyebrow>
        <Text style={[type.body, { color: c.textSecondary }]}>
          {accessToken
            ? 'Saved to your account, so it also applies on the website and your other devices.'
            : 'Choose how QueenZone looks on this device. Sign in to save a choice to your account.'}
        </Text>
        {accountError ? (
          <Text accessibilityRole="alert" style={[type.caption, { color: c.danger }]}>
            {accountError}
          </Text>
        ) : null}
      </View>
      {accessToken ? (
        <>
          <ChoiceList
            options={systemOptions}
            disabled={pendingAccountChoice !== null}
            selected={pendingAccountChoice ?? accountPreference}
            onChoose={(value) => void chooseForAccount(value)}
          />
          <View style={{ paddingHorizontal: space.xl, paddingTop: space.xl, paddingBottom: space.md, gap: space.sm }}>
            <Eyebrow tone="muted">This device only</Eyebrow>
            <Text style={[type.body, { color: c.textSecondary }]}>
              Override the account choice on this phone only. Other devices keep following your account.
            </Text>
          </View>
          <ChoiceList options={deviceOptions} selected={devicePreference} onChoose={setPreference} />
        </>
      ) : (
        <ChoiceList options={systemOptions} selected={devicePreference} onChoose={setPreference} />
      )}
    </ScrollView>
  );
}
