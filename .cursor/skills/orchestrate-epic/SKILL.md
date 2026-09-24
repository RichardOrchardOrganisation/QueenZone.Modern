---
name: orchestrate-epic
description: Cursor Custom Mode only. QueenZone overlay for the issue-queue protocol. Use in Cursor after pinning /orchestrate-epic. Do not auto-apply in Grok or other non-Cursor agents; they implement in-chat per AGENTS.md.
disable-model-invocation: true
icon: git-branch
color: purple
---

# Orchestrate issues (QueenZone)

You are the parent coordinator. Do not implement issues yourself.

This skill runs **only** in Cursor with `/orchestrate-epic` pinned. Grok, Claude, Codex, and Composer must not load it because the user listed issue numbers; they implement as a single agent per `AGENTS.md`.

The portable protocol is the **issue-queue** Cursor plugin (`/orchestrate-issues`). This skill is the QueenZone pin: overlay plus a local copy of the loop so a clone of this repo works without the plugin. **Do not also pin `/orchestrate-issues` in this chat.**

Subagents: `.cursor/agents/planner.md`, `implementer.md`, `verifier.md`, `reviewer.md`. Tell every child to read `AGENTS.md`.

Parent model: **Grok 4.6** at **high** (xhigh only if the split is messy). Leave child frontmatter models in place.

## QueenZone overlay

- Issues live in `RichardOrchardOrganisation/QueenZone.Modern`.
- Agent slug is `cursor/` unless the user named another (`grok/`, `claude/`, …). Never push to `main`.
- One PR per issue unless the user asked to batch. `Closes #<n>`; `Relates to #<epic>` when there is a parent. Fill `.github/pull_request_template.md`.
- Do **not** isolate git worktrees. `.cursor/worktrees.json` runs `dotnet restore QueenZone.sln` and dominates wall-clock.
- Do not mix website UI and mobile UI in one implementer unless that single issue requires both. An API issue may still add a client call if the issue says so.
- Pause for humans: Bitwarden, Apple/Google credentials, TestFlight/device checks, product questions.

| Surface | Typical paths | Tests to name in child prompts |
| --- | --- | --- |
| Website | `src/QueenZone.Web`, Razor Pages | `dotnet test` on the touched test project |
| API / data | `src/QueenZone.Web/Api`, `src/QueenZone.Data` | Web.Tests + coverage for changed `.cs` |
| Mobile | `src/QueenZone.Mobile` (Expo, not in `QueenZone.sln`) | `npm run preflight` in that tree (typecheck + `npm test` + Expo Doctor). Doctor is a CI gate; `tsc` + Jest alone will miss SDK package drift. |

Also enforce: SQL only in `QueenZone.Data`; visitor/admin pages as Razor Pages; no Expo Go as a supported runtime. If surface is mobile, tell the implementer to read `src/QueenZone.Mobile/README.md`.

## Loop

Same as issue-queue. Sequential only. Scoreboard in this chat; drop child logs.

1. **One issue** → skip planner. Two or more and order unclear → spawn planner once (issue numbers, not full bodies).
2. Next unblocked item. One issue, one subagent at a time.
3. **Implementer** (no PR). Wait.
4. **Verifier**. Wait. Fail → one retry implementer, then pause.
5. **Reviewer** once.
   - Approve / nits → PR (nits in Follow-up).
   - Request changes → one implementer response (blocking list verbatim) → verifier once → **do not review again**. Verifier pass → PR. Verifier fail → pause.
6. `gh pr create` after merging `origin/main` into the branch.
7. Repeat until the queue is empty or paused.

A single issue is a valid queue (`work on #757`). Fetch each issue body when its turn starts.

## Child prompts

Implementer: issue, surface, paths, acceptance criteria, **named tests from the overlay table**, agent slug, no sibling issues, commit+push, no PR, no worktree.

Review response: reviewer's blocking items verbatim, one pass, no new scope.

Verifier: same named tests, no product edits, no PR, no worktree.

Reviewer: single-pass, diff vs `origin/main`, no full test suite, no PR.
