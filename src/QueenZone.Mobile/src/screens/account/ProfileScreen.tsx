import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { Image } from 'expo-image';
import { useCallback, useEffect, useState } from 'react';
import { Pressable, ScrollView, Text, View } from 'react-native';
import { getAppConfig } from '../../config/appConfig';
import { avatarUrl, formatMemberSince, type MemberProfile } from '../../api/me';
import type { HomeStackParamList } from '../../navigation/types';
import { useSession } from '../../session/SessionContext';
import { openSignIn } from '../../session/signInNavigation';
import { openSuggestNews } from '../../share/news/NewsShare';
import { space, type, useTheme } from '../../theme';
import { messagesA11yLabel } from '../messages/inboxMeta';
import { useUnreadConversationCount } from '../messages/useUnreadConversationCount';
import { testIds } from '../../test/testIds';
import { ArchiveFooter } from '../../ui/ArchiveFooter';
import { Button } from '../../ui/Button';
import { LoadingBlock } from '../../ui/ScreenStates';
import { CrestSeal } from '../../ui/CrestSeal';
import { Eyebrow } from '../../ui/Eyebrow';
import { SettingsRow } from '../../ui/SettingsRow';

type Props = NativeStackScreenProps<HomeStackParamList, 'Profile'>;

function initials(name: string | null): string {
  if (!name) {
    return '';
  }
  const parts = name.replace(/_/g, ' ').trim().split(/\s+/);
  if (parts.length === 1) {
    return parts[0].slice(0, 2).toUpperCase();
  }
  return `${parts[0][0] ?? ''}${parts[1][0] ?? ''}`.toUpperCase();
}

export function ProfileScreen({ navigation }: Props) {
  const { c, mode } = useTheme();
  const { isSignedIn, isRestoring, displayName, profile, refreshProfile, signOut } = useSession();
  const unreadCount = useUnreadConversationCount();
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (isSignedIn) {
      void refreshProfile();
    }
  }, [isSignedIn, refreshProfile]);

  const onSignOut = useCallback(async () => {
    setBusy(true);
    try {
      await signOut();
    } finally {
      setBusy(false);
    }
  }, [signOut]);

  if (isRestoring && !isSignedIn) {
    return (
      <View testID={testIds.profileRestoring} style={{ flex: 1, backgroundColor: c.surfacePage }}>
        <LoadingBlock label="Restoring your session…" />
      </View>
    );
  }

  if (!isSignedIn) {
    return (
      <ScrollView
        testID={testIds.profileSignedOut}
        style={{ flex: 1, backgroundColor: c.surfacePage }}
        contentContainerStyle={{
          paddingHorizontal: space.xl,
          paddingTop: space.section,
          paddingBottom: space.section,
          alignItems: 'center',
          gap: space.lg,
        }}
      >
        <CrestSeal height={48} opacity={0.5} />
        <Text style={[type.pageTitle, { color: c.textPrimary, textAlign: 'center' }]}>Join the archive</Text>
        <Text style={[type.body, { color: c.textSecondary, textAlign: 'center' }]}>
          Sign in to save articles, post on the forum, and keep reading history on this device.
        </Text>
        <View style={{ alignSelf: 'stretch', gap: 10, marginTop: space.sm }}>
          <Button label="Sign in" onPress={() => openSignIn(navigation)} />
        </View>
        <Pressable accessibilityRole="button" onPress={() => navigation.navigate('Contact')}>
          <Text style={[type.button, { color: c.accentPrimary }]}>Contact</Text>
        </Pressable>
        <Pressable accessibilityRole="button" onPress={() => navigation.navigate('AnalyticsSettings')}>
          <Text style={[type.button, { color: c.accentPrimary }]}>Analytics preferences</Text>
        </Pressable>
        <Pressable accessibilityRole="button" onPress={() => navigation.navigate('Appearance')}>
          <Text style={[type.button, { color: c.accentPrimary }]}>Appearance</Text>
        </Pressable>
        <ArchiveFooter />
      </ScrollView>
    );
  }

  const member: MemberProfile | null = profile;
  const since = member ? formatMemberSince(member.createdAt) : '';
  const imageUri = avatarUrl(getAppConfig().apiBaseUrl, member?.avatarPath ?? null, member?.displayName);

  return (
    <ScrollView testID={testIds.profileSignedIn} style={{ flex: 1, backgroundColor: c.surfacePage }}>
      <View
        style={{
          flexDirection: 'row',
          alignItems: 'center',
          gap: 16,
          paddingHorizontal: space.xl,
          paddingVertical: space.xl,
        }}
      >
        <View
          style={{
            width: 62,
            height: 62,
            borderRadius: 31,
            borderWidth: 1,
            borderColor: c.border,
            overflow: 'hidden',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          {imageUri ? (
            <Image source={{ uri: imageUri }} style={{ width: 62, height: 62 }} />
          ) : (
            <Text style={[type.articleTitle, { color: c.textPrimary }]}>{initials(displayName)}</Text>
          )}
        </View>
        <View style={{ flex: 1, gap: 6 }}>
          <Text testID={testIds.profileDisplayName} style={[type.articleTitle, { color: c.textPrimary }]}>
            {displayName}
          </Text>
          {since ? (
            <Text style={[type.meta, { color: c.accentPrimary }]}>Member since {since}</Text>
          ) : null}
          {member?.email ? <Text style={[type.caption, { color: c.textMuted }]}>{member.email}</Text> : null}
        </View>
      </View>

      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xxl, paddingBottom: space.md }}>
        <Eyebrow tone="muted">Account</Eyebrow>
      </View>
      <SettingsRow title="Account settings" onPress={() => navigation.navigate('Settings')} />
      <SettingsRow
        title="Appearance"
        value={mode === 'light' ? 'Light' : 'Dark'}
        onPress={() => navigation.navigate('Appearance')}
      />
      <SettingsRow
        title="Messages"
        value={unreadCount > 0 ? String(unreadCount) : undefined}
        accessibilityLabel={messagesA11yLabel(unreadCount)}
        testID={testIds.profileMessages}
        onPress={() => navigation.navigate('Inbox')}
      />
      <SettingsRow
        title="My submissions"
        testID={testIds.profileMySubmissions}
        onPress={() => navigation.navigate('MySubmissions')}
      />
      <SettingsRow
        title="Downloaded performances"
        onPress={() => navigation.navigate('SavedList', { kind: 'offline' })}
      />
      <SettingsRow title="Suggest news" onPress={() => openSuggestNews(navigation)} />
      <SettingsRow title="Contact" onPress={() => navigation.navigate('Contact')} />
      <SettingsRow
        title="Analytics preferences"
        subtitle="Control anonymous app-usage analytics."
        onPress={() => navigation.navigate('AnalyticsSettings')}
      />
      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xl, paddingBottom: space.xl, gap: 10 }}>
        <Button label="Sign out" variant="outline" loading={busy} onPress={() => void onSignOut()} />
      </View>
      <ArchiveFooter />
    </ScrollView>
  );
}
