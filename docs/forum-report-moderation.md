# Forum report moderation

The site administrators own the forum report queue at `/admin/forum-reports`.

- Check open reports each business day.
- Aim to review a new report within three business days. This is a service target, not a guaranteed response time.
- Review the evidence snapshot, surrounding context, reporter, author, prior reports, and current post state.
- Mark the report `Reviewed`, `Dismissed`, or `Actioned`. Every view and status change is audited.
- Use the existing post-hide, author-content-hide, or member-suspension controls when action is required.
- Escalate credible threats, immediate safety risks, or illegal content to the site owner at once.

Reports do not hide posts automatically. Reporter identities and report counts must not appear on public pages, APIs, feeds, or search results.

Terminal reports follow the same **180-day** retention target as private-message reports. Audit records are retained after the evidence snapshot is removed. Until automated forum-report purging is added, administrators must not delete report rows manually.
