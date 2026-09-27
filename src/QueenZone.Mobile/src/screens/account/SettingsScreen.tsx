import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { Image } from 'expo-image';
import * as ImagePicker from 'expo-image-picker';
import { useCallback, useEffect, useState } from 'react';
import { Pressable, ScrollView, Text, TextInput, View } from 'react-native';
import { getAppConfig } from '../../config/appConfig';
import { fetchJson, sendJson } from '../../api/client';
import { ApiError } from '../../api/errors';
import { uploadMemberAvatar } from '../../api/memberAvatar';
import {
  avatarUrl,
  messagePrivacyOptions,
  parseMemberProfile,
  validateDisplayName,
  type MessagePrivacy,
  type MemberProfile,
} from '../../api/me';
import {
  fetchNotificationPreferences,
  patchNotificationPreferences,
  type NotificationPreferenceKey,
  type NotificationPreferences,
} from '../../api/notificationPreferences';
import type { HomeStackParamList } from '../../navigation/types';
import { MemberGate } from '../../session/MemberGate';
import { useSession } from '../../session/SessionContext';
import { testIds } from '../../test/testIds';
import { radius, space, type, useTheme } from '../../theme';
import { Button } from '../../ui/Button';
import { Eyebrow } from '../../ui/Eyebrow';
import { SettingsRow } from '../../ui/SettingsRow';

type Props = NativeStackScreenProps<HomeStackParamList, 'Settings'>;

export function SettingsScreen({ navigation }: Props) {
  return (
    <MemberGate title="Settings">
      <SettingsForm navigation={navigation} />
    </MemberGate>
  );
}

function SettingsForm({ navigation }: Pick<Props, 'navigation'>) {
  const { c } = useTheme();
  const { accessToken, refreshProfile } = useSession();
  const [profile, setProfile] = useState<MemberProfile | null>(null);
  const [displayName, setDisplayName] = useState('');
  const [privacy, setPrivacy] = useState<MessagePrivacy>('members');
  const [preferences, setPreferences] = useState<NotificationPreferences | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [savingName, setSavingName] = useState(false);
  const [savingPrivacy, setSavingPrivacy] = useState(false);
  const [avatarBusy, setAvatarBusy] = useState(false);
  const [legacyBusy, setLegacyBusy] = useState(false);
  const [adoptLegacyName, setAdoptLegacyName] = useState(true);
  const [selectedLegacyId, setSelectedLegacyId] = useState<number | null>(null);

  const load = useCallback(async () => {
    if (!accessToken) {
      return;
    }

    setError(null);
    try {
      const [next, nextPreferences] = await Promise.all([
        fetchJson('/me', { accessToken }).then(parseMemberProfile),
        fetchNotificationPreferences(accessToken),
      ]);
      setProfile(next);
      setDisplayName(next.displayName);
      setPrivacy(next.messagePrivacy);
      setSelectedLegacyId(next.legacyLink.claimableMatches[0]?.userId ?? next.legacyLink.match?.userId ?? null);
      setPreferences(nextPreferences);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load account settings.');
    }
  }, [accessToken]);

  useEffect(() => {
    void load();
  }, [load]);

  async function applyProfile(next: MemberProfile, message: string) {
    setProfile(next);
    setDisplayName(next.displayName);
    setPrivacy(next.messagePrivacy);
    setStatus(message);
    await refreshProfile();
  }

  async function saveDisplayName() {
    if (!accessToken || !profile) {
      return;
    }

    const invalid = validateDisplayName(displayName, profile.limits);
    if (invalid) {
      setError(invalid);
      return;
    }

    setSavingName(true);
    setError(null);
    try {
      const next = parseMemberProfile(
        await sendJson('/me', {
          method: 'PATCH',
          accessToken,
          body: { displayName: displayName.trim() },
        }),
      );
      await applyProfile(next, 'Display name updated.');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not update display name.');
    } finally {
      setSavingName(false);
    }
  }

  async function savePrivacy(nextPrivacy: MessagePrivacy) {
    if (!accessToken) {
      return;
    }

    setPrivacy(nextPrivacy);
    setSavingPrivacy(true);
    setError(null);
    try {
      const next = parseMemberProfile(
        await sendJson('/me', {
          method: 'PATCH',
          accessToken,
          body: { messagePrivacy: nextPrivacy },
        }),
      );
      await applyProfile(next, 'Messaging privacy updated.');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not update messaging privacy.');
    } finally {
      setSavingPrivacy(false);
    }
  }

  async function savePreference(key: NotificationPreferenceKey, value: boolean) {
    if (!accessToken || !preferences) {
      return;
    }

    const previous = preferences;
    setPreferences({ ...previous, [key]: value });
    setError(null);
    try {
      const next = await patchNotificationPreferences(accessToken, { [key]: value });
      setPreferences(next);
      setStatus('Notification preferences updated.');
    } catch (err) {
      setPreferences(previous);
      setError(err instanceof ApiError ? err.message : 'Could not update notification preferences.');
    }
  }

  async function pickAvatar(fromCamera: boolean) {
    if (!accessToken) {
      return;
    }

    const permission = fromCamera
      ? await ImagePicker.requestCameraPermissionsAsync()
      : await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!permission.granted) {
      setError(fromCamera ? 'Camera permission is required to take an avatar photo.' : 'Photo library permission is required to choose an avatar.');
      return;
    }

    const picked = fromCamera
      ? await ImagePicker.launchCameraAsync({ mediaTypes: ['images'], quality: 0.8, allowsEditing: true, aspect: [1, 1] })
      : await ImagePicker.launchImageLibraryAsync({ mediaTypes: ['images'], quality: 0.8, allowsEditing: true, aspect: [1, 1] });
    if (picked.canceled || !picked.assets[0]) {
      return;
    }

    const asset = picked.assets[0];

    setAvatarBusy(true);
    setError(null);
    try {
      const next = await uploadMemberAvatar(
        {
          uri: asset.uri,
          name: asset.fileName ?? 'avatar.jpg',
          type: asset.mimeType ?? 'image/jpeg',
        },
        accessToken,
      );
      await applyProfile(next, 'Avatar updated.');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not update avatar.');
    } finally {
      setAvatarBusy(false);
    }
  }

  async function removeAvatar() {
    if (!accessToken) {
      return;
    }

    setAvatarBusy(true);
    setError(null);
    try {
      const next = parseMemberProfile(await sendJson('/me/avatar', { method: 'DELETE', accessToken }));
      await applyProfile(next, 'Avatar removed.');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not remove avatar.');
    } finally {
      setAvatarBusy(false);
    }
  }

  async function claimLegacy() {
    if (!accessToken || selectedLegacyId == null) {
      setError('Choose which legacy forum account to claim.');
      return;
    }

    setLegacyBusy(true);
    setError(null);
    try {
      const next = parseMemberProfile(
        await sendJson('/me/legacy-link', {
          accessToken,
          body: { legacyUserId: selectedLegacyId, adoptDisplayName: adoptLegacyName },
        }),
      );
      await applyProfile(next, adoptLegacyName ? 'Legacy account claimed and display name updated.' : 'Legacy account claimed.');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not claim the legacy account.');
    } finally {
      setLegacyBusy(false);
    }
  }

  async function unlinkLegacy() {
    if (!accessToken) {
      return;
    }

    setLegacyBusy(true);
    setError(null);
    try {
      const next = parseMemberProfile(await sendJson('/me/legacy-link', { method: 'DELETE', accessToken }));
      await applyProfile(next, 'Legacy account unlinked.');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not unlink the legacy account.');
    } finally {
      setLegacyBusy(false);
    }
  }

  const imageUri = avatarUrl(getAppConfig().apiBaseUrl, profile?.avatarPath ?? null, profile?.displayName);
  const limits = profile?.limits;

  return (
    <ScrollView style={{ flex: 1, backgroundColor: c.surfacePage }} contentContainerStyle={{ paddingBottom: space.section }}>
      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xl, gap: space.md }}>
        <Text style={[type.body, { color: c.textSecondary }]}>
          Update the name shown on your posts and contributions.
        </Text>
        {status ? (
          <Text style={[type.body, { color: c.accentPrimary }]}>
            {status}
          </Text>
        ) : null}
        {error ? (
          <Text style={[type.body, { color: c.danger }]} accessibilityRole="alert">
            {error}
          </Text>
        ) : null}
        {profile ? (
          <Text style={[type.caption, { color: c.textMuted }]}>{profile.email}</Text>
        ) : null}
      </View>

      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xxl, paddingBottom: space.md }}>
        <Eyebrow tone="muted">Profile avatar</Eyebrow>
      </View>
      <View style={{ paddingHorizontal: space.xl, flexDirection: 'row', gap: space.md, alignItems: 'center' }}>
        <View
          style={{
            width: 72,
            height: 72,
            borderRadius: 36,
            borderWidth: 1,
            borderColor: c.border,
            overflow: 'hidden',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          {imageUri ? (
            <Image source={{ uri: imageUri }} style={{ width: 72, height: 72 }} />
          ) : (
            <Text style={[type.meta, { color: c.textMuted }]}>None</Text>
          )}
        </View>
        <View style={{ flex: 1, gap: 8 }}>
          <Button label="Take photo" size="sm" loading={avatarBusy} onPress={() => void pickAvatar(true)} />
          <Button label="Choose photo" size="sm" variant="outline" loading={avatarBusy} onPress={() => void pickAvatar(false)} />
          {profile?.hasAvatar ? (
            <Button label="Remove avatar" size="sm" variant="ghost" loading={avatarBusy} onPress={() => void removeAvatar()} />
          ) : null}
        </View>
      </View>
      <Text style={[type.caption, { color: c.textMuted, paddingHorizontal: space.xl, marginTop: space.sm }]}>
        JPEG, PNG, or WebP. Max 2 MB. Cropped to a square and stored as WebP.
      </Text>

      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xxl, gap: space.md }}>
        <Eyebrow tone="muted">Display name</Eyebrow>
        <TextInput
          value={displayName}
          onChangeText={setDisplayName}
          maxLength={limits?.maxDisplayNameLength ?? 100}
          autoCapitalize="words"
          autoCorrect={false}
          accessibilityLabel="Display name"
          style={{
            minHeight: 48,
            borderWidth: 1,
            borderColor: c.border,
            borderRadius: radius.xs,
            paddingHorizontal: space.md,
            color: c.textPrimary,
            ...type.body,
          }}
        />
        <Text style={[type.caption, { color: c.textMuted }]}>
          {limits?.minDisplayNameLength ?? 2}–{limits?.maxDisplayNameLength ?? 100} characters. This name appears on
          forum posts, photo attributions, and other contributions. Display names do not need to be unique.
        </Text>
        <Button label="Save display name" loading={savingName} onPress={() => void saveDisplayName()} />
      </View>

      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xxl, gap: space.md }}>
        <Eyebrow tone="muted">Who can message me</Eyebrow>
        <Text style={[type.caption, { color: c.textMuted }]}>
          This controls who may start a new private conversation. Replies in existing conversations still work unless
          you block that member.
        </Text>
        {messagePrivacyOptions.map((option) => (
          <Pressable
            key={option.value}
            accessibilityRole="radio"
            accessibilityState={{ selected: privacy === option.value }}
            onPress={() => void savePrivacy(option.value)}
            style={{
              minHeight: 48,
              borderWidth: 1,
              borderColor: privacy === option.value ? c.accentPrimary : c.border,
              borderRadius: radius.xs,
              paddingHorizontal: space.md,
              justifyContent: 'center',
            }}
          >
            <Text style={[type.listTitle, { color: c.textPrimary }]}>{option.label}</Text>
          </Pressable>
        ))}
        {savingPrivacy ? <Text style={[type.caption, { color: c.textMuted }]}>Saving…</Text> : null}
      </View>

      {preferences ? (
        <View style={{ paddingTop: space.xxl }}>
          <View style={{ paddingHorizontal: space.xl, paddingBottom: space.md }}>
            <Eyebrow tone="muted">Notifications</Eyebrow>
          </View>
          <SettingsRow
            title="Forum replies"
            subtitle="You still need to Watch a topic to get forum reply pushes."
            switchValue={preferences.forumReply}
            onSwitch={(value) => void savePreference('forumReply', value)}
            accessibilityLabel="Forum replies"
            testID={testIds.settingsNotifyForumReply}
          />
          <SettingsRow
            title="Private messages"
            switchValue={preferences.privateMessage}
            onSwitch={(value) => void savePreference('privateMessage', value)}
            accessibilityLabel="Private messages"
            testID={testIds.settingsNotifyPrivateMessage}
          />
          <SettingsRow
            title="News"
            switchValue={preferences.news}
            onSwitch={(value) => void savePreference('news', value)}
            accessibilityLabel="News"
            testID={testIds.settingsNotifyNews}
          />
        </View>
      ) : null}

      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xxl, gap: space.md }}>
        <Eyebrow tone="muted">Linked sign-in providers</Eyebrow>
        <Text style={[type.body, { color: c.textSecondary }]}>
          {profile?.linkedProviders.length ? profile.linkedProviders.join(', ') : 'None linked'}
        </Text>
      </View>

      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xxl, gap: space.md }}>
        <Eyebrow tone="muted">Legacy forum account</Eyebrow>
        {profile?.legacyLink.kind === 'linked' && profile.legacyLink.match ? (
          <>
            <Text style={[type.body, { color: c.textSecondary }]}>
              Linked to legacy forum account {profile.legacyLink.match.username} (id {profile.legacyLink.match.userId}).
            </Text>
            <Button label="Unlink legacy account" variant="outline" loading={legacyBusy} onPress={() => void unlinkLegacy()} />
          </>
        ) : profile?.legacyLink.kind === 'claimable' && profile.legacyLink.claimableMatches.length > 0 ? (
          <>
            <Text style={[type.body, { color: c.textSecondary }]}>
              We found a classic QueenZone forum account matching your email. Claim it to connect your modern
              membership with your archive history.
            </Text>
            {profile.legacyLink.claimableMatches.map((match) => (
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
          </>
        ) : (
          <Text style={[type.caption, { color: c.textMuted }]}>No legacy forum account is linked to this email.</Text>
        )}
      </View>

      <View style={{ paddingHorizontal: space.xl, paddingTop: space.xxl, gap: space.md, paddingBottom: space.xl }}>
        <Eyebrow tone="muted">Delete account</Eyebrow>
        {profile?.scheduledDeletionAt ? (
          <Text style={[type.body, { color: c.textSecondary }]}>
            Deletion is scheduled. Your public identity is anonymised until then, but you can still change your mind.
          </Text>
        ) : (
          <Text style={[type.body, { color: c.textSecondary }]}>
            Delete your account, sign-in details, avatar, and modern contributions.
          </Text>
        )}
        <Button
          label={profile?.scheduledDeletionAt ? 'Review or cancel deletion' : 'Delete my account'}
          variant="outline"
          onPress={() => navigation.navigate('DeleteAccount')}
        />
      </View>
    </ScrollView>
  );
}
