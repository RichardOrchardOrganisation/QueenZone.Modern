# CodeQL

Issue: [#1418](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1418) (Bob Architecture lock, Option A).

QueenZone uses GitHub **default setup** for CodeQL (`dynamic/github-code-scanning/codeql`). There is no advanced `codeql.yml` workflow in this repo. C#, JavaScript/TypeScript, Python, and Actions stay on the default scan.

## Why `java-kotlin` is skipped

Run [1353](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/34187320605) on `main` at `d7b0d879` failed only `Analyze (java-kotlin)` after the Expo wallpaper module landed (#1410). The other four languages passed. This was not a security finding.

The only first-party Java/Kotlin in the tree is `src/QueenZone.Mobile/modules/queenzone-wallpaper/android/.../QueenZoneWallpaperModule.kt`. That module's `build.gradle` applies `com.android.library` without a plugin version (Expo pattern — the version comes from the generated app root). Default setup uses `build-mode: none` for Java. It still finds Gradle under `src/QueenZone.Mobile/modules/queenzone-wallpaper/android`, cannot resolve the project, extracts no Java/Kotlin, and fails with `no-source-code-seen-during-build`.

Option A path-excludes Expo Android modules. After that exclude, **no Java/Kotlin remains**. CodeQL still schedules `Analyze (java-kotlin)` when the language is enabled, and an empty job fails the same way. A red empty job is not acceptable.

**Disable Java/Kotlin on the default scan.** Do not add an Android SDK or Gradle build for CodeQL. Green or skipped-by-design both satisfy #1418; a red empty job does not.

## Path exclude (next Expo module)

[`.github/codeql/codeql-config.yml`](../../.github/codeql/codeql-config.yml) ignores:

- `src/QueenZone.Mobile/modules/**/android`
- `**/modules/**/android/**`

That covers the next Expo native module, not only wallpaper. It does not exclude C# / JS / Python / Actions.

Default setup does not read the file until the `github-codeql-config-file` repository property points at it. Set the property to `.github/codeql/codeql-config.yml` so a later re-enable of `java-kotlin` (if first-party Java appears **outside** Expo modules) still ignores those trees. See [Repository properties for code scanning](https://docs.github.com/en/code-security/concepts/code-scanning/repository-properties).

## Operator: keep `java-kotlin` unchecked

1. Settings → Advanced Security → CodeQL analysis → **View CodeQL configuration** → **Edit**.
2. Uncheck **Java/Kotlin**. Leave C#, JavaScript/TypeScript, Python, and Actions selected.
3. Save changes (this starts a default-setup run).
4. Set repository property `github-codeql-config-file` to `.github/codeql/codeql-config.yml` if that property exists on the org (create it first if needed).

Revisit this skip only when first-party Java/Kotlin is added **outside** `src/QueenZone.Mobile/modules/**/android`.
