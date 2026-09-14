# ADR 0020: Production Azure Region — `canadaeast`

## Status

Accepted and implemented on **10 September 2026**. Supersedes [ADR 0017](0017-production-region-eastus.md). Stages 1–4 of #1272 are complete. Stage 5 began on **14 September 2026** by retiring the old web estate and production database after the accepted four-day observation period.

## Context

ADR 0017 selected `eastus`, with `eastus2` as its capacity fallback, for the
production migration in [#1272](https://github.com/richardorchard/QueenZone.Modern/issues/1272).
The choice was a pre-launch hypothesis based on the site's historical US and
European audience rather than current telemetry.

On **9 September 2026**, Microsoft confirmed that the East US regions were not
available for this subscription. The first protected `eastus` apply had already
stopped safely after creating only an empty Storage account and unused telemetry
resources. It did not copy data or change DNS, hostnames, deployment targets, or
the existing Australia East production resources.

The current subscription reports Azure SQL provisioning as `Available` in
Canada East. It also reports 30 regional App Service instances available, and
Azure lists the B1 Linux App Service SKU there. These checks reduce provisioning
risk but do not replace a reviewed create-only plan and protected apply.

Microsoft's P50 Azure inter-region measurements provide a proxy for the likely
origin-latency difference. Compared with Australia East, Canada East reduces
round-trip time by about 135–177 ms for representative US regions and 178–184 ms
for the UK and Western Europe. These are Azure network measurements, not
QueenZone end-user telemetry. See
[Azure network round-trip latency statistics](https://learn.microsoft.com/azure/networking/azure-network-latency).

## Decision

**Production moves to `canadaeast`.**

Canada East is the closest available substitute for the rejected East US
regions. It keeps the origin close to both eastern North America and Western
Europe while retaining the existing assumption that the US will be the larger
part of the audience.

The App Service, SQL server and database, Storage account, Log Analytics
workspace, and Application Insights resource move together. Keeping data in
Australia East would add cross-region latency to every database and Blob
Storage operation and undermine the purpose of the move.

The dev environment remains in `australiaeast` for the maintainer's local
feedback loop. The production cutover was gated: Australia East remained live
until the Canada East candidate passed the staged copy, deployment, smoke-test,
and cutover checks.

There is no automatic fallback region. If Canada East fails provisioning or
post-launch telemetry disproves the audience assumption, stop and record a new
decision rather than silently selecting another region.

## Consequences

Benefits:

- US and European dynamic pages and API calls avoid most of the Australia
  origin delay.
- The migration remains pre-launch, when traffic-cutover risk is lowest.
- The target remains a single low-cost B1 App Service and S0 SQL database.

Tradeoffs:

- Australian production access becomes about 200 ms slower per origin
  round-trip than Australia East.
- The audience assumption remains unverified until launch telemetry exists.
- The three empty East US resources had to be removed safely before their names
  could be reused in Canada East; that cleanup completed on **9 September 2026**.

## Related

- [ADR 0017](0017-production-region-eastus.md) — superseded East US decision
- [#1271](https://github.com/richardorchard/QueenZone.Modern/issues/1271) — original region decision
- [#1272](https://github.com/richardorchard/QueenZone.Modern/issues/1272) — production migration
- [`production-region-migration.md`](../architecture/production-region-migration.md) — staged migration and cleanup gates
