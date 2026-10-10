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

The apply identity needs `Microsoft.Authorization/policyDefinitions/write` on
the subscription and `policyAssignments/write` on the resource group (Resource
Policy Contributor or equivalent).

Check compliance after apply:

```sh
az policy state summarize --resource-group Queenzone-RG \
  --filter "policyAssignmentName eq 'qz-blob-public-production'"
```
