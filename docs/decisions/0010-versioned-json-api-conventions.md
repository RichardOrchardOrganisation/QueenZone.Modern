# ADR 0010: Versioned `/api/v1` JSON API Conventions

## Status

Accepted.

## Context

Epic [#719](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/719) adds a React Native mobile app. The site is Razor Pages with cookie auth; mobile auth (#720, #721) already landed under `/api/v1/auth`. Later epics will add news, forum, messages, and gallery JSON.

Without one request/response/error/pagination convention, those endpoints would drift. Unversioned routes in `src/QueenZone.Web/Endpoints/` (RSS, uploads, streaming) must stay unchanged.

## Decision

- Keep a versioned JSON API area at `/api/v1` in `src/QueenZone.Web/Api/`, alongside Razor Pages.
- Version with the URL path. Additive changes stay in v1; breaking changes require `/api/v2`.
- Use camelCase JSON, ISO-8601 UTC dates, and string enums.
- Use RFC 7807 Problem Details for resource errors. Keep RFC 6749 `{ error, error_description }` on OAuth token/authorize responses.
- Paginate collections with `page` / `pageSize` (defaults 1 / 20, max 100) and `ApiPagedResponse<T>`.
- Generate OpenAPI at runtime (`/openapi/v1.json`) from endpoints tagged with group name `v1`.

## Consequences

Benefits:

- Website and mobile client can evolve independently.
- Later epics share types (`ApiPagedResponse<T>`, `ApiPagination`) and one OpenAPI document.
- HTML error pages are not returned to JSON clients.

Tradeoffs:

- Two error shapes (Problem Details vs OAuth2) must be documented and preserved.
- OpenAPI is generated at runtime rather than committed as a build artifact; tests assert the served document.

## Related

- [ADR 0019](0019-api-versioning-convention.md) — mobile/store-lag operational convention (how long v1 stays live, how to add `/api/v2` alongside it). JSON conventions stay here.
