# Low-Interruption Native Continue Design

Date: 2026-04-19
Status: approved in-session for spec handoff

## Goal

Add a user-facing `少打断执行` action to WebCode, mobile web, and Feishu so the user can ask the current CLI tool to continue in its own native low-interruption mode.

The action must preserve one core rule:

- WebCode does not become a second task manager or loop engine

`plan`, `todo_list`, `backlog`, stop conditions, and progress semantics remain owned by the CLI tool itself.

## Problem

Users want a one-click way to tell the active coding agent to keep working with fewer interruptions after it has already produced a plan or visible unfinished work.

WebCode currently has no unified continuation affordance on assistant replies across:

- web
- mobile web
- Feishu streaming cards

At the same time, WebCode already parses and renders CLI-emitted task artifacts such as `todo_list`, but that rendering is only a mirror of CLI output. If WebCode starts managing unfinished work itself, it creates a second source of truth that can drift from the native CLI session.

## Non-Goals

This design explicitly does not do the following:

- WebCode does not own or merge `plan`, `todo_list`, or `backlog`
- WebCode does not maintain a cross-turn loop state machine
- WebCode does not rephrase or expand the CLI's intent with continuation prompts
- WebCode does not synthesize new task items from historical messages
- WebCode does not impose its own stop condition beyond process completion

## Source Of Truth

The CLI tool remains the only authority for:

- whether there is unfinished work
- what the current `plan` is
- what the current `todo_list` is
- what counts as `backlog`
- when the current run is blocked
- when the current run is complete

WebCode may inspect current CLI output to decide whether a continuation affordance should be shown, but that inspection is only presentation logic. It is not task-state ownership.

## Product Semantics

`少打断执行` means:

- start one new native continue or resume run for the active CLI tool
- do not add a continuation prompt
- do not wrap the CLI in a WebCode-managed loop
- let the CLI process run until the CLI itself exits

It does not mean:

- "WebCode keeps re-calling the tool until all inferred tasks are done"
- "WebCode takes over backlog management"

## Eligibility Rules

The action is shown only on assistant replies.

The action is eligible only when the latest visible CLI output for that reply indicates unfinished work. Detection priority is:

1. structured CLI task artifacts such as `todo_list`
2. CLI-emitted `plan` or `backlog` text in the current reply content

WebCode does not aggregate unfinished work across older replies once newer CLI output exists.

To avoid multiple competing continue entry points in one session:

- only the latest completed assistant reply in the current session may expose an active `少打断执行` action
- older replies become historical output only

## UX Design

### Shared Interaction

Across all surfaces:

- the button label is `少打断执行`
- clicking it creates a new assistant reply, not an in-place continuation of the old reply
- if the current session already has a CLI process running, the button is disabled

WebCode does not present a custom loop lifecycle such as `paused_waiting_user` or `completed`. The UI follows the normal running and completion behavior already used for CLI process streaming.

### Web

On desktop web:

- place `少打断执行` at the bottom of the latest eligible assistant message card
- when clicked, append a new assistant reply and stream the new native continue run there
- do not mutate the prior completed assistant reply beyond removing or disabling the action

### Mobile Web

On mobile web:

- place `少打断执行` at the bottom of the latest eligible assistant bubble
- use the same semantics as desktop web
- append a new assistant bubble for the new native continue run

### Feishu

On Feishu:

- add a bottom action area to the streaming or completed assistant card
- expose `少打断执行` only on the latest eligible assistant card
- clicking it creates a new streaming card for the new native continue run
- do not reopen or rewrite the old completed card as the active running card

## Runtime Model

WebCode has two execution entry paths for a session:

1. normal send-message execution
2. low-interruption native continue execution

Both paths:

- use the same WebCode session id
- reuse the same native CLI thread or session id when the tool supports it
- stream the process output the same way WebCode already does today

The difference is only the argument template used for launch.

## Tool Configuration Model

Each CLI tool may define a dedicated native low-interruption continue template, for example:

- `LowInterruptionArgumentTemplate`

If this template is absent for a tool:

- WebCode does not expose `少打断执行` for that tool

This keeps native continue behavior tool-specific and configuration-driven instead of hardcoding a fake universal loop contract in UI logic.

## Tool-Specific Expectations

### Codex

Codex should use native resume plus native low-interruption execution, without any appended prompt text.

Target command shape:

```bash
codex exec resume --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox --json --full-auto <cliThreadId>
```

Key properties:

- reuses the existing native Codex thread
- keeps `--json` so WebCode can continue streaming and parsing events
- keeps `--dangerously-bypass-approvals-and-sandbox` because current WebCode Codex execution depends on it
- adds `--full-auto` for lower interruption
- passes no additional continuation prompt

### Claude Code

Claude Code should use its native continue or resume path and its own multi-turn behavior. WebCode should not hardcode one universal Claude CLI string in product logic. The concrete template belongs in tool configuration.

### OpenCode

OpenCode should use its native session continue or agent loop path, again through tool configuration rather than WebCode-owned continuation logic.

## Process Ownership

For `少打断执行`, WebCode owns only process launch and streaming transport.

The CLI owns:

- planning behavior
- internal loop behavior
- user interruption and clarification logic
- completion semantics

WebCode mirrors what the process emits and stops when the process exits.

## Concurrency Rule

Only one CLI process may run per session at a time.

If a session already has an active CLI process:

- `少打断执行` is disabled
- WebCode does not queue a second continue run
- WebCode does not kill the running process
- WebCode does not allow concurrent continue processes for the same session

## Failure Handling

If native continue cannot be launched because the current session lacks a reusable CLI thread or session id:

- WebCode does not attempt to fake continuation through a synthetic prompt
- the action is hidden or disabled for that reply

If the CLI process exits with an error:

- the new assistant reply or Feishu streaming card ends exactly as current CLI process handling already does
- no automatic retry is scheduled by WebCode

## Acceptance Criteria

- desktop web shows `少打断执行` on the latest eligible assistant reply only
- mobile web shows `少打断执行` on the latest eligible assistant reply only
- Feishu shows `少打断执行` in a bottom action area on the latest eligible assistant card only
- clicking `少打断执行` creates a new assistant reply or new Feishu streaming card
- WebCode does not append a continuation prompt for low-interruption execution
- WebCode does not maintain its own `plan`, `todo_list`, or `backlog` state
- WebCode does not maintain a loop lifecycle state machine for this feature
- if a CLI process is already running for the session, `少打断执行` is disabled
- Codex low-interruption continue preserves `--dangerously-bypass-approvals-and-sandbox`
- if no native CLI thread or session id exists, WebCode does not fake continuation

## Risks

### Surface Drift Risk

If web, mobile, and Feishu each add their own continuation semantics, the feature becomes inconsistent and hard to trust.

Mitigation:

- keep semantics identical across all surfaces
- only vary layout, not behavior

### False Eligibility Risk

The button may appear when the current reply text looks unfinished but the CLI session no longer considers it actionable.

Mitigation:

- prefer structured task artifacts over plain text
- only allow the latest assistant reply in the session to remain actionable

### Native CLI Variance Risk

Codex, Claude Code, and OpenCode do not expose the same flags or the same continuation semantics.

Mitigation:

- make low-interruption continue configuration per-tool
- avoid inventing a fake shared loop abstraction in WebCode

### Session Thread Availability Risk

Some replies may not yet have a reusable native CLI thread or session id.

Mitigation:

- hide or disable `少打断执行` when native continuation is not available
- do not fall back to synthetic prompts
