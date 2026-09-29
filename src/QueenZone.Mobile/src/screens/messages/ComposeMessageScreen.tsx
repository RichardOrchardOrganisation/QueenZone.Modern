import { useHeaderHeight } from '@react-navigation/elements';
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  FlatList,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { ApiError, isOfflineFailure, isTimeoutFailure } from '../../api/client';
import {
  composeMessage,
  searchRecipients,
  type MessageRecipient,
} from '../../api/messages';
import type { HomeStackParamList } from '../../navigation/types';
import { resolvePushMemberId } from '../../notifications/pushMemberId';
import { enqueueMessageCompose, flushOfflineQueue, removeOfflineItem } from '../../offlineQueue';
import { MemberGate } from '../../session/MemberGate';
import { useSession } from '../../session/SessionContext';
import { fonts, radius, space, type, useTheme } from '../../theme';
import { testIds } from '../../test/testIds';
import { Button } from '../../ui/Button';
import { conversationBodyMaxLength, validateReplyBody } from './inboxMeta';

type Props = NativeStackScreenProps<HomeStackParamList, 'ComposeMessage'>;

function messageFromUnknownError(err: unknown): string {
  return err instanceof ApiError ? err.message : 'Something went wrong.';
}

export function ComposeMessageScreen({ navigation }: Props) {
  return (
    <MemberGate title="New message">
      <ComposeForm navigation={navigation} />
    </MemberGate>
  );
}

function ComposeForm({ navigation }: Pick<Props, 'navigation'>) {
  const headerHeight = useHeaderHeight();
  const { c } = useTheme();
  const insets = useSafeAreaInsets();
  const { accessToken, profile } = useSession();
  const searchSeq = useRef(0);
  const composeOperationId = useRef<string | null>(null);
  const [query, setQuery] = useState('');
  const [matches, setMatches] = useState<MessageRecipient[]>([]);
  const [searching, setSearching] = useState(false);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [recipient, setRecipient] = useState<MessageRecipient | null>(null);
  const [draft, setDraft] = useState('');
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    const trimmed = query.trim();
    if (!accessToken || !trimmed || recipient) {
      setMatches([]);
      setSearching(false);
      setSearchError(null);
      return;
    }

    const seq = ++searchSeq.current;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      setSearching(true);
      setSearchError(null);
      void searchRecipients(accessToken, trimmed, controller.signal)
        .then((items) => {
          if (seq !== searchSeq.current || controller.signal.aborted) {
            return;
          }
          setMatches(items);
        })
        .catch((err: unknown) => {
          if (seq !== searchSeq.current || controller.signal.aborted || (err instanceof Error && err.name === 'AbortError')) {
            return;
          }
          setMatches([]);
          setSearchError(messageFromUnknownError(err));
        })
        .finally(() => {
          if (seq === searchSeq.current && !controller.signal.aborted) {
            setSearching(false);
          }
        });
    }, 250);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [accessToken, query, recipient]);

  const clearRecipient = useCallback(() => {
    setRecipient(null);
    setSubmitError(null);
  }, []);

  const submit = useCallback(async () => {
    if (!recipient) {
      setSubmitError('Choose a recipient.');
      return;
    }
    const validation = validateReplyBody(draft);
    if (validation) {
      setSubmitError(validation);
      return;
    }
    if (!accessToken) {
      setSubmitError('Sign in to continue.');
      return;
    }

    const memberId = resolvePushMemberId(accessToken, profile?.memberId);
    if (!memberId) {
      setSubmitError('Sign in to continue.');
      return;
    }

    setSubmitting(true);
    setSubmitError(null);
    try {
      const queued = await enqueueMessageCompose({
        memberId,
        recipientMemberId: recipient.memberId,
        body: draft.trim(),
        operationId: composeOperationId.current ?? undefined,
      });
      composeOperationId.current = queued.operationId;
      void flushOfflineQueue();
      try {
        const detail = await composeMessage(
          accessToken,
          recipient.memberId,
          draft.trim(),
          undefined,
          queued.operationId,
        );
        await removeOfflineItem(queued.operationId);
        composeOperationId.current = null;
        navigation.replace('Conversation', { id: detail.conversationId });
      } catch (err: unknown) {
        if (isOfflineFailure(err) || isTimeoutFailure(err)) {
          navigation.goBack();
          return;
        }
        throw err;
      }
    } catch (err: unknown) {
      setSubmitError(messageFromUnknownError(err));
    } finally {
      setSubmitting(false);
    }
  }, [accessToken, draft, navigation, profile?.memberId, recipient]);

  return (
    <KeyboardAvoidingView
      style={[styles.flex, { backgroundColor: c.surfacePage }]}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      keyboardVerticalOffset={Platform.OS === 'ios' ? headerHeight : 0}
    >
      <View style={[styles.body, { paddingBottom: Math.max(insets.bottom, space.md) }]}>
        <Text style={[type.meta, { color: c.textMuted }]}>To</Text>
        {recipient ? (
          <View style={[styles.recipientChip, { borderColor: c.border, backgroundColor: c.surfaceCard }]}>
            <Text style={[type.body, { color: c.textPrimary, flex: 1 }]}>{recipient.displayName}</Text>
            <Pressable
              onPress={clearRecipient}
              accessibilityRole="button"
              accessibilityLabel="Change recipient"
              disabled={submitting}
            >
              <Text style={[type.meta, { color: c.accentPrimary }]}>Change</Text>
            </Pressable>
          </View>
        ) : (
          <>
            <TextInput
              testID={testIds.composeMessageRecipient}
              value={query}
              onChangeText={setQuery}
              placeholder="Search by display name"
              placeholderTextColor={c.textMuted}
              accessibilityLabel="Recipient search"
              autoCapitalize="words"
              autoCorrect={false}
              editable={!submitting}
              style={[
                styles.field,
                styles.singleLine,
                {
                  color: c.textPrimary,
                  borderColor: c.border,
                  backgroundColor: c.surfaceCard,
                },
              ]}
            />
            {searching ? <Text style={[type.caption, { color: c.textMuted }]}>Searching…</Text> : null}
            {searchError ? <Text style={[type.caption, { color: c.textSecondary }]}>{searchError}</Text> : null}
            {!searching && query.trim() && matches.length === 0 && !searchError ? (
              <Text style={[type.caption, { color: c.textMuted }]}>No members matched that name.</Text>
            ) : null}
            <FlatList
              data={matches}
              keyExtractor={(item) => item.memberId}
              keyboardShouldPersistTaps="handled"
              style={styles.matches}
              renderItem={({ item }) => (
                <Pressable
                  testID={`${testIds.composeMessageRecipientPrefix}${item.memberId}`}
                  onPress={() => {
                    setRecipient(item);
                    setQuery(item.displayName);
                    setMatches([]);
                    setSubmitError(null);
                  }}
                  accessibilityRole="button"
                  accessibilityLabel={`Message ${item.displayName}`}
                  style={[styles.matchRow, { borderBottomColor: c.hairline }]}
                >
                  <Text style={[type.body, { color: c.textPrimary }]}>{item.displayName}</Text>
                </Pressable>
              )}
            />
          </>
        )}

        <Text style={[type.meta, { color: c.textMuted, marginTop: space.md }]}>Message</Text>
        <TextInput
          testID={testIds.composeMessageBody}
          value={draft}
          onChangeText={setDraft}
          placeholder="Write a message"
          placeholderTextColor={c.textMuted}
          accessibilityLabel="Message body"
          multiline
          textAlignVertical="top"
          autoCapitalize="sentences"
          maxLength={conversationBodyMaxLength}
          editable={!submitting}
          style={[
            styles.field,
            styles.bodyField,
            {
              color: c.textPrimary,
              borderColor: c.border,
              backgroundColor: c.surfaceCard,
            },
          ]}
        />
        {submitError ? <Text style={[type.caption, { color: c.textSecondary }]}>{submitError}</Text> : null}
        <Button
          label="Send message"
          testID={testIds.composeMessageSubmit}
          onPress={() => {
            void submit();
          }}
          loading={submitting}
          disabled={!accessToken || !recipient}
        />
      </View>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  body: {
    flex: 1,
    paddingHorizontal: space.xl,
    paddingTop: space.lg,
    gap: space.sm,
  },
  field: {
    borderWidth: 1,
    borderRadius: radius.xs,
    paddingHorizontal: 12,
    fontFamily: fonts.body,
    fontSize: 16,
  },
  singleLine: {
    minHeight: 48,
    paddingVertical: 12,
  },
  bodyField: {
    minHeight: 140,
    paddingTop: 12,
    paddingBottom: 12,
  },
  recipientChip: {
    minHeight: 48,
    borderWidth: 1,
    borderRadius: radius.xs,
    paddingHorizontal: 12,
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.md,
  },
  matches: {
    maxHeight: 180,
  },
  matchRow: {
    paddingVertical: space.md,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
});
