import type { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import type { CompositeScreenProps } from '@react-navigation/native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { useCallback, useMemo, useState } from 'react';
import { FlatList, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import type { ForumRecentThread, PhotoCategoryListItem } from '../../api';
import { nestedTabParams } from '../../navigation/nestedTab';
import type { HomeStackParamList, RootTabParamList } from '../../navigation/types';
import { useSession } from '../../session/SessionContext';
import { openSignIn } from '../../session/signInNavigation';
import { getAppConfig } from '../../config/appConfig';
import { formatHomeFooter } from '../../config/buildMetadata';
import { fonts, space, type, useTheme } from '../../theme';
import { ArchiveFooter } from '../../ui/ArchiveFooter';
import { Chip } from '../../ui/Chip';
import { ThemedRefreshControl } from '../../ui/ThemedRefreshControl';
import { testIds } from '../../test/testIds';
import { HomeForumSection } from './HomeForumSection';
import { HomeGallerySection } from './HomeGallerySection';
import { HomeHeroSection } from './HomeHeroSection';
import { HomeMessagesSection } from './HomeMessagesSection';
import { HomeNewsSection } from './HomeNewsSection';
import { HomeOnThisDaySection } from './HomeOnThisDaySection';
import { HomePollCard } from './HomePollCard';
import { HomeSprintCard } from './HomeSprintCard';
import { HomeQueenQuoteSection } from './HomeQueenQuoteSection';
import { TabRootMasthead } from './TabRootMasthead';
import { useHomeScreenData } from './useHomeScreenData';
import { homeFilters, liveStripIsVisible, liveStripLabel, visibleSectionsForFilter, type HomeFilterKey } from './homeMeta';

type Props = CompositeScreenProps<
  NativeStackScreenProps<HomeStackParamList, 'Home'>,
  BottomTabScreenProps<RootTabParamList>
>;

export function HomeScreen({ navigation }: Props) {
  const insets = useSafeAreaInsets();
  const { c } = useTheme();
  const { isSignedIn, accessToken } = useSession();
  const appConfig = getAppConfig();
  const { apiBaseUrl, appEnv } = appConfig;
  const [filter, setFilter] = useState<HomeFilterKey>('all');
  const visibleSections = useMemo(() => visibleSectionsForFilter(filter), [filter]);

  const data = useHomeScreenData(isSignedIn, accessToken);

  const openNewsStory = useCallback((id: number) => navigation.navigate('Story', { id }), [navigation]);

  const openThread = useCallback(
    (thread: ForumRecentThread) => {
      navigation.navigate('ForumTab', nestedTabParams('Thread', { id: thread.topicId, title: thread.title }));
    },
    [navigation],
  );

  const openGalleryCategory = useCallback(
    (category: PhotoCategoryListItem) => {
      navigation.navigate('PhotosTab', nestedTabParams('PhotoCategory', { slug: category.slug, name: category.name }));
    },
    [navigation],
  );

  const openConversation = useCallback(
    (conversationId: string) => navigation.navigate('Conversation', { id: conversationId }),
    [navigation],
  );

  const openQuote = useCallback((id: number) => navigation.navigate('Quote', { id }), [navigation]);

  const showOnThisDay = visibleSections.has('onThisDay') && data.onThisDayEventVisible;
  const showQueenQuotes = visibleSections.has('onThisDay') && data.queenQuotesVisible;

  return (
    <FlatList
      testID={testIds.homeScreen}
      style={[styles.flex, { backgroundColor: c.surfacePage }]}
      data={[]}
      renderItem={() => null}
      alwaysBounceVertical
      refreshControl={
        <ThemedRefreshControl refreshing={data.pull.refreshing} onRefresh={data.pull.onRefresh} />
      }
      ListHeaderComponent={
        <>
          <TabRootMasthead
            topInset={insets.top}
            onSearch={() => navigation.navigate('Search')}
            onMessagesPress={() => navigation.navigate('Inbox')}
            onProfilePress={() => navigation.navigate('Profile')}
          />

          {appEnv !== 'production' ? (
            <View
              testID={testIds.homeEnvironment}
              style={[styles.environmentStrip, { backgroundColor: c.accentTintWeak }]}
            >
              <Text
                style={[styles.environmentLabel, { color: c.accentPrimary }]}
              >
                {appEnv.toUpperCase()} · {apiBaseUrl}
              </Text>
            </View>
          ) : null}

          {data.liveActivity.view.kind === 'content' &&
          liveStripIsVisible(data.liveActivity.view.data.newForumRepliesToday) ? (
            <View style={[styles.liveStrip, { backgroundColor: c.surfaceRaised }]}>
              <View style={[styles.liveStripDot, { backgroundColor: c.accentPrimary }]} />
              <Text numberOfLines={1} style={[styles.liveStripLabel, { color: c.textSecondary }]}>
                {liveStripLabel(data.liveActivity.view.data.newForumRepliesToday)}
              </Text>
            </View>
          ) : null}

          <HomeSprintCard onPlay={() => navigation.navigate('ArchiveTab', nestedTabParams('QuizSprint'))} />

          <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.filters}>
            {homeFilters.map((option) => (
              <Chip
                key={option.key}
                label={option.label}
                active={filter === option.key}
                onPress={() => setFilter(option.key)}
              />
            ))}
          </ScrollView>

          {visibleSections.has('hero') ? (
            <HomeHeroSection
              newsView={data.news.view}
              hero={data.hero}
              apiBaseUrl={apiBaseUrl}
              onOpenStory={openNewsStory}
              onReloadNews={data.news.reload}
            />
          ) : null}

          {visibleSections.has('news') ? (
            <HomeNewsSection
              newsView={data.news.view}
              latestNews={data.latestNews}
              totalNewsCount={data.totalNewsCount}
              apiBaseUrl={apiBaseUrl}
              onOpenStory={openNewsStory}
              onReloadNews={data.news.reload}
              onSeeAll={() => navigation.navigate('NewsTab', { screen: 'NewsIndex' })}
            />
          ) : null}

          {visibleSections.has('forum') ? (
            <HomeForumSection
              forumView={data.forum.view}
              onOpenThread={openThread}
              onEnterForum={() => navigation.navigate('ForumTab', { screen: 'ForumIndex' })}
              onReloadForum={data.forum.reload}
            />
          ) : null}

          {visibleSections.has('gallery') ? (
            <HomeGallerySection
              galleryView={data.gallery.view}
              onOpenCategory={openGalleryCategory}
              onBrowse={() => navigation.navigate('PhotosTab', { screen: 'PhotoIndex' })}
              onReloadGallery={data.gallery.reload}
            />
          ) : null}

          <HomeMessagesSection
            isSignedIn={isSignedIn}
            messagesView={data.messages.view}
            onOpenConversation={openConversation}
            onOpenInbox={() => navigation.navigate('Inbox')}
            onReloadMessages={data.messages.reload}
            onSignIn={() => openSignIn(navigation, { tab: 'HomeTab', screen: 'Profile' })}
          />

          {showOnThisDay && data.onThisDayEvent ? (
            <HomeOnThisDaySection
              event={data.onThisDayEvent}
              onViewTimeline={() => navigation.navigate('ArchiveTab', nestedTabParams('Timeline'))}
            />
          ) : null}

          {showQueenQuotes && data.onThisDayQuote && data.featuredQuote ? (
            <HomeQueenQuoteSection
              quote={data.onThisDayQuote}
              quoteId={data.featuredQuote.id}
              onOpenQuote={openQuote}
            />
          ) : null}

          {data.homePoll ? (
            <HomePollCard
              poll={data.homePoll}
              isSignedIn={isSignedIn}
              accessToken={accessToken}
              onVoted={() => data.poll.refresh()}
              onSignIn={() => openSignIn(navigation, { tab: 'HomeTab', screen: 'Home' })}
            />
          ) : null}

          <ArchiveFooter />
          <Text testID={testIds.homeVersion} style={[type.caption, styles.footer, { color: c.textMuted }]}>
            {formatHomeFooter(appConfig)}
          </Text>
        </>
      }
    />
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  liveStrip: {
    paddingVertical: 9,
    paddingHorizontal: space.xl,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
  },
  liveStripDot: { width: 6, height: 6, borderRadius: 3 },
  liveStripLabel: { fontFamily: fonts.body, fontSize: 12 },
  environmentStrip: {
    paddingVertical: 8,
    paddingHorizontal: space.xl,
    alignItems: 'center',
  },
  environmentLabel: {
    fontFamily: fonts.bodySemi,
    fontSize: 11,
    letterSpacing: 0.8,
  },
  filters: {
    paddingHorizontal: space.xl,
    paddingTop: space.md,
    paddingBottom: space.md,
    gap: 8,
  },
  footer: {
    textAlign: 'center',
    paddingHorizontal: space.xl,
    paddingBottom: space.xl,
  },
});
