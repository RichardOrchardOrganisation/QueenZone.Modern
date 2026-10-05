import type { ReactNode } from 'react';
import { useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import type { HomePoll } from '../../api';
import { voteHomePoll } from '../../api';
import { fonts, radius, space, type, useTheme } from '../../theme';
import { testIds } from '../../test/testIds';

type Props = {
  poll: HomePoll;
  isSignedIn: boolean;
  accessToken: string | null;
  onVoted: () => Promise<void> | void;
  onSignIn: () => void;
};

export function HomePollCard({ poll, isSignedIn, accessToken, onVoted, onSignIn }: Props) {
  const { c } = useTheme();
  const [pendingOptionId, setPendingOptionId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const canVote = isSignedIn && !poll.viewerHasVoted && !poll.isClosed && Boolean(accessToken);

  const submit = async (optionId: string) => {
    if (!accessToken || pendingOptionId) {
      return;
    }

    setPendingOptionId(optionId);
    setError(null);
    try {
      await voteHomePoll(optionId, accessToken);
      await onVoted();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not cast that vote.');
    } finally {
      setPendingOptionId(null);
    }
  };

  let pollContent: ReactNode;
  if (poll.isClosed) {
    pollContent = 'Closed';
  } else if (poll.viewerHasVoted) {
    pollContent = 'You voted';
  } else {
    pollContent = 'Open';
  }

  return (
    <View
      testID={testIds.homePoll}
      style={{
        marginTop: space.xxl,
        marginHorizontal: space.xl,
        padding: space.xl,
        borderWidth: StyleSheet.hairlineWidth,
        borderRadius: radius.md,
        borderColor: c.border,
        backgroundColor: c.surfaceCard,
        gap: space.md,
      }}
    >
      <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' }}>
        <Text style={[type.eyebrow, { color: c.accentPrimary }]}>Community Poll</Text>
        {poll.isClosed ? (
          <View
            style={{
              paddingVertical: 3,
              paddingHorizontal: space.sm,
              borderRadius: radius.pill,
              backgroundColor: c.accentSpecial,
            }}
          >
            <Text style={[type.eyebrow, { fontSize: 9, letterSpacing: 1, color: c.textOnAccent }]}>
              Closed
            </Text>
          </View>
        ) : null}
      </View>
      <Text style={[type.pageTitle, { color: c.textPrimary, fontSize: 22 }]}>{poll.question}</Text>
      {error ? <Text style={[type.caption, { color: c.danger }]}>{error}</Text> : null}

      {poll.options.map((option) => {
        const selected = poll.selectedOptionId === option.id;
        const label = `${option.text} ${option.count} · ${formatPercent(option.percentage)}%`;
        return (
          <View key={option.id} style={{ gap: 6 }}>
            {canVote ? (
              <Pressable
                testID={`${testIds.homePollVote}-${option.id}`}
                accessibilityRole="radio"
                accessibilityState={{ checked: false }}
                accessibilityLabel={label}
                disabled={pendingOptionId !== null}
                onPress={() => {
                  void submit(option.id);
                }}
                style={{
                  minHeight: 48,
                  paddingVertical: 12,
                  paddingHorizontal: 12,
                  borderWidth: 1,
                  borderColor: c.hairline,
                  borderRadius: radius.xs,
                  flexDirection: 'row',
                  alignItems: 'center',
                  gap: space.md,
                  opacity: pendingOptionId && pendingOptionId !== option.id ? 0.6 : 1,
                }}
              >
                <View
                  style={{
                    width: 18,
                    height: 18,
                    borderRadius: radius.pill,
                    borderWidth: 1.5,
                    borderColor: c.borderStrong,
                  }}
                />
                <Text style={{ fontFamily: fonts.bodyMedium, fontSize: 15, color: c.textPrimary, flex: 1 }}>
                  {option.text}
                </Text>
                <Text style={[type.meta, { color: c.textMuted, textTransform: 'none', letterSpacing: 0 }]}>
                  {option.count} · {formatPercent(option.percentage)}%
                </Text>
              </Pressable>
            ) : (
              <View
                accessibilityRole="text"
                accessibilityLabel={selected ? `${label}. Your vote` : label}
                style={{
                  flexDirection: 'row',
                  alignItems: 'center',
                  gap: space.md,
                  minHeight: 40,
                  paddingHorizontal: 12,
                  paddingVertical: 8,
                  borderWidth: 1,
                  borderRadius: radius.xs,
                  borderColor: selected ? c.accentPrimary : c.hairline,
                  overflow: 'hidden',
                }}
              >
                <View
                  style={{
                    position: 'absolute',
                    top: 0,
                    bottom: 0,
                    left: 0,
                    width: `${Math.max(0, Math.min(100, option.percentage))}%`,
                    backgroundColor: selected ? c.accentTintWeak : c.hairline,
                  }}
                />
                <View
                  style={{
                    width: 18,
                    height: 18,
                    borderRadius: radius.pill,
                    borderWidth: 1.5,
                    alignItems: 'center',
                    justifyContent: 'center',
                    borderColor: selected ? c.accentPrimary : c.borderStrong,
                    backgroundColor: selected ? c.accentPrimary : 'transparent',
                  }}
                >
                  {selected ? (
                    <Text style={{ fontSize: 11, fontFamily: fonts.bodySemi, lineHeight: 12, color: c.textOnAccent }}>
                      ✓
                    </Text>
                  ) : null}
                </View>
                <Text
                  style={{
                    fontFamily: selected ? fonts.bodySemi : fonts.bodyMedium,
                    fontSize: 15,
                    color: c.textPrimary,
                    flex: 1,
                  }}
                >
                  {option.text}
                </Text>
                <Text
                  style={[
                    type.meta,
                    { color: selected ? c.accentPrimary : c.textSecondary, textTransform: 'none', letterSpacing: 0 },
                  ]}
                >
                  {option.count} · {formatPercent(option.percentage)}%
                </Text>
              </View>
            )}
            {!canVote && selected ? (
              <Text style={[type.caption, { color: c.accentPrimary }]}>Your vote</Text>
            ) : null}
          </View>
        );
      })}

      {!isSignedIn && !poll.isClosed ? (
        <Pressable
          testID={testIds.homePollSignIn}
          accessibilityRole="button"
          accessibilityLabel="Sign in to vote"
          onPress={onSignIn}
        >
          <Text style={[type.body, { color: c.accentPrimary }]}>Sign in to vote</Text>
        </Pressable>
      ) : null}

      {canVote ? (
        <Text style={[type.caption, { color: c.textMuted }]}>Votes are final and cannot be changed.</Text>
      ) : null}

      <View style={{ flexDirection: 'row', justifyContent: 'space-between', paddingTop: space.md, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: c.hairline }}>
        <Text style={[type.meta, { color: c.textMuted }]}>
          {poll.totalVotes.toLocaleString()} {poll.totalVotes === 1 ? 'vote' : 'votes'}
        </Text>
        <Text style={[type.meta, { color: c.textMuted }]}>
          {pollContent}
        </Text>
      </View>
    </View>
  );
}

function formatPercent(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(1);
}
