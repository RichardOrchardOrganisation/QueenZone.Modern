import { Modal, Text, View } from 'react-native';
import { useState } from 'react';
import { getAppConfig } from '../config/appConfig';
import { radius, space, type, useTheme } from '../theme';
import { Button } from '../ui/Button';
import { openExternalUrl } from '../ui/openExternalUrl';
import { useAnalyticsConsent } from './consent';
import { updateAnalyticsConsent } from './telemetry';

const privacyUrl = 'https://www.queenzone.org/privacy';

export function AnalyticsConsentPrompt() {
  const { c } = useTheme();
  const consent = useAnalyticsConsent();
  const configured = Boolean(getAppConfig().telemetryDeckAppId);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save(granted: boolean) {
    setSaving(true);
    setError(null);
    try {
      await updateAnalyticsConsent(granted);
    } catch {
      setError('Could not save your choice. Try again.');
    } finally {
      setSaving(false);
    }
  }

  if (!configured || consent !== 'unset') {
    return null;
  }

  return (
    <Modal transparent animationType="fade" statusBarTranslucent onRequestClose={() => undefined}>
      <View
        accessibilityViewIsModal
        style={{
          flex: 1,
          justifyContent: 'center',
          padding: space.xl,
          backgroundColor: 'rgba(0,0,0,0.72)',
        }}
      >
        <View
          style={{
            gap: space.md,
            padding: space.xl,
            borderRadius: radius.md,
            borderWidth: 1,
            borderColor: c.border,
            backgroundColor: c.surfaceRaised,
          }}
        >
          <Text accessibilityRole="header" style={[type.pageTitle, { color: c.textPrimary }]}>Anonymous analytics</Text>
          <Text style={[type.body, { color: c.textSecondary }]}>Help us understand how the app is used.</Text>
          <Text style={[type.body, { color: c.textSecondary }]}>If you allow, QueenZone sends anonymous app activity, the main section visited, and coarse country or device-region information to TelemetryDeck.</Text>
          <Text style={[type.caption, { color: c.textMuted }]}>We do not send your account details, content, searches, precise location, or advertising identifiers. You can change this later under Profile → Analytics preferences.</Text>
          {error ? <Text accessibilityRole="alert" style={[type.body, { color: c.danger }]}>{error}</Text> : null}
          <View style={{ gap: space.sm, paddingTop: space.sm }}>
            <Button label="Allow anonymous analytics" loading={saving} onPress={() => void save(true)} />
            <Button label="Don't allow" variant="outline" disabled={saving} onPress={() => void save(false)} />
            <Button label="Read privacy policy" variant="ghost" disabled={saving} onPress={() => void openExternalUrl(privacyUrl)} />
          </View>
        </View>
      </View>
    </Modal>
  );
}
