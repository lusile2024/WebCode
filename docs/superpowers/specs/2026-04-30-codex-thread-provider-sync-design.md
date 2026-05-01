# Codex Thread Provider Sync Design

Date: 2026-04-30
Status: approved in discussion, pending implementation planning

## Goal

Add a thread-scoped provider resync action for the current Codex session in WebCode.

The action should make the current session's Codex thread visible under the current `cc-switch` active provider by rewriting only the session-local Codex artifacts for that thread.

This feature must work on desktop web, mobile web, and Feishu because all three surfaces already route through the same `sync_session_provider` session action.

The feature must handle two initial states:

- the current session already has a local workspace snapshot
- the current session is still bootstrapping from the global `~/.codex` source, and the local project/workspace `.codex` is created only when sync is requested

## Scope

In scope:

- current session's Codex thread only
- session-local `.codex` materialization if the current session has not been copied yet
- rewriting the current thread's provider metadata in copied `rollout-*.jsonl` files
- rewriting the current thread's `threads.model_provider` row in the session-local `state_5.sqlite` if it exists
- showing the same sync result path on desktop web, mobile web, and Feishu
- best-effort handling for locked files and missing sqlite
- reusing existing `CliThreadId` recovery

Out of scope:

- syncing all Codex sessions in the workspace
- changing the machine-global `~/.codex/config.toml`
- changing the active provider in `cc-switch`
- changing unrelated threads in the same workspace
- adding a provider picker or global sync screen
- backing up and restoring the whole Codex home
- changing other CLI tools

## Problem

When a Codex provider changes, the current thread can become invisible under the new provider because provider metadata in the session-local copied Codex state still points at the old provider. The current WebCode flow already knows how to materialize a session-local Codex snapshot, but it stops at copying config, auth, and thread artifacts. It does not rewrite the provider metadata for the current thread.

The important boundary is that WebCode may start from the global `~/.codex` source, but the sync action must always end on the session-local copy. If the current WebCode session has not been materialized yet, the sync action should create that local copy first and then rewrite only the current thread there.

## Design Principles

1. Treat the current Codex thread as the unit of sync.
2. Keep the session-local `.codex` copy as the only write target.
3. Materialize first, then rewrite provider metadata.
4. Skip locked pieces instead of mutating unrelated state.
5. Reuse existing thread-id recovery and session snapshot machinery.

## Runtime Model

For a Codex session sync request:

1. Resolve the current WebCode session.
2. Resolve the effective Codex thread id from the session, using the existing `CliThreadId` fallback path if needed.
3. Resolve the current `cc-switch` active provider for Codex.
4. Resolve the session-local Codex workspace root.
5. If the local workspace has not been materialized yet, seed it from the existing global Codex source through the current snapshot flow.
6. Rewrite only the files and sqlite rows that belong to the resolved thread id.
7. Persist the updated session snapshot metadata and return a sync summary.

The sync target is the current session-local Codex copy, not the user's global `~/.codex` directory.
The rewrite target is the current `cc-switch` active provider, even if the session snapshot currently points at a different provider.

### Thread Resolution

The sync action is allowed only when the current session has a resolvable Codex thread id.

Resolution order should reuse existing code paths:

- in-memory `CliThreadId`
- persisted `ChatSession.CliThreadId`
- recovered thread id from existing session output where available

If no thread id can be resolved, the action should fail fast with a clear message instead of guessing.

### Local Materialization

If the current session workspace does not yet contain a local Codex snapshot, the sync action should create it using the same session-materialization path that normal Codex launches already use.

That materialization step is a seed, not the final sync result:

- it may copy config, auth, and thread artifacts from the global source
- it must not overwrite unrelated global Codex state
- it must not mutate other sessions

After materialization, the thread provider rewrite happens on the local copy only.

### Artifact Rewrite

For the resolved thread id, search the local session `.codex` under both:

- `sessions`
- `archived_sessions`

Use filename matching only as a candidate filter.

The authoritative identity check is the first JSON line:

- parse it as `session_meta`
- confirm that `payload.id` matches the resolved thread id

Only files that pass that identity check may be rewritten.

For every matched rollout file:

- rewrite `payload.model_provider` to the current Codex provider id
- preserve all other first-line payload fields
- keep the rest of the file untouched

If a matching file is locked, skip that file and report it in the result.

### SQLite Rewrite

If the local session `.codex/state_5.sqlite` exists, update only the current thread row in `threads`:

- `WHERE id = @threadId`
- set `model_provider = @targetProvider`

Do not update the whole table.

Do not synthesize a new sqlite file if one does not already exist.

If the sqlite file is locked, report the database update as skipped and keep any successful file rewrites.

## Architecture

### 1. Session Sync Orchestrator

Keep the current `sync_session_provider` action as the entry point on desktop, mobile, and Feishu.

The action should delegate to a dedicated Codex thread sync service rather than inlining provider rewrite logic inside the UI handlers.

### 2. Thread Provider Sync Service

Add a focused service responsible for:

- resolving the sync target provider
- ensuring the current session workspace exists
- collecting rollout file matches for the current thread
- rewriting local rollout files
- rewriting the local sqlite row when present
- returning a result object with changed files, updated row counts, skipped locks, and materialization status

This keeps provider sync separate from launch-time snapshot materialization.

### 3. Session Metadata Update

After a successful sync, update the existing session snapshot metadata that WebCode already stores for Codex sessions, including:

- provider id
- provider name
- provider category
- sync timestamp
- source live config path

No new database schema is required for this feature.

## Failure Handling

If the session has no thread id:

- disable the action or return a clear error

If the workspace must be materialized first and that step fails:

- fail the sync and report the materialization error

If rollout files are locked:

- report the skipped files
- still apply any other eligible local rewrites

If sqlite is absent:

- treat that as a partial success, not a hard failure

If sqlite is locked:

- report the skipped database update
- keep any file changes that already succeeded

If the thread cannot be located in the local snapshot after materialization:

- report that the thread was not found in the local Codex snapshot
- do not fall back to rewriting other threads

## UI Behavior

Desktop web, mobile web, and Feishu should keep using the same session-level sync action already wired through `sync_session_provider`.

The visible behavior should stay simple:

- sync the current Codex thread to the active provider
- show success when the current thread was updated
- show a partial-success warning when some files or sqlite updates were skipped
- do not expose a separate provider picker in this flow

## Testing

Coverage should include:

- current session with an existing local Codex snapshot
- current session that still needs local materialization before sync
- single-thread rewrite only, with another thread in the same workspace left unchanged
- sqlite present vs sqlite absent
- locked rollout file vs unlocked rollout file
- locked sqlite vs unlocked sqlite
- no resolvable `CliThreadId`
- desktop, mobile, and Feishu action paths calling the same backend logic

## Acceptance Criteria

- the sync action updates only the current Codex thread
- the sync action does not mutate global `~/.codex/config.toml`
- the sync action does not rewrite other Codex threads
- if the current session has not been materialized yet, the sync action creates the local workspace copy first
- the sync action rewrites the local thread's rollout metadata to the current provider
- the sync action rewrites only the matching sqlite row when sqlite exists
- if files or sqlite are locked, the action reports partial success rather than silently claiming full success
- desktop, mobile, and Feishu keep the same semantics

## Risks

### Source/Target Confusion

The easiest implementation mistake is writing back into the global `~/.codex` source instead of the session-local copy.

Mitigation:

- make the target path explicit in the sync service
- compare source and target paths before writing
- keep the materialization step separate from the rewrite step

### Live Thread Lock Risk

If the active Codex process still holds the rollout file or sqlite open, a full sync may not be possible immediately.

Mitigation:

- treat lock failures as partial success
- tell the user to rerun sync after the session exits if needed

### False Cross-Thread Match Risk

Filename matching alone is not enough for all rollout layouts.

Mitigation:

- keep the first-line `session_meta` parse as the authoritative match check
- only rewrite files whose `payload.id` matches the current thread id
