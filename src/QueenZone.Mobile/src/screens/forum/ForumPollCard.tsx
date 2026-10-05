import { useCallback, useMemo, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import type { ForumPoll } from '../../api';
import { fonts, radius, space, type, useTheme } from '../../theme';
import { Button } from '../../ui/Button';
import {
  canCastPollVote,
  formatPollResultMeta,
  formatPollStatus,
  pollAuthPrompt,
  pollTokenRequiredMessage,
} from './forumPollMeta';

type Props = {
  poll: ForumPoll;
  isSignedIn: boolean;
  hasAccessToken: boolean;
  busy: boolean;
  error: string | null;
  onVote: (optionIds: string[]) => void;
  onClose: () => void;
  onSignIn: () => void;
};

export function ForumPollCard({
  poll,
  isSignedIn,
  hasAccessToken,
  busy,
  error,
  onVote,
  onClose,
  onSignIn,
}: Props) {
  const { c } = useTheme();
  const [selected, setSelected] = useState<string[]>([]);
  const canVote = canCastPollVote({ canViewerVote: poll.canViewerVote, hasAccessToken });
  const prompt = pollAuthPrompt({
    canViewerVote: poll.canViewerVote,
    viewerHasVoted: poll.viewerHasVoted,
    isClosed: poll.isClosed,
    isSignedIn,
    hasAccessToken,
  });
  let maxChoices = 1;
  if (poll.isMultiChoice) {
    maxChoices = poll.maxChoices != null && poll.maxChoices > 0
      ? poll.maxChoices
      : poll.options.length;
  }

  const toggle = useCallback(
    (optionId: string) => {
      setSelected((current) => {
        if (current.includes(optionId)) {
          return current.filter((id) => id !== optionId);
        }
        if (!poll.isMultiChoice) {
          return [optionId];
        }
        if (current.length >= maxChoices) {
          return current;
        }
        return [...current, optionId];
      });
    },
    [maxChoices, poll.isMultiChoice],
  );

  const voteDisabled = busy || selected.length === 0 || !canVote;
  const status = useMemo(() => formatPollStatus(poll), [poll]);

  return (
    <View
      style={[styles.card, { backgroundColor: c.surfaceCard, borderColor: c.border }]}
      accessibilityLabel={`Poll. ${poll.question}. ${status}`}
    >
      <View style={styles.headerRow}>
        <Text style={[type.eyebrow, { color: c.accentPrimary }]}>Community Poll</Text>
        {poll.isClosed ? (
          <View style={[styles.badge, { backgroundColor: c.accentSpecial }]}>
            <Text style={[type.eyebrow, { fontSize: 9, letterSpacing: 1, color: c.textOnAccent }]}>
              Closed
            </Text>
          </View>
        ) : null}
      </View>
      <Text
        style={[type.cardTitle, { color: c.textPrimary, marginTop: space.sm }]}
        allowFontScaling
        maxFontSizeMultiplier={1.4}
      >
        {poll.question}
      </Text>

      {!canVote ? (
        <View style={styles.list}>
          {poll.options.map((option) => (
            <View
              key={option.optionId}
              style={[
                styles.option,
                styles.resultOption,
                { borderColor: option.selectedByViewer ? c.accentPrimary : c.hairline },
              ]}
              accessibilityRole="text"
              accessibilityLabel={`${option.optionText}, ${formatPollResultMeta(option.voteCount, option.percentage).replace(' · ', ', ')}${option.selectedByViewer ? ', your vote' : ''}`}
            >
              <View
                style={[
                  styles.resultBar,
                  {
                    backgroundColor: option.selectedByViewer ? c.accentTintWeak : c.hairline,
                    width: `${Math.max(0, Math.min(100, option.percentage))}%`,
                  },
                ]}
              />
              <View
                style={[
                  poll.isMultiChoice ? styles.box : styles.radio,
                  { borderColor: option.selectedByViewer ? c.accentPrimary : c.borderStrong },
                  option.selectedByViewer ? { backgroundColor: c.accentPrimary } : null,
                ]}
              >
                {option.selectedByViewer ? (
                  <Text style={[styles.check, { color: c.textOnAccent }]}>✓</Text>
                ) : null}
              </View>
              <Text
                style={[
                  type.listTitle,
                  {
                    color: c.textPrimary,
                    flex: 1,
                    fontFamily: option.selectedByViewer ? fonts.bodySemi : fonts.bodyMedium,
                  },
                ]}
              >
                {option.optionText}
              </Text>
              <Text
                style={[
                  type.meta,
                  { color: option.selectedByViewer ? c.accentPrimary : c.textSecondary, textTransform: 'none', letterSpacing: 0 },
                ]}
              >
                {formatPollResultMeta(option.voteCount, option.percentage)}
              </Text>
            </View>
          ))}
        </View>
      ) : (
        <View style={styles.list}>
          {poll.isMultiChoice ? (
            <Text style={[type.caption, { color: c.textMuted, fontStyle: 'italic' }]}>
              Select all that apply
            </Text>
          ) : null}
          {poll.options.map((option) => {
            const checked = selected.includes(option.optionId);
            return (
              <Pressable
                key={option.optionId}
                accessibilityRole={poll.isMultiChoice ? 'checkbox' : 'radio'}
                accessibilityState={{ checked }}
                accessibilityLabel={option.optionText}
                onPress={() => toggle(option.optionId)}
                style={[
                  styles.option,
                  {
                    borderColor: checked ? c.accentPrimary : c.hairline,
                    backgroundColor: checked ? c.accentTintWeak : c.surfaceRaised,
                  },
                ]}
              >
                <View
                  style={[
                    poll.isMultiChoice ? styles.box : styles.radio,
                    { borderColor: checked ? c.accentPrimary : c.borderStrong },
                    checked ? { backgroundColor: c.accentPrimary, borderColor: c.accentPrimary } : null,
                  ]}
                >
                  {checked ? <Text style={[styles.check, { color: c.textOnAccent }]}>✓</Text> : null}
                </View>
                <Text style={[type.listTitle, { color: c.textPrimary, flex: 1 }]}>
                  {option.optionText}
                </Text>
              </Pressable>
            );
          })}
          <Text style={[type.caption, { color: c.textMuted }]}>
            Votes are final and cannot be changed.
          </Text>
          <Button
            label="Vote"
            onPress={() => onVote(selected)}
            loading={busy}
            disabled={voteDisabled}
          />
        </View>
      )}

      {prompt !== 'none' ? (
        <Text style={[type.caption, { color: c.textSecondary }]}>{pollTokenRequiredMessage}</Text>
      ) : null}

      {prompt === 'signIn' ? (
        <Button label="Sign in" variant="outline" onPress={onSignIn} />
      ) : null}

      {poll.canViewerClose && hasAccessToken ? (
        <Button label="Close poll" variant="ghost" onPress={onClose} loading={busy} disabled={busy} />
      ) : null}

      {error ? <Text style={[type.caption, { color: c.danger }]}>{error}</Text> : null}

      <View style={[styles.metaRow, { borderTopColor: c.hairline }]}>
        <Text style={[type.meta, { color: c.textMuted }]}>{status}</Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: space.xl,
    marginBottom: space.base,
    padding: space.base,
    borderWidth: StyleSheet.hairlineWidth,
    borderRadius: radius.md,
    gap: space.sm,
  },
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  badge: {
    paddingVertical: 3,
    paddingHorizontal: space.sm,
    borderRadius: radius.pill,
  },
  list: {
    marginTop: space.md,
    gap: space.md,
  },
  option: {
    minHeight: 48,
    borderWidth: StyleSheet.hairlineWidth,
    borderRadius: radius.xs,
    paddingHorizontal: space.md,
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.md,
  },
  resultOption: {
    overflow: 'hidden',
  },
  resultBar: {
    position: 'absolute',
    top: 0,
    bottom: 0,
    left: 0,
  },
  radio: {
    width: 18,
    height: 18,
    borderRadius: radius.pill,
    borderWidth: 1.5,
    alignItems: 'center',
    justifyContent: 'center',
  },
  box: {
    width: 18,
    height: 18,
    borderRadius: radius.xs,
    borderWidth: 1.5,
    alignItems: 'center',
    justifyContent: 'center',
  },
  check: {
    fontSize: 11,
    fontFamily: fonts.bodySemi,
    lineHeight: 12,
  },
  metaRow: {
    marginTop: space.sm,
    paddingTop: space.md,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
});
