import { memo } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import type { ForumRecentThread } from '../../api';
import type { SectionView } from '../../hooks/useHomeSection';
import { Eyebrow } from '../../ui/Eyebrow';
import { MetaLine } from '../../ui/MetaLine';
import { SectionErrorBlock } from '../../ui/ScreenStates';
import { initials } from '../../ui/initials';
import { fonts, radius, space, type, useTheme } from '../../theme';
import { formatForumThreadMeta } from './homeMeta';

export const HomeForumSection = memo(function HomeForumSection({
  forumView,
  onOpenThread,
  onEnterForum,
  onReloadForum,
}: {
  forumView: SectionView<ForumRecentThread[]>;
  onOpenThread: (thread: ForumRecentThread) => void;
  onEnterForum: () => void;
  onReloadForum: () => void;
}) {
  const { c } = useTheme();
  return (
    <View style={[styles.section, { backgroundColor: c.surfaceRaised }]}>
      <View style={styles.header}>
        <View style={styles.headerText}>
          <Eyebrow tone="primary" size={10}>
            The community
          </Eyebrow>
          <Text style={[type.pageTitle, styles.title, { color: c.textPrimary }]}>In the forum</Text>
        </View>
        <Pressable accessibilityRole="button" onPress={onEnterForum} hitSlop={8}>
          <Text style={[styles.enter, { color: c.accentPrimary }]}>Enter</Text>
        </Pressable>
      </View>

      {forumView.kind === 'skeleton' ? (
        <View style={styles.skeletonList}>
          {[0, 1, 2].map((key) => (
            <View key={key} style={[styles.skeletonRow, { backgroundColor: c.accentTintWeak }]} />
          ))}
        </View>
      ) : forumView.kind === 'error' ? (
        <SectionErrorBlock message={forumView.message} onRetry={onReloadForum} />
      ) : (
        forumView.data.map((thread, index) => (
          <Pressable
            key={thread.topicId}
            accessible
            accessibilityRole="button"
            accessibilityLabel={thread.title}
            onPress={() => onOpenThread(thread)}
            style={[styles.row, { borderTopColor: c.border }]}
          >
            <View style={[styles.avatar, { backgroundColor: c.surfaceCard, borderColor: c.borderStrong }]}>
              <Text style={[styles.avatarLabel, { color: c.textPrimary }]}>{initials(thread.categoryName)}</Text>
            </View>
            <View style={styles.rowText}>
              <Text numberOfLines={2} style={[styles.rowTitle, { color: c.textPrimary }]}>
                {thread.title}
              </Text>
              <MetaLine parts={formatForumThreadMeta(thread)} />
            </View>
            {index === 0 ? <View style={[styles.newDot, { backgroundColor: c.accentPrimary }]} /> : null}
          </Pressable>
        ))
      )}
    </View>
  );
});

const styles = StyleSheet.create({
  section: {
    marginTop: space.xxl,
    paddingVertical: 26,
    paddingHorizontal: space.xl,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    justifyContent: 'space-between',
    marginBottom: space.md,
  },
  headerText: { gap: 6 },
  title: { fontSize: 23 },
  enter: {
    fontFamily: fonts.bodyMedium,
    fontSize: 12,
    letterSpacing: 0.7,
    textTransform: 'uppercase',
  },
  skeletonList: { gap: 12 },
  skeletonRow: { height: 44, borderRadius: radius.xs },
  row: {
    paddingVertical: 14,
    borderTopWidth: 1,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
  },
  avatar: {
    width: space.avatar,
    height: space.avatar,
    borderRadius: radius.avatar,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  avatarLabel: { fontFamily: fonts.display, fontSize: 12 },
  rowText: { flex: 1, gap: 4 },
  rowTitle: { fontFamily: fonts.bodyMedium, fontSize: 14.5 },
  newDot: { width: 6, height: 6, borderRadius: 3 },
});
