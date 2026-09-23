# Member account deletion

The signed-in member can choose **Delete my account** in the mobile Settings screen or at `/account/delete`. They type `DELETE` to confirm. The API accepts `immediate: true`; older clients that omit it retain the scheduled request contract.

## Immediate lifecycle

1. The request disables the account, hides its public identity and avatar, and signs the member out. All mobile refresh grants are revoked.
2. The purge removes personal sign-in fields and the legacy-account link. A non-personal member tombstone remains for database relationships. It replaces modern forum post and private message bodies with deleted placeholders; removes submitted article, quiz, trivia, suggestion, photo, and performance content; and removes promoted media where the submission can be traced to its member. A modern forum thread retains its title and other members' replies. Its starter and deleted posts display `Deleted member`, and its title remains in forum and site search.
3. Blob paths — avatars, attachments, submission uploads, and promoted gallery/stage media — are put in a durable `MemberDeletionBlobs` outbox in the same database transaction as the purge. Public `PIC_FILES_T` / `Q_STAGE_T` rows are removed in that transaction so published media disappears with PII. The request then attempts blob deletion immediately. The hosted deletion service retries failed blob deletes and incomplete purges. Blob failures never abort the purge or other due members. Sign in with Apple refresh tokens, when available, are encrypted at rest and revoked through Apple's token revocation endpoint; failures are retried. Apple logins without a stored token cannot be revoked by the server; the app and website give members the Apple Account removal path.
4. `MemberAccountDeletionAuditLog` records the account ID, action, and timestamp without copying the member's email or content. The member record remains as `Deleted member` with a synthetic email. Legacy public archive content remains, but `LinkedLegacyUserId` is cleared.

The response says deletion **is being processed** because external blob removal and Apple revocation can finish later. Most requests finish within minutes; the hosted service checks unfinished cleanup every six hours. It includes an opaque Data Protection status receipt. The mobile screen retains that receipt locally after sign-out, checks it automatically when opened, and can query `/api/v1/account-deletion-status`; the website redirects to `/account/deletion-status?receipt=...`. The receipt reveals only `processing` or `complete`, remains usable after sign-out, and should be treated as a private link. Completion requires the account purge, its blob outbox to be empty, and any stored Apple refresh token to have been revoked. The account cannot be restored after an immediate request.

## Existing scheduled requests

Members who already have a 30-day scheduled request can still sign in and cancel it before the due date. The original name and avatar reference are kept in private recovery fields until cancellation or purge. Older clients may still create scheduled requests by omitting `immediate: true`; the current mobile and web interfaces submit immediate requests. The hosted service uses the same purge for scheduled requests after 30 days.

## Other member content and moderation

The other participant's private conversation remains, with the deleted member's messages reduced to placeholders. Modern forum posts similarly remain as placeholders to preserve thread continuity. Reports involving the deleted member are removed, and preceding conversation context in other reports is cleared. This account-deletion rule supersedes the general reported-message retention rule in [ADR 0015](../decisions/0015-private-message-report-retention-and-audit.md) for reports involving a deleted member.

Deleted accounts are excluded from recipient search, and conversations with them cannot receive new replies.
