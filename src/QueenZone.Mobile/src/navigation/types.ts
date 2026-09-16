import type { NavigatorScreenParams } from '@react-navigation/native';

export type SignInReturnTo = {
  tab: keyof RootTabParamList;
  screen: string;
  params?: object;
};

export type SignInParams = {
  returnTo?: SignInReturnTo;
};

export type CommonStackParamList = {
  Search: { query?: string } | undefined;
};

export type StoryRouteParamList = {
  Story: { id: number };
};

export type HomeStackParamList = {
  Home: undefined;
  Quote: { id: number };
  Profile: undefined;
  Appearance: undefined;
  Settings: undefined;
  AnalyticsSettings: undefined;
  Contact: undefined;
  Inbox: undefined;
  Archived: undefined;
  Conversation: { id: string };
  ComposeMessage: undefined;
  SavedList: { kind: 'articles' | 'photographs' | 'offline' | 'history' };
  DeleteAccount: undefined;
  MySubmissions: undefined;
  SuggestNews: undefined;
} & CommonStackParamList &
  StoryRouteParamList;

export type NewsStackParamList = {
  NewsIndex: { refreshAt?: number } | undefined;
} & CommonStackParamList &
  StoryRouteParamList;

export type PhotosStackParamList = {
  PhotoIndex: undefined;
  PhotoCategory: { slug: string; name?: string };
  PhotoViewer: { slug: string; picId: number; size?: string };
  PhotoSubmit: undefined;
} & CommonStackParamList;

export type ArchiveStackParamList = {
  ArchiveHub: undefined;
  Articles: undefined;
  Biography: undefined;
  BiographyChapter: { id: number };
  Discography: undefined;
  Album: { id: number };
  Timeline: { focusId?: number } | undefined;
  TimelineEvent: { id: number };
  FreddieTribute: undefined;
  FanPerformances: undefined;
  FanPerformanceDetail: { id: number };
  FanPerformanceDownloads: undefined;
  FanPerformanceSubmit: undefined;
  Trivia: undefined;
  AboutArchive: undefined;
} & CommonStackParamList &
  StoryRouteParamList;

export type ForumStackParamList = {
  ForumIndex: undefined;
  Category: { id: number; name?: string };
  Thread: { id: number | string; title?: string; postId?: number; reportedPostId?: number };
  ForumReport: { postId: number; authorUsername: string; threadId: number | string; threadTitle?: string };
  Composer: {
    threadId?: number;
    threadTitle?: string;
    categoryId?: number;
    categoryName?: string;
    isLocked?: boolean;
  };
} & CommonStackParamList;

export type RootTabParamList = {
  HomeTab: NavigatorScreenParams<HomeStackParamList>;
  NewsTab: NavigatorScreenParams<NewsStackParamList>;
  PhotosTab: NavigatorScreenParams<PhotosStackParamList>;
  ArchiveTab: NavigatorScreenParams<ArchiveStackParamList>;
  ForumTab: NavigatorScreenParams<ForumStackParamList>;
};

export type RootStackParamList = {
  Tabs: NavigatorScreenParams<RootTabParamList> | undefined;
  SignIn: SignInParams | undefined;
};

declare global {
  namespace ReactNavigation {
    // eslint-disable-next-line @typescript-eslint/no-empty-object-type -- React Navigation module augmentation requires an interface.
    interface RootParamList extends RootStackParamList {}
  }
}
