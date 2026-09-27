# ADR 0014: Push Notification Transport And Dispatch Model

## Status

Accepted.

## Context

[Epic #756](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/756) needs the backend to actually deliver push notifications for forum replies, private messages, and news to iOS and Android devices ([#757](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/757)–[#760](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/760)). Two decisions were left open by those stories:

1. **Transport** — how the backend gets a message to a device: through Apple's and Google's push services directly, or through a third-party relay.
2. **Dispatch mechanism** — how sending is wired into the existing forum-post, message-send, and news-publish write paths ([#759](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/759)) without a queue or worker infrastructure this project doesn't otherwise have.

Constraints already set elsewhere in the repo:

- [ADR 0011](0011-mobile-project-location-and-build-tooling.md) explicitly declined Expo Application Services (EAS) — "No Expo account, EAS project owner, or EAS-hosted credential store is required." Expo's push notification service is normally paired with an EAS project, so using it here would cut against that decision.
- [`hosting-scale-and-cache.md`](../architecture/hosting-scale-and-cache.md) commits to a single B1 App Service instance with no Redis, no queue, and no autoscaling, and says to "assume single instance in all performance and caching designs" until that document is updated.
- The product owner has confirmed push delivery is **best-effort with no retry** — a failed send is logged (per #760) and dropped, not requeued.
- There is no existing precedent in this codebase for an async, decoupled dispatch-after-write pattern (no email sender, no message queue); the only `BackgroundService`s in the repo are periodic sweeps (`GalleryOrphanSweepHostedService`, `MemberAccountDeletionHostedService`, `SearchIndexSeedHostedService`), not per-request work queues.
- Credentials for third-party providers already follow a documented pattern: Bitwarden Secrets Manager as the source of truth, synced into App Service settings, named `Category__Key` (e.g. `Authentication__Discord__ClientSecret`, `Analytics__GoogleAnalyticsServiceAccountJson`) — see [ADR 0008](0008-app-service-settings-ownership.md) and [`opentofu-inventory.md`](../architecture/opentofu-inventory.md).

## Decision

### 1. Transport: call APNs and FCM directly

The backend calls Apple's and Google's push endpoints directly. It does not send through Expo's push notification service.

- **APNs** — Apple's HTTP/2 provider API (`api.sandbox.push.apple.com` for locally installed builds signed with a development provisioning profile; `api.push.apple.com` for distribution builds, including TestFlight and the App Store), authenticated with a token-based APNs Auth Key: an ES256 JWT signed with a `.p8` private key, `kid` = Key ID, `iss` = Team ID. APNs device tokens are environment-specific, so a sandbox token cannot be sent through the production endpoint or vice versa. Apple allows reusing one signed token for up to roughly an hour, so the provider does not need to sign a fresh JWT per notification. See Apple's [`aps-environment` entitlement documentation](https://developer.apple.com/documentation/bundleresources/entitlements/aps-environment).
- **FCM** — Google's HTTP v1 API (`https://fcm.googleapis.com/v1/projects/{project-id}/messages:send`), authenticated with an OAuth2 access token minted from a Firebase service-account JSON.
- Generating and storing these credentials is its own explicit step per platform, tracked separately from this ADR: the APNs Auth Key (distinct from the code-signing credential already wired up for TestFlight in [#808](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/808)), and the Firebase project + service account for FCM. Both follow the existing Bitwarden → App Service settings convention:
  - `PushNotifications__Apns__TeamId`, `PushNotifications__Apns__KeyId`, `PushNotifications__Apns__PrivateKeyPem`, `PushNotifications__Apns__Environment` (`sandbox` / `production`)
  - `PushNotifications__Fcm__ServiceAccountJson`, `PushNotifications__Fcm__ProjectId`
- Rationale: ADR 0011 already ruled out EAS-hosted credential management for this solo-maintained project. Going direct keeps the new dependency surface to outbound HTTPS calls and two sets of self-owned credentials — nothing hosted by a third party sits between this app and Apple/Google, and nothing here requires an EAS project to exist.
- No specific NuGet package is mandated. Implement with `HttpClient` plus a JWT library already available in the ecosystem (e.g. `System.IdentityModel.Tokens.Jwt`) for the APNs token, or a small maintained client if one meaningfully reduces boilerplate — but it must not depend on EAS or Expo's hosted push relay.

### 2. Dispatch: synchronous, in-process, best-effort, no retry

Notification dispatch happens **inline, awaited, at the end of the existing write path** (forum reply save, message send, news publish) — not on a background queue or worker.

**Volume reasoning**, since this is what makes synchronous dispatch reliable rather than a corner cut:

| Event | Typical fan-out | Why synchronous is fine |
| --- | --- | --- |
| Private message send | 1 recipient | Single HTTP call; negligible latency. |
| Forum reply (topic followers) | Low tens, at this forum's scale | A handful of concurrent HTTP calls; sub-second. |
| News publish (opted-in subscribers) | The largest fan-out, bounded by total opted-in members | Use each provider's batch shape — FCM's HTTP v1 batching (up to 500 tokens per call) in one request, and concurrent APNs HTTP/2 requests over one shared `HttpClient`/`SocketsHttpHandler` connection, bounded by a `SemaphoreSlim` cap (recommended: ~20 concurrent). This completes in well under a second at hobby-scale membership and adds negligible latency to the write's response. |

Explicitly **not** doing, for the initial implementation:

- **A background queue** (`Channel<T>` + `BackgroundService`, or any external queue product). Real complexity — consumer lifetime, shutdown draining, DI scope handling — not justified at current or reasonably foreseeable volume, and not required to meet the "never block or fail the write" requirement in #759 as long as the dispatch call itself is wrapped in try/catch and never rethrows.
- **Detached `Task.Run` fire-and-forget.** A task detached from the request loses safe access to scoped services (`DbContext`, preference lookups) unless a manual `IServiceScopeFactory` scope is created, and risks an unobserved exception escaping the #760 logging path. Inline `await` inside a try/catch is simpler, easier to reason about, and no slower in practice at these volumes.
- **Retry/backoff.** A provider error (timeout, 5xx, network failure) is caught at the call site, logged per #760 with enough context to diagnose (member id, category, provider error — no token), and the notification is dropped. This is acceptable specifically because retry was explicitly waived as a requirement; it is a deliberate scope cut, not an oversight.

**Escalation trigger** (revisit later, do not build now): if a single event's recipient count regularly exceeds roughly 500, or the added write-path latency becomes noticeable in practice, move to an in-process bounded `Channel<T>` + `BackgroundService` consumer. That is still single-instance-safe and needs no new Azure resource — consistent with `hosting-scale-and-cache.md`'s posture of staying process-local until traffic actually justifies more. Do not reach for a hosted queue (Service Bus, etc.) without updating that document first, for the same reason Redis is off the table today.

## Consequences

Benefits:

- No new hosted dependency (no EAS project, no third-party push relay) — consistent with ADR 0011 and this project's solo-maintainer, low-budget posture.
- No new runtime infrastructure (no queue, no extra `BackgroundService`) — consistent with `hosting-scale-and-cache.md`'s single-instance assumption.
- Simple to reason about: a failed send is caught, logged, and dropped, matching the already-agreed best-effort/no-retry bar exactly, with no partial-delivery or duplicate-delivery edge cases a retry mechanism would introduce.

Tradeoffs:

- The write request's latency includes push dispatch time. Bounded and small at current volumes (see table above), but a genuine cost that a fully decoupled design wouldn't have.
- A transient provider outage or timeout silently drops that notification permanently — by design, but worth remembering when triaging "a member says they didn't get notified."
- If News subscriber counts or a single topic's follower count grow well beyond hobby scale, this design needs the escalation step above; it is not infinitely scalable by construction.

## Related

- [#756](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/756) — Epic: Push notifications
- [#757](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/757) — Device token registration and per-member storage
- [#759](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/759) — Hook notification dispatch into existing write paths
- [#760](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/760) — Monitor and log push notification delivery failures
- [ADR 0008](0008-app-service-settings-ownership.md) — credential storage convention
- [ADR 0011](0011-mobile-project-location-and-build-tooling.md) — EAS rejection
- [`hosting-scale-and-cache.md`](../architecture/hosting-scale-and-cache.md) — single-instance constraint
