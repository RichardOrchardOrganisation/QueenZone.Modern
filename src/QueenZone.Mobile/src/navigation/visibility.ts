/**
 * Public vs member navigation contract.
 *
 * Tab IA follows the v2 mobile handoff: five tabs on both platforms
 * (Home · News · Photography · Archive · Forum). Profile, settings, and
 * private messages sit behind the Home masthead avatar — never a sixth tab.
 *
 * Member-only *screens* still match the website's content boundary.
 */

export const publicTabNames = [
  'HomeTab',
  'NewsTab',
  'PhotosTab',
  'ArchiveTab',
  'ForumTab',
] as const;

export const signedInOnlyTabNames = [] as const;

export type PublicTabName = (typeof publicTabNames)[number];
export type SignedInOnlyTabName = (typeof signedInOnlyTabNames)[number];
export type TabName = PublicTabName | SignedInOnlyTabName;

export const publicScreenNames = [
  'Home',
  'ArchiveHub',
  'Articles',
  'AboutArchive',
  'Biography',
  'BiographyChapter',
  'Discography',
  'Album',
  'Timeline',
  'TimelineEvent',
  'FreddieTribute',
  'FanPerformances',
  'FanPerformanceDetail',
  'Trivia',
  'Story',
  'Quote',
  'Search',
  'NewsIndex',
  'PhotoIndex',
  'PhotoCategory',
  'PhotoViewer',
  'ForumIndex',
  'Category',
  'Thread',
  'Profile',
  'Appearance',
  'Contact',
  'SignIn',
  'SuggestNews',
] as const;

export const memberOnlyScreenNames = [
  'Inbox',
  'Archived',
  'Conversation',
  'ComposeMessage',
  'Composer',
  'PhotoSubmit',
  'FanPerformanceSubmit',
  'FanPerformanceDownloads',
  'MySubmissions',
  'Settings',
  'SavedList',
  'DeleteAccount',
] as const;

export type MemberOnlyScreenName = (typeof memberOnlyScreenNames)[number];

export function getVisibleTabNames(_isSignedIn: boolean): readonly TabName[] {
  return publicTabNames;
}

export function isMemberOnlyScreen(name: string): boolean {
  return (memberOnlyScreenNames as readonly string[]).includes(name);
}

/**
 * Screens that hide the bottom tab bar.
 *
 * Section lists (Biography, FanPerformances, Timeline, …) stay visible so users
 * can switch tabs while browsing. Immersive / pushed-detail routes hide the bar
 * per the mobile handoff (Story, PhotoViewer, Thread, Profile, …).
 */
export const detailScreenNames = [
  'Story',
  'Quote',
  'TimelineEvent',
  'BiographyChapter',
  'Album',
  'PhotoViewer',
  'PhotoSubmit',
  'FanPerformanceSubmit',
  'FanPerformanceDownloads',
  'Thread',
  'Composer',
  'Conversation',
  'ComposeMessage',
  'FanPerformanceDetail',
  'MySubmissions',
  'Contact',
  'Settings',
  'Appearance',
  'Profile',
  'SignIn',
  'Inbox',
  'Archived',
  'SavedList',
  'DeleteAccount',
  'Search',
  'SuggestNews',
] as const;

export function shouldHideTabBar(routeName: string | undefined): boolean {
  if (!routeName) {
    return false;
  }

  return (detailScreenNames as readonly string[]).includes(routeName);
}
