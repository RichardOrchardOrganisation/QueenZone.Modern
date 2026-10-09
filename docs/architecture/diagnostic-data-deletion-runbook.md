# Manual diagnostic-data deletion

Status: proposed procedure for review, 9 October 2026. Richard has confirmed that he will handle deletion requests and review account deletions daily. No provider deletion automation is implemented by this change; deployment remains a separate approval.

## Operator and intake

Confirmed operator: Richard is responsible for deletion requests and the daily manual review below. His confirmation does not create an automated provider-deletion capability.

Monitor `support@queenzone.org` and the existing `/contact` privacy/data requests. Review the existing account-deletion audit records and status receipts daily so self-service account deletion is also considered for associated provider data. Do not rely on the user sending a second email. Record the internal account ID, request date and completion status in a restricted operator record; avoid copying content, credentials or diagnostic payloads.

Verify a request using the existing account context or the email associated with the account. Ask only for additional information necessary to resolve a mismatch. A requester may optionally give an approximate problem date and app version. Never request passwords, tokens, private message content, government ID or a full diagnostic export.

## Fulfilment

1. Use the existing account-deletion flow for account requests. Check its status receipt for account, blob and Apple-token cleanup. The receipt does not cover diagnostics or backups. Keep only the minimum account reference needed for the provider review before personal sign-in fields are purged.
2. Determine whether identifiable diagnostics can actually be associated with the requester. QueenZone's reviewed mobile Sentry bootstrap does not call `setUser`; do not invent an email/member-ID match or add identifiers solely to make deletion easier. Use only already available, necessary identifiers and a bounded timeframe. Do not browse unrelated events or export payloads.
3. Where associated data exists, use documented provider deletion functions or submit a minimal deletion request to the provider. Sentry's DPA supplies deletion and assistance provisions. Confirm the DPA is accepted before relying on it. Follow the applicable permanent-deletion approval process for the exact data selected; this draft does not grant blanket permission to delete issues, projects or unrelated users' events. If Sentry only exposes deletion of a whole issue containing other users' events, stop and ask Sentry for an appropriately scoped method.
4. Check other service-provider stores for account-associated identifiable data when relevant. Azure application logs require a separate retention/deletion assessment; changing Sentry settings does not alter Azure. TelemetryDeck events are not sent a member ID or email by this app; do not promise retrospective individual matching or deletion where the provider cannot perform it.
5. Record the scoped action, provider ticket/reference, date and outcome without retaining raw data. Reply with what was removed, which provider requests remain pending, data that could not be reliably associated, and any lawful retention exception. Keep pending cases open and follow up until there is an outcome. A routine expiry date is not proof that a requested provider deletion occurred.

## Retention and limits

- Do not promise that account deletion immediately erases operational diagnostics or backups. Do not claim completion of provider deletion based on the QueenZone receipt.
- Verify and record Sentry's actual plan/data-type retention before publication; its official retention table describes defaults, not proof of this organisation's settings. Request provider assistance when data cannot be removed through the available interface.
- The earlier non-secret production configuration review found Azure Application Insights request/trace tables at 90 days, SQL short-term backups at 7 days, and blob soft deletion at 7 days. These are separate stores; configured expiry is not an individual deletion guarantee or proof that a particular event was collected. Restoring a backup must preserve/reapply completed account deletions before restored data is served.
- TelemetryDeck currently describes no guaranteed cold-storage expiry and an expected 7–10-year anonymous-event lifetime. Do not equate its anonymity claim with the ability to erase a specific user's historical events.
- Do not introduce a selective account-content deletion service. This procedure fulfils account-associated diagnostic checks and ordinary privacy requests through the existing support route.

## Sentry control evidence

Only project `queenzone-mobile` in organisation `self-0tb` was changed. Reload confirmed `scrubIPAddresses=true`; `dataScrubber=true` and `dataScrubberDefaults=true` were preserved. Sensitive/safe fields remain empty and no organisation rule was added.

Added an Errors/Transactions/Attachments advanced rule:

- Method: Replace, default placeholder `[Filtered]`.
- Regex: `(?i)(?:^|[?&])q=([^&#]*)`.
- Only replace first capture match: enabled.
- Source: `$http.url || $http.query_string || $breadcrumb.data.url || $span.description || $span.data.'http.url' || $span.data.'url.full' || $span.data.'http.query'`.

Twelve synthetic regex cases passed before saving, covering repeated/empty/encoded values, uppercase `Q`, query-only strings and span descriptions, with negative cases for unrelated parameters and route text. Sentry accepted the rule syntax and reload confirmed persistence. No event-ID autocomplete, user payloads or test telemetry were used. The local synthetic test used Python's equivalent basic regex semantics; it was not an end-to-end Sentry ingestion test.

This rule filters `q` values in the named string fields, preserving routes, methods, parameter names, pagination and other values. It is not a blanket redaction guarantee: it does not cover arbitrary error text, unknown field layouts, encoded parameter names, standalone span ingestion outside the selected dataset, or other logging providers. It applies to future ingestion only and does not erase historical events. Do not broaden its scope silently.

## Sources

- [Sentry advanced scrubbing](https://docs.sentry.io/security-legal-pii/scrubbing/advanced-datascrubbing/)
- [Sentry DPA](https://sentry.io/legal/dpa/)
- [Sentry retention periods](https://docs.sentry.io/security-legal-pii/security/data-retention-periods/)
- [Google account deletion requirements](https://support.google.com/googleplay/android-developer/answer/13327111?hl=en)
- [Member account deletion](member-account-deletion.md)

Richard has confirmed responsibility for the support/contact intake and daily account-deletion review. Before deployment, verify provider deletion/escalation access and record actual Sentry retention; do not treat the operator commitment as proof of those provider capabilities. DPA acceptance, deployment and Play submission remain separate owner actions; none was performed in this task.
