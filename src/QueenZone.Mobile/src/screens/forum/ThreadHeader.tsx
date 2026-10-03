import { StyleSheet, Text, View } from 'react-native';
import { testIds } from '../../test/testIds';
import { space, type, useTheme } from '../../theme';
import { ErrorBlock, OfflineBanner, SectionErrorBlock } from '../../ui/ScreenStates';
import { ForumPollCard } from './ForumPollCard';
import { ForumWatchControl } from './ForumWatchControl';
import type { useForumThread } from './useForumThread';
import type { threadHeading } from './threadPresentation';

type Props = {
  heading: ReturnType<typeof threadHeading>; offlineSnapshot: boolean; snapshotCachedAt: string | null;
  moderationError: string | null; reloadModeration: () => void; retry: () => void;
  forumThread: ReturnType<typeof useForumThread>; isSignedIn: boolean; accessToken: string | null;
  onSignIn: () => void;
};
export function ThreadHeader({ heading, offlineSnapshot, snapshotCachedAt, moderationError, reloadModeration, retry, forumThread, isSignedIn, accessToken, onSignIn }: Props) {
  const { c } = useTheme();
  return (
    <View style={styles.header}>
      {offlineSnapshot ? <OfflineBanner cachedAt={snapshotCachedAt} testID={testIds.offlineBanner} /> : null}
      <Text style={[type.eyebrow, { color: c.accentPrimary }]}>{heading.forumName}</Text>
      <Text
        style={[type.articleTitle, { color: c.textPrimary, marginTop: space.sm }]}
        allowFontScaling
        maxFontSizeMultiplier={1.4}
      >
        {heading.title}
      </Text>
      {heading.stats ? (
        <Text style={[type.meta, { color: c.textMuted, marginTop: space.md }]}>{heading.stats}</Text>
      ) : null}
      {moderationError ? (
        <SectionErrorBlock
          message={moderationError}
          onRetry={reloadModeration}
        />
      ) : null}
      <ForumWatchControl
        isSignedIn={isSignedIn}
        watching={forumThread.watching}
        watchBusy={forumThread.watchBusy}
        watchError={forumThread.watchError}
        disabled={offlineSnapshot}
        onToggle={forumThread.toggleWatch}
      />
      {forumThread.poll && !offlineSnapshot ? (
        <View style={styles.poll}>
          <ForumPollCard
            poll={forumThread.poll}
            isSignedIn={isSignedIn}
            hasAccessToken={Boolean(accessToken)}
            busy={forumThread.pollBusy}
            error={forumThread.pollError}
            onVote={forumThread.votePoll}
            onClose={forumThread.closePoll}
            onSignIn={onSignIn}
          />
        </View>
      ) : null}
      {forumThread.pollError && !forumThread.poll ? (
        <ErrorBlock message={forumThread.pollError} onRetry={retry} />
      ) : null}
    </View>
  );
}
const styles = StyleSheet.create({
  header: { paddingHorizontal: space.xl, paddingTop: space.xl, paddingBottom: space.base },
  poll: { marginHorizontal: -space.xl, marginTop: space.lg },
});
