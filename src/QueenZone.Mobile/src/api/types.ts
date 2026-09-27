/**
 * DTOs mirroring `QueenZone.Web` content API models (`ContentApiModels.cs`).
 * Property names are camelCase to match the JSON contract.
 */

export type ApiPagedResponse<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type ProblemDetails = {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  code?: string;
};

export type NewsListItem = {
  id: number;
  title: string;
  excerpt: string;
  publishedAt: string;
  detailPath: string;
  imageUrl?: string | null;
  thumbnailUrl?: string | null;
};

/** Last-N forum reply preview on news detail. Not the opening post. */
export type NewsDiscussionPreview = {
  authorDisplayName: string;
  postedAt: string;
  excerpt: string;
};

export type NewsDetail = {
  id: number;
  title: string;
  excerpt: string;
  body: string;
  publishedAt: string;
  sourceUrl: string | null;
  detailPath: string;
  imageUrl?: string | null;
  thumbnailUrl?: string | null;
  topicId?: number | null;
  discussionReplyCount?: number | null;
  discussionPreview?: NewsDiscussionPreview[] | null;
};

/** Earliest/latest published years in the archive; both null when there are no articles. */
export type NewsYearRange = {
  minYear: number | null;
  maxYear: number | null;
};

/** List-card shape for `/api/v1/content/articles`. Editorial archive, not news. */
export type ArticleListItem = {
  id: number;
  title: string;
  excerpt: string;
  publishedAt: string;
  detailPath: string;
  categoryName?: string | null;
};

/** Detail shape for `/api/v1/content/articles/{id}`. `source` is a URL or plain text. */
export type ArticleDetail = {
  id: number;
  title: string;
  excerpt: string;
  body: string;
  publishedAt: string;
  source: string | null;
  categoryName: string | null;
  detailPath: string;
};

export type BiographyChapterListItem = {
  id: number;
  title: string;
  summary: string;
  displaySequence: number;
  detailPath: string;
};

export type BiographyChapterNav = {
  id: number;
  title: string;
  detailPath: string;
};

export type BiographyChapterDetail = {
  id: number;
  title: string;
  summary: string;
  body: string;
  displaySequence: number;
  detailPath: string;
  previous: BiographyChapterNav | null;
  next: BiographyChapterNav | null;
};

export type AlbumListItem = {
  albumId: number;
  name: string;
  releaseYear: number | null;
  thumbnailUrl: string | null;
  detailPath: string;
};

export type AlbumSong = {
  songId: number;
  title: string;
  isSingle: boolean;
  lyrics: string | null;
  notes: string | null;
};

export type AlbumDetail = {
  albumId: number;
  name: string;
  releaseYear: number | null;
  artistName: string;
  generalNotes: string | null;
  coverUrl: string | null;
  detailPath: string;
  songs: AlbumSong[];
};

/** Shape for `/api/v1/content/timeline`, `/api/v1/content/timeline/{id}`, and `/api/v1/content/on-this-day`. */
export type TimelineEvent = {
  id: number;
  title: string;
  summary: string;
  eventDate: string;
  formattedDate: string;
  category: string;
  categoryLabel: string;
  sourceUrl: string | null;
};

export type FreddieTribute = {
  id: number;
  name: string;
  thought: string;
  country: string | null;
  dateText: string;
  timeText: string | null;
};

export type PhotoCategoryListItem = {
  catId: number;
  name: string;
  slug: string;
  imageCount: number;
  coverThumbnailUrl: string | null;
  detailPath: string;
};

export type PhotoListItem = {
  picId: number;
  catId: number;
  categoryName: string;
  categorySlug: string;
  title: string;
  thumbnailUrl: string;
  thumbWidth: number;
  thumbHeight: number;
  pictureWidth: number;
  pictureHeight: number;
  pictureDimensionsLabel: string | null;
  year: number;
  dateTime: string;
  detailPath: string;
  categoryPath: string;
};

export type PhotoNav = {
  picId: number;
  detailPath: string;
  imageUrl?: string | null;
  pictureWidth?: number | null;
  pictureHeight?: number | null;
};

export type PhotoDetail = {
  picId: number;
  catId: number;
  categoryName: string;
  categorySlug: string;
  title: string;
  imageUrl: string;
  thumbnailUrl: string;
  thumbWidth: number;
  thumbHeight: number;
  pictureWidth: number;
  pictureHeight: number;
  pictureDimensionsLabel: string | null;
  year: number;
  dateTime: string;
  submittedByDisplayName: string | null;
  detailPath: string;
  categoryPath: string;
  index: number;
  count: number;
  previous: PhotoNav | null;
  next: PhotoNav | null;
};

/** Result of `POST /api/v1/member/photo-submissions` (`PhotoSubmissionCreatedDto`). */
export type PhotoSubmissionCreated = {
  id: string;
  status: string;
  title: string;
  submittedAt: string;
};

/** Result of `POST /api/v1/member/news-suggestions` (`NewsSuggestionCreatedDto`). */
export type NewsSuggestionCreated = {
  id: string;
  status: string;
  url: string;
  title: string | null;
  submittedAt: string;
};

export type FanPerformance = {
  id: number;
  title: string;
  performedBy: string;
  description: string;
  dateAdded: string;
  durationSeconds: number | null;
  detailPath: string;
  audioPath: string;
  contributorMemberId?: string | null;
  contributorDisplayName?: string | null;
};

export type FanPerformanceSubmissionCreated = {
  id: string;
  status: string;
  title: string;
  submittedAt: string;
};

/** Shape for `/api/v1/forum/stats`. `threadCount` matches website `GetForumThreadCountAsync`. */
export type ForumIndexStats = {
  boardCount: number;
  threadCount: number;
  postCount: number;
};

export type ForumCategoryListItem = {
  id: number;
  name: string;
  description: string | null;
  postCount: number;
  lastActivityAt: string | null;
  latestThreadTitle: string | null;
  detailPath: string;
};

export type ForumTopicListItem = {
  id: number;
  title: string;
  lastActivityAt: string;
  authorUsername: string;
  replyCount: number;
  lastPostUsername: string | null;
  isSticky: boolean;
  detailPath: string;
};

export type ForumTopicDetail = {
  id: number;
  title: string;
  forumId: number;
  forumName: string;
  categoryPath: string;
  detailPath: string;
  postCount: number;
  hasPoll: boolean | null;
  isLocked: boolean;
};

export type ForumTopicWatch = {
  watching: boolean;
};

export type ForumAttachment = {
  fileName: string;
  url: string;
  extension: string;
  formattedSize: string;
  isImage: boolean;
  thumbnailUrl: string | null;
  /** Bearer alias under `/api/v1/forum/attachments/...`. Do not open `url`. */
  downloadUrl: string;
};

export type ForumPost = {
  id: number;
  body: string;
  postedAt: string;
  authorUsername: string;
  signature: string | null;
  authorMemberSince: string | null;
  authorMemberId: string | null;
  editedAt: string | null;
  editCount: number;
  attachments: ForumAttachment[];
};

export type ForumTopicCreated = {
  id: number;
  starterPostId: number;
  title: string;
  detailPath: string;
};

export type ForumPostCreated = {
  id: number;
  topicId: number;
  detailPath: string;
};

export type ForumPostReportResponse = {
  reportId: string;
  status: 'Open' | 'Reviewed' | 'Dismissed' | 'Actioned';
  alreadyReported: boolean;
};

export type ForumPostModerationState = {
  reportedPostIds: number[];
  blockedMemberIds: string[];
};

export type ForumPollOption = {
  optionId: string;
  optionText: string;
  displayOrder: number;
  voteCount: number;
  percentage: number;
  selectedByViewer: boolean;
};

export type ForumRecentThread = {
  topicId: number;
  title: string;
  categoryId: number;
  categoryName: string;
  replyCount: number;
  lastActivityAt: string;
  detailPath: string;
};

/** Shape for `/api/v1/content/live-activity`. No presence tracking exists, so this
 * deliberately carries only the honestly-computable forum-replies-today count. */
export type LiveActivitySummary = {
  newForumRepliesToday: number;
};

/** Shape for `/api/v1/content/quotes/random` and `/api/v1/content/quotes/{id}`. */
export type RandomQuote = {
  id: number;
  text: string;
  whoSaid: string;
  context?: string | null;
};

/** Shape for `/api/v1/content/trivia/random`. Optional fields are omitted when blank. */
export type RandomTrivia = {
  id: number;
  text: string;
  category?: string | null;
  difficulty?: string | null;
  source?: string | null;
};

/** Shape for `GET /api/v1/content/home-poll`. Null when no poll is current. */
export type HomePoll = {
  id: string;
  question: string;
  options: HomePollOption[];
  totalVotes: number;
  isClosed: boolean;
  viewerHasVoted: boolean;
  selectedOptionId: string | null;
};

export type HomePollOption = {
  id: string;
  text: string;
  count: number;
  percentage: number;
};

/** Shape for `GET /api/v1/content/quizzes` list items. */
export type QuizListItem = {
  id: string;
  title: string;
  description: string | null;
  questionCount: number;
};

/** An answer option shaped for play — no correct-answer flag. */
export type QuizOption = {
  id: string;
  text: string;
};

export type QuizQuestion = {
  id: string;
  text: string;
  points: number;
  options: QuizOption[];
};

/**
 * Shape for `GET /api/v1/content/quizzes/{id}`. Options only — the correct answer is
 * never sent to the client before submit.
 */
export type QuizDetail = {
  id: string;
  title: string;
  description: string | null;
  questions: QuizQuestion[];
};

export type QuizAnswerSubmission = {
  questionId: string;
  selectedOptionId: string | null;
};

export type QuizAnswerResult = {
  questionId: string;
  questionText: string;
  selectedOptionId: string | null;
  selectedOptionText: string | null;
  correctOptionId: string;
  correctOptionText: string;
  isCorrect: boolean;
  pointsAwarded: number;
};

/**
 * Result of `POST /api/v1/content/quizzes/{id}/attempts`. Scoring is computed
 * server-side; this is the only place the correct answers are ever revealed.
 */
export type QuizResult = {
  quizId: string;
  quizTitle: string;
  score: number;
  maxScore: number;
  correctCount: number;
  questionCount: number;
  recorded: boolean;
  answers: QuizAnswerResult[];
};

export type QuizLeaderboardEntry = {
  rank: number;
  displayName: string;
  score: number;
  attemptCount: number;
};

/** Shape for `GET /api/v1/content/quizzes/leaderboard`. */
export type QuizLeaderboard = {
  top: QuizLeaderboardEntry[];
  viewer: QuizLeaderboardEntry | null;
  totalMembers: number;
};

/** A Quiz Sprint question: options only, the answer key stays on the server. */
export type QuizSprintQuestion = {
  id: string;
  text: string;
  options: QuizOption[];
};

/** Shape for `POST /api/v1/content/quizzes/sprint/start`. Times are Unix milliseconds (UTC). */
export type QuizSprintRound = {
  ticket: string;
  serverNowUnixMilliseconds: number;
  expiresAtUnixMilliseconds: number;
  durationSeconds: number;
  questions: QuizSprintQuestion[];
};

/** Shape for `POST /api/v1/content/quizzes/sprint/answer`. */
export type QuizSprintAnswerCheck = {
  isCorrect: boolean;
  correctOptionId: string;
};

export type QuizSprintReviewItem = {
  questionId: string;
  questionText: string;
  isCorrect: boolean;
  correctAnswer: string;
};

/**
 * Shape for `POST /api/v1/content/quizzes/sprint/finish`. `recorded` is true only when the
 * caller was signed in and the run reached today's leaderboard.
 */
export type QuizSprintResult = {
  attempted: number;
  correct: number;
  points: number;
  bestStreak: number;
  recorded: boolean;
  rank: number | null;
  answers: QuizSprintReviewItem[];
  /** Anonymous runs only: sign in within an hour and send this to the claim endpoint to add the run. */
  claimToken?: string | null;
};

/** Shape for `POST /api/v1/content/quizzes/sprint/claim`. */
export type QuizSprintClaimResult = {
  status: 'claimed' | 'already_claimed';
  points: number;
  rank: number | null;
};

export type QuizSprintLeaderboardEntry = {
  rank: number;
  displayName: string;
  score: number;
  bestStreak: number;
  /** Runs played in the scope: 1 for a best-run entry, the member's run count on the total board. */
  runs?: number;
};

/**
 * Shape for `GET /api/v1/content/quizzes/sprint/leaderboard`; `players` counts members ranked in `scope`.
 * `daily` = best run today, `all` = best run ever, `total` = points summed over every run.
 */
export type QuizSprintBoard = {
  scope: 'daily' | 'all' | 'total';
  top: QuizSprintLeaderboardEntry[];
  viewer: QuizSprintLeaderboardEntry | null;
  players: number;
};

/** Shape for `GET /api/v1/content/quizzes/sprint/daily` (today, UTC). */
export type QuizSprintDailyBoard = {
  top: QuizSprintLeaderboardEntry[];
  viewer: QuizSprintLeaderboardEntry | null;
  playersToday: number;
};

/** One hit from `GET /api/v1/search`. `id` is parsed from numeric source keys. */
export type SearchResult = {
  contentType: string;
  sourceKey: string;
  title: string;
  summary: string;
  url: string;
  publishedAt: string | null;
  imageUrl: string | null;
  category: string | null;
  authorDisplayName: string | null;
  id: number | null;
};

export type ForumPoll = {
  pollId: string;
  topicId: number;
  question: string;
  isMultiChoice: boolean;
  maxChoices: number | null;
  closesAt: string | null;
  closedAt: string | null;
  createdAt: string;
  totalVotes: number;
  distinctVoters: number;
  viewerHasVoted: boolean;
  isClosed: boolean;
  canViewerVote: boolean;
  canViewerClose: boolean;
  options: ForumPollOption[];
};
