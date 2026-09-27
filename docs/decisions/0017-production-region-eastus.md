# ADR 0017: Production Azure Region — `eastus`

## Status

Superseded by [ADR 0020](0020-production-region-canadaeast.md) after Microsoft
confirmed that the East US regions were unavailable for this subscription.

## Context

[Epic #1264](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1264) is building a real dev/prod split. As part of that epic, region migration for production (previously deferred) came back into scope as Phase 0 ([#1271](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1271), this ADR) and Phase 7 ([#1272](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1272), the actual cutover).

Production (the App Service currently misnamed `queenzone-dev`, its SQL server `queenzone-sql-server`, and its storage account `queenzone`) runs today in **Australia East**. That is a poor origin region for the site's likely audience: Cloudflare's proxy only speeds up the visitor-to-edge leg of a request, not the edge-to-origin leg that every cache miss, forum/auth page, and mobile API call still has to pay in full.

The site is **not yet publicly announced**, so there is no live Application Insights geo data to base this on. This decision is therefore based on **historical, anecdotal traffic** — the maintainer's recollection that the site's prior incarnation, years ago, drew traffic mostly from the US and Europe — not current telemetry. Doing the move now, before launch, is close to the best window it will ever have: once there's a real audience, a region migration becomes materially riskier and harder to justify disrupting.

The dev environment (Phases 1–2 of #1264) is a separate, explicit exception: it deliberately **stays in `australiaeast`** regardless of where production ends up, since it exists for the maintainer's own fast feedback loop and the maintainer is in Australia. Nothing in this ADR applies to dev.

## Decision

**Production moves to `eastus`.**

Reasoning, in order of weight:

1. **Best single-region compromise for a mixed US/Europe audience.** `eastus` is Azure's primary East Coast hub — the shortest well-connected hop across the Atlantic to Western Europe, while remaining strong for the (presumably larger) US side. A Europe-based region (North/West Europe) would likely be the wrong call if US traffic is the bigger share, as recalled. `centralus` was also considered and has no real advantage over `eastus` for this specific split, while being slightly worse for the Europe side.
2. **Cost and availability.** `eastus` is typically Azure's cheapest or near-cheapest region for the SKUs this project actually uses (B1 App Service, Basic-tier SQL), and new features/SKUs tend to land there first. This matters given the project's explicit minimal-cost, single-instance posture (see [`hosting-scale-and-cache.md`](../architecture/hosting-scale-and-cache.md)).
3. **Cloudflare already absorbs most of the geography that matters for cacheable content.** Static assets and anonymous HTML are served from Cloudflare's edge regardless of origin region. Origin region choice mainly affects the dynamic/API path (forum, auth, mobile) — which is exactly why a good-enough compromise region is an acceptable call here, rather than something requiring live telemetry to pick precisely.

**Fallback:** `eastus2` is an acceptable substitute if `eastus` has capacity or SKU availability issues when #1272 is actually executed — same profile, essentially interchangeable for this purpose.

**This is a hypothesis, not a permanent decision.** Once the site has an announced audience and real Application Insights geo data, this region choice should be revisited and confirmed (or not) against actual traffic. If the real split turns out meaningfully different from the historical recollection above (e.g. genuinely Europe-dominant, or an unexpected APAC segment), a second region move may be worth it later. That would be a new, separate follow-up — it is not a reason to hold up #1272 now, and this ADR does not need to be re-litigated before #1272 starts on that basis.

### Scope: SQL server and storage account move too

The App Service is not the only production resource that has to move. **The existing SQL server (`queenzone-sql-server`) and storage account (`queenzone`) move to `eastus` in the same #1272 effort**, alongside the App Service.

This is stated explicitly here so #1272 does not have to re-decide it: moving only the App Service to `eastus` while leaving the SQL server and storage account in `australiaeast` would leave the App Service making cross-region calls back to `australiaeast` for every database query and blob access — on the dynamic/API path this move exists to speed up. That would erase most of the latency benefit of the migration while still paying its full cutover cost and risk.

Out of scope for this ADR: the mechanics of the #1272 cutover itself (import strategy, `prevent_destroy` handling, cutover sequencing, DNS/traffic switch, rollback plan). Those belong to #1272's own design, not this region decision.

## Consequences

Benefits:

- Production's dynamic/API path (the part Cloudflare's proxy cannot shortcut) gets meaningfully closer to its actual audience, for both the App Service and its database/storage dependencies.
- `#1272` starts with a settled target instead of needing to research or relitigate the region question.
- Doing this pre-launch, while there is no live audience, keeps the blast radius of a region cutover as low as it will ever be for this project.

Tradeoffs:

- The region choice is not data-driven — it is a best-effort call based on years-old, anecdotal traffic memory. It could turn out wrong once real telemetry exists.
- `eastus`/`eastus2` capacity or pricing could shift by the time #1272 is actually executed; the fallback substitute exists for exactly this reason.
- A second migration is explicitly on the table if post-launch telemetry disagrees — this ADR accepts that as a reasonable future cost rather than blocking on more certainty now.

## Related

- [#1264](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1264) — Epic: Dev environment and release promotion flow
- [#1271](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1271) — Phase 0: Document the target Azure region (this ADR)
- [#1272](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1272) — Phase 7: Migrate production to the new region and rename to logical resource names
- [`azure-hosting-plan.md`](../architecture/azure-hosting-plan.md) — overall Azure shape
- [`hosting-scale-and-cache.md`](../architecture/hosting-scale-and-cache.md) — single-instance, minimal-cost posture this decision is consistent with
- [`opentofu-inventory.md`](../architecture/opentofu-inventory.md) — live estate ownership for OpenTofu
- [`opentofu-contributor-runbook.md`](../architecture/opentofu-contributor-runbook.md) — required reading before touching the production OpenTofu root
