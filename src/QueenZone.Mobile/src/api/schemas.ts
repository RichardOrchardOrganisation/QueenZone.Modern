/**
 * Runtime JSON shapes for `/api/v1` responses the mobile client consumes.
 *
 * TypeScript `as T` casts in `client.ts` do not fail when the server drops or
 * renames a field. These zod schemas are the consumer-contract boundary (#869).
 * Generated OpenAPI → TypeScript types are optional later and still insufficient
 * alone — they do not exercise Problem Details, auth, multipart, or runtime
 * error handling.
 */
import { z } from 'zod';

export class ContractAssertionError extends Error {
  readonly endpoint: string;

  constructor(endpoint: string, details: string) {
    super(`Contract ${endpoint} failed: ${details}`);
    this.name = 'ContractAssertionError';
    this.endpoint = endpoint;
  }
}

const isoDateTime = z.string().min(1);
const guid = z.string().uuid();

export const problemDetailsSchema = z
  .object({
    type: z.string().optional(),
    title: z.string().optional(),
    status: z.number().int().optional(),
    detail: z.string().optional(),
    instance: z.string().optional(),
    code: z.string().optional(),
  })
  .passthrough();

export function pagedSchema<T extends z.ZodTypeAny>(item: T) {
  return z.object({
    items: z.array(item),
    page: z.number().int().positive(),
    pageSize: z.number().int().positive(),
    totalCount: z.number().int().nonnegative(),
    totalPages: z.number().int().nonnegative(),
  });
}

export const searchResultSchema = z.object({
  contentType: z.string().min(1),
  sourceKey: z.string().min(1),
  title: z.string().min(1),
  summary: z.string(),
  url: z.string().min(1),
  publishedAt: isoDateTime.nullish(),
  imageUrl: z.string().nullish(),
  category: z.string().nullish(),
  authorDisplayName: z.string().nullish(),
  id: z.number().int().nullish(),
});

export const newsListItemSchema = z.object({
  id: z.number().int(),
  title: z.string().min(1),
  excerpt: z.string(),
  publishedAt: isoDateTime,
  detailPath: z.string().min(1),
  imageUrl: z.string().nullish(),
  thumbnailUrl: z.string().nullish(),
});

export const newsDiscussionPreviewSchema = z.object({
  authorDisplayName: z.string(),
  postedAt: isoDateTime,
  excerpt: z.string(),
});

export const articleListItemSchema = z.object({
  id: z.number().int(),
  title: z.string().min(1),
  excerpt: z.string(),
  publishedAt: isoDateTime,
  detailPath: z.string().min(1),
  categoryName: z.string().nullish(),
});

export const articleDetailSchema = z.object({
  id: z.number().int(),
  title: z.string().min(1),
  excerpt: z.string(),
  body: z.string().min(1),
  publishedAt: isoDateTime,
  source: z.string().nullable(),
  categoryName: z.string().nullable(),
  detailPath: z.string().min(1),
});

export const newsDetailSchema = z.object({
  id: z.number().int(),
  title: z.string().min(1),
  excerpt: z.string(),
  body: z.string().min(1),
  publishedAt: isoDateTime,
  sourceUrl: z.string().nullable(),
  detailPath: z.string().min(1),
  imageUrl: z.string().nullish(),
  thumbnailUrl: z.string().nullish(),
  topicId: z.number().int().nullish(),
  discussionReplyCount: z.number().int().nonnegative().nullish(),
  discussionPreview: z.array(newsDiscussionPreviewSchema).nullish(),
});

export const photoCategoryListItemSchema = z.object({
  catId: z.number().int(),
  name: z.string().min(1),
  slug: z.string().min(1),
  imageCount: z.number().int().nonnegative(),
  coverThumbnailUrl: z.string().nullable(),
  detailPath: z.string().min(1),
});

export const photoListItemSchema = z.object({
  picId: z.number().int(),
  catId: z.number().int(),
  categoryName: z.string().min(1),
  categorySlug: z.string().min(1),
  title: z.string().min(1),
  thumbnailUrl: z.string().min(1),
  thumbWidth: z.number().int(),
  thumbHeight: z.number().int(),
  pictureWidth: z.number().int(),
  pictureHeight: z.number().int(),
  pictureDimensionsLabel: z.string().nullable(),
  year: z.number().int(),
  dateTime: isoDateTime,
  detailPath: z.string().min(1),
  categoryPath: z.string().min(1),
});

export const photoNavSchema = z.object({
  picId: z.number().int(),
  detailPath: z.string().min(1),
  imageUrl: z.string().min(1).optional().nullable(),
  pictureWidth: z.number().int().optional().nullable(),
  pictureHeight: z.number().int().optional().nullable(),
});

export const photoDetailSchema = z.object({
  picId: z.number().int(),
  catId: z.number().int(),
  categoryName: z.string().min(1),
  categorySlug: z.string().min(1),
  title: z.string().min(1),
  imageUrl: z.string().min(1),
  thumbnailUrl: z.string().min(1),
  thumbWidth: z.number().int(),
  thumbHeight: z.number().int(),
  pictureWidth: z.number().int(),
  pictureHeight: z.number().int(),
  pictureDimensionsLabel: z.string().nullable(),
  year: z.number().int(),
  dateTime: isoDateTime,
  submittedByDisplayName: z.string().nullable(),
  detailPath: z.string().min(1),
  categoryPath: z.string().min(1),
  index: z.number().int().nonnegative(),
  count: z.number().int().nonnegative(),
  previous: photoNavSchema.nullable(),
  next: photoNavSchema.nullable(),
});

export const fanPerformanceSchema = z.object({
  id: z.number().int(),
  title: z.string().min(1),
  performedBy: z.string().min(1),
  description: z.string(),
  dateAdded: isoDateTime,
  durationSeconds: z.number().int().nullable(),
  detailPath: z.string().min(1),
  audioPath: z.string().min(1),
});

export const forumTopicDetailSchema = z.object({
  id: z.number().int(),
  title: z.string().min(1),
  forumId: z.number().int(),
  forumName: z.string().min(1),
  categoryPath: z.string().min(1),
  detailPath: z.string().min(1),
  postCount: z.number().int().nonnegative(),
  hasPoll: z.boolean().nullable(),
  isLocked: z.boolean(),
});

export const forumTopicWatchSchema = z.object({
  watching: z.boolean(),
});

export const forumAttachmentSchema = z.object({
  fileName: z.string().min(1),
  url: z.string().min(1),
  extension: z.string(),
  formattedSize: z.string(),
  isImage: z.boolean(),
  thumbnailUrl: z.string().nullable(),
  downloadUrl: z.string().min(1),
});

export const forumPostSchema = z.object({
  id: z.number().int(),
  body: z.string(),
  postedAt: isoDateTime,
  authorUsername: z.string().min(1),
  signature: z.string().nullable(),
  authorMemberSince: isoDateTime.nullable(),
  authorMemberId: guid.nullable(),
  editedAt: isoDateTime.nullable(),
  editCount: z.number().int().nonnegative(),
  attachments: z.array(forumAttachmentSchema),
});

export const forumPollOptionSchema = z.object({
  optionId: guid,
  optionText: z.string().min(1),
  displayOrder: z.number().int(),
  voteCount: z.number().int().nonnegative(),
  percentage: z.number(),
  selectedByViewer: z.boolean(),
});

export const forumPollSchema = z.object({
  pollId: guid,
  topicId: z.number().int(),
  question: z.string().min(1),
  isMultiChoice: z.boolean(),
  maxChoices: z.number().int().nullable(),
  closesAt: isoDateTime.nullable(),
  closedAt: isoDateTime.nullable(),
  createdAt: isoDateTime,
  totalVotes: z.number().int().nonnegative(),
  distinctVoters: z.number().int().nonnegative(),
  viewerHasVoted: z.boolean(),
  isClosed: z.boolean(),
  canViewerVote: z.boolean(),
  canViewerClose: z.boolean(),
  options: z.array(forumPollOptionSchema).min(1),
});

export const forumPostCreatedSchema = z.object({
  id: z.number().int(),
  topicId: z.number().int(),
  detailPath: z.string().min(1),
});

export const photoSubmissionCreatedSchema = z.object({
  id: guid,
  status: z.string().min(1),
  title: z.string(),
  submittedAt: isoDateTime,
});

export const newsSuggestionCreatedSchema = z.object({
  id: guid,
  status: z.string().min(1),
  url: z.string().min(1),
  title: z.string().nullable(),
  submittedAt: isoDateTime,
});

export const inboxConversationSchema = z.object({
  conversationId: guid,
  otherParticipantId: guid,
  otherParticipantDisplayName: z.string().min(1),
  lastMessagePreview: z.string(),
  lastMessageAt: isoDateTime,
  hasUnread: z.boolean(),
  unreadCount: z.number().int().nonnegative(),
  detailPath: z.string().min(1),
});

export const conversationMessageSchema = z.object({
  id: guid,
  senderMemberId: guid,
  senderDisplayName: z.string().min(1),
  body: z.string(),
  createdAt: isoDateTime,
  isMine: z.boolean(),
  sortKey: z.number(),
  reportedByViewer: z.boolean(),
});

export const conversationDetailSchema = z.object({
  conversationId: guid,
  otherParticipantId: guid,
  otherParticipantDisplayName: z.string().min(1),
  messages: z.array(conversationMessageSchema),
  page: z.number().int().positive(),
  pageSize: z.number().int().positive(),
  totalCount: z.number().int().nonnegative(),
  totalPages: z.number().int().nonnegative(),
  detailPath: z.string().min(1),
  canSendReply: z.boolean(),
});

export const notificationPreferencesSchema = z.object({
  forumReply: z.boolean(),
  privateMessage: z.boolean(),
  news: z.boolean(),
});

export const memberProfileSchema = z.object({
  memberId: guid,
  email: z.string().min(1),
  displayName: z.string().min(1),
  createdAt: isoDateTime,
  lastLoginAt: isoDateTime.nullable(),
  hasAvatar: z.boolean(),
  avatarPath: z.string().nullable(),
  avatarThumbPath: z.string().nullable(),
  messagePrivacy: z.enum(['members', 'followed', 'nobody']),
  linkedProviders: z.array(z.string()),
  legacyLink: z.object({
    kind: z.enum(['none', 'linked', 'claimable', 'unavailable']),
    match: z
      .object({
        userId: z.number().int(),
        username: z.string().min(1),
      })
      .nullable(),
    claimableMatches: z.array(
      z.object({
        userId: z.number().int(),
        username: z.string().min(1),
      }),
    ),
    unavailableMatches: z.array(
      z.object({
        userId: z.number().int(),
        username: z.string().min(1),
      }),
    ),
  }),
  scheduledDeletionAt: isoDateTime.nullable(),
  limits: z.object({
    minDisplayNameLength: z.number().int().positive(),
    maxDisplayNameLength: z.number().int().positive(),
    maxAvatarBytes: z.number().int().positive(),
    allowedAvatarContentTypes: z.array(z.string()),
    deletionRetentionDays: z.number().int().positive(),
  }),
  deletion: z.object({
    confirmationPhrase: z.string().min(1),
    confirmationHint: z.string().min(1),
    requestedTitle: z.string().min(1),
    requestedMessage: z.string().min(1),
    whatHappens: z.array(z.string()),
  }),
});

export function parseContract<T>(endpoint: string, schema: z.ZodType<T>, data: unknown): T {
  const result = schema.safeParse(data);
  if (result.success) {
    return result.data;
  }

  const details = result.error.issues
    .map((issue) => `${issue.path.length > 0 ? issue.path.join('.') : '(root)'}: ${issue.message}`)
    .join('; ');
  throw new ContractAssertionError(endpoint, details);
}

export function expectedStatus(endpoint: string, actual: number, expected: number): void {
  if (actual !== expected) {
    throw new ContractAssertionError(endpoint, `expected status ${expected}, received ${actual}`);
  }
}

export function expectedField(
  endpoint: string,
  field: string,
  actual: unknown,
  predicate: (value: unknown) => boolean,
  expected: string,
): void {
  if (!predicate(actual)) {
    throw new ContractAssertionError(endpoint, `expected field ${field} ${expected}, received ${String(actual)}`);
  }
}
