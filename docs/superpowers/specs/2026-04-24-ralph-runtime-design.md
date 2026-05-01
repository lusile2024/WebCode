# Superpowers Ralph Runtime Design

Date: 2026-04-24

## Problem

Superpowers currently provides a strong methodology through skills, but it does not own a persistent runtime with explicit loop semantics. In practice this leaves a gap between:

- Skills that define how agents should plan, execute, and verify work
- Hosts such as Codex, WebCode, and Feishu that need a durable way to keep work moving until it is either complete, blocked, paused, or waiting for user input

This gap shows up most clearly in long-running tasks. A plain `codex exec resume --full-auto` flow can reduce interaction friction, but it does not provide governance semantics such as:

- workspace-scoped single active run ownership
- explicit phase transitions
- persistent pause and resume control
- completion decisions based on verification evidence instead of natural language
- a shared protocol that non-Codex hosts can call directly

Ralph exists to close that gap.

## Goals

The first version of Ralph should:

- expose a Codex-native command entrypoint such as `/ralph`
- run through a local daemon that owns runtime state and loop control
- support one active Ralph run per workspace
- expose a shared HTTP plus SSE protocol that WebCode and Feishu can reuse
- treat completion as a governed state, not as a model utterance
- preserve Superpowers skill methodology instead of replacing it

## Non-Goals

The MVP does not attempt to provide:

- multi-provider support
- WebSocket transport
- distributed or remote daemons
- multiple concurrent active runs inside the same workspace
- advanced team or swarm orchestration inside the daemon
- automated approval delegation
- rich dashboard UI beyond projected runtime events
- replacement of existing Superpowers skills

## User Experience

Human users interact with Ralph through Codex-native commands. Systems interact with Ralph through the daemon API.

Codex command surface:

- `/ralph` starts or attaches to the active run for the current workspace
- `/ralph-status` shows the current phase, round, latest summary, blocker state, and verification status
- `/ralph-attach` attaches the current conversation to an existing run
- `/ralph-pause` requests a cooperative pause
- `/ralph-resume` resumes a paused run
- `/ralph-cancel` cancels the current run

External host behavior:

- WebCode calls the daemon API directly
- Feishu calls the daemon API directly
- neither WebCode nor Feishu should invoke Codex-native commands as their integration surface

## Architecture

Ralph is composed of five components.

### 1. Codex Command Shell

Responsibilities:

- gather current workspace context from the Codex session
- build a curated context snapshot
- discover or launch the daemon
- call the daemon HTTP API
- subscribe to projected SSE events
- render user-facing summaries back into the current Codex conversation

Non-responsibilities:

- loop ownership
- phase transitions
- completion decisions
- workspace locking
- direct `codex resume` iteration

### 2. Snapshot Builder

Responsibilities:

- derive `workspaceKey` from worktree root, repo root, or cwd
- extract a curated snapshot from the invoking host
- normalize task, constraints, approved design, approved plan, unfinished work, and verification requirements

Non-responsibilities:

- execution
- verification
- run state management

### 3. `ralphd`

Responsibilities:

- run lifecycle ownership
- workspace locking
- phase transitions
- round scheduling
- pause, resume, cancel, and input handling
- persistence
- completion gate evaluation
- projected event generation

Non-responsibilities:

- direct UI rendering
- host-specific presentation logic
- redefining Superpowers methodology

### 4. Codex Bridge

Responsibilities:

- start or resume managed Codex threads
- call `codex exec --json`
- call `codex exec resume --json --full-auto`
- collect raw JSONL output
- translate Codex-native events into normalized runtime facts

Non-responsibilities:

- daemon API ownership
- completion decisions
- user-facing event projection

### 5. Projection and Event Layer

Responsibilities:

- translate runtime state into stable Ralph semantic events
- provide a host-neutral stream consumable by Codex, WebCode, and Feishu

Non-responsibilities:

- parsing raw Codex JSONL directly in UI clients
- phase transitions
- loop control

## Default Decisions

The following decisions are fixed for the MVP:

- Ralph is entered through Codex-native commands, not through a separate top-level CLI
- Ralph runtime ownership belongs to a local daemon, not to a skill-only prompt loop
- daemon transport is HTTP for control plus SSE for projected events
- one workspace may have at most one active run
- the default execution lane uses `superpowers:subagent-driven-development`
- the verification lane must use `superpowers:verification-before-completion`
- completion requires a fixed sentinel string
- WebCode and Feishu call the daemon API directly
- clients consume projected Ralph events, not raw Codex JSONL
- the command shell never owns loop state or completion logic

## Run Model

### WorkspaceKey

`workspaceKey` identifies the execution scope. It is derived from:

1. worktree root when present
2. repository root when present
3. current working directory otherwise

Only one active run may exist for a given `workspaceKey`.

### RalphRun

Each run stores at least:

- `runId`
- `workspaceKey`
- `workspacePath`
- `source`
- `sourceSessionId`
- `managedThreadId`
- `phase`
- `round`
- `stopReason`
- `startedAt`
- `lastActivityAt`

### ContextSnapshot

The snapshot is curated, not cloned from the full transcript. It should contain:

- task statement
- constraints
- approved design summary
- approved plan summary
- unfinished work
- verification requirements
- workspace metadata

### RunEvent

Each projected event stores at least:

- `eventId`
- `runId`
- `workspaceKey`
- `timestamp`
- `type`
- `phase`
- `payload`

## State Machine

Ralph run phases:

- `grounding`
- `planning`
- `executing`
- `verifying`
- `awaiting_user`
- `paused`
- `blocked`
- `completed`
- `failed`
- `cancelled`

Key state rules:

- `completed` may only be entered from `verifying`
- `awaiting_user` is advanced through explicit user input, not through resume alone
- `paused` is distinct from `awaiting_user`
- only one active run may exist per `workspaceKey`

Active phases are:

- `grounding`
- `planning`
- `executing`
- `verifying`
- `awaiting_user`
- `paused`

Typical transitions:

- `grounding -> planning`
- `grounding -> executing`
- `planning -> executing`
- `executing -> verifying`
- `verifying -> executing`
- `planning -> awaiting_user`
- `executing -> awaiting_user`
- `executing -> blocked`
- `verifying -> completed`
- `* -> cancelled`
- `* -> failed`

## Runtime Loop

One Ralph round equals one managed Codex turn.

The daemon loop for each round is:

1. acquire workspace lock
2. load run state
3. stop if phase is terminal
4. stop progression if phase is `paused`, `awaiting_user`, or `blocked`
5. build the effective snapshot for the current phase
6. generate the phase-specific prompt contract
7. invoke the Codex bridge for one turn
8. collect normalized facts
9. update ledgers and run state
10. evaluate round outcome
11. transition phase or complete the run

Round progression heuristics for the MVP:

- `maxExecutionRounds = 12`
- `verificationCheckpointEvery = 3`
- `maxNoProgressRounds = 2`
- `maxRepeatedBlockerRounds = 3`

Progress for a round should require at least one of:

- task ledger state change
- recorded file changes
- new verification evidence
- blocker removal
- forward phase movement

## Completion Gate

Completion is allowed only when all four of the following are true:

1. `execution_ok`
   - the current turn ended without unresolved runtime failure
2. `unfinished_work_cleared`
   - task ledger is clear or structured unfinished work signals are gone
3. `verification_green`
   - required tests, builds, or other verification commands were run and read successfully
4. `completion_sentinel_seen`
   - the managed thread emitted the fixed sentinel

Default sentinel:

`SUPERPOWERS_RALPH_COMPLETE`

Weak textual clues such as "done" or "completed" are not sufficient to complete a run on their own.

## Skill Integration

Ralph does not replace Superpowers skills. It orchestrates when to use them.

Phase mapping:

- `planning`
  - `superpowers:brainstorming`
  - `superpowers:writing-plans`
- `executing`
  - `superpowers:subagent-driven-development`
- `verifying`
  - `superpowers:verification-before-completion`
  - optionally `superpowers:requesting-code-review` for higher-risk changes

Skill responsibilities remain behavioral. Ralph responsibilities remain runtime and governance oriented.

Phase prompt contracts must ensure:

- planning respects approval gates
- execution does not prematurely declare completion
- verification runs required commands, reads output, and only emits the sentinel when the completion gate is satisfied

## API and Event Protocol

### Control API

- `POST /v1/runs/start`
- `GET /v1/workspaces/{workspaceKey}/active-run`
- `GET /v1/runs/{runId}`
- `POST /v1/runs/{runId}/pause`
- `POST /v1/runs/{runId}/resume`
- `POST /v1/runs/{runId}/input`
- `POST /v1/runs/{runId}/cancel`
- `POST /v1/runs/{runId}/attach`

`POST /v1/runs/start` behavior:

- if no active run exists for the workspace, create one
- if an active run already exists, return that run and suggest attach instead of creating a new one

### Event Stream

Projected events are delivered over:

- `GET /v1/runs/{runId}/events`

Projected event types:

- `run.started`
- `run.phase_changed`
- `run.round_started`
- `run.round_summary`
- `run.awaiting_user`
- `run.blocked`
- `run.verification_started`
- `run.verification_result`
- `run.completed`
- `run.failed`
- `run.cancelled`
- `run.heartbeat`

Clients must consume projected events only. Raw Codex JSONL remains an internal diagnostic artifact.

## Persistence

Ralph runtime state should live under `.superpowers/runtime/`.

Recommended layout:

- `.superpowers/runtime/daemon.json`
- `.superpowers/runtime/runs/<run-id>.json`
- `.superpowers/runtime/workspaces/<workspace-key>.json`
- `.superpowers/runtime/logs/<run-id>.jsonl`
- `.superpowers/runtime/snapshots/<run-id>.md`

This layout supports:

- daemon discovery
- crash recovery
- reattach
- auditability

## MVP Delivery Plan

### Phase 0: Daemon Skeleton

Deliver:

- health endpoint
- run start and status endpoints
- SSE plumbing
- workspace locking
- daemon discovery state

Use a fake engine behind the daemon.

### Phase 1: Fake Round Engine

Deliver:

- deterministic round progression
- projected event testing
- host integration validation for Codex, WebCode, and Feishu

Goals:

- validate the runtime structure before connecting to real Codex

### Phase 2: Codex Bridge

Deliver:

- real `codex exec --json` integration
- real `codex exec resume --json --full-auto` integration
- raw JSONL capture
- normalized fact extraction

Goals:

- prove one real managed turn can flow through the daemon cleanly

### Phase 3: True Ralph Semantics

Deliver:

- `paused`
- `awaiting_user`
- `input`
- ledgers for tasks, verification, and blockers
- four-stage completion gate
- sentinel handling
- crash recovery and reattach

Goals:

- achieve true Ralph runtime semantics

## Risks and Mitigations

### Snapshot Too Thin

Risk:

- the managed run loses important constraints or plan context

Mitigation:

- keep snapshot structured and explicit
- validate snapshot coverage during early testing

### Snapshot Too Noisy

Risk:

- transcript clutter pollutes the managed run

Mitigation:

- use curated snapshots by default
- avoid full transcript cloning for the MVP

### Completion Gate Too Weak

Risk:

- runs complete based on model language instead of evidence

Mitigation:

- require structured ledgers, verification evidence, and sentinel confirmation

### Completion Gate Too Strong

Risk:

- runs stall in `verifying` and fail to converge

Mitigation:

- tune phase prompts and gate thresholds
- add explicit fallback to `awaiting_user` or `blocked` when convergence fails

### Command Shell Expands Into a Second Runtime

Risk:

- loop ownership leaks into the command layer and architecture collapses

Mitigation:

- make the command shell a strict control surface only
- keep all phase transitions and completion logic inside `ralphd`

## Out of Scope for MVP

Explicitly excluded from the first version:

- multi-provider adapters
- daemon remote access
- advanced team orchestration inside the daemon
- approval policy engines
- PR automation
- rich visual dashboards
- multi-run scheduling beyond one active run per workspace

## Summary

Superpowers Ralph should be implemented as a workspace-scoped persistent runtime mode. It should be entered from Codex-native commands, executed by a local daemon, and exposed to external systems through a shared HTTP plus SSE API.

Ralph owns:

- loop control
- state transitions
- workspace locking
- verification gating
- completion semantics

Superpowers skills continue to own:

- planning behavior
- execution behavior
- verification behavior

Humans use commands. Systems use the daemon API.
