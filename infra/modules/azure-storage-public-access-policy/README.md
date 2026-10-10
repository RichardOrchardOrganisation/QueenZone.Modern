# azure-storage-public-access-policy

Azure Policy guard for #2211 (Ship B). The storage accounts keep
`allowBlobPublicAccess = true` because Cloudflare `cdn`/`cdn2` read the origin
anonymously. This module makes any *other* public container loud (Audit) or
impossible (Deny) at the ARM plane, which covers portal/CLI drift like #2209.

- One custom definition per root (`qz-blob-container-public-access-<suffix>`,
  subscription scope) and one assignment per resource group passed in
  `resource_group_ids`.
- Rule: `Microsoft.Storage/storageAccounts/blobServices/containers` with
  `publicAccess` present and not `None`, and `fullName` (`<account>/default/<container>`)
  not in `allowedContainers`, triggers the effect.
- `allowed_public_containers` is a per-account map, so a name allowed on one
  account is not allowed on another. Roots feed it from the
  `public_blob_containers` outputs of `azure-data` and `azure-mobile-builds`.
- `effect` defaults to `Audit`. Once the compliance state is clean, set the
  root variable `storage_public_access_policy_effect = "Deny"` and apply.

Scope: `Queenzone-RG` (queenzoneprod and queenzonemobilebuilds, production
root) and `Queenzone-Dev-RG` (queenzonedev, dev root).

## Permissions (one-off bootstrap by Richard)

The OpenTofu Apply identity (`QueenZone OpenTofu Apply`, SP object
`e5e5ea3b-2a6e-4b62-abb8-947e5e66378c`) has only **Contributor** on
Queenzone-RG and Queenzone-Dev-RG. Contributor excludes
`Microsoft.Authorization/*/write`, so on its own it cannot create policy
definitions, policy assignments or role assignments.

- **Policy definition:** custom definitions can only exist at subscription or
  management-group scope, never at RG scope. The minimum extra right is
  `Microsoft.Authorization/policyDefinitions/{read,write,delete}` at the
  subscription. `infra/bootstrap/Bootstrap-OpenTofuState.ps1` grants it through the
  custom role `QueenZone OpenTofu Apply - Policy Definition Writer`. The Plan
  identity gets `... Plan - Policy Definition Reader` so it can refresh.
- **Policy assignment:** the env roots create `Resource Policy Contributor`
  for Apply on their own RG only (`azurerm_role_assignment.opentofu_apply_resource_policy_contributor`).
  To let OpenTofu create that role assignment, the bootstrap grants Apply
  `Role Based Access Control Administrator` on each RG, with an ABAC condition
  that only allows assigning or removing Resource Policy Contributor.

Run the bootstrap once as an Owner (Richard) before the first apply:
`pwsh infra/bootstrap/Bootstrap-OpenTofuState.ps1` (use `-WhatIf` first).

Check compliance after apply:

```sh
az policy state summarize --resource-group Queenzone-RG \
  --filter "policyAssignmentName eq 'qz-blob-public-production'"
```
