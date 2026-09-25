# ADR 0019: API Versioning Convention (Mobile / Store-Lag)

## Status

Accepted.

## Context

[ADR 0010](0010-versioned-json-api-conventions.md) already records the
`/api/v1` JSON contract: camelCase, Problem Details, pagination, OpenAPI, and
the rule that a breaking change requires `/api/v2`. The living endpoint list
is [`docs/architecture/json-api-v1.md`](../architecture/json-api-v1.md).

What 0010 does not record is the **operational** convention for a store-shipped
client. QueenZone.Mobile installs lag App Store / Play review, and many members
do not auto-update. A server-only cutover that retires `/api/v1` the same week
`/api/v2` ships would strand those builds.

[Issue #1270](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1270)
(epic [#1264](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1264)
Phase 6) asks for that convention in writing. This ADR complements 0010; it
does not replace the JSON/error/pagination rules there.

The version already lives in the URL path. `ApiV1` in
`src/QueenZone.Web/Api/ApiV1.cs` is the committed prefix:

- `ApiV1.Prefix` = `/api/v1`
- `ApiV1.OpenApiDocumentName` = `v1` (OpenAPI at `/openapi/v1.json`)
- Endpoints map on `MapGroup("/api/v1")` (or `ApiV1.Prefix`) with
  `.WithGroupName(ApiV1.OpenApiDocumentName)`

The mobile client builds absolute URLs with `apiV1Url()` in
`src/QueenZone.Mobile/src/config/appConfig.ts`. There is no `/api/v2` code
today. This ADR does not add any.

## Decision

### 1. Cut `/api/v2` only for a breaking change

A breaking change is a removed field, changed field semantics, or changed auth
behavior that an existing `/api/v1` client would mishandle. Additive work stays
on v1: new optional fields, new endpoints, new optional query parameters.

JSON conventions for any new version stay those in ADR 0010 unless a later ADR
replaces them.

### 2. Keep v1 alive while installed store builds depend on it

After a v2 ships, keep v1 functioning for as long as any installed store build
still calls it. App Store and Play review lag plus members who do not
auto-update means this is **weeks, not days**.

Do not retire or break-edit the v1 group on the same release that introduces
v2.

### 3. Add v2 as a new prefix and OpenAPI group, not a breaking edit of v1

When a breaking change is actually required:

1. Add a sibling `ApiV2` (same shape as `ApiV1`) with `Prefix = "/api/v2"` and
   its own OpenAPI document name (`v2`, served at `/openapi/v2.json`).
2. Map the new surface on `MapGroup("/api/v2")` with `.WithGroupName("v2")`.
   Register a second `AddOpenApi("v2", ...)` document that includes only that
   group. Do not retarget the existing v1 `ShouldInclude` filter or
   `/openapi/v1.json`.
3. Leave the v1 `MapGroup`, fallback, and `ApiV1.IsApiPath` helper intact.
   Copy-and-adapt endpoints that must change; do not mutate v1 handlers in
   place to the new contract.
4. Point new or updated mobile builds at `/api/v2` (a sibling of `apiV1Url()`).
   Store builds that still call `/api/v1` keep working until decision 2 is
   satisfied.

Do not implement `/api/v2` until a concrete breaking change needs it.

## Consequences

Benefits:

- Old TestFlight / Play / store builds keep a working API after a breaking
  server change.
- Additive mobile work does not force a version bump or a dual-client matrix.
- The next version has an obvious home (`ApiV2` + new MapGroup) instead of
  in-place edits that silently break installed apps.

Tradeoffs:

- Two URL prefixes and two OpenAPI documents must run in parallel during the
  overlap window.
- Server changes that look "cleanup" (renames, stricter auth, removed fields)
  are not free — they wait for a real v2 or stay additive on v1.

## Related

- [ADR 0010](0010-versioned-json-api-conventions.md) — `/api/v1` JSON conventions
- [`docs/architecture/json-api-v1.md`](../architecture/json-api-v1.md) — living v1 contract
- [`src/QueenZone.Web/Api/ApiV1.cs`](../../src/QueenZone.Web/Api/ApiV1.cs) — committed prefix
- [#1270](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1270) — this convention
- [#1264](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1264) — parent epic
