# GitHub organization transfer: Azure OIDC identities

Pre-transfer preparation for [#1736](https://github.com/richardorchard/QueenZone.Modern/issues/1736) under [#1733](https://github.com/richardorchard/QueenZone.Modern/issues/1733). Inventory read on 24 September 2026. This page records no token, secret, or full client ID. Do not change Azure roles or remove old-owner federated credentials during preparation.

GitHub's [OIDC reference](https://docs.github.com/en/actions/reference/security/oidc) says a repository transferred after 15 July 2026 uses an immutable `sub` with owner and repository IDs. The current repository OIDC API reports `use_default: true`, `use_immutable_subject: false`, and prefix `repo:richardorchard/QueenZone.Modern`. The destination organization has no custom subject template in its OIDC settings. The public GitHub IDs are organization `333232587` and repository `1265145026`. Therefore the **expected**, still unobserved post-transfer prefix is:

```text
repo:RichardOrchardOrganisation@333232587/QueenZone.Modern@1265145026
```

Append `:environment:<environment-name>` exactly. GitHub's actual post-transfer claim remains authoritative; the manual `OIDC identity smoke` workflow prints only the decoded `sub` and `aud` claims, never the bearer token, then performs an Azure login and read-only account check. Run it from `main` after transfer, inspect each environment's claim, and reconcile Entra credentials before relying on deploy or OpenTofu jobs. The workflow itself does not update settings, databases, or resources. The `opentofu-apply` environment still requires its existing reviewer approval even for this read-only smoke.

## Identity matrix

All listed Entra credentials use issuer `https://token.actions.githubusercontent.com` and audience `api://AzureADTokenExchange`. Client IDs are read from each environment's `ARM_CLIENT_ID` variable; the matrix does not copy their values. The expected new subject for each row is the prefix above plus the shown environment suffix.

| GitHub environment | Entra app | Existing matching credential | Planned new credential | Existing FIC count / expected after stage | Verification |
| --- | --- | --- | --- | --- | --- |
| `dev-deploy` | QueenZone Dev Deploy | `github-dev-deploy` (old owner) | `github-org-dev-deploy` | 2 / 4, shared with `dev-data-refresh` | Post-transfer OIDC smoke; next normal dev deploy |
| `dev-data-refresh` | QueenZone Dev Deploy | `github-dev-data-refresh` (old owner) | `github-org-dev-data-refresh` | 2 / 4, shared with `dev-deploy` | Post-transfer OIDC smoke; next normal snapshot run |
| `prod-deploy` | QueenZone Deploy | `github-prod-deploy` (old owner) | `github-org-prod-deploy` | 4 / 5 | Post-transfer OIDC smoke; `app-service-setting-names-check.yml` read-only run |
| `opentofu-plan` | QueenZone OpenTofu Plan | `github-opentofu-plan` (old owner) | `github-org-opentofu-plan` | 1 / 2 | Post-transfer OIDC smoke; normal OpenTofu plan/backend smoke |
| `opentofu-apply` | QueenZone OpenTofu Apply | `github-opentofu-apply` (old owner) | `github-org-opentofu-apply` | 1 / 2 | Post-transfer OIDC smoke with reviewer; no apply as an identity test |

The QueenZone Deploy app also has old-owner credentials for `prod-release`, `prod-data-read`, and `prod-google-play`. Those three environments do not call `azure/login` in the current workflows; keep their credentials untouched during cutover and investigate their later retirement separately. All apps remain below [Entra's 20-credential limit](https://learn.microsoft.com/en-us/graph/api/resources/federatedidentitycredentials-overview). The planned new credentials have **not** been created as of this inventory. Stage only the five exact subject, issuer, and audience triples above after authorization, verify them by listing credentials, and retain all old credentials until new-owner login succeeds and rollback is no longer needed.

## Script and cutover contract

`Bootstrap-DeployIdentity.ps1`, `Bootstrap-OpenTofuState.ps1`, and `Test-OpenTofuState.ps1` retain their old-owner defaults for pre-transfer recovery. When using a different `-GitHubRepository`, supply **both** `-OidcOwnerId` and `-OidcRepositoryId`; the scripts reject a new owner without IDs rather than creating a name-only credential. Bootstrap scripts also accept distinct federated credential names so they cannot overwrite an old-owner entry. The bootstrap scripts manage more than credentials (Azure roles, state resources and GitHub environments); **do not use them solely to stage these five FICs**. Their `-WhatIf` mode prints the subject repository segment for a safe dry run.

After transfer, a read-only OpenTofu state check can use:

```powershell
./infra/bootstrap/Test-OpenTofuState.ps1 `
  -GitHubRepository 'RichardOrchardOrganisation/QueenZone.Modern' `
  -OidcOwnerId '333232587' -OidcRepositoryId '1265145026' `
  -PlanFederatedCredentialName 'github-org-opentofu-plan' `
  -ApplyFederatedCredentialName 'github-org-opentofu-apply'
```

Change the scripts' default owner only after the transfer and new OIDC claims have been verified. Keep old-owner credentials until the dev deployment and read-only production/OpenTofu checks succeed; retire them in a separate reviewed cleanup. Do not run production migration, tag promotion, store publishing, or OpenTofu apply to prove authentication.
