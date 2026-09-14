# Architectural Decision Records

Index of accepted (and superseded) decisions in this folder. New ADRs take the
next free number.

| ADR | Title | Status |
| --- | --- | --- |
| [0001](0001-read-only-first.md) | Archive-first public site with live news | Accepted |
| [0002](0002-use-aspnet-core.md) | Use ASP.NET Core for the new site | Accepted |
| [0003](0003-use-dapper-initially.md) | Use Dapper initially for legacy database access | Accepted (library choice superseded by 0006) |
| [0004](0004-legacy-schema-is-import-source.md) | Treat legacy schema as import source | Superseded by 0021 |
| [0005](0005-admin-news-publishing.md) | Admin news publishing with Microsoft Entra ID | Accepted |
| [0006](0006-hybrid-ef-core-admin-writes.md) | Hybrid EF Core for admin writes | Accepted |
| [0007](0007-rich-text-editor-quill.md) | Quill rich text editor for shared authoring | Accepted |
| [0008](0008-app-service-settings-ownership.md) | App Service application settings stay outside OpenTofu | Accepted |
| [0009](0009-react-native-for-mobile-app.md) | React Native for the native mobile app | Accepted |
| [0010](0010-versioned-json-api-conventions.md) | Versioned `/api/v1` JSON API conventions | Accepted |
| [0011](0011-mobile-project-location-and-build-tooling.md) | Mobile project location and build tooling | Accepted |
| [0012](0012-react-navigation-app-shell.md) | React Navigation app shell | Accepted |
| [0013](0013-static-web-app-mobile-test-distribution.md) | Static Web App for mobile test distribution | Superseded |
| [0014](0014-push-notification-transport-and-dispatch.md) | Push notification transport and dispatch model | Accepted |
| [0015](0015-private-message-report-retention-and-audit.md) | Private message report retention and moderator access audit | Accepted |
| [0016](0016-news-forum-topic-on-first-publish.md) | News-forum topic on first article publish | Accepted |
| [0017](0017-production-region-eastus.md) | Production Azure region — `eastus` | Superseded by 0020 |
| [0018](0018-mobile-server-state-strategy.md) | Mobile server-state strategy | Accepted |
| [0019](0019-api-versioning-convention.md) | API versioning convention (mobile / store-lag) | Accepted |
| [0020](0020-production-region-canadaeast.md) | Production Azure region — `canadaeast` | Accepted |
| [0021](0021-legacy-database-is-production.md) | The legacy database is the production database | Accepted |
| [0022](0022-nightly-legacy-db-sync-strategy.md) | Nightly legacy DB sync strategy | Accepted |
