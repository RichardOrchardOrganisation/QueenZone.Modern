import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { ScrollView, Text, View } from 'react-native';
import { useState } from 'react';
import { useAnalyticsConsent } from '../../analytics/consent';
import { updateAnalyticsConsent } from '../../analytics/telemetry';
import type { HomeStackParamList } from '../../navigation/types';
import { space, type, useTheme } from '../../theme';
import { Button } from '../../ui/Button';
import { Eyebrow } from '../../ui/Eyebrow';
import { openExternalUrl } from '../../ui/openExternalUrl';
import { SettingsRow } from '../../ui/SettingsRow';

type Props = NativeStackScreenProps<HomeStackParamList, 'AnalyticsSettings'>;

const privacyUrl = 'https://www.queenzone.org/privacy';

export function AnalyticsSettingsScreen(_: Props) {
  const { c } = useTheme();
  const consent = useAnalyticsConsent();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save(granted: boolean) {
    setSaving(true);
    setError(null);
    try {
      await updateAnalyticsConsent(granted);
    } catch {
      setError('Could not save the analytics preference. Try again.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <ScrollView style={{ flex: 1, backgroundColor: c.surfacePage }} contentContainerStyle={{ paddingBottom: space.section }}>
      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xl, paddingBottom: space.md, gap: space.md }}>
        <Eyebrow tone="muted">Privacy</Eyebrow>
        <Text style={[type.body, { color: c.textSecondary }]}>Anonymous analytics helps us count active app installations, rank the five main sections, and understand usage by coarse country or device region.</Text>
        <Text style={[type.caption, { color: c.textMuted }]}>It never includes your QueenZone account, content, searches, precise location, or advertising identifiers. Turning it off stops future collection and deletes the analytics-only identifier stored on this device.</Text>
        {error ? <Text accessibilityRole="alert" style={[type.body, { color: c.danger }]}>{error}</Text> : null}
      </View>
      <SettingsRow
        title="Share anonymous analytics"
        subtitle={consent === 'loading' ? 'Loading preference…' : 'You can change this at any time.'}
        switchValue={consent === 'granted'}
        onSwitch={(value) => void save(value)}
        disabled={saving || consent === 'loading'}
        accessibilityLabel="Share anonymous analytics"
      />
      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xl }}>
        <Button label="Read privacy policy" variant="outline" onPress={() => void openExternalUrl(privacyUrl)} />
      </View>
    </ScrollView>
  );
}
