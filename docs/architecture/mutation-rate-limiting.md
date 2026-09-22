# Platform-neutral mutation rate limiting

QueenZone applies the same outer abuse controls to website, Android, and iOS
mutations. Mobile device attestation is not an API security boundary: the same
actions are available through multiple clients, so authorization, validation,
idempotency, domain quotas, and server-side rate limits remain authoritative.

## Policies

`RateLimiting:Mutations` configures two opt-in ASP.NET Core policies:

| Policy | Normal partition | Default | Purpose |
| --- | --- | --- | --- |
| `qz-anonymous-write` | Processed client IP | 20 requests / minute | Public state-changing routes |
| `qz-authenticated-write` | Member ID | 20 requests / minute | Signed-in state-changing routes |

Authenticated writes also pass through a coarse IP safety net (120 requests per
minute by default). ASP.NET Core composes that selective global limiter with the
member policy. The IP threshold is deliberately looser: members sharing a home,
office, carrier NAT, or VPN should normally be governed by their independent
member buckets, while rapid account rotation from one source still has a ceiling.

The safety net activates only when endpoint metadata names
`qz-authenticated-write`; all other routes receive a no-op global partition.
`qz-member-write` remains registered as a compatibility alias after the #1644
migration, although no current endpoint uses it.

Both mutation policies explicitly bypass `GET`, `HEAD`, `OPTIONS`, and `TRACE`.
This matters for Razor Pages: one page endpoint can contain both GET and POST
handlers, so page-level metadata protects writes without charging ordinary reads.

## Endpoint mapping

The following non-admin surfaces opt in. Website and API equivalents use the
same named policy, so an authenticated member cannot obtain a second allowance
by switching clients.

| Surface | Policy | Covered mutations |
| --- | --- | --- |
| Contact website and API | `qz-anonymous-write` | Contact submission |
| Quiz Sprint website and public API | `qz-anonymous-write` | Start, answer, and finish |
| Home poll and ordinary quizzes | `qz-authenticated-write` | Vote, attempt, and Sprint claim |
| Member account website and `/api/v1/me` | `qz-authenticated-write` | Profile, social/privacy settings, avatar, legacy link, deletion request/cancel |
| Forum website and API | `qz-authenticated-write` | Topic/reply/edit, poll vote/close, watch, report, block |
| Private messages website and API | `qz-authenticated-write` | Compose/reply, archive, report, block |
| Member submissions website and API | `qz-authenticated-write` | Photo, news, article/autosave, trivia, quiz question, fan performance |
| Notifications API | `qz-authenticated-write` | Device registration/removal and preference changes |
| Fan-performance reports | `qz-authenticated-write` | Website and API reports |

The API endpoint metadata documents Problem Details `429` for these writes.
Rate limiting runs before antiforgery, minimal-API body binding, form buffering,
idempotency handling, and the endpoint handler. A rejected upload therefore does
not read the multipart form, open a blob upload, mutate a row, enqueue a
notification, or consume an idempotency key.

### Intentional exceptions

- `/api/v1/auth/*` and website login retain the stronger `qz-auth` policy and
  OAuth error shape.
- Editor-image uploads retain the narrower `qz-upload` policy.
- Fan-performance audio and browse reads retain their dedicated policies.
- Search retains `qz-search`; it is an expensive read, not a mutation.
- `POST /api/v1/me/forum/posts/moderation-state` is a read-shaped batch lookup
  and is not charged as a mutation.
- Account logout/revoke only invalidates local auth state or a supplied token and
  retains the existing auth policy; it creates no durable public work.
- `POST /trivia?handler=Next` only renders another random fact and is not a write.
- Admin/Entra mutations remain outside this rollout.

## Client address and trust boundary

Partitions use `HttpContext.Connection.RemoteIpAddress` after forwarded-header
processing. Production restricts origin ingress to Cloudflare, which is the
trust boundary documented in `azure-hosting-plan.md`. Synthetic requests without
a remote address fall back to connection ID and then request trace ID, rather
than sharing a process-wide `unknown` bucket.

IP limits are an abuse signal, not identity or authorization. Authenticated
fairness always prefers the member ID.

## Response and failure behavior

Rejected API requests use Problem Details with HTTP 429. OAuth endpoints retain
their RFC 6749 error body. `Retry-After` is forwarded when the limiter supplies
it. Browser routes receive the same status without an API body.

Every limiter has a zero-length queue. Requests that exceed a limit are rejected
instead of occupying B1 worker memory while waiting.

Counters are process-local. They reset on deploy, restart, or worker recycle.
That is intentional under the single-instance B1 decision in
`hosting-scale-and-cache.md`; do not scale out without revisiting the shared-cache
and rate-limit design.

## Domain controls remain in force

The generic policies are only an outer burst guard. They do not replace the
database-backed forum and private-message checks, contact-form anti-bot stamp,
submission limits, upload quotas, one-vote constraints, or idempotency keys.
The endpoint inventory regression guard is tracked separately by #1645.
