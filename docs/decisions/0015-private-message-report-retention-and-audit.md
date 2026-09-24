# ADR 0015: Private Message Report Retention And Moderator Access Audit

## Status

Accepted and implemented.

**Account deletion amendment (September 2026):** An immediate account deletion removes reports involving that member and clears copied conversation context from other reports in the affected conversations. This specific deletion rule overrides the general snapshot retention rule below. See [member account deletion](../architecture/member-account-deletion.md).

- **Decision 1** (snapshot survives independent of the live message) was already true of the existing `PrivateMessageReportEntity` snapshot fields; nothing further was needed.
- **Decision 2** (180-day terminal-status purge) is implemented: `IPrivateMessageRepository.PurgeExpiredReportsAsync` (`src/QueenZone.Data/Repositories/EfPrivateMessageRepository.cs`, `InMemoryPrivateMessageRepository.cs`) deletes reports whose most recent `StatusChanged` audit row is older than `PrivateMessageLimits.ReportRetentionAfterTerminalStatus`, run daily by the `private-message-report-purge` maintenance job (`QueenZone.Maintenance.Worker`, #1677; `docs/architecture/maintenance-jobs-scheduling.md`). `PrivateMessageReportPurgeHostedService` runs the same job in-process only in Development.
- **Decision 3** (audit log) is implemented end to end: the `PrivateMessageReportAuditLogEntity` table (migration `20260825105513_AddPrivateMessageReportAuditLog`), and the admin moderator review surface at `/admin/private-messages` (issue #470, `src/QueenZone.Web/Pages/Admin/PrivateMessages/`) writes a `Viewed` row on each report detail-page load and a `StatusChanged` row on each status transition, via `IPrivateMessageRepository.AppendReportViewedAuditAsync`/`UpdateReportStatusAsync`.
- **Decision 4** (user-facing labels) is reflected in the admin UI copy on the report detail page, which explicitly describes the snapshot as retained independently of the live conversation rather than implying a hard delete.

## Context

Issue [#473](https://github.com/richardorchard/QueenZone.Modern/issues/473) asks for a documented retention, deletion, and audit policy for private messaging, with the acceptance criterion that "permanent deletion behavior is documented before implementation."

Part of #473 is already settled and shipped:

- Message timestamps and per-user archive/removal state exist today on `PrivateMessageEntity` and `PrivateConversationParticipantEntity` (`src/QueenZone.Data/Entities/PrivateMessageEntity.cs`, `PrivateConversationParticipantEntity.cs`). Messages are immutable, so there is no edit/update timestamp to add.
- Account-deletion retention policy is decided and implemented (PR #613, issue #586): private-message bodies and conversation rows are retained for the other participant, sender attribution changes to `Deleted member`, deleted accounts are excluded from recipient search, and conversations cannot receive new replies once either participant has a pending or completed deletion. See `docs/architecture/member-account-deletion.md`.

What remains open, and is the subject of this ADR, is the part #473 explicitly carved out for later: **reported-message retention, moderator access to reported content, and access auditing.** Legal-hold rules are noted as a related concern but are not addressed here (see Non-goals).

The data model for reports already exists ahead of the moderator workflow itself:

- `PrivateMessageReportEntity` (`src/QueenZone.Data/Entities/PrivateMessageReportEntity.cs`) snapshots the reported message body, sender display name, message timestamp, and sort key, plus up to `PrivateMessageLimits.ReportPrecedingMessageCount` messages of preceding context (`PrecedingContextJson`), all captured **at report time**.
- `PrivateMessageReportStatus` (`src/QueenZone.Data/Members/PrivateMessageReportStatus.cs`) defines `Open` → `Reviewed` / `Dismissed` / `Actioned`, with the comment that status transitions are "the moderator review surface (issue #470)."
- `IPrivateMessageRepository` already exposes `CreateReportAsync`, `GetReportAsync`, and `GetReportedMessageIdsAsync`.

No admin/moderator UI or endpoint exists yet to review reports (no `Pages/Admin/Messages` or equivalent) — that is issue #470's scope. This ADR is a prerequisite for #470: it needs to know what it is allowed to retain, show, and must log before it can be built.

The repo has an existing audit-log convention for admin actions: `PhotoAdminAuditLogEntity` (`Id`, `PicId`, `Action`, `ActorEmail`, `OccurredAt`, `Details?`) and its repository `EfNewsAuditRepository`/`EfPhotoAuditRepository`-style siblings. `MemberAccountDeletionAuditLogEntity` is a narrower system-actor variant without an actor-identity field. No audit log exists yet for access to private-message report content.

## Decision

### 1. Report snapshots are the retained record, independent of message lifecycle

The `PrivateMessageReportEntity` snapshot (body, sender name, timestamp, sort key, preceding context) is the durable moderation record. It is captured once, at report time, and is **not** re-synced from the live message afterwards.

- If the underlying `PrivateMessageEntity` or `PrivateConversationEntity` is later deleted for any reason (participant account purge, future user-initiated message deletion, etc.), the report snapshot is **not** deleted alongside it. The report's `Message`/`Conversation` navigation properties may become dangling (no longer resolvable), but `MessageBodySnapshot`, `SenderDisplayNameSnapshot`, `MessageCreatedAtSnapshot`, and `PrecedingContextJson` remain intact and reviewable.
- This makes "permanent deletion" of a message mean *the live conversation copy is gone*, not *all record of it everywhere is gone*. A report already filed against that message keeps its snapshot.

### 2. Reports are retained for a fixed window after resolution, then purged

- Reports in `Open` or `Reviewed` status are retained indefinitely — an open moderation matter is never auto-purged.
- Once a report reaches a terminal status (`Dismissed` or `Actioned`), it is retained for **180 days** after the status change, then eligible for permanent deletion by a background sweep (mirroring the pattern of the existing account-deletion background purge described in `docs/architecture/member-account-deletion.md`).
- This window exists so a dismissed report can still be referenced if the same reported member is reported again, or if an appeal is raised, without keeping message content forever once a matter is closed and the window lapses.
- 180 days is a starting policy value, not a hard architectural constraint; it can be revisited without a new ADR if operational experience shows it should change, but the *mechanism* (indefinite while open, timed purge after terminal status) should not change without amending this ADR.

### 3. Moderator/admin access to reported content is logged

Add `PrivateMessageReportAuditLogEntity`, modeled on `PhotoAdminAuditLogEntity`:

| Field | Type | Notes |
| --- | --- | --- |
| `Id` | `long` | Identity |
| `ReportId` | `Guid` | FK to `PrivateMessageReportEntity.Id` |
| `Action` | `string` | `Viewed` or `StatusChanged` (`PrivateMessageReportAuditAction`) |
| `ActorEmail` | `string` | Moderator/admin identity, not a copied member email |
| `OccurredAt` | `DateTimeOffset` | |
| `Details` | `string?` | Old/new status (e.g. `"Open -> Dismissed"`) on a `StatusChanged` row; null on `Viewed` |

- A `Viewed` row is written the first time a moderator opens a given report's content in a session-worthy way (i.e., when the report detail view is loaded), not on every list-page render that merely shows report metadata (reporter, reason, status) without the message body/context.
- `StatusChanged` is written whenever `UpdateReportStatusAsync` actually changes a report's status (a no-op re-save of the same status writes nothing), independent of whether a `Viewed` row exists for that access.
- The audit log itself is retained indefinitely and is not subject to the report purge window in decision 2 — it must outlive the report it references so "who looked at this and when" remains answerable even after the underlying report is purged. `ReportId` is therefore not a strict FK requiring cascade delete; deleting a purged report does not delete its audit trail.
- Implemented end to end: the moderator review surface at `/admin/private-messages` (issue #470) calls `AppendReportViewedAuditAsync` on detail-page load and `UpdateReportStatusAsync` on status change, so a status transition and its audit row are written atomically in the same repository call and can never happen without each other.

### 4. User-facing labels

Any UI or copy describing message/report deletion (member-facing "delete this message," admin-facing "delete report") must not claim content is immediately and completely erased when it is instead retained per decisions 1–2. Use language consistent with the account-deletion feature's existing pattern (`docs/architecture/member-account-deletion.md`): describe what becomes hidden/inaccessible to the member versus what is retained and for how long, rather than a bare "deleted."

## Non-goals

- **Legal hold.** No legal-hold mechanism (a flag that suspends the decision-2 purge window for a report under litigation/subpoena) is designed here. If a legal-hold requirement materializes, it should extend decision 2 (e.g., an `IsOnLegalHold` flag on the report that blocks the sweep) via an amendment to this ADR rather than inventing a parallel mechanism.
- **Global private-message browsing for admins.** The moderator surface (`/admin/private-messages`, issue #470) only ever loads reports and the snapshotted content attached to them — there is no admin capability to browse or search private messages outside of a filed report. That would need a separate explicit policy and its own ADR amendment.
- **Per-message (as opposed to per-conversation) archive/delete state for ordinary, non-reported messages.** Out of scope for #473; today's per-user state is conversation-level (decision already shipped, see Context).

## Consequences

Benefits:

- The moderator review surface at `/admin/private-messages` (issue #470) has a settled data contract it builds against: what a report retains, for how long, and what must be logged when a moderator views it.
- Report content survives independent message/account deletion, so moderation and appeals remain possible without depending on live conversation state.
- The audit table follows an established repo convention (`PhotoAdminAuditLogEntity`-style), so it fits existing `EfNewsAuditRepository`-style repository patterns and doesn't introduce a new logging approach.
- Status changes and their audit rows are written in the same repository call (`UpdateReportStatusAsync`), so the moderator UI cannot accidentally change a report's status without producing a matching audit entry.

Tradeoffs:

- Report snapshots mean a reported message's content can outlive the sender's or recipient's decision to delete their account or (in the future) delete the message itself. This is an intentional retained-for-moderation carve-out and should be reflected in user-facing copy per decision 4, not treated as a bug.
- The 180-day terminal-status purge window requires a background sweep job, similar in shape to the existing account-deletion purge service, adding one more scheduled job to operate and monitor.
- The audit log survives report purges by design, so `ReportId` referential integrity is soft (application-enforced), not database-enforced via cascade delete — this must be handled deliberately in the EF mapping (no cascade delete configured from report to audit log).
