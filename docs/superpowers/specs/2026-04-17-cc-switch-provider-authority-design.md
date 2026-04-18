# CC Switch Provider Authority Design

Date: 2026-04-17
Status: revised for direct implementation by user instruction

## Goal

Make `cc-switch` the only provider authority for Claude Code, Codex, and OpenCode in WebCode, while preserving terminal-like session semantics.

WebCode must stop behaving like a second provider manager. It must no longer allow manual environment-variable entry, provider editing, or profile switching for managed CLIs. Provider choice happens only in `cc-switch`.

At the same time, an already created WebCode or Feishu one-time-process session must behave like an already opened terminal window:

- the session keeps using the provider/config snapshot it started with
- changing provider in `cc-switch` affects only new sessions
- an existing session changes provider only when the user explicitly requests sync

## Problem

WebCode currently has two conflicting behaviors:

- setup and settings have already been moving toward `cc-switch` ownership
- runtime execution for managed tools still reads the current live config each time a one-time process is launched

That means the process is stateless but the conversation is stateful:

- the WebCode session id and CLI thread id are reused
- the provider backing the same session can silently change after a `cc-switch` provider switch

This breaks user expectation. A WebCode session should not unexpectedly jump to a different provider just because the machine-global active provider changed after the session was already in use.

## Source Of Truth

`cc-switch` remains the source of truth for provider selection.

WebCode reads provider state from:

1. `~/.cc-switch/settings.json`
2. `~/.cc-switch/cc-switch.db`
3. the live CLI config files written by `cc-switch`

Priority for current provider selection must match `cc-switch` behavior:

1. device-level current provider override from `settings.json`
2. fallback to `providers.is_current` in `cc-switch.db`

WebCode must not let users edit or override provider state locally. It may, however, materialize a session-local copy of the active live config as an execution snapshot for an existing session.

That snapshot is not a second provider authority. It is only the persisted launch state of a session that was originally sourced from `cc-switch`.

## Runtime Model

The runtime model is machine-global at selection time and session-local at execution time.

Mapping:

- `claude-code` -> `claude`
- `codex` -> `codex`
- `opencode` -> `opencode`

For each managed tool, WebCode resolves:

- whether `cc-switch` is installed and readable
- the active provider id for that tool
- the active provider display name for that tool
- whether the corresponding live config path exists
- whether the tool is launch-ready

### Session Pinning

On first managed-tool execution in a session:

- WebCode reads the current `cc-switch` active provider and live config
- WebCode copies that live config into the session workspace
- WebCode stores snapshot metadata on the session record

On later executions in the same session:

- WebCode uses the session-local snapshot
- WebCode does not automatically switch to the newly active `cc-switch` provider

On explicit sync:

- WebCode re-reads the current `cc-switch` active provider
- WebCode overwrites the session-local snapshot with the current live config
- WebCode updates the stored snapshot metadata

This preserves terminal-like semantics for WebCode UI and Feishu one-time-process sessions.

## UX Changes

### Setup Wizard

The setup wizard remains a `cc-switch` readiness flow instead of an environment-variable entry flow.

Target structure:

1. account and workspace setup
2. assistant selection
3. `cc-switch` readiness review

The readiness step shows:

- whether `cc-switch` is detected
- where its config directory lives
- the active provider for each selected tool
- whether each selected tool is ready
- blocking reasons when a selected tool is not ready

The wizard must not contain:

- import local config
- manual env var entry
- provider-specific config pages
- per-tool profile management

Initialization may complete only when all selected tools are ready through `cc-switch`.

### Runtime Settings Modal

The existing environment-variable modal becomes a read-only `cc-switch` status modal.

It should show:

- this tool is managed by `cc-switch`
- current machine-active provider id and name
- config directory and live config path
- readiness status
- reason when unavailable

It should not allow:

- editing keys or values
- saving
- resetting
- profile creation or activation

It may offer:

- a refresh action
- guidance to switch provider in `cc-switch`

### Session UI

Session UI must make the difference between machine-active state and session-pinned state visible.

For managed-tool sessions, WebCode should expose:

- pinned provider display name/id
- pinned source timestamp when available
- sync action to update the session to the current `cc-switch` active provider

If a pinned session snapshot becomes missing or unusable, the session should not silently fall back to current global live config. It should block execution and ask the user to sync explicitly.

### Runtime Tool Availability

Unavailable tools are disabled on a per-tool basis.

If the current `cc-switch` state only makes Codex launch-ready, WebCode may still offer Codex while Claude Code and OpenCode remain unavailable.

## Service Design

Keep a dedicated read-only integration surface such as `ICcSwitchService`.

Responsibilities:

- locate `cc-switch` config directory
- read `settings.json`
- read `cc-switch.db`
- resolve current provider per tool
- compute live config paths
- expose enough metadata to copy the active live config into a session snapshot
- report per-tool readiness and failure reasons

Non-responsibilities:

- editing `cc-switch` state
- writing to `cc-switch.db`
- switching providers
- inventing provider data independent of `cc-switch`

## Execution Changes

`CliExecutorService` must stop treating “read current global live config on every launch” as the runtime model for managed tools.

Target behavior:

- before first managed-tool launch in a session, verify `cc-switch` launch readiness and materialize a session-local config snapshot
- on later launches, use the session-local snapshot instead of current global live config
- if the selected tool is not launch-ready when a new snapshot is needed, fail with a precise `cc-switch`-related message
- if a pinned snapshot is expected but missing, fail with a sync-required message

For managed tools, WebCode still must not regenerate provider configs from WebCode-owned env vars. The only valid source is the `cc-switch` live config copied into the session workspace.

## Stored Session Metadata

WebCode may persist the following session-level metadata for managed tools:

- pinned provider id
- pinned provider display name
- pinned provider category
- path of the original live config that was copied
- snapshot file path in the session workspace
- last snapshot sync time
- whether the session uses `cc-switch` pinned config

This metadata exists only to describe and validate the session snapshot.

## Failure Handling

When `cc-switch` is missing or invalid:

- the WebCode application still loads
- CLI-related flows are blocked
- the setup readiness page and runtime status modal explain the problem

When a session snapshot is invalid:

- the session remains visible
- execution is blocked for that session
- the user is told to sync to the current `cc-switch` active provider

Failure classes:

- `cc-switch` directory missing
- `settings.json` unreadable
- `cc-switch.db` unreadable
- no active provider for a required tool
- active provider id points to a missing provider row
- live config file missing for a selected tool
- session snapshot missing
- session snapshot unreadable

## Compatibility And Migration

The old WebCode env/profile tables may remain temporarily for compile compatibility, but they are removed from the active configuration path for managed tools.

Migration expectations:

- setup no longer writes `ClaudeCodeEnvVars`, `CodexEnvVars`, `OpenCodeEnvVars`
- runtime env modal no longer edits `CliToolEnvironmentService`
- managed-tool launch no longer depends on WebCode-stored provider env vars
- managed-tool sessions gain pinned snapshot metadata and session-local config files

The old data may remain on disk short-term, but it is no longer authoritative.

## Risks

### Main Risk

`cc-switch` stores current-provider information partly in device-level settings and partly in the database. Reading only one source would produce stale or incorrect snapshot data.

Mitigation:

- implement the same precedence that `cc-switch` uses

### Session Consistency Risk

If WebCode silently falls back from a missing session snapshot to the current machine-global live config, the same session can jump providers unexpectedly.

Mitigation:

- treat missing snapshot as a sync-required error, not as an implicit provider switch

### Tool-Specific Layout Risk

Claude Code, Codex, and OpenCode use different config file locations and project-level override semantics.

Mitigation:

- materialize snapshots using tool-specific known filenames and workspace-local config locations

## Acceptance Criteria

- setup contains no manual CLI env-var entry
- runtime settings contain no manual CLI env-var entry
- provider switching is only possible in `cc-switch`
- existing managed-tool sessions keep using their pinned provider snapshot after `cc-switch` changes
- new managed-tool sessions use the currently active `cc-switch` provider
- sessions can explicitly sync to the current `cc-switch` active provider
- missing session snapshots block execution with a sync-required message
- unavailable tools are blocked per tool, not globally
