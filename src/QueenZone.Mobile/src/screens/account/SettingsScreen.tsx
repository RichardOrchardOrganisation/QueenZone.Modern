import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { useHeaderHeight } from '@react-navigation/elements';
import { Image } from 'expo-image';
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, Text, TextInput, View } from 'react-native';
import { messagePrivacyOptions } from '../../api/me';
import type { HomeStackParamList } from '../../navigation/types';
import { MemberGate } from '../../session/MemberGate';
import { testIds } from '../../test/testIds';
import { radius, space, type, useTheme } from '../../theme';
import { Button } from '../../ui/Button';
import { Eyebrow } from '../../ui/Eyebrow';
import { SettingsRow } from '../../ui/SettingsRow';
import { useSettingsForm } from './useSettingsForm';
import { SettingsLegacySection } from './SettingsLegacySection';
import { settingsAccountView } from './settingsPresentation';

type Props = NativeStackScreenProps<HomeStackParamList, 'Settings'>;

export function SettingsScreen({ navigation }: Props) {
  return (
    <MemberGate title="Settings">
      <SettingsForm navigation={navigation} />
    </MemberGate>
  );
}

function SettingsForm({ navigation }: Pick<Props, 'navigation'>) {
  const headerHeight = useHeaderHeight();
  const { c } = useTheme();
  const settings = useSettingsForm();
  const { profile, displayName, setDisplayName, privacy, preferences, error, status,
    savingName, savingPrivacy, avatarBusy, imageUri, saveDisplayName, savePrivacy,
    savePreference, pickAvatar, removeAvatar } = settings;
  const account = settingsAccountView(profile);

  return (
    <KeyboardAvoidingView
      style={{ flex: 1, backgroundColor: c.surfacePage }}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      keyboardVerticalOffset={Platform.OS === 'ios' ? headerHeight : 0}
    >
    <ScrollView style={{ flex: 1 }} keyboardShouldPersistTaps="handled" contentContainerStyle={{ paddingBottom: space.section }}>
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
          maxLength={account.maxDisplayNameLength}
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
          {account.minDisplayNameLength}–{account.maxDisplayNameLength} characters. This name appears on
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
          {account.linkedProviders}
        </Text>
      </View>

      <SettingsLegacySection settings={settings} />

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
          label={account.deletionLabel}
          variant="outline"
          onPress={() => navigation.navigate('DeleteAccount')}
        />
      </View>
    </ScrollView>
    </KeyboardAvoidingView>
  );
}
