# Sanitised OpenTofu import inventory

Read-only audit artefacts for [#624](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/624).

| File | Contents |
| --- | --- |
| `azure-resources.json` | Azure resource IDs, SKUs, treatments, never-recreate flags |
| `storage-containers.json` | Container public-access flags and related issues |
| `cloudflare-hostnames.json` | Zone/account IDs, DNS, SSL, Worker route, rules treatment |
| `workers/pictures-queenzone-org.js` | Live Worker source snapshot for hostname `cdn2.queenzone.org` (script name only; no secrets) |
| `workers/pictures-legacy-redirect.js` | Legacy `pictures.queenzone.org` compatibility Worker source snapshot (no secrets) |
| `ownership-matrix.csv` | Compact treatment matrix (import / data / outside / defer) |
| `github-bitwarden.json` | Environment/secret/setting *names* only; `appServiceSettingNames` is also the required-name list checked nightly by `scripts/Test-AppServiceSettingNames.ps1` (ADR 0008) |

Narrative ownership matrix: [`docs/architecture/opentofu-inventory.md`](../../docs/architecture/opentofu-inventory.md).

No secret values, connection strings, publish profiles, or tokens are stored here.
