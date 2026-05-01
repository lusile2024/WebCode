# Session Launch Overrides Design

Date: 2026-04-22
Status: approved in discussion, pending implementation planning

## Goal

Add session-scoped launch overrides for managed CLI tools in WebCode without affecting other sessions.

The feature must let users pin launch preferences at the WebCode session level:

- `Codex`: session-level `model` and `reasoning effort`
- `Claude Code`: session-level `model`
- `OpenCode`: session-level `model`

These overrides must be isolated to a single session, must survive session reload and resume, and must not mutate machine-global provider state or other sessions.

At the same time, `cc-switch` remains the only authority for provider selection for managed tools.

## Scope

In scope:

- session-scoped launch overrides for `codex`, `claude-code`, and `opencode`
- persisted session metadata
- runtime merge of provider snapshot and session launch overrides
- Web UI for editing overrides
- Feishu session-management card support for editing overrides
- Feishu session list folding behavior: default 3 sessions, expand on demand

Out of scope:

- provider switching outside `cc-switch`
- per-message temporary model switching
- changing CLI tool type inside an existing session
- custom reasoning controls for `Claude Code` or `OpenCode`
- global defaults UI

## Requirements

### Session Semantics

Launch overrides follow the same persistence model as session provider snapshots:

- the override belongs to the session
- reloading or resuming the session preserves the override
- changing one session never changes another session
- changing one tool override inside a session does not change another tool override inside the same session

### Tool Rules

- `Codex` supports `model` and `reasoning effort`
- `Claude Code` supports `model` only
- `OpenCode` supports `model` only

For `Codex`, allowed reasoning effort values are:

- `low`
- `medium`
- `high`
- `xhigh`

### Provider Authority

Provider selection still comes only from `cc-switch`.

Session launch overrides must not:

- edit `cc-switch`
- replace provider snapshot logic
- change provider id or provider name
- write machine-global live config for managed tools

The runtime must first resolve the session-local provider snapshot, then apply the session-local launch override for the active tool.

## Problem

WebCode already supports session-local provider snapshots for managed tools, which prevents existing sessions from silently jumping to a different provider after a `cc-switch` change.

However, model selection is still missing at the same session boundary:

- users may want one `Codex` session to stay on `gpt-5.4`
- another `Codex` session may need a cheaper or different model
- a `Claude Code` session may need `sonnet`
- an `OpenCode` session may need a provider/model string that is different from another session

If model configuration is stored globally, the session boundary breaks. If it is injected only into the current process without persistence, resume behavior breaks. If it is mixed into provider authority, the `cc-switch` contract breaks.

The design must therefore introduce a separate session-local launch override layer.

## Design Principles

1. Keep provider authority and launch override authority separate.
2. Keep override storage session-local and tool-scoped.
3. Make runtime behavior deterministic: same session + same tool = same launch config until explicitly changed.
4. Reset only the current session runtime when overrides change.
5. Reuse existing session persistence and card-action patterns instead of inventing parallel flows.

## Data Model

Do not store a single session-wide `ModelOverride` field.

That would leak values across tools inside the same session. A session may be associated with a current tool, but the stored data should still be tool-scoped so that switching tools later does not corrupt previously saved values.

### Session Storage

Add a JSON column to `ChatSession`, for example:

- `ToolLaunchOverridesJson`

Expose it as a typed object on the session domain model:

- `SessionHistory.ToolLaunchOverrides`

Recommended shape:

```json
{
  "codex": {
    "model": "gpt-5.4",
    "reasoningEffort": "high"
  },
  "claude-code": {
    "model": "sonnet"
  },
  "opencode": {
    "model": "openai/gpt-5"
  }
}
```

### Benefits

- no cross-tool contamination inside a session
- future-safe if more launch attributes are added later
- no need to keep adding nullable database columns
- easy to pass through existing session GET and PUT flows

## Runtime Model

For managed tools, launch configuration is built in two layers:

1. session-local provider snapshot
2. session-local tool launch override

The override never replaces the snapshot. It only amends launch-specific fields.

### Merge Order

For a managed-tool execution:

1. resolve the current session
2. resolve the effective managed tool id
3. ensure the session-local provider snapshot exists and is valid
4. read the tool-scoped launch override from the session record
5. generate the final tool launch configuration by applying the override over the session snapshot
6. launch the CLI with the merged session-local configuration

### Tool-Specific Behavior

#### Codex

Codex already reads session-local configuration from `.codex/config.toml` in the workspace snapshot path used by managed sessions.

The merged config should apply:

- `model`
- `model_reasoning_effort`

Optional later extension:

- `model_reasoning_summary`
- `model_verbosity`

But the first implementation only needs `model` and `model_reasoning_effort`.

#### Claude Code

Keep provider snapshot behavior unchanged.

Apply the session override at launch time by appending `--model <value>` to the built arguments for the current session when the override exists.

#### OpenCode

Keep provider snapshot behavior unchanged.

Apply the session override at launch time by appending `--model <value>` to the built arguments for the current session when the override exists.

## Runtime Reset Behavior

When a launch override changes, the current session runtime must be reset without deleting the session or workspace.

Required effects:

- clear the in-memory CLI thread id for that session
- clear the persisted `ChatSession.CliThreadId`
- terminate persistent processes for that session
- keep workspace files intact
- keep chat messages intact
- keep provider snapshot intact

This reset must be session-only.

Changing session A must not:

- clear session B thread ids
- kill session B processes
- alter session B overrides
- alter global provider state

### Why Reset Is Required

Existing CLI thread reuse would otherwise make override changes ambiguous:

- some CLIs may ignore model changes inside a reused native thread
- some persistent processes may keep old launch arguments

The simplest consistent rule is:

- saving an override resets the current session runtime
- the next execution for that session starts a fresh native thread using the new merged config

## Web UI

### Entry Point

Add a session-level action in the session list item actions:

- `模型设置`

This action belongs next to:

- sync provider
- share
- rename
- delete

It must not live in the global settings panel and must not be mixed into rename.

### Dialog

Introduce a dedicated `SessionLaunchOverrideDialog`.

The dialog edits:

- one session
- one tool override, based on the current tool for that session

Dialog fields:

- `Codex`
  - `Model`
  - `Reasoning Effort`
- `Claude Code`
  - `Model`
- `OpenCode`
  - `Model`

### Field Style

`Model` should support:

- free text entry
- a small suggested-values dropdown

Reason:

- model naming differs across CLIs
- a hard enum would become stale quickly

`Reasoning Effort` should be a constrained select for `Codex`.

### User Messaging

The dialog should show:

- current tool
- pinned provider summary
- a note that provider still comes from `cc-switch`

On save:

- if editing the current session, show that the new config applies on the next execution and the session runtime has been reset
- if editing another session, show that the override was saved and will apply when that session is used next

### Session List Summary

When an override exists for the effective tool, show a lightweight summary in the session list:

- `模型: gpt-5.4 · 思考: high`
- `模型: sonnet`

This should appear below the pinned provider line and only when relevant.

## Feishu UX

Feishu must support the same session semantics as Web UI. It must not become a second-class path where session launch overrides are invisible or impossible to manage.

### Session Manager Card Folding

The Feishu session manager card should default to a folded view:

- show the most recent 3 sessions only

At the bottom, show:

- `更多会话`

When expanded:

- show all sessions
- show `收起` to return to folded mode

If the card would become too large for Feishu limits, the expanded view may degrade into pagination, but the product semantics remain:

- folded by default
- explicit expand to full list

### Session Actions

Each session row in the Feishu session manager card should include:

- switch
- rename
- sync provider
- model settings
- close

`model settings` opens a dedicated form card, not a free-text slash command flow.

### Form Behavior

The Feishu form mirrors the Web dialog:

- `Codex`: `Model` + `Reasoning Effort`
- `Claude Code`: `Model`
- `OpenCode`: `Model`

Recommended actions:

- `show_session_launch_settings_form`
- `save_session_launch_settings`
- `clear_session_launch_settings`

### Summary Display

Feishu session cards should display override summaries when present:

- `🤖 模型: ...`
- `🧠 思考: ...` for `Codex`

The current execution status card should also display the active override summary for the current session so the user can see the launch state that will be used.

## API And Persistence Flow

The existing session persistence model is already broad enough for this change:

- session objects are loaded as full records
- session updates already flow through session save/update APIs

So the first version does not need a complex new controller surface.

Valid implementation options:

1. extend existing session GET and PUT DTOs with the new launch-override object
2. add a dedicated session-launch-settings endpoint later if UI ergonomics require it

Recommendation for first implementation:

- extend existing session persistence path
- keep override serialization inside session save/load mapping

## Validation Rules

### Codex

- `model` may be any non-empty string
- `reasoningEffort` must be one of:
  - `low`
  - `medium`
  - `high`
  - `xhigh`

### Claude Code

- `model` may be any non-empty string

### OpenCode

- `model` may be any non-empty string

### Common

- blank input clears that field
- clearing all fields for a tool removes that tool entry from the JSON object
- clearing all tool entries removes the JSON payload entirely

## Compatibility

Old sessions remain valid.

Behavior for sessions with no override does not change.

This feature is only surfaced for managed tools:

- `codex`
- `claude-code`
- `opencode`

Unmanaged tools continue to use their existing launch behavior.

## Verification

Implementation is complete only if the following hold:

1. changing session A override does not affect session B
2. `Codex` reasoning effort applies only to the targeted session
3. saving an override clears only the targeted session runtime
4. provider sync preserves launch overrides
5. Web session list shows override summary when present
6. Feishu session manager defaults to 3 items and supports expand/collapse
7. Feishu session settings save successfully and apply on the next execution
8. old sessions without overrides continue to behave exactly as before

## Risks

### Runtime Drift Risk

If launch overrides are applied without clearing session runtime state, the CLI may continue using stale launch configuration.

Mitigation:

- always reset the session runtime on override changes

### Cross-Tool Leakage Risk

If overrides are stored as session-global fields instead of tool-scoped fields, one tool configuration may pollute another.

Mitigation:

- store overrides per tool in a structured JSON object

### Feishu Card Length Risk

Showing every session by default can make the card noisy and difficult to use.

Mitigation:

- fold to 3 by default
- expand explicitly
- paginate if necessary in expanded mode

## Non-Goals For First Version

The first version should not add:

- global model defaults
- per-message launch overrides
- batch editing overrides for many sessions at once
- reasoning controls for `Claude Code` or `OpenCode`
- provider editing in WebCode or Feishu
