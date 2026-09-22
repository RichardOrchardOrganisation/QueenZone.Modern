# OpenTofu contributor runbook

Issue: [#619](https://github.com/richardorchard/QueenZone.Modern/issues/619), step 3 of epic [#615](https://github.com/richardorchard/QueenZone.Modern/issues/615).

## Foundation boundary

The separate dev root and first-use setup are described in the [dev provisioning runbook](opentofu-dev-environment.md).

The production root is [`infra/environments/production/`](../../infra/environments/production/). It pins OpenTofu **1.12.5**, AzureRM **5.0.1**, and Cloudflare **5.23.0**. The dependency lock file contains official checksums for Linux AMD64, Windows AMD64, macOS AMD64, and macOS ARM64.

Issue #619 declared no managed resources. The current adoption state is:

- `azure-web`: #622 declares the resource group, web, telemetry, hostname, TLS binding, and ingress resources with declarative imports;
- `azure-data`: #628 owns SQL, Storage, containers, and data-protection configuration;
- `cloudflare-edge`: #626 declares the zone, DNS records, TLS/security/cache zone settings, both Worker scripts, and both routes with declarative imports.

Uploaded App Service certificates are the deliberate exception in #622. Their
private PFX material and renewal path are outside state; the imported hostname
bindings preserve the live SNI state and certificate thumbprints.

## Local validation

Install the version in `.opentofu-version`, then run:

```powershell
./scripts/Test-OpenTofu.ps1
./scripts/Test-OpenTofu.ps1 -UseRemoteBackend
```

The default uses `-backend=false` and needs no cloud credentials. The remote check uses the Entra-authenticated backend from #616. Cloudflare credentials are not required until a configuration reads Cloudflare data or plans managed resources.

The validation script runs formatting, credential-pattern checks, lifecycle checks, `init`, and `validate` for both production and dev roots and their modules. Critical Azure/Cloudflare resources fail validation unless they set `lifecycle { prevent_destroy = true }`; broad `ignore_changes = all` is forbidden.

## Provider upgrades

Provider versions are exact, not floating. Upgrade one provider at a time in a separate PR:

1. Read the official provider changelog and migration guide.
2. Change the exact version in the production root and matching modules.
3. Run `tofu init -upgrade` in the production root.
4. Refresh cross-platform checksums:

```powershell
tofu -chdir=infra/environments/production providers lock `
  -platform=linux_amd64 `
  -platform=windows_amd64 `
  -platform=darwin_amd64 `
  -platform=darwin_arm64
```

5. Run local validation, then a refresh-only production plan with trusted credentials.
6. Review state-upgrade and replacement risk before applying.

Never hand-edit `.terraform.lock.hcl`.

## Planning

Use the committed backend configuration and environment-provided credentials:

```powershell
tofu -chdir=infra/environments/production init `
  -reconfigure `
  -backend-config=../../backend/production.backend.hcl
tofu -chdir=infra/environments/production plan -out=$env:TEMP\queenzone-production.tfplan
tofu -chdir=infra/environments/production show $env:TEMP\queenzone-production.tfplan
```

Plan files and state are sensitive and must remain outside the repository. Do not apply a plan containing an unexplained delete or replacement.

A pull request touching `infra/**` runs this same plan automatically (read-only, posted as a PR comment) via [`opentofu-plan.yml`](../../.github/workflows/opentofu-plan.yml); merging to `main` runs an approval-gated apply via [`opentofu-apply.yml`](../../.github/workflows/opentofu-apply.yml). See [`opentofu-ci-and-runbooks.md`](opentofu-ci-and-runbooks.md) for how those fit together, drift detection, and rollback guidance.

## First import of a resource

1. Re-probe the live resource immediately before declaring it.
2. Match the live name, location, SKU, capacity, access controls, and provider defaults.
3. Add `prevent_destroy = true` for every critical resource.
4. Commit the resource block before import so its address is stable.
5. Back up state using the #616 runbook.
6. Import the exact existing ID. Never use `-allow-missing-config`.
7. Run `plan -refresh-only`, followed by a normal plan.
8. Stop if the normal plan proposes creation, replacement, or an unexplained update.

Import commands belong in the relevant issue/PR evidence, not reusable scripts containing mutable IDs or credentials.

## State moves and removals

Prefer committed `moved` blocks. Use `tofu state mv` only when a declarative move is impossible, after a state backup and with all applies stopped. `tofu state rm` removes management without deleting the live resource, but it can cause a later plan to recreate it; require explicit review and document the intended ownership change. A committed `removed` block with `lifecycle { destroy = false }` is the reviewable form of that state-only removal when the resource block itself is gone. It plans a forget, not a delete. A resource block that remains, with `count` dropping to zero, can set literal `lifecycle { destroy = false }` beside `prevent_destroy` for the same forget. Do not use either form to hide a destroy of SQL, Storage, or App Service.

Lock recovery, backend restoration, credential rotation, and force-unlock controls remain in [`opentofu-state-and-identity.md`](opentofu-state-and-identity.md).

## Destroy safety for live data

OpenTofu does **not** automatically refuse to delete databases, storage
accounts, or other live-data resources. A resource removed from configuration,
or a `tofu destroy`, will delete the corresponding Azure object unless
protection is opted in.

This stack opts in. Irreplaceable production resources must set
`lifecycle { prevent_destroy = true }`. `scripts/Test-OpenTofuSafety.ps1`
fails if those resource types omit it. A production apply that would destroy or
replace a protected resource fails instead of deleting it.

`prevent_destroy` blocks **destroy and replacement** only. It does **not**
block:

- in-place updates (SKU, backup retention, container ACLs, soft-delete days)
- Azure Portal / `az` deletion outside OpenTofu
- application, EF, or operator writes to rows and blobs
- a later PR that removes the lifecycle flag

OpenTofu also does not manage SQL schema, table data, or blob objects. Removing
a *container* or *database* resource from the module is still a destroy of that
Azure object and every object inside it; `prevent_destroy` is what stops that
apply.

Never run `tofu destroy` against production. Do not apply a plan that deletes or
replaces SQL, Storage, App Service, hostname bindings, or the `cdn2` Worker.
To stop managing a resource without deleting it, use `tofu state rm` after
review (see [State moves and removals](#state-moves-and-removals)).

Resources that must never be recreated are listed in
[`opentofu-inventory.md`](opentofu-inventory.md).

## Production safeguards

- AzureRM cannot auto-register resource providers; registration remains an operator/platform concern.
- Azure resource-group deletion fails while the group contains resources.
- Production variables pin the existing resource group, region, B1 SKU, single worker, Cloudflare zone, and `cdn2` Worker route.
- Secret values come only from environment/Bitwarden flows. They never belong in HCL, `.tfvars`, plans, outputs, or state.
- Application deployment and EF migrations stay in the existing workflows. OpenTofu changes the infrastructure control plane only.
