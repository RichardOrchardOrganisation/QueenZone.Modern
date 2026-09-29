import { useHeaderHeight } from '@react-navigation/elements';
import { useCallback, useLayoutEffect, useState } from 'react';
import {
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Switch,
  Text,
  TextInput,
  View,
} from 'react-native';
import * as DocumentPicker from 'expo-document-picker';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { ApiError } from '../../api';
import {
  createFanPerformanceSubmission,
  type AudioUploadFile,
  type FanPerformanceSubmissionCreated,
} from '../../api/fanPerformanceSubmissions';
import type { ArchiveStackParamList } from '../../navigation/types';
import { MemberGate } from '../../session/MemberGate';
import { useSession } from '../../session/SessionContext';
import { space, type, useTheme } from '../../theme';
import { Button } from '../../ui/Button';
import { testIds } from '../../test/testIds';
import {
  audioFromDocumentAsset,
  fanPerformanceDescriptionMaxLength,
  fanPerformanceSubmitCopy,
  fanPerformanceTitleMaxLength,
  validateFanPerformanceSubmit,
} from './fanPerformanceSubmitMeta';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'FanPerformanceSubmit'>;

export function FanPerformanceSubmitScreen({ navigation }: Props) {
  return (
    <MemberGate title="Submit a fan performance">
      <FanPerformanceSubmitForm navigation={navigation} />
    </MemberGate>
  );
}

function FanPerformanceSubmitForm({ navigation }: Pick<Props, 'navigation'>) {
  const headerHeight = useHeaderHeight();
  const insets = useSafeAreaInsets();
  const { c } = useTheme();
  const { accessToken } = useSession();
  const [title, setTitle] = useState('');
  const [coveredSong, setCoveredSong] = useState('');
  const [performedBy, setPerformedBy] = useState('');
  const [description, setDescription] = useState('');
  const [rightsAccepted, setRightsAccepted] = useState(false);
  const [audio, setAudio] = useState<AudioUploadFile | null>(null);
  const [fileSize, setFileSize] = useState<number | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [confirmation, setConfirmation] = useState<FanPerformanceSubmissionCreated | null>(null);

  useLayoutEffect(() => {
    navigation.setOptions({
      title: confirmation ? fanPerformanceSubmitCopy.confirmationTitle : fanPerformanceSubmitCopy.title,
    });
  }, [confirmation, navigation]);

  const resetForm = useCallback(() => {
    setTitle('');
    setCoveredSong('');
    setPerformedBy('');
    setDescription('');
    setRightsAccepted(false);
    setAudio(null);
    setFileSize(null);
    setSubmitError(null);
    setConfirmation(null);
  }, []);

  const pickAudio = useCallback(async () => {
    setSubmitError(null);
    try {
      const picked = await DocumentPicker.getDocumentAsync({
        copyToCacheDirectory: true,
        multiple: false,
        type: ['audio/mpeg', 'audio/mp3', 'audio/flac', 'audio/*'],
      });
      if (picked.canceled || !picked.assets?.[0]) {
        return;
      }

      const mapped = audioFromDocumentAsset(picked.assets[0]);
      setAudio(mapped.file);
      setFileSize(mapped.fileSize);
    } catch {
      setSubmitError('Could not open the file picker.');
    }
  }, []);

  const submit = useCallback(async () => {
    const validation = validateFanPerformanceSubmit({
      title,
      coveredSong,
      performedBy,
      description,
      rightsDeclarationAccepted: rightsAccepted,
      audio,
      fileSize,
    });
    if (validation) {
      setSubmitError(validation);
      return;
    }
    if (!accessToken) {
      setSubmitError('Sign in to submit a fan performance.');
      return;
    }
    if (!audio) {
      setSubmitError('Choose an audio file to upload.');
      return;
    }

    setSubmitting(true);
    setSubmitError(null);
    try {
      const created = await createFanPerformanceSubmission(
        {
          title,
          coveredSong,
          performedBy,
          description,
          rightsDeclarationAccepted: rightsAccepted,
          audio,
        },
        accessToken,
      );
      setConfirmation(created);
    } catch (err: unknown) {
      setSubmitError(err instanceof ApiError ? err.message : 'Could not submit fan performance.');
    } finally {
      setSubmitting(false);
    }
  }, [accessToken, audio, coveredSong, description, fileSize, performedBy, rightsAccepted, title]);

  return (
    <KeyboardAvoidingView
      style={[styles.flex, { backgroundColor: c.surfacePage }]}
      behavior="padding"
      keyboardVerticalOffset={Platform.OS === 'ios' ? headerHeight : 0}
    >
      <ScrollView
        style={styles.flex}
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={[styles.content, { paddingBottom: insets.bottom + space.xxl }]}
      >
        <Text style={[type.eyebrow, { color: c.accentPrimary }]}>{fanPerformanceSubmitCopy.eyebrow}</Text>
        <Text style={[type.pageTitle, { color: c.textPrimary }]} maxFontSizeMultiplier={1.4} allowFontScaling>
          {confirmation ? fanPerformanceSubmitCopy.confirmationTitle : fanPerformanceSubmitCopy.title}
        </Text>
        <Text style={[type.body, { color: c.textSecondary }]} allowFontScaling>
          {confirmation ? fanPerformanceSubmitCopy.confirmationMessage : fanPerformanceSubmitCopy.intro}
        </Text>

        {confirmation ? (
          <View>
            <Text style={[type.body, { color: c.textPrimary }]}>{confirmation.title}</Text>
            <Button label={fanPerformanceSubmitCopy.anotherAction} onPress={resetForm} />
          </View>
        ) : (
          <View style={styles.form}>
            <Field label="Title" value={title} onChangeText={setTitle} maxLength={fanPerformanceTitleMaxLength} />
            <Field
              label="Queen song covered"
              value={coveredSong}
              onChangeText={setCoveredSong}
              maxLength={fanPerformanceTitleMaxLength}
            />
            <Field
              label="Performed by"
              value={performedBy}
              onChangeText={setPerformedBy}
              maxLength={fanPerformanceTitleMaxLength}
            />
            <Field
              label="Description"
              value={description}
              onChangeText={setDescription}
              maxLength={fanPerformanceDescriptionMaxLength}
              multiline
            />
            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Choose audio file"
              testID={testIds.fanPerformanceSubmitPick}
              onPress={() => void pickAudio()}
              style={[styles.pick, { borderColor: c.border, backgroundColor: c.surfaceCard }]}
            >
              <Text style={[type.body, { color: c.textPrimary }]}>
                {audio ? audio.name : 'Choose an existing audio file'}
              </Text>
            </Pressable>
            <View style={styles.rights}>
              <Switch
                value={rightsAccepted}
                onValueChange={setRightsAccepted}
                accessibilityLabel="Rights declaration"
              />
              <Text style={[type.caption, { color: c.textSecondary, flex: 1 }]}>
                {fanPerformanceSubmitCopy.rightsDeclaration}
              </Text>
            </View>
            <Text style={[type.caption, { color: c.textMuted }]}>{fanPerformanceSubmitCopy.help}</Text>
            {submitError ? <Text style={[type.body, { color: c.danger }]}>{submitError}</Text> : null}
            <Button
              label={fanPerformanceSubmitCopy.submitAction}
              loading={submitting}
              onPress={() => void submit()}
              testID={testIds.fanPerformanceSubmitSend}
            />
          </View>
        )}
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

function Field({
  label,
  value,
  onChangeText,
  maxLength,
  multiline = false,
}: {
  label: string;
  value: string;
  onChangeText: (value: string) => void;
  maxLength: number;
  multiline?: boolean;
}) {
  const { c } = useTheme();
  return (
    <View style={styles.field}>
      <Text style={[type.caption, { color: c.textSecondary }]}>{label}</Text>
      <TextInput
        value={value}
        onChangeText={onChangeText}
        maxLength={maxLength}
        multiline={multiline}
        accessibilityLabel={label}
        style={[
          styles.input,
          {
            color: c.textPrimary,
            borderColor: c.border,
            backgroundColor: c.surfaceCard,
            minHeight: multiline ? 96 : 44,
          },
        ]}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  content: {
    paddingHorizontal: space.xl,
    paddingTop: space.base,
    gap: space.md,
  },
  form: { gap: space.md },
  field: { gap: space.xs },
  input: {
    borderWidth: 1,
    borderRadius: 8,
    paddingHorizontal: space.md,
    paddingVertical: space.sm,
  },
  pick: {
    borderWidth: 1,
    borderRadius: 8,
    padding: space.base,
    minHeight: 48,
    justifyContent: 'center',
  },
  rights: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.md,
  },
});
