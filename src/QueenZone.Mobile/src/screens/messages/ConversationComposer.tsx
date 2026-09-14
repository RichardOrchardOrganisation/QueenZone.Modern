import { memo, useCallback, useEffect, useState } from 'react';
import { Keyboard, Platform, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { ApiError } from '../../api/client';
import { fonts, radius, space, type, useTheme } from '../../theme';
import { testIds } from '../../test/testIds';
import { Button } from '../../ui/Button';
import { conversationBodyMaxLength, validateReplyBody } from './inboxMeta';

function messageFromUnknownError(err: unknown): string {
  return err instanceof ApiError ? err.message : 'Something went wrong.';
}

type Props = {
  correspondentName: string;
  canSend: boolean;
  archiving: boolean;
  archiveDisabled?: boolean;
  archiveError: string | null;
  blockError: string | null;
  onArchive: () => void;
  onSend: (body: string) => Promise<void>;
};

/**
 * Home-indicator inset stays when the keyboard is closed. On iOS, drop it
 * while the keyboard is open so it does not stack on KeyboardAvoidingView
 * padding. Android keeps the safe-area inset (adjustResize).
 */
export function conversationComposerPaddingBottom(
  insetBottom: number,
  keyboardOpen: boolean,
): number {
  if (Platform.OS === 'ios' && keyboardOpen) {
    return space.md;
  }
  return Math.max(insetBottom, space.md);
}

/**
 * Owns reply-draft state so keystrokes never re-render the conversation list.
 */
export const ConversationComposer = memo(function ConversationComposer({
  correspondentName,
  canSend,
  archiving,
  archiveDisabled = false,
  archiveError,
  blockError,
  onArchive,
  onSend,
}: Props) {
  const { c } = useTheme();
  const insets = useSafeAreaInsets();
  const [keyboardOpen, setKeyboardOpen] = useState(false);
  const [draft, setDraft] = useState('');
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (Platform.OS !== 'ios') {
      return;
    }

    const show = Keyboard.addListener('keyboardWillShow', () => setKeyboardOpen(true));
    const hide = Keyboard.addListener('keyboardWillHide', () => setKeyboardOpen(false));
    return () => {
      show.remove();
      hide.remove();
    };
  }, []);

  const submit = useCallback(async () => {
    const validation = validateReplyBody(draft);
    if (validation) {
      setSubmitError(validation);
      return;
    }

    setSubmitting(true);
    setSubmitError(null);
    try {
      await onSend(draft.trim());
      setDraft('');
    } catch (err: unknown) {
      setSubmitError(messageFromUnknownError(err));
    } finally {
      setSubmitting(false);
    }
  }, [draft, onSend]);

  return (
    <View
      testID={testIds.conversationComposer}
      style={[
        styles.composer,
        {
          borderTopColor: c.hairline,
          backgroundColor: c.surfacePage,
          paddingBottom: conversationComposerPaddingBottom(insets.bottom, keyboardOpen),
        },
      ]}
    >
      <View style={styles.composerContext}>
        <Text style={[styles.attribution, { color: c.textMuted }]} numberOfLines={1}>
          REPLYING TO {correspondentName.toUpperCase()}
        </Text>
        <Pressable
          testID={testIds.conversationArchive}
          accessibilityRole="button"
          accessibilityLabel="Archive conversation"
          accessibilityState={{ disabled: archiving || archiveDisabled, busy: archiving }}
          disabled={archiving || archiveDisabled}
          onPress={onArchive}
          hitSlop={8}
          style={[styles.archiveTap, archiving ? { opacity: 0.5 } : null]}
        >
          <Text style={[styles.attribution, { color: c.accentPrimary }]}>
            {archiving ? 'ARCHIVING…' : 'ARCHIVE'}
          </Text>
        </Pressable>
      </View>
      <TextInput
        testID={testIds.conversationReply}
        value={draft}
        onChangeText={setDraft}
        placeholder="Write a reply"
        placeholderTextColor={c.textMuted}
        accessibilityLabel="Reply"
        multiline
        textAlignVertical="top"
        enterKeyHint="send"
        autoCapitalize="sentences"
        maxLength={conversationBodyMaxLength}
        editable={!submitting}
        style={[
          styles.field,
          { borderColor: c.borderStrong, backgroundColor: c.surfaceCard, color: c.textPrimary },
        ]}
      />
      {submitError ? (
        <Text style={[type.caption, { color: c.textSecondary }]}>{submitError}</Text>
      ) : null}
      {archiveError ? (
        <Text style={[type.caption, { color: c.textSecondary }]}>{archiveError}</Text>
      ) : null}
      {blockError ? (
        <Text style={[type.caption, { color: c.textSecondary }]}>{blockError}</Text>
      ) : null}
      <Button
        label="Send reply"
        testID={testIds.conversationReplySubmit}
        onPress={() => {
          void submit();
        }}
        loading={submitting}
        disabled={!canSend || draft.trim().length === 0}
      />
    </View>
  );
});

const styles = StyleSheet.create({
  composer: {
    paddingHorizontal: space.base,
    paddingTop: space.md,
    borderTopWidth: StyleSheet.hairlineWidth,
    gap: 10,
  },
  composerContext: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  archiveTap: { minHeight: 44, justifyContent: 'center' },
  attribution: {
    fontFamily: fonts.titling,
    fontSize: 9.5,
    letterSpacing: 1.9,
  },
  field: {
    minHeight: 44,
    maxHeight: 120,
    borderWidth: 1,
    borderRadius: radius.md,
    paddingHorizontal: 14,
    paddingVertical: 12,
    fontFamily: fonts.body,
    fontSize: 16,
  },
});
