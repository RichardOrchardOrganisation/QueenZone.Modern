# ADR 0011: Mobile Project Location And Build Tooling

## Status

Accepted.

## Context

[ADR 0009](0009-react-native-for-mobile-app.md) selects React Native for the
QueenZone mobile application. [Issue #790](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/790)
requires the project's location and initialization approach before client work
can begin.

The mobile client and the versioned `/api/v1` backend will evolve together. The
client must also derive its theme from the web design tokens in this repository.
A separate repository would add release, dependency, issue-tracking, and access
administration without providing useful isolation for the current single-maintainer
project.

The planned application needs native capabilities, including background audio,
lock-screen media controls, camera or photo-library access, and push
notifications. Expo Go cannot represent the production native runtime, but an
Expo development build can include native libraries and native configuration.
Starting with bare React Native would make the project own the generated Xcode
and Gradle projects before a sustained custom-native requirement exists.

GitHub Actions already provides the repository's CI system. Android builds can
run on Linux and iOS builds can run on macOS, so adopting Expo Application
Services (EAS) is not required to build either platform.

## Decision

Create the mobile application in this monorepo at:

```text
src/QueenZone.Mobile/
```

Keep its Node dependency graph and build commands separate from `QueenZone.sln`.
Commit its package-manager lock file and pin the supported Node and Expo SDK
versions.

Initialize it as an **Expo-based React Native application using TypeScript,
development builds, and Continuous Native Generation**.

- Use `expo-dev-client` as the supported development runtime.
- Use Expo configuration and config plugins for native settings where practical.
- Do not treat Expo Go as a supported build or test environment.
- Do not commit generated `ios/` and `android/` projects initially. Generate them
  from committed configuration during local and CI builds.
- Reconsider ownership of the generated native projects only when sustained
  Swift, Kotlin, Xcode, or Gradle customization makes generation less reliable
  than maintaining them directly.

Treat **Android and iOS as equal supported platforms**. Either platform may be
implemented or released first for practical reasons, but neither is the
secondary port. Native dependencies must be assessed for both platforms when
introduced.

Use the same application identifier on both platforms:

```text
org.queenzone.mobile
```

Use **GitHub Actions** as the primary CI build system:

- Build Android on a GitHub-hosted Linux runner.
- Build iOS on a GitHub-hosted macOS runner.
- Run mobile jobs only when mobile code, shared mobile contracts, or the mobile
  workflow changes.
- Skip the website .NET suite and App Service deploy when a change is
  mobile-only (`src/QueenZone.Mobile/`). Mixed web + mobile PRs still run both.
- Build unsigned development or simulator targets for pull-request validation.
- Upload useful build outputs and diagnostics as short-lived workflow artifacts.
- Add signing credentials and store submission as a separate release concern;
  keep those credentials in GitHub secrets or another approved secret store,
  never in the repository.

A clean checkout must support local development builds with the platform's
standard SDK installed:

```text
npx expo run:android
npx expo run:ios
```

The iOS command requires macOS and Xcode. The Android command requires the
Android SDK and a supported JDK. The mobile README must document the exact pinned
prerequisites and clean-checkout commands when the project is initialized.

Do **not** adopt EAS for the initial project. No Expo account, EAS project owner,
or EAS-hosted credential store is required by this decision. EAS may be
reconsidered later if its build distribution, credential management, over-the-air
updates, or store-submission services provide a clear benefit over GitHub Actions
and local tooling.

## Consequences

Benefits:

- API and client changes can be reviewed together in one pull request.
- Shared contracts and design-token changes remain visible to both sides of the
  application.
- Expo config plugins and generated native projects reduce routine native build
  maintenance without blocking native libraries or custom native code.
- GitHub Actions provides independent Android and iOS build evidence without a
  second hosted build platform.
- Android and iOS receive equal architectural status from the start.

Tradeoffs:

- The repository gains a second toolchain, dependency graph, and CI workload.
- macOS runners are required to prove iOS compilation.
- Generated native projects require deterministic pinned tooling; Expo SDK or
  native dependency upgrades can change generated output.
- Signing and store release automation still need separate decisions and secure
  credential setup before production distribution.
