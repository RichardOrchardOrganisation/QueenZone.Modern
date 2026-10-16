# Agent release protection proposal â€” pending owner approval

## Verified repository state

Read-only API checks on 14 October 2026 found: public repository, organization plan Free; no repository/inherited rulesets returned; protected main with 16 required GitHub Actions checks, admin enforcement, no force push or deletion, PR requirement enabled but zero approving reviews, no required code-owner review, no stale-review dismissal and no last-push approval. Status checks are not strict. A merge queue is configured with ALLGREEN, minimum one and maximum three entries. The inspected API does not expose a separate requiresMergeQueue boolean; keep the existing queue policy and verify its UI rule before any settings update. No CODEOWNERS file was found in the checkout.

The existing authenticated account has repository admin permission. An agent using that account cannot be independently constrained against Richard by CODEOWNERS or a rule Richard can edit. Independent review also cannot be supplied by the same account that authored a PR.

## Minimal recommended configuration

1. Retain all existing protected-main checks and the merge queue. Add Scripts unit tests (GitHub Actions check) to required checks so release-package and pruning regressions cannot be skipped by merge. No bypass for ordinary agent PRs or releases.
2. Add a narrow CODEOWNERS file covering /.github/, /scripts/release/, scripts invoked by the production workflow (including classification, smoke, Kudu, and build/test policy), /infra/, dependency/build configuration that can change those programs, and CODEOWNERS itself. Use Richard or a separately authorized trusted reviewer as owner. Enable required code-owner review with zero generic required approvals, retaining PR-required and admin-enforced rules; enable stale-review dismissal. Intended outcome: ordinary application PRs keep automated checks/queue, while workflow/security changes need review at change time, not on every production deployment. Validate this distinction with controlled owned/unowned test PRs after approval before removing release reviewers. Include transitive executable dependencies in the owned set; a narrow YAML-only list leaves bypasses.
3. Add a v* tag ruleset that prohibits updates and deletions with no agent bypass. Separately restrict new release-tag creation to the chosen release actor. A creation-only bypass must not bypass the immutability ruleset. Existing tags are never moved. Tag creation restrictions alone do not constrain which commit is tagged.
4. Only after the repaired workflow and change-time controls are verified, remove required_reviewers from prod-release-gated and prod-deploy-gated. Preserve environment names, v* tag-only policies, variables, issuer/audience/subject, credentials and Azure scopes. Leave prod-gate-override owner-reviewed and unchanged; routine agents cannot bypass failing proofs. Keep prod-data-read, store publishing and OpenTofu protections untouched.
5. To make review separation meaningful, ordinary agents must use a distinct non-admin actor; release-tag authority should be narrowly assigned rather than using Richard's admin session. This is a separately approved identity/credential change, NOT part of this patch. No new identity or token has been created.

These are proposed settings, not executed updates. CODEOWNERS content is not installed by the implementation patch, so no claim of enforced owner review is made. The implementation leaves all current approvals and identity restrictions in place.

## What this does and does not enforce

The automated preflight enforces exact release proofs, package identity and ordering for the reviewed workflow. Code-owner protection can enforce review of changes on protected main. Neither prevents an authorized tag writer from pointing v* at an arbitrary commit whose workflow uses the allowed environment. Existing OIDC trust is environment-scoped, not proof-scoped. A modified tagged workflow can omit YAML gates. The patch does not claim tamper resistance against that actor, repository administrators, credential compromise, or arbitrary code already accepted into a trusted build.

The minimum no-click configuration therefore requires explicitly trusting the release actor to tag only reviewed main commits containing the gated workflow. An ancestry check inside that workflow is useful but is not an independent security boundary. If Richard instead requires enforcement even against a malicious or compromised release actor, reviewer removal must wait for an external trusted enforcement boundary owned with #2291. GitHub custom deployment-protection Apps can independently verify trusted workflow provenance, allowed ancestry, exact/latest/full/fresh proof artifacts and the prepared package before automatic approval. They require separately hosted trusted code, webhook verification, Actions read, Deployments write and suitable repository read access. Repository policy currently says not to add a GitHub App protection rule: this is a capability assessment only, not an authorized implementation recommendation. A trusted reusable-workflow/identity design is another larger option requiring separately reviewed OIDC and credential changes. Neither has been installed or prototyped here.

## Supported features and sources

This is a public organization repository on Free, so repository branch/tag rulesets and CODEOWNERS are supported; do not recommend private/internal push-path restrictions or enterprise-only organization policies as if available here. Repository rulesets can separate tag creation from immutability. [GitHub rulesets documentation](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/about-rulesets).

CODEOWNERS must itself be protected, and approval by any listed owner is sufficient. An owner needs write access. [GitHub CODEOWNERS documentation](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners).

Merge queues are available for public organization repositories, and required checks need merge_group triggers. This repository already has merge_group triggers and a queue. [GitHub merge queue documentation](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/managing-a-merge-queue).

Custom deployment-protection rules are available for public repositories on all plans but remain a preview feature. They are not a drop-in change to the current workflow or authorized security scope. [GitHub custom protection documentation](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/create-custom-protection-rules).

### Proposed owned-path seed (review before installation)

```text
/.github/ @richardorchard
/scripts/ @richardorchard
/infra/ @richardorchard
/.config/ @richardorchard
/global.json @richardorchard
/NuGet.config @richardorchard
/nuget.config @richardorchard
*.csproj @richardorchard
*.props @richardorchard
*.targets @richardorchard
```

This deliberately protects every workflow and script, including this verifier, proof writers, cleanup and tests, plus build entry points. It does not use a global owner for ordinary application source. A separate agent author is necessary if Richard is the reviewer. Owners and the exact dependency set need owner acceptance; this seed is documentation only.
