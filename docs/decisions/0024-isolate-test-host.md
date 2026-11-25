# ADR 0024: Isolate test authentication and fixtures from production

- Status: Accepted
- Date: 2026-11-24
- Issue: #2440

## Context

Header-based authentication handlers shipped in QueenZone.Web. Environment checks prevented normal production registration, but an incorrect environment could enable them. Browser and mobile-contract fixtures also shipped in the web assembly.

The deployed dev App Service uses the same production-shaped web artifact and can require sample data. In-memory repositories, shared stores and sample seeds therefore serve a supported runtime mode as well as tests.

## Decision

Keep in-memory repositories, shared stores and sample seeds in QueenZone.Data. Do not make dev sample mode depend on a test assembly. Existing environment, database and storage guards remain in force.

Move header-based admin/member handlers, crossword/member browser fixtures and the mobile contract bootstrap into QueenZone.Testing, under tests/. It references QueenZone.Web; production projects never reference it. QueenZone.TestHost is the explicit executable for local header authentication, deterministic browser tests, mirror E2E and mobile contracts.

QueenZone.Web exposes a composition interface. The test assembly initializer and dedicated host explicitly register its implementation. Runtime configuration cannot register it. A standalone web executable fails closed if its selected environment requires test authentication; no environment name can introduce missing handler code.

Production publishes continue to target src/QueenZone.Web/QueenZone.Web.csproj. Test-host publishes target tests/QueenZone.TestHost/QueenZone.TestHost.csproj and must never be deployed. Automated assembly and publish checks guard this dependency boundary.

## Consequences

Local developers needing header authentication run QueenZone.TestHost. Entra-configured local hosts can still run QueenZone.Web. Tests retain their existing authentication and fixture behavior. The dedicated host uses the web application's Razor assembly, settings and static assets.

This change removes executable authentication shortcuts from production artifacts. It deliberately does not remove sample repositories from those artifacts or change dev App Service data selection.
