# Fan performance submissions — epics and user stories

Planning scope for letting members submit their own Queen covers to the Fan Performances archive, plus an admin queue to preview, approve, and publish them.

Tracked as [#1290](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1290) (epic) with children [#1291](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1291)–[#1296](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1296). Detailed acceptance criteria live in those issues; this document is the narrative and the open questions.

## Where this starts from

Fan Performances is currently **read-only and legacy-backed**:

- `EfFanPerformanceRepository` reads `dbo.Q_STAGE_T` (proc `Q_STAGE_T_PAGE_SP`, `DISPLAY = 1`) — there is no write path at all today.
- Audio lives in the private `songfiles` Blob container and is only reachable through the member-gated proxy `/fan-performances/{id}/audio` (`FanPerformanceEndpoints.cs`), rate-limited via `FanPerformanceRateLimitingOptions`. No CDN or blob URL is ever published (`SongFileUrl`).
- Public pages are `Pages/FanPerformances/`; the API projection is in `Api/Content/`; search indexes it as `SiteSearchContentType.FanPerformance`.

The submission side has strong precedent to copy rather than invent — **photo submissions** (`PhotoSubmissionEntity`, `PhotoSubmissionStatus`, `PhotoSubmissionWorkflow`, `PhotoSubmissionAuditLogEntity`, `Pages/Submit/Photo`, `Pages/Admin/PhotoSubmissions/`) and the newer **trivia submissions** (`TriviaFactSubmissionEntity`, `Pages/Admin/TriviaSubmissions/`). Member-facing status already has a home in `Pages/Account/MySubmissions` and `_SubmissionStatusBadge`, and admin dashboard rollups already use `SubmissionTypeCounts`.

Two things make audio different from every existing submission type, and they drive most of the risk below:

1. **Payload size and format.** `BlobUploadOptions` defaults to 10 MB and image MIME types; `ugc-photos` allows 20 MB. A lossless or long cover can exceed both. Audio needs its own container policy and its own sniffing rules (`BlobContentSniffer`).
2. **Rights.** A fan cover of a Queen song is a derivative work. Publishing one is a different legal posture than publishing a photo, and the moderation flow has to capture an explicit declaration and support takedown.

Ordering is dependency order, not priority. Epic 1 is a prerequisite for everything else.

---

## Epic 1 — Modern write path for fan performances ([#1291](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1291))

Today's repository is read-only over a legacy table. Nothing else here can land until published rows can be written and read consistently. Per [ADR 0006](../decisions/0006-hybrid-ef-core-admin-writes.md), admin writes go through EF Core; public reads may keep projecting from legacy shapes.

- As a backend maintainer, I want a decision recorded (ADR or issue) on whether published fan performances keep living in `Q_STAGE_T` or move to a modern table with a legacy-compatible read projection, so the publish step has a defined target before any UI work starts.
- As a backend maintainer, I want an `IAdminFanPerformanceRepository` with create/update/hide operations following the `IAdminPhotoRepository` shape, including the optimistic concurrency token pattern added in `20260831065121_AddAdminOptimisticConcurrencyTokens`, so two admins can't silently overwrite each other.
- As a backend maintainer, I want an in-memory twin of that repository alongside the EF one, matching the `InMemoryAdminPhotoRepository` convention, so page and service tests don't need SQL.
- As a backend maintainer, I want publishing a performance to invalidate the public query cache entries that back `Pages/FanPerformances/` and the `/api/v1` content projection, so a newly published track appears without a restart.
- As a backend maintainer, I want a published performance to be enqueued for search reindex as `SiteSearchContentType.FanPerformance`, so it is findable the same way legacy rows are.
- As an admin, I want to unpublish (hide) a previously published performance without deleting the blob, so a rights complaint can be actioned in seconds and reversed if it was wrong.

## Epic 2 — Audio upload foundation ([#1292](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1292))

- As a backend maintainer, I want a dedicated UGC container for pending audio (e.g. `ugc-fan-performances`) registered in `BlobUploadContainers`, kept separate from the published `songfiles` container, so unreviewed audio can never be reached by the public read path.
- As a backend maintainer, I want a container policy in `BlobUploadOptions` with an audio-appropriate size limit and an explicit allow-list (`audio/mpeg`, `audio/mp3`, `audio/flac`, `audio/x-flac`, and a decision on `audio/mp4`/m4a), so uploads are validated by declared type *and* sniffed content, not by extension.
- As a backend maintainer, I want `BlobContentSniffer` to recognise the accepted audio containers by magic bytes (ID3 / MPEG frame sync, `fLaC`), so a renamed executable or oversized image is rejected before it is stored.
- As a security reviewer, I want the size ceiling agreed explicitly against B1 hosting limits and request timeouts (see [`hosting-scale-and-cache.md`](../architecture/hosting-scale-and-cache.md)), so large uploads fail fast with a clear message rather than timing out mid-request.
- As a backend maintainer, I want fan-performance uploads to consume the existing per-member daily quota via `MemberUploadQuotaService`, counting bytes as well as uploads, so audio can't be used to bypass the limits every other UGC path respects.
- As a maintainer, I want track duration derived once at submission time (reusing or extending `FanPerformanceDurationResolver`) and stored, so neither the review queue nor the public list has to probe the blob to show a duration.

## Epic 3 — Member submission flow ([#1293](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1293))

- As a signed-in member, I want a "Submit a performance" form at `/submit/fan-performance`, matching the structure of `Pages/Submit/Photo`, so submitting feels like every other contribution on the site.
- As a submitting member, I want to provide title, the Queen song being covered, who performed it, and a description, so my entry carries the same metadata published rows have (`FanPerformance.Title`, `PerformedBy`, `Description`).
- As a submitting member, I want to confirm explicitly that the recording is my own performance and that I agree to it being published on QueenZone, with that declaration stored on the submission, so the rights position is recorded at the point of submission rather than reconstructed later.
- As a submitting member, I want the form to reject an unsupported or oversized file *before* it uploads where the browser can tell, and with a clear server-side message where it can't, so I'm not left guessing after a long upload.
- As a submitting member, I want a confirmation page after submitting (mirroring `PhotoConfirmation`), so I know it arrived and that a human reviews it.
- As a submitting member, I want to see my performance submissions and their status in `/account/mysubmissions` alongside my photo, article, news, and trivia submissions, reusing `_SubmissionStatusBadge`.
- As a submitting member, I want to be told when a reviewer needs more information and to be able to respond, matching the `NeedsInfo` state the photo workflow already supports.
- As a submitting member, I want to withdraw a submission that hasn't yet been published, so I can pull a recording I'm no longer happy with.
- As a security reviewer, I want the submission endpoint rate-limited and anti-forgery protected in line with the other `Pages/Submit/` forms, so the new write path isn't the weak one.

## Epic 4 — Admin review, preview, and publish ([#1294](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1294))

- As an admin, I want a `/admin/fan-performance-submissions` queue listing pending items with submitter, title, song, duration, and file size, following the `Pages/Admin/PhotoSubmissions/Index` shape, so review is one familiar screen.
- As an admin, I want a detail page per submission showing all submitted metadata, the member's rights declaration, and the submission's audit history.
- As an admin, I want to **play the pending audio in the browser before deciding**, streamed through an admin-authorised endpoint reading the pending container — never a blob or CDN URL, matching the `/fan-performances/{id}/audio` proxy pattern and its range-request support so I can scrub through a track.
- As an admin, I want the same status workflow the photo queue uses (`Pending → UnderReview → NeedsInfo → Approved/Rejected`, with approved and rejected terminal), implemented as a `FanPerformanceSubmissionWorkflow` mirroring `PhotoSubmissionWorkflow`, so transitions are validated in one place rather than in page handlers.
- As an admin, I want to edit the title, performer, and description before publishing, so a good recording isn't rejected over a typo.
- As an admin, I want approving a submission to move the audio from the pending container into `songfiles` and create the published row in one operation, recording the resulting id on the submission (mirroring `PhotoSubmissionPromotedPicId`), so approval and publication can't drift apart.
- As an admin, I want approval to be safe to retry if the blob copy or row insert fails halfway, so a partial publish leaves the submission reviewable rather than stuck or duplicated.
- As an admin, I want to reject with a reason and optional reviewer notes, so the member gets an explanation and the next reviewer sees the history.
- As an admin, I want every action written to a `FanPerformanceSubmissionAuditLogEntity` with actor email, action, and timestamp, matching `PhotoSubmissionAuditLogEntity`, so moderation is accountable.
- As an admin, I want pending fan-performance counts included in the admin dashboard rollup via `SubmissionTypeCounts` / `AdminDashboardService`, so a growing queue is visible without visiting the page.
- As an admin, I want rejected or withdrawn submissions' audio purged on a defined schedule, so we aren't storing recordings we've declined indefinitely.

## Epic 5 — Notifications and member feedback ([#1295](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1295))

- As a submitting member, I want to be notified when my submission is approved and published, with a link to the live page, so I can share it.
- As a submitting member, I want to be notified when it is rejected or needs more information, including the reviewer's reason.
- As an admin, I want a notification (or dashboard signal) when the queue has been waiting longer than a set period, so submissions don't quietly rot.

## Epic 6 — Public surface and mobile parity ([#1296](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1296))

- As a listener, I want a published fan performance to show who submitted it, so contributors get credit.
- As a listener, I want a way to report a performance that infringes rights or is otherwise inappropriate, routing to the admin queue, so takedown doesn't depend on someone emailing the site owner.
- As a member, I want to submit a performance from the mobile app, picking a file or recording from my device — this extends Epic 5 of [`mobile-app-epics.md`](mobile-app-epics.md), which currently covers listening only.
- As a member, I want to see my submission's status in the app, matching the photo-submission status story already in mobile Epic 4.

---

## Open questions to settle before accepting scope

1. **Rights policy.** Does QueenZone accept covers of copyrighted Queen material, and under what stated terms? This is a product/legal decision, not an engineering one, and it gates Epic 3's declaration wording and Epic 4's rejection reasons.
2. **Storage target.** `Q_STAGE_T` extension vs. a modern table (Epic 1's first story). Everything downstream of publish depends on the answer.
3. **Size ceiling and hosting.** Production is single-instance B1 with no Redis. An agreed max upload size needs checking against request limits before Epic 2's policy values are fixed.
4. **Moderation capacity.** This is a solo-maintained site. A queue nobody drains is worse than no submission form — Epic 5's staleness signal exists for that reason, but the prior question is whether the volume is wanted.
5. **Transcoding.** Out of scope as written: files are stored and served as uploaded. If a consistent output format is wanted, that is a separate epic with real cost on B1.

## Non-goals

- Transcoding, normalisation, or waveform generation.
- Public commenting or rating on fan performances.
- Video submissions.
- Automated (non-human) moderation of audio content.
