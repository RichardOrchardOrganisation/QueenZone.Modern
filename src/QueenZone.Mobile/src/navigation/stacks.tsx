import { createNativeStackNavigator } from '@react-navigation/native-stack';
import type { ReactNode } from 'react';
import { testIds } from '../test/testIds';
import { useTheme, type ColorScheme } from '../theme';
import { HomeScreen } from '../screens/home/HomeScreen';
import { QuoteScreen } from '../screens/home/QuoteScreen';

import { ProfileScreen } from '../screens/account/ProfileScreen';
import { SettingsScreen } from '../screens/account/SettingsScreen';
import { AnalyticsSettingsScreen } from '../screens/account/AnalyticsSettingsScreen';
import { ContactScreen } from '../screens/account/ContactScreen';
import { DeleteAccountScreen } from '../screens/account/DeleteAccountScreen';
import { SavedListScreen } from '../screens/account/SavedListScreen';
import { MySubmissionsScreen } from '../screens/account/MySubmissionsScreen';
import { InboxScreen } from '../screens/messages/InboxScreen';
import { ArchivedScreen } from '../screens/messages/ArchivedScreen';
import { ConversationScreen } from '../screens/messages/ConversationScreen';
import { ComposeMessageScreen } from '../screens/messages/ComposeMessageScreen';
import { ArchiveHubScreen } from '../screens/archive/ArchiveHubScreen';
import { ArticlesIndexScreen } from '../screens/archive/ArticlesIndexScreen';
import { BiographyScreen } from '../screens/archive/BiographyScreen';
import { BiographyChapterScreen } from '../screens/archive/BiographyChapterScreen';
import { DiscographyScreen } from '../screens/archive/DiscographyScreen';
import { AlbumScreen } from '../screens/archive/AlbumScreen';
import { TimelineScreen } from '../screens/archive/TimelineScreen';
import { TimelineEventScreen } from '../screens/archive/TimelineEventScreen';
import { FreddieTributeScreen } from '../screens/archive/FreddieTributeScreen';
import { FanPerformancesScreen } from '../screens/archive/FanPerformancesScreen';
import { FanPerformanceDetailScreen } from '../screens/archive/FanPerformanceDetailScreen';
import { FanPerformanceSubmitScreen } from '../screens/archive/FanPerformanceSubmitScreen';
import { FanPerformanceDownloadsScreen } from '../screens/archive/FanPerformanceDownloadsScreen';
import { StoryScreen } from '../screens/archive/StoryScreen';
import { AboutArchiveScreen } from '../screens/archive/AboutArchiveScreen';
import { TriviaScreen } from '../screens/archive/TriviaScreen';
import { SearchRouteScreen } from '../screens/archive/SearchScreen';
import { NewsIndexScreen } from '../screens/news/NewsIndexScreen';
import { NewsStoryScreen } from '../screens/news/NewsStoryScreen';
import { SuggestNewsScreen } from '../screens/news/SuggestNewsScreen';
import { PhotosScreen } from '../screens/photos/PhotosScreen';
import { PhotoCategoryScreen } from '../screens/photos/PhotoCategoryScreen';
import { PhotoViewerScreen } from '../screens/photos/PhotoViewerScreen';
import { PhotoSubmitScreen } from '../screens/photos/PhotoSubmitScreen';
import { ForumScreen } from '../screens/forum/ForumScreen';
import { CategoryScreen } from '../screens/forum/CategoryScreen';
import { ThreadScreen } from '../screens/forum/ThreadScreen';
import { ForumReportScreen } from '../screens/forum/ForumReportScreen';
import { ComposerScreen } from '../screens/forum/ComposerScreen';
import { ForumIndexHeaderRight, HeaderBackButton, NewsIndexHeaderRight, SearchIdentityHeaderRight } from './headerButtons';
import type {
  ArchiveStackParamList,
  ForumStackParamList,
  HomeStackParamList,
  NewsStackParamList,
  PhotosStackParamList,
} from './types';

export function stackScreenOptions(c: ColorScheme) {
  return {
    headerStyle: { backgroundColor: c.surfacePage },
    headerTintColor: c.accentPrimary,
    headerTitleStyle: { color: c.textPrimary, fontWeight: '600' as const },
    headerShadowVisible: false,
    contentStyle: { backgroundColor: c.surfacePage },
  };
}

const Home = createNativeStackNavigator<HomeStackParamList>();
const News = createNativeStackNavigator<NewsStackParamList>();
const Photos = createNativeStackNavigator<PhotosStackParamList>();
const Archive = createNativeStackNavigator<ArchiveStackParamList>();
const Forum = createNativeStackNavigator<ForumStackParamList>();

type CommonScreensOptions = {
  story?: 'news' | 'archive';
};

type CommonStackScreenComponent =
  | typeof SearchRouteScreen
  | typeof NewsStoryScreen
  | typeof StoryScreen;

type CommonStackScreens = {
  // TypedNavigator.Screen is generic over each stack's ParamList; the helper
  // only registers Search (all five) and Story (Home / News / Archive).
  Screen: (props: never) => unknown;
};

export function commonScreens(Stack: CommonStackScreens, options?: CommonScreensOptions) {
  const Screen = Stack.Screen as (props: {
    name: 'Search' | 'Story';
    component: CommonStackScreenComponent;
  }) => ReactNode;

  return (
    <>
      <Screen name="Search" component={SearchRouteScreen} />
      {options?.story === 'news' ? <Screen name="Story" component={NewsStoryScreen} /> : null}
      {options?.story === 'archive' ? <Screen name="Story" component={StoryScreen} /> : null}
    </>
  );
}

export function HomeStack() {
  const { c } = useTheme();
  return (
    <Home.Navigator screenOptions={stackScreenOptions(c)}>
      <Home.Screen name="Home" component={HomeScreen} options={{ headerShown: false }} />
      <Home.Screen name="Quote" component={QuoteScreen} />
      {commonScreens(Home, { story: 'news' })}
      <Home.Screen
        name="Profile"
        component={ProfileScreen}
        options={({ navigation }) => ({
          headerLeft: () => (
            <HeaderBackButton testID={testIds.profileBack} onPress={() => navigation.goBack()} />
          ),
        })}
      />
      <Home.Screen name="Settings" component={SettingsScreen} />
      <Home.Screen
        name="AnalyticsSettings"
        component={AnalyticsSettingsScreen}
        options={{ title: 'Analytics preferences' }}
      />
      <Home.Screen name="Contact" component={ContactScreen} options={{ title: 'Contact' }} />
      <Home.Screen name="DeleteAccount" component={DeleteAccountScreen} options={{ title: 'Delete account' }} />
      <Home.Screen name="Inbox" component={InboxScreen} options={{ title: 'Messages' }} />
      <Home.Screen name="Archived" component={ArchivedScreen} options={{ title: 'Archived messages' }} />
      <Home.Screen name="Conversation" component={ConversationScreen} />
      <Home.Screen name="ComposeMessage" component={ComposeMessageScreen} options={{ title: 'New message' }} />
      <Home.Screen name="SavedList" component={SavedListScreen} options={{ title: 'Library' }} />
      <Home.Screen name="MySubmissions" component={MySubmissionsScreen} options={{ title: 'My submissions' }} />
      <Home.Screen name="SuggestNews" component={SuggestNewsScreen} options={{ title: 'Suggest news' }} />
    </Home.Navigator>
  );
}

export function NewsStack() {
  const { c } = useTheme();
  return (
    <News.Navigator screenOptions={stackScreenOptions(c)}>
      <News.Screen
        name="NewsIndex"
        component={NewsIndexScreen}
        options={({ navigation }) => ({
          title: 'News',
          headerRight: () => <NewsIndexHeaderRight navigation={navigation} />,
        })}
      />
      {commonScreens(News, { story: 'news' })}
    </News.Navigator>
  );
}

export function PhotosStack() {
  const { c } = useTheme();
  return (
    <Photos.Navigator screenOptions={stackScreenOptions(c)}>
      <Photos.Screen
        name="PhotoIndex"
        component={PhotosScreen}
        options={({ navigation }) => ({
          title: 'Photography',
          headerRight: () => (
            <SearchIdentityHeaderRight
              navigation={navigation}
              onSearch={() => navigation.navigate('Search')}
            />
          ),
        })}
      />
      <Photos.Screen name="PhotoCategory" component={PhotoCategoryScreen} options={{ title: 'Collection' }} />
      <Photos.Screen
        name="PhotoViewer"
        component={PhotoViewerScreen}
        options={{
          headerShown: false,
          title: 'Photograph',
          gestureEnabled: true,
          fullScreenGestureEnabled: false,
        }}
      />
      <Photos.Screen name="PhotoSubmit" component={PhotoSubmitScreen} options={{ title: 'Submit a photo' }} />
      {commonScreens(Photos)}
    </Photos.Navigator>
  );
}

export function ArchiveStack() {
  const { c } = useTheme();
  return (
    <Archive.Navigator screenOptions={stackScreenOptions(c)}>
      <Archive.Screen
        name="ArchiveHub"
        component={ArchiveHubScreen}
        options={({ navigation }) => ({
          title: 'Archive',
          headerRight: () => (
            <SearchIdentityHeaderRight
              navigation={navigation}
              onSearch={() => navigation.navigate('Search')}
            />
          ),
        })}
      />
      <Archive.Screen name="Articles" component={ArticlesIndexScreen} options={{ title: 'Articles' }} />
      <Archive.Screen name="Biography" component={BiographyScreen} />
      <Archive.Screen name="BiographyChapter" component={BiographyChapterScreen} options={{ title: 'Chapter' }} />
      <Archive.Screen name="Discography" component={DiscographyScreen} />
      <Archive.Screen name="Album" component={AlbumScreen} options={{ title: 'Album' }} />
      <Archive.Screen name="Timeline" component={TimelineScreen} />
      <Archive.Screen name="TimelineEvent" component={TimelineEventScreen} />
      <Archive.Screen
        name="FreddieTribute"
        component={FreddieTributeScreen}
        options={{ title: 'Freddie Tribute' }}
      />
      <Archive.Screen
        name="FanPerformances"
        component={FanPerformancesScreen}
        options={{ title: 'Fan performances' }}
      />
      <Archive.Screen
        name="FanPerformanceDetail"
        component={FanPerformanceDetailScreen}
        options={{ title: 'Fan performance' }}
      />
      <Archive.Screen
        name="FanPerformanceSubmit"
        component={FanPerformanceSubmitScreen}
        options={{ title: 'Submit a fan performance' }}
      />
      <Archive.Screen
        name="FanPerformanceDownloads"
        component={FanPerformanceDownloadsScreen}
        options={{ title: 'Downloads' }}
      />
      <Archive.Screen name="Trivia" component={TriviaScreen} options={{ title: 'Trivia' }} />
      <Archive.Screen
        name="AboutArchive"
        component={AboutArchiveScreen}
        options={{ title: 'The archive' }}
      />
      {commonScreens(Archive, { story: 'archive' })}
    </Archive.Navigator>
  );
}

export function ForumStack() {
  const { c } = useTheme();
  return (
    <Forum.Navigator screenOptions={stackScreenOptions(c)}>
      <Forum.Screen
        name="ForumIndex"
        component={ForumScreen}
        options={({ navigation }) => ({
          title: 'Forum',
          headerRight: () => <ForumIndexHeaderRight navigation={navigation} />,
        })}
      />
      <Forum.Screen name="Category" component={CategoryScreen} options={{ title: 'Board' }} />
      <Forum.Screen name="Thread" component={ThreadScreen} />
      <Forum.Screen name="ForumReport" component={ForumReportScreen} options={{ title: 'Report post', presentation: 'modal' }} />
      <Forum.Screen name="Composer" component={ComposerScreen} options={{ title: 'Compose', presentation: 'modal' }} />
      {commonScreens(Forum)}
    </Forum.Navigator>
  );
}
