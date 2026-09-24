# GitHub organization transfer: Azure OIDC identities

Azure OIDC cutover for [#1736](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1736) under [#1733](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1733). The five destination-owner credentials were staged before the transfer on 24 September 2026 and verified afterward. This page records no token, secret, or full client ID. Existing old-owner credentials remain for rollback.

GitHub's [OIDC reference](https://docs.github.com/en/actions/reference/security/oidc) says a repository transferred after 15 July 2026 uses an immutable `sub` with owner and repository IDs. Before transfer, the repository OIDC API reported `use_default: true`, `use_immutable_subject: false`, and prefix `repo:richardorchard/QueenZone.Modern`. After transfer it reports `use_default: true` and `use_immutable_subject: true`. The public GitHub IDs are organization `333232587` and repository `1265145026`. The **observed** post-transfer prefix is:

```text
repo:RichardOrchardOrganisation@333232587/QueenZone.Modern@1265145026
```

Append `:environment:<environment-name>` exactly. The [OIDC identity smoke run](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/35993948845) on `main` passed all five environments after required reviewer approval for `dev-data-refresh` and `opentofu-apply`. Each logged `sub` matched the staged credential exactly, `aud` was `api://AzureADTokenExchange`, Azure login succeeded, and a read-only account check passed. The workflow did not update settings, databases, or resources.

## Identity matrix

All listed Entra credentials use issuer `https://token.actions.githubusercontent.com` and audience `api://AzureADTokenExchange`. Client IDs are read from each environment's `ARM_CLIENT_ID` variable; the matrix does not copy their values. The verified subject for each row is the prefix above plus the shown environment suffix.

| GitHub environment | Entra app | Existing matching credential | Staged destination-owner credential | Verified FIC count | Post-transfer result |
| --- | --- | --- | --- | --- | --- |
| `dev-deploy` | QueenZone Dev Deploy | `github-dev-deploy` (old owner) | `github-org-dev-deploy` | 4, shared with `dev-data-refresh` | OIDC and Azure login passed; next normal dev deploy |
| `dev-data-refresh` | QueenZone Dev Deploy | `github-dev-data-refresh` (old owner) | `github-org-dev-data-refresh` | 4, shared with `dev-deploy` | OIDC and Azure login passed; next normal snapshot run |
| `prod-deploy` | QueenZone Deploy | `github-prod-deploy` (old owner) | `github-org-prod-deploy` | 5 | OIDC and Azure login passed; read-only app-setting check passed (run `35994854205`) |
| `opentofu-plan` | QueenZone OpenTofu Plan | `github-opentofu-plan` (old owner) | `github-org-opentofu-plan` | 2 | OIDC and Azure login passed; local backend/state read passed |
| `opentofu-apply` | QueenZone OpenTofu Apply | `github-opentofu-apply` (old owner) | `github-org-opentofu-apply` | 2 | OIDC and Azure login passed with reviewer; no apply was run |

The QueenZone Deploy app also has old-owner credentials for `prod-release`, `prod-data-read`, and `prod-google-play`. Those three environments do not call `azure/login` in the current workflows; keep their credentials untouched during cutover and investigate their later retirement separately. All apps remain below [Entra's 20-credential limit](https://learn.microsoft.com/en-us/graph/api/resources/federatedidentitycredentials-overview). On 24 September 2026, the five destination-owner credentials above were created and read back from Entra. Each listed credential has the expected exact subject, issuer, and audience; all five old-owner credentials remain present. No Azure role assignments were changed. The post-transfer GitHub OIDC smoke and Azure login checks passed on 24 September 2026. Retain all old credentials until new-owner login succeeds and rollback is no longer needed.

## Script and cutover contract

`Bootstrap-DeployIdentity.ps1`, `Bootstrap-OpenTofuState.ps1`, and `Test-OpenTofuState.ps1` now default to the organization owner, immutable IDs, and staged credential names. For any new owner, supply **both** `-OidcOwnerId` and `-OidcRepositoryId`; the helper rejects a different owner without IDs rather than creating a name-only credential. To inspect an old-owner credential during rollback, explicitly pass the old repository, clear both ID parameters, and select its old credential name. The bootstrap scripts manage more than credentials (Azure roles, state resources and GitHub environments); do not use them solely for identity checks. Their `-WhatIf` mode prints the subject repository segment for a safe dry run.

The read-only OpenTofu state check passed after transfer with the new defaults:

```powershell
./infra/bootstrap/Test-OpenTofuState.ps1
```

Keep old-owner credentials until the post-transfer dev deployment succeeds and rollback is no longer needed; retire them in a separate reviewed cleanup. Do not run production migration, tag promotion, store publishing, or OpenTofu apply solely to prove authentication.
