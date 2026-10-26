# Production SCM rule `temp-veronica`

The Canada East App Service `queenzone-prod` keeps SCM (Kudu / Advanced
tool site) on its own restriction list: `scm_use_main_ip_restriction = false`
and SCM default Deny. The main site's Cloudflare allow rules are unchanged.

`temp-veronica` is one named SCM `/32` for Richard's own IP. Its resting
state is **Deny**. He flips only that rule's action to **Allow** in the Azure
portal while he needs log stream or Kudu (Veronica telemetry), then flips it
back to Deny.

The IPv4 address is the non-secret repository variable `QZ_SCM_DENY_IP` (bare
address, no `/32`). Production OpenTofu requires `TF_VAR_scm_deny_ip` /
`var.scm_deny_ip` as a non-blank IPv4; a missing or whitespace-only value
fails closed. Do not commit the address, add it to Bitwarden, or put it in
the `BITWARDEN_OPENTOFU_*` secret lists.

## Flip to Allow (short window)

1. Azure portal → App Service `queenzone-prod` → **Networking** → **Access
   restrictions** → **Advanced tool site** (SCM / Kudu).
2. Find the rule named `temp-veronica` (priority 100). Leave the IP, name,
   priority, and every other rule alone.
3. Change only its action to **Allow**. Save.
4. Use log stream or Kudu from that IP. SCM still requires publish-profile or
   Azure CLI authentication; this rule only opens the network path.

## Flip back to Deny

Change the same rule's action back to **Deny** as soon as the window ends,
including on errors or cancelled sessions. Re-read the SCM list and confirm
`temp-veronica` is Deny, priority 100, SCM still independent of the main
site, and the Cloudflare main-site rules are untouched.

The Azure CLI equivalent (no upsert, no delete/recreate) is in
[`docs/agent-handoff-cheatsheet.md`](../agent-handoff-cheatsheet.md#temporary-veronica-access-to-production-scm).

## Drift while it is Allow

The nightly [`opentofu-drift.yml`](../../.github/workflows/opentofu-drift.yml)
plan then wants to set `temp-veronica.action` back to Deny. When live is
Allow, config is Deny, and **that action field on this named rule is the
only change**, the workflow records the expected operator window and does
**not** open or comment on an `opentofu-drift` issue. A Deny-to-Allow
toggle, or any other SCM restriction change (name, IP, priority, extra
`github-deploy-*` leftover, default action, inheritance), still opens an
issue.

An OpenTofu apply during an Allow window restores Deny. Do not apply
production infra while the diagnostic window must stay open.

## Deploy interaction

`deploy.yml` adds `github-deploy-<run>-<attempt>` as a separate SCM **Allow**
`/32` for the current GitHub runner at priority 100, then removes it by
**name**. The names do not collide (`temp-veronica` vs `github-deploy-*`).
Azure matches each rule by address, so the resting Deny on Richard's IP does
not block the runner, and the runner Allow does not open Richard's IP.
OpenTofu still treats the SCM list as authoritative: an apply that runs
while a deploy is in progress removes the runner's live `github-deploy-*`
Allow and can 403 that deploy. That race existed before this rule. Do not
apply production infra during a zip deploy.
