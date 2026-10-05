import type { InboxConversation, ConversationDetail, ConversationMessage } from '../api/messages';
import type { MemberProfile } from '../api/me';
import type { AuthTokens } from '../api/auth';
import type {
  ApiPagedResponse,
  ArticleDetail,
  ArticleListItem,
  FanPerformance,
  ForumAttachment,
  ForumPost,
  ForumRecentThread,
  ForumTopicDetail,
  NewsDetail,
  NewsListItem,
} from '../api/types';
import type { OfflineQueueItem } from '../offlineQueue/types';
import type { Session } from '../session/SessionContext';

export const apiOrigin = 'http://qz.test';

export function memberProfileFixture(overrides: Partial<MemberProfile> = {}): MemberProfile {
  return {
    memberId: 'member-1',
    email: 'freddie@qz.test',
    displayName: 'Freddie',
    createdAt: '1970-09-05T00:00:00.000Z',
    lastLoginAt: null,
    hasAvatar: false,
    avatarPath: null,
    avatarThumbPath: null,
    messagePrivacy: 'members',
    themePreference: 'system',
    linkedProviders: ['Google'],
    legacyLink: {
      kind: 'none',
      match: null,
      claimableMatches: [],
      unavailableMatches: [],
    },
    scheduledDeletionAt: null,
    limits: {
      minDisplayNameLength: 2,
      maxDisplayNameLength: 100,
      maxAvatarBytes: 2_000_000,
      allowedAvatarContentTypes: ['image/jpeg'],
      deletionRetentionDays: 30,
    },
    deletion: {
      confirmationPhrase: 'DELETE',
      confirmationHint: 'Type DELETE',
      requestedTitle: 'Deletion requested',
      requestedMessage: 'Your account will be removed.',
      whatHappens: [],
    },
    ...overrides,
  };
}

export function memberProfilePayload(overrides: Record<string, unknown> = {}) {
  return {
    memberId: 'member-1',
    email: 'freddie@qz.test',
    displayName: 'Freddie',
    ...overrides,
  };
}

export function authTokensFixture(overrides: Partial<AuthTokens> = {}): AuthTokens {
  return {
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    expiresIn: 900,
    ...overrides,
  };
}

export function newsItemFixture(overrides: Partial<NewsListItem> = {}): NewsListItem {
  return {
    id: 42,
    title: 'Queen headline',
    excerpt: 'A restored archive story.',
    publishedAt: '2024-01-15T12:00:00.000Z',
    detailPath: '/news/42',
    ...overrides,
  };
}

export function newsDetailFixture(overrides: Partial<NewsDetail> = {}): NewsDetail {
  return {
    id: 42,
    title: 'Queen headline',
    excerpt: 'A restored archive story.',
    body: '<p>The restored article body.</p>',
    publishedAt: '2024-01-15T12:00:00.000Z',
    sourceUrl: null,
    detailPath: '/news/42',
    ...overrides,
  };
}

export function articleItemFixture(overrides: Partial<ArticleListItem> = {}): ArticleListItem {
  return {
    id: 101,
    title: 'Inside the Making of Bohemian Rhapsody',
    excerpt: 'Six weeks, three studios and a chorus recorded more than 180 times.',
    publishedAt: '2024-03-12T00:00:00.000Z',
    detailPath: '/articles/101/inside-the-making-of-bohemian-rhapsody',
    categoryName: 'Recording',
    ...overrides,
  };
}

export function articleDetailFixture(overrides: Partial<ArticleDetail> = {}): ArticleDetail {
  return {
    id: 101,
    title: 'Inside the Making of Bohemian Rhapsody',
    excerpt: 'Six weeks, three studios and a chorus recorded more than 180 times.',
    body: '<p>Six weeks, three studios and a chorus recorded more than 180 times.</p>',
    publishedAt: '2024-03-12T00:00:00.000Z',
    source: 'Queenzone archive',
    categoryName: 'Recording',
    detailPath: '/articles/101/inside-the-making-of-bohemian-rhapsody',
    ...overrides,
  };
}

export function fanPerformanceFixture(overrides: Partial<FanPerformance> = {}): FanPerformance {
  return {
    id: 187,
    title: 'Somebody to Love',
    performedBy: 'Jane',
    description: 'A studio cover.',
    dateAdded: '2024-01-15T12:00:00.000Z',
    durationSeconds: 320,
    detailPath: '/fan-performances/187',
    audioPath: '/api/v1/content/fan-performances/187/audio',
    ...overrides,
  };
}

export function forumTopicDetailFixture(overrides: Partial<ForumTopicDetail> = {}): ForumTopicDetail {
  return {
    id: 1002,
    title: 'Ranking every studio album',
    forumId: 1,
    forumName: 'The Music',
    categoryPath: '/forum/1/the-music',
    detailPath: '/forum/topic/1002/ranking-every-studio-album',
    postCount: 1,
    hasPoll: false,
    isLocked: false,
    ...overrides,
  };
}

export function forumAttachmentFixture(overrides: Partial<ForumAttachment> = {}): ForumAttachment {
  return {
    fileName: 'anoto-setlist-scan.jpg',
    url: '/forum/attachment/legacy/1002',
    extension: 'JPG',
    formattedSize: '129.1 KB',
    isImage: true,
    thumbnailUrl: null,
    downloadUrl: '/api/v1/forum/attachments/legacy/1002',
    ...overrides,
  };
}

export function forumPostFixture(overrides: Partial<ForumPost> = {}): ForumPost {
  return {
    id: 1,
    body: '<p>Hello</p>',
    postedAt: '2024-06-01T10:00:00.000Z',
    authorUsername: 'brightonrock',
    signature: null,
    authorMemberSince: null,
    authorMemberId: null,
    editedAt: null,
    editCount: 0,
    attachments: [],
    ...overrides,
  };
}

export function forumRecentThreadFixture(overrides: Partial<ForumRecentThread> = {}): ForumRecentThread {
  return {
    topicId: 1002,
    title: 'Ranking every studio album',
    categoryId: 1,
    categoryName: 'General',
    replyCount: 12,
    lastActivityAt: '2026-01-01T00:00:00.000Z',
    detailPath: '/forum/topic/1002/ranking-every-studio-album',
    ...overrides,
  };
}

export function inboxConversationFixture(overrides: Partial<InboxConversation> = {}): InboxConversation {
  return {
    conversationId: 'convo-1',
    otherParticipantId: 'member-2',
    otherParticipantDisplayName: 'Brian',
    lastMessagePreview: 'See you at Wembley',
    lastMessageAt: '2024-01-15T12:00:00.000Z',
    hasUnread: false,
    unreadCount: 0,
    detailPath: '/messages/convo-1',
    ...overrides,
  };
}

export function conversationMessageFixture(
  overrides: Partial<ConversationMessage> = {},
): ConversationMessage {
  return {
    id: '11111111-2222-3333-4444-555555555555',
    senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    senderDisplayName: 'Bob',
    body: 'Hello',
    createdAt: '2026-08-19T12:00:00.000Z',
    isMine: false,
    sortKey: 1,
    reportedByViewer: false,
    ...overrides,
  };
}

export function conversationDetailFixture(overrides: Partial<ConversationDetail> = {}): ConversationDetail {
  return {
    conversationId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
    otherParticipantId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    otherParticipantDisplayName: 'Bob',
    messages: [],
    page: 1,
    pageSize: 50,
    totalCount: 0,
    totalPages: 1,
    detailPath: '/messages/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
    canSendReply: true,
    hasBlockedOtherParticipant: false,
    ...overrides,
  };
}

export function sessionFixture(overrides: Partial<Session> = {}): Session {
  return {
    isSignedIn: false,
    isRestoring: false,
    displayName: null,
    accessToken: null,
    profile: null,
    ...overrides,
  };
}

export function offlineQueueItemFixture(overrides: Partial<OfflineQueueItem> = {}): OfflineQueueItem {
  return {
    schemaVersion: 1,
    operationId: 'op-1',
    memberId: 'member-1',
    kind: 'forum.reply',
    target: { topicId: 1002 },
    payload: { body: 'Hello' },
    createdAt: '2024-01-01T00:00:00.000Z',
    updatedAt: '2024-01-01T00:00:00.000Z',
    attemptCount: 0,
    nextRetryAt: '2024-01-01T00:00:00.000Z',
    state: 'queued',
    lastError: null,
    ...overrides,
  };
}

export function pagedResponse<T>(items: T[], page = 1, totalPages = 1): ApiPagedResponse<T> {
  return {
    items,
    page,
    pageSize: 20,
    totalCount: items.length,
    totalPages,
  };
}

export function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

export function jsonResponse(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(status === 204 ? null : JSON.stringify(body), {
    status,
    headers: {
      'Content-Type': 'application/json',
      ...headers,
    },
  });
}
