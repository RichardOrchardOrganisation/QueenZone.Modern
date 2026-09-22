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
`qz-member-write` remains temporarily available for routes awaiting the #1644
migration.

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
Endpoint rollout and explicit exceptions are tracked by #1644; the endpoint
inventory regression guard is tracked by #1645.
