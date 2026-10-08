# Provenance

Initial extraction from RichardOrchardOrganisation/QueenZone.Modern on 2026-10-05, source commit `39e11b32b9aa41669e2204c8dcc1caf67d7a97cb`. The working copy contained a locally edited AGENTS.md; policy extraction used that supplied working-copy guidance. No QueenZone files were modified.

Adapted executable sources: scripts/Test-CoverageGate.ps1, scripts/Get-CrapReport.ps1, scripts/Test-MobileCoverageGate.mjs, scripts/check-suppressions.mjs, scripts/check-suppressions.test.mjs, and coverlet.runsettings. Original MIT copyright is retained in LICENSE.

Portable policies were distilled from AGENTS.md and docs/architecture/testing-policy.md. QueenZone-specific issue links, deployment/database configuration, feature fixtures, mobile libraries, and Cursor orchestration were removed. Test fixture namespaces use Example; application paths are configurable.

The .NET executable gate and CI used 91% global/70% changed-line coverage at extraction, despite an older 51% statement in AGENTS.md. CRAP is report-only. QueenZone mobile’s measured baseline is not a generic starter requirement.
