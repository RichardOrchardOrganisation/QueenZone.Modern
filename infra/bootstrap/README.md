# OpenTofu state bootstrap

This directory owns the one-time control plane for QueenZone OpenTofu state. It is deliberately separate from the future production stack, so that stack never needs to create the backend that stores its own state.

Run from an authenticated operator workstation:

```powershell
az account set --subscription 610e3b3a-028d-4f1b-ac1d-a5567a4f8b9d
./infra/bootstrap/Bootstrap-OpenTofuState.ps1 -WhatIf
./infra/bootstrap/Bootstrap-OpenTofuState.ps1
./infra/bootstrap/Test-OpenTofuState.ps1
```

The bootstrap is idempotent. It creates or verifies:

- `Queenzone-IaC-RG` in Australia East;
- private container `tfstate` in `queenzonetfstate`;
- Entra-only blob access, TLS 1.2, blob versioning, 30-day blob/container soft delete, and a `CanNotDelete` lock;
- separate `QueenZone OpenTofu Plan` and `QueenZone OpenTofu Apply` Entra applications;
- GitHub environment OIDC credentials for `opentofu-plan` and `opentofu-apply`;
- container-scoped state access, Reader plus a minimal custom `config/list` role for plan, and resource-group-scoped Contributor for apply;
- protected-branch-only GitHub environments, with `opentofu-apply` requiring approval.

The script does not create Cloudflare tokens, import application resources, or run an OpenTofu apply against production resources.

## Telemetry-read identity

`Bootstrap-TelemetryReadIdentity.ps1` is a sibling of `Bootstrap-DeployIdentity.ps1`. Richard runs it from an authorised workstation that can create Entra app registrations. The OpenTofu apply identity must not be given those rights.

```powershell
az account set --subscription 610e3b3a-028d-4f1b-ac1d-a5567a4f8b9d
./infra/bootstrap/Bootstrap-TelemetryReadIdentity.ps1 -WhatIf
./infra/bootstrap/Bootstrap-TelemetryReadIdentity.ps1
```

The script creates or verifies:

- Entra application `QueenZone Telemetry Read`;
- federated credential `github-org-telemetry-read` whose subject is the
  immutable prefix in [`github-organization-oidc.md`](../../docs/architecture/github-organization-oidc.md)
  plus `:environment:telemetry-read`;
- GitHub environment `telemetry-read` (protected branches, no required reviewer)
  with `ARM_CLIENT_ID`, `ARM_TENANT_ID`, and `ARM_SUBSCRIPTION_ID`;
- `Monitoring Reader` on `Queenzone-RG`;
- `Log Analytics Reader` on `queenzone-prod-law`.

See [`docs/architecture/opentofu-state-and-identity.md`](../../docs/architecture/opentofu-state-and-identity.md) for recovery, rotation, and Cloudflare token controls.
For the organization transfer and immutable GitHub OIDC subjects, see [`docs/architecture/github-organization-oidc.md`](../../docs/architecture/github-organization-oidc.md). The bootstrap defaults remain on the old owner until the transfer is verified; a new owner requires explicit immutable IDs.
