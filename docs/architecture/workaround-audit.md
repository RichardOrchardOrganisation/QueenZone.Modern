# Workaround audit

Agents treat the codebase as their memory. Once a workaround exists, it becomes the pattern, and every copy makes the next copy more likely. This page lists each known workaround with its location, why it exists, and a decision. `AGENTS.md` ("Workarounds and suppressions") holds the short "do not copy" rules. `node scripts/check-suppressions.mjs` enforces them in CI.

Audited on 2026-09-26 for [#1801](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1801).

The audit searched tracked source files for `eslint-disable`, `@ts-ignore` / `@ts-expect-error` / `@ts-nocheck`, `#pragma warning disable`, `NOSONAR`, `SuppressMessage`, `ExcludeFromCodeCoverage`, `NuGetAuditSuppress`, `istanbul` / `c8 ignore`, and `HACK` / `FIXME` / "TODO workaround" comments. It also covered `NoWarn` and `.editorconfig` severity overrides, version pins and `overrides`, and duplicated private helpers. It skipped EF-generated `Migrations/`, vendored `wwwroot/lib/`, `design/` and `docs/`.

There are none of these: `NOSONAR`, `@ts-ignore` / `@ts-expect-error`, `HACK` / `FIXME`, `NoWarn`, `.editorconfig` severity overrides, or `SuppressMessage`. The only `TODO` strings in code are the `AzureAd:ClientId` placeholder value, which `AzureAdClientId` treats as unset on purpose.

## Decisions

**Remove now** means it was done in [#1823](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/pull/1823) (suppressions and pins) or [#1825](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/pull/1825) (duplicated helpers). **Remove with #N** means it stays until that issue lands. **Keep** means it stays, with the reason given, and the ratchet stops new copies.

### Removed now

| Location | What it was | Why it could go |
| --- | --- | --- |
| `src/QueenZone.Mobile/src/hooks/usePagedContent.ts` | 3 × `react-hooks/exhaustive-deps` disables around `applyPageMeta` | `applyPageMeta` only touches refs and setters, so a stable `useCallback` can be listed. |
| `InboxScreen.tsx`, `ArchivedScreen.tsx`, `ForumScreen.tsx` | 5 × `exhaustive-deps` disables for `paged.refresh` / `forumStats.reload` | Destructure the function and list it. |
| `src/QueenZone.Mobile/eslint.config.js` | 8 React Compiler rules turned off, pointing at the closed #1143 | No violations. |
| `src/QueenZone.Web/Pages/Submit/News.cshtml.cs` | `#pragma warning disable CS8509` | Every failure renders the same page, so an `is Accepted` check is enough. |
| `Directory.Build.props` | `NuGetAuditSuppress` for AngleSharp 0.17.1 (GHSA-pgww-w46g-26qg) | HtmlSanitizer 9.2.1039 resolves AngleSharp 1.7.2. |
| `QueenZone.Data.csproj`, `Directory.Packages.props` | Direct `System.Security.Cryptography.Xml` pin | No package in the graph references it any more. |
| Six `InMemory*Repository` classes and `EfHelpRequestRepository` | Private copies of `NormalizeOptional(value, maxLength)` | Identical to `SubmissionInput.NormalizeOptional` (#1788). |
| `EfDeviceTokenRepository`, `EfHomePollRepository`, `EfPrivateMessageRepository` | Pass-through `IsUniqueConstraintViolation` wrappers | Callers use the `DbUpdateExceptionExtensions` extension directly. |

### Remove with a linked issue

| Location | What | Why it exists | Issue |
| --- | --- | --- | --- |
| `src/QueenZone.Mobile/package.json` | `react-native-reanimated` 4.5.1 and `react-native-worklets` 0.10.1 exact pins | Expo SDK 57 matrix. Worklets 0.12.2 (GHSA-5fj2-4frq-gpxm) needs SDK 58. | [#1782](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1782) |
| `src/QueenZone.Mobile/src/screens/photos/ZoomableArchiveImage.tsx` and its test | Pinch and double-tap run on the JS thread (`runOnJS`), locked by a test | Works around a native Reanimated worklet abort on SDK 57. Re-test after the upgrade. | [#1782](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1782) |
| `src/QueenZone.Mobile/package.json` `overrides.image-size` 1.2.1 and `npm-advisory-allowlist.json` (2 GHSAs) | Metro 0.84 needs the v1 sync API, and 2.x has no patched release | Build-time only. The allowlist rows expire on 2026-11-24, which forces a re-check. Re-check on the SDK 58 Metro. | [#1782](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1782) |
| `src/QueenZone.Mobile/eslint.config.js` | 6 React Compiler rules still `off` (`refs` 58, `set-state-in-effect` 30, `immutability` 22, `preserve-manual-memoization` 2, `globals` 1, `purity` 1) | Mostly the "latest ref" `fetcherRef.current = fetcher` pattern in the server-state hooks. | [#1821](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1821) |
| `HtmlTagRegex` (×5), `ToOffset` (×4), `EnsureRowVersion` (×4), trivia `NormalizeOptional` / `NormalizeDifficulty` (×5), `EfIdempotencyStore.IsUniqueConstraintViolation` | Duplicated one-off helpers | Copied before a shared home existed. | [#1822](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1822) |

### Keep

| Location | Count | Why it stays |
| --- | --- | --- |
| `[ExcludeFromCodeCoverage]` on `src/QueenZone.Data/Repositories/*`, `Sql/*`, `Storage/AzureBlobStorageBackend` | 56 | These are SQL Server- and Azure-only paths (stored procedures, `COL_LENGTH` probes, legacy tables) that SQLite unit tests can't run. They're covered by the in-memory repositories plus the opt-in SQL Express probes (see `AGENTS.md`). New code should reach coverage through SQLite or in-memory tests. Only add the attribute with a `Justification` naming the probe that covers it. |
| `[ExcludeFromCodeCoverage]` on `src/QueenZone.Data/Entities/*` | 46 | EF entity property bags. Newer entities don't carry it, so don't add it to new ones. |
| `[ExcludeFromCodeCoverage]` on `src/QueenZone.Tools/*` commands and `DevSnapshot` | 15 | One-shot operator tools against production SQL and blobs. Their pure logic is tested in `QueenZone.Tools.Tests`. |
| `[ExcludeFromCodeCoverage]` on thin SDK adapters in `QueenZone.Web` (`SmtpEmailSender`, `GoogleAnalyticsDataClient`, auth/telemetry registration, `UgcHtml` sanitizer wiring) | 5 | Each wraps a third-party client behind an injectable seam that is tested. |
| `src/QueenZone.Data/Repositories/EfAdminNewsRepository.cs` `#pragma warning disable EF1003` | 3 | The raw SQL comes from fixed schema-detection branches (`LegacyNewsSchema`), not user input. Parameters still go through `{0}` placeholders. Don't copy this for SQL built from request data. |
| `src/QueenZone.Web/Api/Member/MemberApiEndpoints.cs` `#pragma warning disable CS8509` | 1 | Maps each closed `SubmitOutcome` variant to a different HTTP result. C# can't prove the hierarchy is closed, and a throwing discard arm would be dead code. If every failure is handled the same way, use an `is` check instead (see `Submit/News.cshtml.cs`). |
| `@typescript-eslint/no-require-imports` disables in mobile | 28 | Three legitimate cases: Jest `jest.mock` factories (they must `require`), CommonJS files Expo loads in Node (`app.config.ts`, `apiEnvironments.cjs`, config plugins), and platform-gated lazy native modules (`widgetSync`, `androidWallpaper`, AsyncStorage in `offlineQueue` / `share`). Each line already gives its reason. |
| `ZoomableArchiveImage.tsx` `react-hooks/exhaustive-deps` | 2 | Reanimated shared values are stable refs, but the lint rule can't tell. |
| `navigation/types.ts` `no-empty-object-type` | 1 | React Navigation's `RootParamList` augmentation must be an interface. |
| `uploadFile.native.test.tsx` `import/first` | 1 | `jest.mock` has to run before the import under test. |
| `src/QueenZone.Mobile/package.json` `overrides` for `react-dom` / `react-test-renderer` 19.2.3 | 2 | Keeps peer React packages on the exact `react` version Expo SDK 57 ships. Move them with `react`. |
| Exact mobile versions such as `@react-native-async-storage/async-storage` 2.2.0 and `react-native-svg` 15.15.4 | — | These are the SDK 57 bundled versions written by `npx expo install`. Expo Doctor enforces them. |

## Keeping this current

- `config/suppression-baseline.json` holds the per-file count of **unlinked** suppressions. `node scripts/check-suppressions.mjs` runs in the `Suppression check` workflow and fails when:
  - a file gains a suppression that doesn't name an issue on the same line (`#1234` or an issue URL), or
  - a file drops below its baseline. Run `node scripts/check-suppressions.mjs --write` and commit the lower baseline. The ceiling only goes down.
- `node scripts/check-suppressions.mjs --list` prints every unlinked suppression with its line.
- When a "remove with" issue lands, delete its row here. When you add a "keep", add a row with the reason.
