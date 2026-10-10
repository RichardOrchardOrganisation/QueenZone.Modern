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
- `effect` defaults to `Audit`. Switch to Deny only after the Audit compliance
  check below reports 0 non-compliant containers in both RGs (`tofu test` is
  mocked and proves nothing about live state). Then set
  `storage_public_access_policy_effect = "Deny"` and apply.
- `location` is required. Roots pass their resource group's location; it only
  decides where the assignment's system-assigned identity lives. That identity
  has no role assignments and matters only for Modify/DeployIfNotExists.
- Accounts a root doesn't manage (an `azure-data` instance with
  `manage_storage_account = false` reports a null account) are skipped.

## Limits

- Deny is enforced only for Azure Resource Manager requests. A container ACL
  set through the storage data-plane API (Set Container ACL with a shared key or
  SAS) bypasses Deny. Audit compliance still reports it on the next evaluation,
  and the drift check catches it. No app code does this today.
- Resource Policy Contributor on the RG (held by OpenTofu Apply) can create
  policy exemptions and delete this assignment. That's accepted because Apply
  already has Contributor there and runs only behind the reviewed
  `opentofu-apply` gate. Review any `Microsoft.Authorization/policyExemptions`
  in these RGs.

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

The RBAC Administrator condition follows Microsoft's "Constrain roles and
principals" template. It limits both the role (Resource Policy Contributor) and
the principal (the Apply identity only), for write and for delete. On rerun the
script compares the existing condition. If it differs, the script deletes and
recreates the assignment (`-WhatIf` shows that) and fails if the read-back
doesn't match.

Run the bootstrap once as an Owner (Richard) before the first apply:
`pwsh infra/bootstrap/Bootstrap-OpenTofuState.ps1 -WhatIf`, then without `-WhatIf`.

**First-apply 403s:** new role assignments can take several minutes (up to
about 10) to take effect. If the first apply fails creating the policy
definition or assignment with `AuthorizationFailed` / 403 right after the
bootstrap or the in-root role assignment, wait 10 minutes and re-run
`opentofu-apply.yml` for that root. The apply is idempotent.

Check Audit compliance after apply (required before Deny):

```sh
az policy state summarize --resource-group Queenzone-RG \
  --filter "policyAssignmentName eq 'qz-blob-public-production'"
az policy state trigger-scan --resource-group Queenzone-RG   # optional, forces evaluation
az policy state list --resource-group Queenzone-RG \
  --filter "policyAssignmentName eq 'qz-blob-public-production' and complianceState eq 'NonCompliant'" \
  --query "[].resourceId"
# repeat for Queenzone-Dev-RG / qz-blob-public-dev
```
