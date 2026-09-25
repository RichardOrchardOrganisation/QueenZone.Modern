# OpenTofu remote state and workload identities

Issue: [#616](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/616), step 2 of epic [#615](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/615).

**Bootstrapped and locally verified:** 2026-08-13. **First real production plan/apply:** 2026-09-03 — see [`opentofu-ci-and-runbooks.md`](opentofu-ci-and-runbooks.md#what-actually-happened-on-the-first-real-apply). Production is now under full OpenTofu management (76 resources imported across Azure and Cloudflare, zero deletes).

## Boundary

The state control plane is separate from `Queenzone-RG` and the `queenzone` media account. The production stack consumes the backend; it does not create or manage it.

| Control | Value |
| --- | --- |
| Resource group | `Queenzone-IaC-RG` |
| Storage account | `queenzonetfstate` |
| Container | `tfstate` (private) |
| State key | `production.tfstate` |
| Authentication | Entra ID only; shared-key access disabled |
| Data protection | Blob versioning; blob and container soft delete, 30 days |
| Deletion protection | `CanNotDelete` lock on the storage account |
| Locking | AzureRM backend blob lease; never use `-lock=false` for writes |

The non-secret backend settings are committed at [`infra/backend/production.backend.hcl`](../../infra/backend/production.backend.hcl). Subscription, tenant, and workload client IDs come from the authenticated Azure CLI locally or GitHub environment variables in Actions.

## Identity and privilege model

| Principal | State scope | Azure production scope | GitHub trust |
| --- | --- | --- | --- |
| Authorised local operator | `Storage Blob Data Contributor` on `tfstate` only | Existing operator access; bootstrap adds none | N/A |
| `QueenZone OpenTofu Plan` | `Storage Blob Data Contributor` on `tfstate` only | `Reader` on `Queenzone-RG`, plus the custom `QueenZone OpenTofu Plan - App Service Config Reader` role (`Microsoft.Web/sites/config/list/action` only) on `Queenzone-RG` | `opentofu-plan` environment |
| `QueenZone OpenTofu Apply` | `Storage Blob Data Contributor` on `tfstate` only | `Contributor` on `Queenzone-RG` | approval-gated `opentofu-apply` environment |

**Dev extension, 2026-09-04:** after explicit maintainer approval, the dev
resource group was created through a one-resource OpenTofu plan in `dev.tfstate`.
The same plan identity now also has Reader and the config-reader custom role on
`Queenzone-Dev-RG`; the apply identity has Contributor on that group. The custom
role's assignable scopes contain both QueenZone groups, with its single action
unchanged. No subscription-wide role assignment was added. Existing state
container permissions cover both `production.tfstate` and `dev.tfstate`.

Neither workload identity is Owner or User Access Administrator. Contributor is limited to the QueenZone resource group and cannot change Azure RBAC. State access is a separate container-scoped data-plane assignment, not storage-account key access.

The built-in `Reader` role does not cover `Microsoft.Web/sites/config/list/action` — Azure gates that "list" action separately even though it is read-only, since it can return app-service config content (e.g. auth settings). `azurerm_linux_web_app` needs it to read auth settings during a plan, so the plan identity also holds a minimal custom role granting exactly that one action on `Queenzone-RG` (confirmed the hard way on PR #1208's first real production plan run — see [`Bootstrap-OpenTofuState.ps1`](../../infra/bootstrap/Bootstrap-OpenTofuState.ps1)).

Both GitHub environments accept protected branches only. The apply environment requires `richardorchard` approval. The federated subjects name the environments, so an untrusted fork or pull request cannot exchange its OIDC token unless it first passes the repository environment controls. The manual [`opentofu-backend-smoke.yml`](../../.github/workflows/opentofu-backend-smoke.yml) workflow verifies each identity without declaring or importing application resources. Full plan/apply automation, drift detection, and the rest of the operational runbooks are in [`opentofu-ci-and-runbooks.md`](opentofu-ci-and-runbooks.md) (#625).

## Bootstrap and local backend migration

Prerequisites: Azure CLI and GitHub CLI authenticated as an authorised owner. Install OpenTofu with `winget install --exact --id OpenTofu.Tofu` when it is not already available.

```powershell
az account set --subscription 610e3b3a-028d-4f1b-ac1d-a5567a4f8b9d
./infra/bootstrap/Bootstrap-OpenTofuState.ps1 -WhatIf
./infra/bootstrap/Bootstrap-OpenTofuState.ps1
./infra/bootstrap/Test-OpenTofuState.ps1
```

For the later production configuration:

```powershell
tofu -chdir=infra/environments/production init `
  -backend-config=../../backend/production.backend.hcl `
  -migrate-state
```

Before confirming migration, copy any local state outside the repository and inspect `tofu state list`. Never commit a state file or pass credentials through `-backend-config`. Production state now exists and is fully populated — see [`opentofu-ci-and-runbooks.md`](opentofu-ci-and-runbooks.md#what-actually-happened-on-the-first-real-apply).

## Cloudflare token strategy

Create two Cloudflare API tokens. Restrict both to account `f93121b2086286e79a7a9fdb8d03cb4c` and zone `079fc2f37095c82fb3a2b4da65718b2b`; do not use the Global API Key.

| Token | Account permissions | Zone permissions |
| --- | --- | --- |
| Plan | Workers Scripts Read | Zone Read, DNS Read, Zone Settings Read, SSL and Certificates Read, Workers Routes Read, Zone WAF Read, Cache Rules Read |
| Apply | Workers Scripts Edit | Zone Read, DNS Edit, Zone Settings Edit, SSL and Certificates Edit, Workers Routes Edit, Zone WAF Edit, Cache Rules Edit |

Add only permissions required by an observed provider denial. Do not grant account-wide zone edit, billing, member, token-management, or unrelated Workers permissions.

The zone WAF and cache-rule permissions are required for the managed
`cloudflare_ruleset` resources. The plan token first required `Zone WAF Read`
and `Cache Rules Read` after the crawler and archive-cache rulesets were applied
on 2026-09-09; without them, provider refresh fails with `403 Forbidden` before
an otherwise safe plan can complete.

Store the values in the `Queenzone Development` Bitwarden Secrets Manager project as `CLOUDFLARE_API_TOKEN_TOFU_PLAN` and `CLOUDFLARE_API_TOKEN_TOFU_APPLY`. GitHub later retrieves them through its existing Bitwarden Secrets Manager action into the matching protected environment. OpenTofu reads `CLOUDFLARE_API_TOKEN` from the process environment; the value must never appear in HCL, `.tfvars`, workflow output, plans, or state.

Both tokens were created and wired on 2026-09-01, ahead of the first real production plan/apply on 2026-09-03. Live Worker publishes use `CLOUDFLARE_WORKER_READWRITE` (Workers Scripts Edit); do not reuse it as the OpenTofu apply token — the scopes differ and were reviewed separately when these two were created. Inventory/ops reads use the unrelated `CLOUDFLARE_API_TOKEN_READONLY`.

The bootstrap does not create the OpenTofu tokens — they were created manually per the table above. Their Bitwarden output names are wired via the `BITWARDEN_OPENTOFU_PLAN_SECRETS`/`BITWARDEN_OPENTOFU_APPLY_SECRETS` repository variables, consumed by the workflows in [`opentofu-ci-and-runbooks.md`](opentofu-ci-and-runbooks.md). Follow the rotation sequence below if either token needs replacing.

Rotation sequence:

1. Create a replacement token with the same scope and a new expiry.
2. Update the matching Bitwarden secret without printing it.
3. Run the manual backend smoke plus the relevant Cloudflare read or no-op plan.
4. Revoke the old token only after the new token succeeds.
5. Record the rotation date and owner, never the token value.

## Backup and recovery

### State backup

Before imports, moves, provider upgrades, or a high-risk apply:

```powershell
tofu -chdir=infra/environments/production state pull > $env:TEMP\queenzone-production.tfstate.backup
```

Treat the backup as a secret. Keep it outside the repository, encrypt it at rest, restrict access, and delete it after the recovery window. Azure blob versions and 30-day soft delete are the primary in-account recovery path; an operator backup covers account-level incidents.

### Restore a prior version

1. Stop all plan/apply jobs and record the current blob version ID.
2. Download the chosen earlier blob version to an encrypted local path.
3. Inspect its lineage and serial without exposing contents.
4. Use `tofu state push` only from a clean checkout at the matching commit.
5. Run a refresh-only plan, then a normal plan, before allowing applies.

Do not delete newer versions until recovery is confirmed.

### Lock recovery and force unlock

The AzureRM backend uses a blob lease for state locking. First confirm no GitHub job or operator process is active. Capture the lock ID and job evidence, then run:

```powershell
tofu -chdir=infra/environments/production force-unlock <LOCK_ID>
```

Force-unlock removes coordination only; it does not repair state. Never break the blob lease directly while an OpenTofu process may still be writing.

### Account or container deletion

The storage-account lock must be removed before account deletion. Treat lock removal as break-glass: require a reviewed change, record who removed it, restore the account/container within the soft-delete window, reapply the lock, then run the backend smoke. If the account itself is lost, restore the latest encrypted operator backup into a newly approved backend and use `tofu init -migrate-state`; do not recreate production resources.

## Credential recovery

- Local operator: reauthenticate with `az login`, then restore only the container-scoped data role.
- GitHub OIDC: add a replacement Entra application/federated credential, update the environment's non-secret `ARM_CLIENT_ID`, run the smoke, then remove the old application and assignments.
- Apply approval: restore the required reviewer and protected-branch policy before enabling the apply identity.
- Cloudflare: follow the create-test-revoke rotation sequence above.

No backend credential, token, storage key, state content, or application resource belongs in this repository.
