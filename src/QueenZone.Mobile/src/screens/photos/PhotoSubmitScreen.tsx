import { useHeaderHeight } from '@react-navigation/elements';
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import {
  Keyboard,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { Image } from 'expo-image';
import * as ImagePicker from 'expo-image-picker';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { ApiError, fetchPhotoCategories, type PhotoCategoryListItem, type PhotoSubmissionCreated } from '../../api';
import { createPhotoSubmission, type PhotoUploadFile } from '../../api/photoSubmissions';
import type { PhotosStackParamList } from '../../navigation/types';
import { MemberGate } from '../../session/MemberGate';
import { useSession } from '../../session/SessionContext';
import { fonts, radius, space, type, useTheme } from '../../theme';
import { Button } from '../../ui/Button';
import { Chip } from '../../ui/Chip';
import {
  archiveImagePickerOptions,
  formatSubmittedAt,
  parseApproximateDate,
  parseApproximateYear,
  photoDescriptionMaxLength,
  photoFromPickerAsset,
  photoSubmitCopy,
  photoTitleMaxLength,
  validatePhotoSubmit,
} from './photoSubmitMeta';

type Props = NativeStackScreenProps<PhotosStackParamList, 'PhotoSubmit'>;

export function PhotoSubmitScreen({ navigation }: Props) {
  return (
    <MemberGate title="Submit a photo">
      <PhotoSubmitForm navigation={navigation} />
    </MemberGate>
  );
}

function PhotoSubmitForm({ navigation }: Pick<Props, 'navigation'>) {
  const headerHeight = useHeaderHeight();
  const insets = useSafeAreaInsets();
  const { c } = useTheme();
  const { accessToken } = useSession();
  const scrollRef = useRef<ScrollView>(null);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [suggestedCategory, setSuggestedCategory] = useState('');
  const [approximateYear, setApproximateYear] = useState('');
  const [approximateDate, setApproximateDate] = useState('');
  const [photo, setPhoto] = useState<PhotoUploadFile | null>(null);
  const [fileSize, setFileSize] = useState<number | null>(null);
  const [categories, setCategories] = useState<PhotoCategoryListItem[]>([]);
  const [categoriesError, setCategoriesError] = useState<string | null>(null);
  const [categoriesReload, setCategoriesReload] = useState(0);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [confirmation, setConfirmation] = useState<PhotoSubmissionCreated | null>(null);

  const revealSubmitAfterPicker = useCallback(() => {
    Keyboard.dismiss();
    requestAnimationFrame(() => {
      scrollRef.current?.scrollToEnd({ animated: true });
    });
  }, []);

  useLayoutEffect(() => {
    navigation.setOptions({
      title: confirmation ? photoSubmitCopy.confirmationTitle : photoSubmitCopy.title,
    });
  }, [confirmation, navigation]);

  useEffect(() => {
    const controller = new AbortController();
    setCategoriesError(null);
    void fetchPhotoCategories({ page: 1, pageSize: 100, signal: controller.signal })
      .then((page) => {
        setCategories(page.items);
        setCategoriesError(null);
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        setCategories([]);
        setCategoriesError(err instanceof ApiError ? err.message : photoSubmitCopy.categoriesLoadError);
      });
    return () => controller.abort();
  }, [categoriesReload]);

  const resetForm = useCallback(() => {
    setTitle('');
    setDescription('');
    setSuggestedCategory('');
    setApproximateYear('');
    setApproximateDate('');
    setPhoto(null);
    setFileSize(null);
    setSubmitError(null);
    setConfirmation(null);
  }, []);

  const pickPhoto = useCallback(async (fromCamera: boolean) => {
    Keyboard.dismiss();
    setSubmitError(null);
    const permission = fromCamera
      ? await ImagePicker.requestCameraPermissionsAsync()
      : await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!permission.granted) {
      setSubmitError(
        fromCamera
          ? 'Camera permission is required to take a photo.'
          : 'Photo library permission is required to choose a photo.',
      );
      revealSubmitAfterPicker();
      return;
    }

    try {
      const pickerOptions: ImagePicker.ImagePickerOptions = {
        ...archiveImagePickerOptions,
        preferredAssetRepresentationMode:
          ImagePicker.UIImagePickerPreferredAssetRepresentationMode.Compatible,
      };
      const picked = fromCamera
        ? await ImagePicker.launchCameraAsync(pickerOptions)
        : await ImagePicker.launchImageLibraryAsync(pickerOptions);
      if (picked.canceled || !picked.assets[0]) {
        revealSubmitAfterPicker();
        return;
      }

      const mapped = photoFromPickerAsset(picked.assets[0]);
      if ('error' in mapped) {
        setSubmitError(mapped.error);
        revealSubmitAfterPicker();
        return;
      }

      setPhoto(mapped.photo);
      setFileSize(mapped.fileSize);
      revealSubmitAfterPicker();
    } catch {
      setSubmitError(fromCamera ? 'The camera is not available on this device.' : 'Could not open the photo library.');
      revealSubmitAfterPicker();
    }
  }, [revealSubmitAfterPicker]);

  const submit = useCallback(async () => {
    const validation = validatePhotoSubmit({
      title,
      description,
      suggestedCategory,
      approximateYear,
      approximateDate,
      photo,
      fileSize,
    });
    if (validation) {
      setSubmitError(validation);
      return;
    }
    if (!accessToken) {
      setSubmitError('Sign in to submit a photo.');
      return;
    }
    if (!photo) {
      setSubmitError('Choose a photo to upload.');
      return;
    }

    const year = parseApproximateYear(approximateYear);
    const date = parseApproximateDate(approximateDate);
    setSubmitting(true);
    setSubmitError(null);
    try {
      const created = await createPhotoSubmission(
        {
          title,
          description,
          suggestedCategory,
          approximateYear: typeof year === 'number' ? year : undefined,
          approximateDate: typeof date === 'string' ? date : undefined,
          photo,
        },
        accessToken,
      );
      setConfirmation(created);
    } catch (err: unknown) {
      setSubmitError(err instanceof ApiError ? err.message : 'Could not submit photo.');
    } finally {
      setSubmitting(false);
    }
  }, [
    accessToken,
    approximateDate,
    approximateYear,
    description,
    fileSize,
    photo,
    suggestedCategory,
    title,
  ]);

  return (
    <KeyboardAvoidingView
      style={[styles.flex, { backgroundColor: c.surfacePage }]}
      behavior="padding"
      keyboardVerticalOffset={Platform.OS === 'ios' ? headerHeight : 0}
    >
      <ScrollView
        ref={scrollRef}
        style={styles.flex}
        keyboardShouldPersistTaps="handled"
        keyboardDismissMode="on-drag"
        contentContainerStyle={[styles.content, { paddingBottom: insets.bottom + space.xxl }]}
      >
        <Text style={[type.eyebrow, { color: c.accentPrimary }]}>{photoSubmitCopy.eyebrow}</Text>
        <Text style={[type.pageTitle, { color: c.textPrimary }]} maxFontSizeMultiplier={1.4} allowFontScaling>
          {confirmation ? photoSubmitCopy.confirmationTitle : photoSubmitCopy.title}
        </Text>
        <Text style={[type.body, { color: c.textSecondary }]} allowFontScaling>
          {confirmation ? photoSubmitCopy.confirmationMessage : photoSubmitCopy.intro}
        </Text>

        {confirmation ? (
          <View style={[styles.notice, { borderColor: c.accentPrimary }]} accessibilityRole="text">
            <Text style={[type.body, { color: c.textPrimary }]} accessibilityRole="alert">
              {photoSubmitCopy.confirmationMessage}
            </Text>
            {(photo) && (
              <Image
                source={{ uri: photo.uri }}
                style={styles.preview}
                contentFit="cover"
                accessibilityLabel={confirmation.title}
              />
            )}
            <MetaRow label="Title" value={confirmation.title} color={c.textPrimary} muted={c.textMuted} />
            {(description.trim()) && (
              <MetaRow label="Description" value={description.trim()} color={c.textPrimary} muted={c.textMuted} />
            )}
            {(suggestedCategory.trim()) && (
              <MetaRow
                label={photoSubmitCopy.categoryLabel}
                value={suggestedCategory.trim()}
                color={c.textPrimary}
                muted={c.textMuted}
              />
            )}
            <MetaRow label="Status" value={confirmation.status} color={c.textPrimary} muted={c.textMuted} />
            <MetaRow
              label="Submitted"
              value={formatSubmittedAt(confirmation.submittedAt)}
              color={c.textPrimary}
              muted={c.textMuted}
            />
            <Button label={photoSubmitCopy.anotherAction} variant="outline" onPress={resetForm} />
          </View>
        ) : (
          <View style={styles.fields}>
            <FieldLabel color={c.textMuted}>Title</FieldLabel>
            <TextInput
              value={title}
              onChangeText={setTitle}
              maxLength={photoTitleMaxLength}
              accessibilityLabel="Title"
              placeholder="Photo title"
              placeholderTextColor={c.textMuted}
              style={[styles.input, { color: c.textPrimary, borderColor: c.border, backgroundColor: c.surfaceCard }]}
            />

            <FieldLabel color={c.textMuted}>Description (optional)</FieldLabel>
            <TextInput
              value={description}
              onChangeText={setDescription}
              maxLength={photoDescriptionMaxLength}
              multiline
              textAlignVertical="top"
              accessibilityLabel="Description"
              placeholder="Concert, memorabilia, or archive find"
              placeholderTextColor={c.textMuted}
              style={[
                styles.input,
                styles.textarea,
                { color: c.textPrimary, borderColor: c.border, backgroundColor: c.surfaceCard },
              ]}
            />

            <FieldLabel color={c.textMuted}>{photoSubmitCopy.categoryLabel}</FieldLabel>
            {(categoriesError) && (
              <View>
                <Text style={[type.body, { color: c.danger }]} accessibilityRole="alert">
                  {categoriesError}
                </Text>
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel="Retry loading photo categories"
                  onPress={() => setCategoriesReload((n) => n + 1)}
                  hitSlop={8}
                >
                  <Text style={[type.button, { color: c.accentPrimary, marginTop: space.xs }]}>Retry</Text>
                </Pressable>
              </View>
            )}
            {(categories.length > 0) && (
              <View style={styles.chips}>
                {categories.map((category) => {
                  const active = suggestedCategory.trim() === category.name;
                  return (
                    <Chip
                      key={category.slug}
                      label={category.name}
                      active={active}
                      onPress={() => {
                        setSuggestedCategory(active ? '' : category.name);
                        if (!active) {
                          setSubmitError(null);
                        }
                      }}
                    />
                  );
                })}
              </View>
            )}

            <FieldLabel color={c.textMuted}>Approximate year (optional)</FieldLabel>
            <TextInput
              value={approximateYear}
              onChangeText={setApproximateYear}
              keyboardType="number-pad"
              maxLength={4}
              accessibilityLabel="Approximate year"
              placeholder="1986"
              placeholderTextColor={c.textMuted}
              style={[styles.input, { color: c.textPrimary, borderColor: c.border, backgroundColor: c.surfaceCard }]}
            />

            <FieldLabel color={c.textMuted}>Approximate date (optional)</FieldLabel>
            <TextInput
              value={approximateDate}
              onChangeText={setApproximateDate}
              autoCapitalize="none"
              autoCorrect={false}
              accessibilityLabel="Approximate date"
              placeholder="YYYY-MM-DD"
              placeholderTextColor={c.textMuted}
              style={[styles.input, { color: c.textPrimary, borderColor: c.border, backgroundColor: c.surfaceCard }]}
            />

            <FieldLabel color={c.textMuted}>Photo</FieldLabel>
            {(photo) && (
              <Image source={{ uri: photo.uri }} style={styles.preview} contentFit="cover" accessibilityLabel={photo.name} />
            )}
            <View style={styles.pickerRow}>
              <Button
                label="Take photo"
                size="sm"
                onPress={() => {
                  void pickPhoto(true);
                }}
              />
              <Button
                label="Choose from library"
                size="sm"
                variant="outline"
                onPress={() => {
                  void pickPhoto(false);
                }}
              />
            </View>
            <Text style={[type.caption, { color: c.textMuted }]}>{photoSubmitCopy.help}</Text>

            {(submitError) && (
              <Text style={[type.body, { color: c.danger }]} accessibilityRole="alert">
                {submitError}
              </Text>
            )}

            <Button
              label={photoSubmitCopy.submitAction}
              loading={submitting}
              disabled={!accessToken || Boolean(categoriesError)}
              onPress={() => {
                void submit();
              }}
            />
          </View>
        )}
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

function FieldLabel({ color, children }: { color: string; children: string }) {
  return <Text style={[type.listTitle, { color }]}>{children}</Text>;
}

function MetaRow({
  label,
  value,
  color,
  muted,
}: {
  label: string;
  value: string;
  color: string;
  muted: string;
}) {
  return (
    <View style={styles.meta}>
      <Text style={[type.caption, { color: muted }]}>{label}</Text>
      <Text style={[type.body, { color }]}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  flex: {
    flex: 1,
  },
  content: {
    paddingHorizontal: space.xl,
    paddingTop: space.base,
    gap: space.md,
  },
  fields: {
    gap: space.sm,
  },
  chips: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: space.sm,
  },
  pickerRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: space.sm,
  },
  input: {
    minHeight: 48,
    borderWidth: 1,
    borderRadius: radius.xs,
    paddingHorizontal: space.md,
    paddingVertical: space.sm,
    fontFamily: fonts.body,
    fontSize: type.body.fontSize,
  },
  textarea: {
    minHeight: 120,
  },
  preview: {
    width: '100%',
    height: 200,
    borderRadius: radius.sm,
  },
  notice: {
    borderWidth: 1,
    borderRadius: radius.md,
    padding: space.base,
    gap: space.sm,
  },
  meta: {
    gap: 2,
  },
});
