import type { ReactNode } from 'react';
import { Pressable, Text, View } from 'react-native';
import { radius, space, type, useTheme } from '../../theme';
import { Button } from '../../ui/Button';
import { Eyebrow } from '../../ui/Eyebrow';
import type { useSettingsForm } from './useSettingsForm';
import { settingsLegacyView } from './settingsPresentation';

export function SettingsLegacySection({ settings }: { settings: ReturnType<typeof useSettingsForm> }) {
  const { c } = useTheme();
  const { profile, legacyBusy, selectedLegacyId, setSelectedLegacyId, adoptLegacyName, setAdoptLegacyName, claimLegacy, unlinkLegacy } = settings;
  const view = settingsLegacyView(profile);
  let accountContent: ReactNode;
  if (view.linkedMatch) {
    accountContent = <>
      <Text style={[type.body, { color: c.textSecondary }]}>
        Linked to legacy forum account {view.linkedMatch.username} (id {view.linkedMatch.userId}).
      </Text>
      <Button label="Unlink legacy account" variant="outline" loading={legacyBusy} onPress={() => void unlinkLegacy()} />
    </>;
  } else if (view.claimable) {
    accountContent = <>
      <Text style={[type.body, { color: c.textSecondary }]}>
        We found a classic QueenZone forum account matching your email. Claim it to connect your modern
        membership with your archive history.
      </Text>
      {view.claimableMatches.map((match) => (
        <Pressable
          key={match.userId}
          accessibilityRole="radio"
          accessibilityState={{ selected: selectedLegacyId === match.userId }}
          onPress={() => setSelectedLegacyId(match.userId)}
          style={{
            minHeight: 48,
            borderWidth: 1,
            borderColor: selectedLegacyId === match.userId ? c.accentPrimary : c.border,
            borderRadius: radius.xs,
            paddingHorizontal: space.md,
            justifyContent: 'center',
          }}
        >
          <Text style={[type.listTitle, { color: c.textPrimary }]}>
            {match.username} (id {match.userId})
          </Text>
        </Pressable>
      ))}
      <Pressable
        accessibilityRole="checkbox"
        accessibilityState={{ checked: adoptLegacyName }}
        onPress={() => setAdoptLegacyName((value) => !value)}
      >
        <Text style={[type.body, { color: c.textPrimary }]}>
          {adoptLegacyName ? '☑' : '☐'} Use the selected legacy username as my display name
        </Text>
      </Pressable>
      <Button label="Claim legacy account" loading={legacyBusy} onPress={() => void claimLegacy()} />
    </>;
  } else {
    accountContent = <Text style={[type.caption, { color: c.textMuted }]}>No legacy forum account is linked to this email.</Text>;
  }

  return (
      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xxl, gap: space.md }}>
        <Eyebrow tone="muted">Legacy forum account</Eyebrow>
        {accountContent}
      </View>

  );
}
