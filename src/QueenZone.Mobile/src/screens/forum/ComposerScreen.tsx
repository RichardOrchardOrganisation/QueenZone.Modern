import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import {
  KeyboardAvoidingView,
  Linking,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import * as DocumentPicker from 'expo-document-picker';
import { cacheDirectory, writeAsStringAsync } from 'expo-file-system/legacy';
import * as ImagePicker from 'expo-image-picker';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import {
  ApiError,
  createForumReply,
  createForumTopic,
  fetchForumCategories,
  isOfflineFailure,
  isTimeoutFailure,
  type ForumCategoryListItem,
} from '../../api';
import type { ForumStackParamList } from '../../navigation/types';
import { getAppConfig } from '../../config';
import { resolvePushMemberId } from '../../notifications/pushMemberId';
import { enqueueForumReply, flushOfflineQueue, removeOfflineItem } from '../../offlineQueue';
import { MemberGate } from '../../session/MemberGate';
import { useSession } from '../../session/SessionContext';
import {
  defaultSmokeAttachAsset,
  isSmokeAttachEnabled,
  parseSmokeAttachAsset,
  smokeAttachFileName,
  stashSmokeAttachAsset,
  takePendingSmokeAttachAsset,
} from '../../session/smokeAttach';
import { testIds } from '../../test/testIds';
import { fonts, radius, space, type, useTheme } from '../../theme';
import { Button } from '../../ui/Button';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';
import {
  attachmentFromPickerAsset,
  composerAttachCopy,
  composerCopy,
  composerMode,
  forumImagePickerOptions,
  validateComposer,
  type ComposerAttachment,
  type ComposerPickerAsset,
} from './composerMeta';

type Props = NativeStackScreenProps<ForumStackParamList, 'Composer'>;

function messageFromUnknownError(err: unknown): string {
  return err instanceof ApiError ? err.message : 'Something went wrong.';
}

function smokeAttachAllowed(): boolean {
  const config = getAppConfig();
  return isSmokeAttachEnabled({
    dev: typeof __DEV__ !== 'undefined' ? __DEV__ : false,
    appEnv: config.appEnv,
    smokeEmbed: config.smokeEmbed,
  });
}

export function ComposerScreen({ navigation, route }: Props) {
  return (
    <MemberGate title="Compose">
      <ComposerForm navigation={navigation} route={route} />
    </MemberGate>
  );
}

function ComposerForm({ navigation, route }: Props) {
  const { c } = useTheme();
  const { accessToken, profile } = useSession();
  const mode = composerMode(route.params);
  const copy = composerCopy(mode);
  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');
  const [categoryId, setCategoryId] = useState(route.params?.categoryId);
  const [categoryName, setCategoryName] = useState(route.params?.categoryName);
  const [boards, setBoards] = useState<ForumCategoryListItem[]>([]);
  const [boardsError, setBoardsError] = useState<string | null>(null);
  const [boardsLoading, setBoardsLoading] = useState(mode === 'newTopic' && categoryId == null);
  const [boardsReloadToken, setBoardsReloadToken] = useState(0);
  const [attachment, setAttachment] = useState<ComposerAttachment | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [awaitingInject, setAwaitingInject] = useState(false);
  const awaitingInjectRef = useRef(false);

  useLayoutEffect(() => {
    navigation.setOptions({ title: copy.title });
  }, [copy.title, navigation]);

  useEffect(() => {
    if (mode !== 'newTopic' || categoryId != null) {
      return;
    }

    const controller = new AbortController();
    setBoardsLoading(true);
    setBoardsError(null);
    fetchForumCategories({ page: 1, pageSize: 50, signal: controller.signal })
      .then((page) => {
        setBoards(page.items);
        setBoardsError(null);
      })
      .catch((err: unknown) => {
        if (err instanceof Error && err.name === 'AbortError') {
          return;
        }
        setBoardsError(messageFromUnknownError(err));
      })
      .finally(() => setBoardsLoading(false));

    return () => controller.abort();
  }, [boardsReloadToken, categoryId, mode]);

  const retryBoards = useCallback(() => {
    setBoardsLoading(true);
    setBoardsError(null);
    setBoardsReloadToken((n) => n + 1);
  }, []);

  const applyPickedAsset = useCallback((asset: ComposerPickerAsset) => {
    const mapped = attachmentFromPickerAsset(asset);
    if ('error' in mapped) {
      setSubmitError(mapped.error);
      return;
    }
    setAttachment(mapped);
    awaitingInjectRef.current = false;
    setAwaitingInject(false);
  }, []);

  useEffect(() => {
    if (!smokeAttachAllowed()) {
      return;
    }

    const handleUrl = (url: string | null) => {
      if (!url) {
        return;
      }
      const asset = parseSmokeAttachAsset(url);
      if (!asset) {
        return;
      }
      if (awaitingInjectRef.current) {
        applyPickedAsset(asset);
        return;
      }
      stashSmokeAttachAsset(asset);
    };

    const subscription = Linking.addEventListener('url', ({ url }) => handleUrl(url));
    return () => subscription.remove();
  }, [applyPickedAsset]);

  const pickFromPhotos = useCallback(async () => {
    setSubmitError(null);
    const permission = await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!permission.granted) {
      setSubmitError(composerAttachCopy.photosPermission);
      return;
    }

    try {
      const picked = await ImagePicker.launchImageLibraryAsync({
        ...forumImagePickerOptions,
        preferredAssetRepresentationMode:
          ImagePicker.UIImagePickerPreferredAssetRepresentationMode.Compatible,
      });
      if (picked.canceled || !picked.assets[0]) {
        return;
      }
      applyPickedAsset(picked.assets[0]);
    } catch {
      setSubmitError(composerAttachCopy.photosUnavailable);
    }
  }, [applyPickedAsset]);

  const pickFromFiles = useCallback(async () => {
    setSubmitError(null);
    if (smokeAttachAllowed()) {
      const pending = takePendingSmokeAttachAsset();
      if (pending) {
        applyPickedAsset(pending);
        return;
      }
      awaitingInjectRef.current = true;
      setAwaitingInject(true);
      return;
    }

    try {
      const picked = await DocumentPicker.getDocumentAsync({
        copyToCacheDirectory: true,
        multiple: false,
      });
      if (picked.canceled || !picked.assets?.[0]) {
        return;
      }
      applyPickedAsset(picked.assets[0]);
    } catch {
      setSubmitError(composerAttachCopy.filesUnavailable);
    }
  }, [applyPickedAsset]);

  const injectSmokeAttach = useCallback(async () => {
    setSubmitError(null);
    const pending = takePendingSmokeAttachAsset();
    if (pending) {
      applyPickedAsset(pending);
      return;
    }

    if (Platform.OS === 'android') {
      if (!cacheDirectory) {
        setSubmitError(composerAttachCopy.filesUnavailable);
        return;
      }

      const uri = `${cacheDirectory}${smokeAttachFileName}`;
      try {
        await writeAsStringAsync(uri, 'QueenZone Maestro attach fixture\n');
      } catch {
        setSubmitError(composerAttachCopy.filesUnavailable);
        return;
      }
      applyPickedAsset({ uri, name: smokeAttachFileName, mimeType: 'text/plain' });
      return;
    }

    applyPickedAsset(defaultSmokeAttachAsset(Platform.OS));
  }, [applyPickedAsset]);

  const submit = useCallback(async () => {
    const validation = validateComposer({
      mode,
      title,
      body,
      categoryId,
      isLocked: route.params?.isLocked,
    });
    if (validation) {
      setSubmitError(validation);
      return;
    }
    if (!accessToken) {
      setSubmitError('Sign in to publish.');
      return;
    }

    setSubmitting(true);
    setSubmitError(null);
    try {
      if (mode === 'reply' && route.params?.threadId != null) {
        if (attachment) {
          await createForumReply(
            route.params.threadId,
            { body: body.trim(), file: attachment },
            accessToken,
          );
        } else {
          const memberId = resolvePushMemberId(accessToken, profile?.memberId);
          if (!memberId) {
            setSubmitError('Sign in to publish.');
            return;
          }
          const queued = await enqueueForumReply({
            memberId,
            topicId: route.params.threadId,
            body: body.trim(),
          });
          void flushOfflineQueue();
          try {
            await createForumReply(
              route.params.threadId,
              { body: queued.payload.body },
              accessToken,
              undefined,
              queued.operationId,
            );
            await removeOfflineItem(queued.operationId);
          } catch (err: unknown) {
            if (!isOfflineFailure(err) && !isTimeoutFailure(err)) {
              throw err;
            }
          }
        }
        navigation.goBack();
        return;
      }

      if (categoryId == null) {
        setSubmitError('Choose a board for this topic.');
        return;
      }

      const created = await createForumTopic(
        categoryId,
        { title: title.trim(), body: body.trim(), ...(attachment ? { file: attachment } : {}) },
        accessToken,
      );
      navigation.replace('Thread', { id: created.id, title: created.title });
    } catch (err: unknown) {
      setSubmitError(messageFromUnknownError(err));
    } finally {
      setSubmitting(false);
    }
  }, [
    accessToken,
    attachment,
    body,
    categoryId,
    mode,
    navigation,
    profile?.memberId,
    route.params?.isLocked,
    route.params?.threadId,
    title,
  ]);

  const context =
    mode === 'reply'
      ? route.params?.threadTitle
      : categoryName;

  if (boardsLoading) {
    return <LoadingBlock label="Loading boards…" />;
  }

  if (mode === 'reply' && route.params?.isLocked) {
    return <ErrorBlock message="This topic is locked." />;
  }

  if (boardsError) {
    return <ErrorBlock message={boardsError} onRetry={retryBoards} />;
  }

  return (
    <KeyboardAvoidingView
      style={[styles.flex, { backgroundColor: c.surfacePage }]}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
    >
      <ScrollView
        contentContainerStyle={styles.content}
        keyboardShouldPersistTaps="handled"
      >
        <Text style={[type.eyebrow, { color: c.accentPrimary }]}>
          {mode === 'reply' ? 'Reply' : 'Post to the community'}
        </Text>
        {context ? (
          <Text
            style={[type.listTitle, { color: c.textPrimary, marginTop: space.sm }]}
            allowFontScaling
          >
            {context}
          </Text>
        ) : null}

        {mode === 'newTopic' && categoryId == null ? (
          <View style={styles.boards}>
            <Text style={[type.meta, { color: c.textMuted }]}>Board</Text>
            {boards.map((board) => (
              <Pressable
                key={board.id}
                accessibilityRole="button"
                accessibilityLabel={`Post to ${board.name}`}
                onPress={() => {
                  setCategoryId(board.id);
                  setCategoryName(board.name);
                }}
                style={[
                  styles.board,
                  { borderColor: c.hairline, backgroundColor: c.surfaceCard },
                ]}
              >
                <Text style={[type.listTitle, { color: c.textPrimary }]}>{board.name}</Text>
              </Pressable>
            ))}
          </View>
        ) : null}

        {mode === 'newTopic' ? (
          <TextInput
            testID={testIds.forumComposerTitle}
            value={title}
            onChangeText={setTitle}
            placeholder="Topic title"
            placeholderTextColor={c.textMuted}
            accessibilityLabel="Topic title"
            maxLength={200}
            style={[
              styles.field,
              {
                color: c.textPrimary,
                borderColor: c.border,
                backgroundColor: c.surfaceCard,
              },
            ]}
          />
        ) : null}

        <TextInput
          value={body}
          onChangeText={setBody}
          placeholder={mode === 'reply' ? 'Write a reply' : 'Write the first post'}
          placeholderTextColor={c.textMuted}
          accessibilityLabel={mode === 'reply' ? 'Reply body' : 'Topic body'}
          testID={testIds.forumComposerBody}
          multiline
          textAlignVertical="top"
          style={[
            styles.field,
            styles.body,
            {
              color: c.textPrimary,
              borderColor: c.border,
              backgroundColor: c.surfaceCard,
            },
          ]}
        />

        {accessToken ? (
          <View style={styles.attach}>
            {attachment ? (
              <Text
                testID={testIds.forumComposerAttachment}
                style={[type.body, { color: c.textPrimary }]}
                accessibilityLabel={`Attached ${attachment.name}`}
              >
                {attachment.name}
              </Text>
            ) : null}
            <View style={styles.pickerRow}>
              <Button
                label={composerAttachCopy.photos}
                size="sm"
                variant="outline"
                testID={testIds.forumComposerAttachPhotos}
                onPress={() => {
                  void pickFromPhotos();
                }}
              />
              <Button
                label={composerAttachCopy.files}
                size="sm"
                variant="outline"
                testID={testIds.forumComposerAttachFiles}
                onPress={() => {
                  void pickFromFiles();
                }}
              />
              {smokeAttachAllowed() && awaitingInject ? (
                <Button
                  label={`Inject ${smokeAttachFileName}`}
                  size="sm"
                  variant="outline"
                  testID={testIds.forumComposerAttachInject}
                  onPress={injectSmokeAttach}
                />
              ) : null}
              {attachment ? (
                <Button
                  label={composerAttachCopy.remove}
                  size="sm"
                  variant="ghost"
                  onPress={() => setAttachment(null)}
                />
              ) : null}
            </View>
            <Text style={[type.caption, { color: c.textMuted }]}>{composerAttachCopy.oneFile}</Text>
          </View>
        ) : (
          <Text style={[type.caption, { color: c.textSecondary }]}>
            Sign in to publish to the site.
          </Text>
        )}

        {submitError ? (
          <Text style={[type.caption, { color: c.textSecondary }]}>{submitError}</Text>
        ) : null}

        <Button
          label={copy.action}
          testID={testIds.forumComposerSubmit}
          onPress={() => {
            void submit();
          }}
          loading={submitting}
          disabled={!accessToken}
        />
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  content: {
    paddingHorizontal: space.xl,
    paddingTop: space.xl,
    paddingBottom: space.section,
    gap: space.base,
  },
  boards: {
    gap: space.sm,
  },
  board: {
    minHeight: 48,
    borderWidth: StyleSheet.hairlineWidth,
    borderRadius: radius.xs,
    paddingHorizontal: space.md,
    justifyContent: 'center',
  },
  field: {
    minHeight: 48,
    borderWidth: 1,
    borderRadius: radius.xs,
    paddingHorizontal: 12,
    fontFamily: fonts.body,
    fontSize: 16,
  },
  body: {
    minHeight: 160,
    paddingTop: 12,
  },
  attach: {
    gap: space.sm,
  },
  pickerRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: space.sm,
  },
});
