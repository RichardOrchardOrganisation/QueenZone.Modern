# ADR 0013: Static Web App for mobile test distribution

## Status

Superseded — 2026-09-04 by [#1267](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1267).

The unused Static Web App and its APK-distribution workflow were decommissioned.
`dev.queenzone.org` is reassigned to the dev web environment.

## Context

GitHub Actions produces an Android APK, but workflow artifacts require a GitHub login, arrive inside a ZIP, and expire after one day. The maintainer needs a stable link that can be opened directly on an Android phone.

An Azure Static Web App named `queenzone-dev` already exists. It can serve the
download page, but a deployed 90 MB APK returns HTTP 500 on a full request even
though byte-range requests succeed. Static Web Apps is therefore not a reliable
large-binary origin for this use case.

## Decision

Use the existing `queenzone-dev` Azure Static Web App for the public build page
at `https://dev.queenzone.org`. Store the APK in the dedicated
`queenzonemobilebuilds` Standard_LRS Storage account. Only the `builds`
container permits anonymous blob reads; container listing remains disabled.

A dedicated GitHub Actions workflow:

- builds a standalone Android release APK against the staging API;
- signs every build with one stable test-only key held in Bitwarden Secrets Manager and fetched by GitHub Actions at runtime;
- assigns the GitHub Actions run number as the Android version code so a newer download is accepted as an upgrade;
- overwrites the Blob Storage object `builds/queenzone-latest.apk` and publishes build metadata in Western Australian time; and
- deploys through the existing GitHub OIDC identity, scoped separately to this Static Web App.

The site is public by URL. It carries `noindex` directives but has no authentication. iOS is excluded until CI produces a signed, device-installable build.

## Consequences

Android can install later builds as upgrades because their package identifier and signing key remain stable. Losing or rotating the test key requires uninstalling the existing test app before installing the next build.

The distribution page and APK are intentionally public. They must contain no
secrets or production-only credentials. The dedicated Storage account remains
isolated from production UGC and media storage. The GitHub OIDC identity has
`Storage Blob Data Contributor` only on this account and `Contributor` only on
the Static Web App.
