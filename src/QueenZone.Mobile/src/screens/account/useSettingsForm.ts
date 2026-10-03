import { useCallback, useEffect, useState } from 'react';
import * as ImagePicker from 'expo-image-picker';
import { getAppConfig } from '../../config/appConfig';
import { fetchJson, sendJson } from '../../api/client';
import { ApiError } from '../../api/errors';
import { uploadMemberAvatar } from '../../api/memberAvatar';
import { avatarUrl, parseMemberProfile, validateDisplayName, type MessagePrivacy, type MemberProfile } from '../../api/me';
import { fetchNotificationPreferences, patchNotificationPreferences, type NotificationPreferenceKey, type NotificationPreferences } from '../../api/notificationPreferences';
import { useSession } from '../../session/SessionContext';
import { initialLegacySelection, legacyClaimStatus } from './settingsPresentation';

export function useSettingsForm() {
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
      setSelectedLegacyId(initialLegacySelection(next));
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
      await applyProfile(next, legacyClaimStatus(adoptLegacyName));
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

  return { profile, displayName, setDisplayName, privacy, preferences, error, status,
    savingName, savingPrivacy, avatarBusy, legacyBusy, adoptLegacyName, setAdoptLegacyName,
    selectedLegacyId, setSelectedLegacyId, imageUri, limits, saveDisplayName, savePrivacy,
    savePreference, pickAvatar, removeAvatar, claimLegacy, unlinkLegacy };
}
