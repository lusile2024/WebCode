# Ralph Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a workspace-scoped Ralph runtime to Superpowers by introducing a local daemon, Codex-native control commands, and a shared HTTP plus SSE protocol that can later be consumed by external hosts.

**Architecture:** Keep Ralph runtime ownership in a local zero-dependency daemon instead of in skills or command markdown. Use Codex-native commands only as control surfaces that build curated snapshots, call the daemon, and attach to projected runtime events. Implement the daemon in phases: state and locking first, then HTTP plus SSE with a fake engine, then the real Codex bridge and completion gate.

**Tech Stack:** Node.js built-ins only (`node:http`, `node:child_process`, `node:fs`, `node:test`, `assert`), Superpowers plugin files, Codex CLI JSONL output, Markdown command shims, shell smoke tests.

**Target repository:** `C:/Users/01467304/.codex/superpowers`

**Depends on:** `docs/superpowers/specs/2026-04-24-ralph-runtime-design.md`

**Scope boundary:** This plan changes only the Superpowers repository. WebCode and Feishu client integration must be implemented in a separate follow-up plan against those codebases after the daemon API stabilizes.

---

## File Map

### Runtime core

- Modify: `.gitignore`
  Ignore `.superpowers/runtime/` artifacts created by the daemon.
- Create: `lib/ralphd/daemon-state.mjs`
  Read and write daemon discovery state (`pid`, `baseUrl`, `token`, `startedAt`, `version`).
- Create: `lib/ralphd/workspace-key.mjs`
  Normalize repo root, worktree root, or cwd into a stable `workspaceKey`.
- Create: `lib/ralphd/run-registry.mjs`
  Enforce one active run per workspace and own in-memory plus file-backed run metadata.
- Create: `lib/ralphd/projected-events.mjs`
  Define Ralph semantic event names and event serialization helpers.
- Create: `lib/ralphd/fake-engine.mjs`
  Emit deterministic round results for phase-0 and phase-1 testing.
- Create: `lib/ralphd/server.mjs`
  Expose the HTTP control surface and SSE event stream.

### Codex integration

- Create: `lib/ralphd/fact-normalizer.mjs`
  Convert raw Codex JSONL lines into normalized runtime facts.
- Create: `lib/ralphd/codex-bridge.mjs`
  Launch `codex exec --json` or `codex exec resume --json --full-auto`, capture output, and return one managed turn result.
- Create: `lib/ralphd/phase-contracts.mjs`
  Build phase-specific prompt contracts for `planning`, `executing`, and `verifying`.
- Create: `lib/ralphd/completion-gate.mjs`
  Implement the four-part completion gate and fixed sentinel handling.

### Command surface and docs

- Create: `scripts/ralphctl.mjs`
  Discover or launch `ralphd`, wrap HTTP API calls, and print human-readable summaries for command shims.
- Create: `commands/ralph.md`
  Start or attach the current workspace run through `scripts/ralphctl.mjs`.
- Create: `commands/ralph-status.md`
  Print current workspace status through `scripts/ralphctl.mjs`.
- Create: `commands/ralph-attach.md`
  Attach the current session to an existing run and summarize the latest state.
- Create: `commands/ralph-pause.md`
  Request cooperative pause.
- Create: `commands/ralph-resume.md`
  Resume a paused run.
- Create: `commands/ralph-cancel.md`
  Cancel the active run for the current workspace.
- Modify: `README.md`
  Document Ralph as a runtime extension, not as a skill replacement.
- Modify: `docs/README.codex.md`
  Document Codex-native Ralph commands and daemon behavior.
- Modify: `.codex-plugin/plugin.json`
  Add one Ralph-oriented default prompt example for discoverability, but keep runtime ownership out of the manifest.

### Tests

- Create: `tests/ralphd/workspace-key.test.mjs`
  Cover repo root, worktree root, cwd fallback, and stable normalization.
- Create: `tests/ralphd/run-registry.test.mjs`
  Cover one-active-run-per-workspace, attach behavior, and terminal-state replacement.
- Create: `tests/ralphd/server.test.mjs`
  Cover health, start, status, attach, pause, resume, cancel, and SSE event flow.
- Create: `tests/ralphd/codex-bridge.test.mjs`
  Cover JSONL normalization, raw log capture, error handling, and managed thread id extraction.
- Create: `tests/ralphd/completion-gate.test.mjs`
  Cover execution, unfinished-work, verification, sentinel, and awaiting-user paths.
- Create: `tests/ralphd/fixtures/fake-codex.mjs`
  Emit controlled JSONL transcripts for bridge and runtime-loop tests.

## Chunk 1: Runtime Foundation

### Task 1: Create workspace keys, daemon state, and run registry

**Files:**
- Modify: `.gitignore`
- Create: `lib/ralphd/daemon-state.mjs`
- Create: `lib/ralphd/workspace-key.mjs`
- Create: `lib/ralphd/run-registry.mjs`
- Create: `tests/ralphd/workspace-key.test.mjs`
- Create: `tests/ralphd/run-registry.test.mjs`

- [ ] **Step 1: Write failing tests for workspace normalization and single-active-run invariants**

Create these test skeletons:

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { buildWorkspaceKey } from '../../lib/ralphd/workspace-key.mjs';
import { createRunRegistry } from '../../lib/ralphd/run-registry.mjs';

test('buildWorkspaceKey prefers repo root over cwd', () => {
  const key = buildWorkspaceKey({
    cwd: 'C:/repo/src',
    repoRoot: 'C:/repo',
    worktreeRoot: null
  });
  assert.equal(key, 'repo:C:/repo');
});

test('run registry returns existing active run for the same workspace', () => {
  const registry = createRunRegistry();
  const first = registry.startRun({ workspaceKey: 'repo:C:/repo', workspacePath: 'C:/repo' });
  const second = registry.startRun({ workspaceKey: 'repo:C:/repo', workspacePath: 'C:/repo' });
  assert.equal(second.result, 'already_running');
  assert.equal(second.runId, first.runId);
});
```

- [ ] **Step 2: Run the focused tests and confirm they fail for missing modules**

Run:

```powershell
cd C:\Users\01467304\.codex\superpowers
node --test tests/ralphd/workspace-key.test.mjs tests/ralphd/run-registry.test.mjs
```

Expected:

- FAIL because `workspace-key.mjs` and `run-registry.mjs` do not exist yet

- [ ] **Step 3: Implement daemon state, workspace key, and registry modules with zero dependencies**

Use these exported shapes:

```js
// lib/ralphd/workspace-key.mjs
export function buildWorkspaceKey({ cwd, repoRoot, worktreeRoot }) {
  const root = worktreeRoot || repoRoot || cwd;
  if (!root) throw new Error('workspace path is required');
  return `repo:${root.replace(/\\/g, '/')}`;
}

// lib/ralphd/run-registry.mjs
export function createRunRegistry() {
  const runs = new Map();
  const activeByWorkspace = new Map();
  return {
    startRun(input) {
      const existing = activeByWorkspace.get(input.workspaceKey);
      if (existing) return { result: 'already_running', runId: existing.runId, phase: existing.phase };
      const run = { runId: `ralph_run_${runs.size + 1}`, phase: 'grounding', round: 0, ...input };
      runs.set(run.runId, run);
      activeByWorkspace.set(input.workspaceKey, run);
      return { result: 'started', runId: run.runId, phase: run.phase };
    },
    getRun(runId) {
      return runs.get(runId) ?? null;
    },
    getActiveRun(workspaceKey) {
      return activeByWorkspace.get(workspaceKey) ?? null;
    },
    updateRun(runId, patch) {
      const current = runs.get(runId);
      if (!current) throw new Error(`unknown run: ${runId}`);
      const next = { ...current, ...patch };
      runs.set(runId, next);
      if (next.phase === 'completed' || next.phase === 'failed' || next.phase === 'cancelled') {
        activeByWorkspace.delete(next.workspaceKey);
      }
      return next;
    }
  };
}
```

Also add this ignore line:

```gitignore
.superpowers/
```

- [ ] **Step 4: Re-run the focused tests and confirm they pass**

Run:

```powershell
cd C:\Users\01467304\.codex\superpowers
node --test tests/ralphd/workspace-key.test.mjs tests/ralphd/run-registry.test.mjs
```

Expected:

- PASS with both test files green

- [ ] **Step 5: Commit the runtime foundation**

```bash
git add .gitignore lib/ralphd/daemon-state.mjs lib/ralphd/workspace-key.mjs lib/ralphd/run-registry.mjs tests/ralphd/workspace-key.test.mjs tests/ralphd/run-registry.test.mjs
git commit -m "Add Ralph runtime state and workspace registry"
```

## Chunk 2: HTTP Control Surface and Fake Engine

### Task 2: Add `ralphd` HTTP endpoints and projected SSE events with a fake engine

**Files:**
- Create: `lib/ralphd/projected-events.mjs`
- Create: `lib/ralphd/fake-engine.mjs`
- Create: `lib/ralphd/server.mjs`
- Create: `tests/ralphd/server.test.mjs`

- [ ] **Step 1: Write failing integration tests for health, start, status, and SSE**

Create this skeleton:

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { startRalphServer } from '../../lib/ralphd/server.mjs';

test('POST /v1/runs/start returns started for a new workspace', async () => {
  const server = await startRalphServer({ port: 0, useFakeEngine: true });
  const response = await fetch(`${server.baseUrl}/v1/runs/start`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', authorization: `Bearer ${server.token}` },
    body: JSON.stringify({
      workspaceKey: 'repo:C:/repo',
      workspacePath: 'C:/repo',
      source: 'codex',
      sourceSessionId: 'thread-1',
      snapshot: { task: 'demo', constraints: [], unfinishedWork: [], verificationRequirements: [] }
    })
  });
  const body = await response.json();
  assert.equal(body.result, 'started');
});
```

- [ ] **Step 2: Run the daemon integration test and confirm it fails**

Run:

```powershell
cd C:\Users\01467304\.codex\superpowers
node --test tests/ralphd/server.test.mjs
```

Expected:

- FAIL because `server.mjs` and supporting modules do not exist yet

- [ ] **Step 3: Implement the daemon server, fake engine, and projected event schema**

Use these response and event shapes:

```js
// POST /v1/runs/start success body
{
  result: 'started',
  runId: 'ralph_run_001',
  workspaceKey: 'repo:C:/repo',
  phase: 'grounding'
}

// already-running body
{
  result: 'already_running',
  runId: 'ralph_run_001',
  workspaceKey: 'repo:C:/repo',
  phase: 'executing',
  attachSuggested: true
}

// projected event
{
  eventId: 'evt_001',
  runId: 'ralph_run_001',
  workspaceKey: 'repo:C:/repo',
  timestamp: '2026-04-24T12:00:00.000Z',
  type: 'run.phase_changed',
  phase: 'executing',
  payload: { round: 1, summary: 'Started fake execution round 1.' }
}
```

Fake engine behavior for this task:

- emit `run.started`
- emit `run.phase_changed` to `grounding`
- emit `run.phase_changed` to `executing`
- emit `run.round_started`
- emit `run.round_summary`
- stop without completion logic

- [ ] **Step 4: Re-run the server integration test and confirm the API and SSE basics pass**

Run:

```powershell
cd C:\Users\01467304\.codex\superpowers
node --test tests/ralphd/server.test.mjs
```

Expected:

- PASS for health, start, status, and at least one SSE round event

- [ ] **Step 5: Commit the daemon skeleton**

```bash
git add lib/ralphd/projected-events.mjs lib/ralphd/fake-engine.mjs lib/ralphd/server.mjs tests/ralphd/server.test.mjs
git commit -m "Add Ralph daemon HTTP and SSE skeleton"
```

## Chunk 3: Codex-Native Control Surface

### Task 3: Add the local `ralphctl` wrapper and Codex command shims

**Files:**
- Create: `scripts/ralphctl.mjs`
- Create: `commands/ralph.md`
- Create: `commands/ralph-status.md`
- Create: `commands/ralph-attach.md`
- Create: `commands/ralph-pause.md`
- Create: `commands/ralph-resume.md`
- Create: `commands/ralph-cancel.md`
- Modify: `README.md`
- Modify: `docs/README.codex.md`
- Modify: `.codex-plugin/plugin.json`
- Create: `tests/ralphd/command-surface.test.mjs`

- [ ] **Step 1: Write failing tests for `ralphctl` verbs and command shim presence**

Create a focused content test:

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

test('ralph command shim calls ralphctl start', () => {
  const body = fs.readFileSync('commands/ralph.md', 'utf8');
  assert.match(body, /scripts\\/ralphctl\\.mjs start/);
});
```

- [ ] **Step 2: Run the command-surface test and confirm it fails**

Run:

```powershell
cd C:\Users\01467304\.codex\superpowers
node --test tests/ralphd/command-surface.test.mjs
```

Expected:

- FAIL because the command files and wrapper do not exist yet

- [ ] **Step 3: Implement `ralphctl` as the thin local client for the daemon**

Use this command surface:

```bash
node scripts/ralphctl.mjs start --workspace "C:/repo" --source codex --source-session thread-1 --snapshot-file .superpowers/runtime/snapshots/current.json
node scripts/ralphctl.mjs status --workspace "C:/repo"
node scripts/ralphctl.mjs pause --workspace "C:/repo"
node scripts/ralphctl.mjs resume --workspace "C:/repo"
node scripts/ralphctl.mjs cancel --workspace "C:/repo"
```

Rules:

- if `daemon.json` is missing or points to a dead process, start `lib/ralphd/server.mjs`
- print concise summaries suitable for command markdown consumption
- do not own loop logic or phase transitions

- [ ] **Step 4: Add command markdown files that treat commands as control surfaces only**

Each command file should instruct the agent to:

- identify the current workspace
- call `node scripts/ralphctl.mjs <verb> ...`
- report the daemon summary back to the user

`commands/ralph.md` must center on:

```markdown
Use the current workspace, build a curated snapshot, call:

`node scripts/ralphctl.mjs start --workspace "<workspace>" --source codex --source-session "<session-id>" --snapshot-file "<snapshot-file>"`

If the result is `already_running`, attach instead of starting a second run.
```

Also update `README.md` and `docs/README.codex.md` with a short Ralph section, and update `.codex-plugin/plugin.json` only by appending one Ralph example to `interface.defaultPrompt`.
Also update `.codex-plugin/plugin.json` by appending one Ralph example to `interface.defaultPrompt`, for example:

```json
"defaultPrompt": [
  "I've got an idea for something I'd like to build.",
  "Let's add a feature to this project.",
  "Use /ralph to keep working in this workspace until the plan is verified or blocked."
]
```

- [ ] **Step 5: Re-run the command-surface test and perform one manual Codex smoke check**

Run:

```powershell
cd C:\Users\01467304\.codex\superpowers
node --test tests/ralphd/command-surface.test.mjs
```

Expected:

- PASS for command file content and `ralphctl` verb coverage

Manual smoke:

- restart Codex
- verify the Ralph command surface is discoverable in the plugin
- run the Ralph entrypoint once and confirm it returns daemon status instead of trying to own the loop itself

- [ ] **Step 6: Commit the command surface**

```bash
git add scripts/ralphctl.mjs commands/ralph.md commands/ralph-status.md commands/ralph-attach.md commands/ralph-pause.md commands/ralph-resume.md commands/ralph-cancel.md README.md docs/README.codex.md .codex-plugin/plugin.json tests/ralphd/command-surface.test.mjs
git commit -m "Add Ralph control commands for Codex"
```

## Chunk 4: Real Codex Turn Handling

### Task 4: Add the Codex bridge and fact normalization layer

**Files:**
- Create: `lib/ralphd/fact-normalizer.mjs`
- Create: `lib/ralphd/codex-bridge.mjs`
- Create: `tests/ralphd/fixtures/fake-codex.mjs`
- Create: `tests/ralphd/codex-bridge.test.mjs`

- [ ] **Step 1: Write failing tests for JSONL parsing and one-turn results**

Create a fixture-driven test with these expectations:

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeCodexJsonl } from '../../lib/ralphd/fact-normalizer.mjs';

test('normalizes todo_list, agent_message, and turn completion facts', () => {
  const facts = normalizeCodexJsonl([
    '{"type":"thread.started","thread_id":"thr_123"}',
    '{"type":"item.updated","item":{"type":"todo_list","items":[{"text":"Task A","completed":false}]}}',
    '{"type":"item.completed","item":{"type":"agent_message","text":"Implemented Task A"}}',
    '{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":2}}'
  ]);
  assert.equal(facts.threadId, 'thr_123');
  assert.equal(facts.todoItems.length, 1);
  assert.equal(facts.assistantSummary, 'Implemented Task A');
});
```

- [ ] **Step 2: Run the focused bridge tests and confirm they fail**

Run:

```powershell
cd C:\Users\01467304\.codex\superpowers
node --test tests/ralphd/codex-bridge.test.mjs
```

Expected:

- FAIL because the bridge and normalizer modules do not exist yet

- [ ] **Step 3: Implement the normalizer and bridge contract**

Bridge result shape:

```js
{
  threadId: 'thr_123',
  turnStatus: 'completed',
  assistantSummary: 'Implemented Task A',
  todoItems: [{ title: 'Task A', status: 'pending' }],
  fileChanges: [],
  verificationFacts: [],
  blockerSignals: [],
  awaitingUserSignals: [],
  rawLogPath: '.superpowers/runtime/logs/ralph_run_001.jsonl'
}
```

Bridge rules:

- support fresh `codex exec --json` for new runs
- support `codex exec resume --json --full-auto <threadId>` for later rounds
- always persist raw JSONL to disk
- never make completion decisions inside the bridge

- [ ] **Step 4: Re-run the bridge tests and confirm they pass**

Run:

```powershell
cd C:\Users\01467304\.codex\superpowers
node --test tests/ralphd/codex-bridge.test.mjs
```

Expected:

- PASS for normalization, thread id extraction, and raw-log capture behavior

- [ ] **Step 5: Commit the Codex bridge**

```bash
git add lib/ralphd/fact-normalizer.mjs lib/ralphd/codex-bridge.mjs tests/ralphd/fixtures/fake-codex.mjs tests/ralphd/codex-bridge.test.mjs
git commit -m "Add Ralph Codex bridge and fact normalization"
```

## Chunk 5: Phase Control and Completion Semantics

### Task 5: Add phase contracts, ledgers, and the four-part completion gate

**Files:**
- Create: `lib/ralphd/phase-contracts.mjs`
- Create: `lib/ralphd/completion-gate.mjs`
- Create: `tests/ralphd/completion-gate.test.mjs`
- Modify: `lib/ralphd/server.mjs`

- [ ] **Step 1: Write failing tests for `awaiting_user`, verification, and sentinel handling**

Create test cases for all required outcomes:

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { evaluateCompletion } from '../../lib/ralphd/completion-gate.mjs';

test('completion requires execution, cleared work, green verification, and sentinel', () => {
  const result = evaluateCompletion({
    turnStatus: 'completed',
    unfinishedWorkCleared: true,
    verificationGreen: true,
    sentinelSeen: true
  });
  assert.equal(result.decision, 'completed');
});

test('awaiting user blocks completion even when execution succeeded', () => {
  const result = evaluateCompletion({
    turnStatus: 'completed',
    unfinishedWorkCleared: true,
    verificationGreen: false,
    sentinelSeen: false,
    awaitingUserSignals: ['Need approval']
  });
  assert.equal(result.decision, 'awaiting_user');
});
```

- [ ] **Step 2: Run the focused completion-gate tests and confirm they fail**

Run:

```powershell
cd C:\Users\01467304\.codex\superpowers
node --test tests/ralphd/completion-gate.test.mjs
```

Expected:

- FAIL because `completion-gate.mjs` does not exist yet

- [ ] **Step 3: Implement the phase prompt contracts and completion gate**

Use these constants and exports:

```js
export const RALPH_SENTINEL = 'SUPERPOWERS_RALPH_COMPLETE';

export function buildPhaseContract({ phase, snapshot, ledgers }) {
  const header = `Phase: ${phase}\nTask: ${snapshot.task}`;
  if (phase === 'verifying') {
    return `${header}\nRun required verification commands, read the output, and emit ${RALPH_SENTINEL} only when all checks are green.`;
  }
  if (phase === 'planning') {
    return `${header}\nRespect approval gates. If user approval is required, report it explicitly instead of continuing.`;
  }
  return `${header}\nAdvance the current task ledger without claiming completion early.`;
}

export function evaluateCompletion(input) {
  if (input.awaitingUserSignals?.length) return { decision: 'awaiting_user', reasons: input.awaitingUserSignals };
  if (input.blockerSignals?.length) return { decision: 'blocked', reasons: input.blockerSignals };
  if (input.turnStatus !== 'completed') return { decision: 'executing', reasons: ['turn not complete'] };
  if (!input.unfinishedWorkCleared) return { decision: 'executing', reasons: ['unfinished work remains'] };
  if (!input.verificationGreen) return { decision: 'executing', reasons: ['verification still red'] };
  if (!input.sentinelSeen) return { decision: 'executing', reasons: ['completion sentinel missing'] };
  return { decision: 'completed', reasons: [] };
}
```

Gate requirements:

- `execution_ok`
- `unfinished_work_cleared`
- `verification_green`
- `completion_sentinel_seen`

Never treat plain text like `done` as sufficient on its own.

- [ ] **Step 4: Re-run the completion-gate tests and confirm they pass**

Run:

```powershell
cd C:\Users\01467304\.codex\superpowers
node --test tests/ralphd/completion-gate.test.mjs
```

Expected:

- PASS for completed, verifying-loop, awaiting-user, and blocked outcomes

- [ ] **Step 5: Commit phase control and completion logic**

```bash
git add lib/ralphd/phase-contracts.mjs lib/ralphd/completion-gate.mjs lib/ralphd/server.mjs tests/ralphd/completion-gate.test.mjs
git commit -m "Add Ralph phase contracts and completion gate"
```

## Chunk 6: Real Runtime Loop and Recovery

### Task 6: Replace the fake engine with the real managed-turn loop and recovery behavior

**Files:**
- Modify: `lib/ralphd/server.mjs`
- Modify: `lib/ralphd/run-registry.mjs`
- Modify: `lib/ralphd/daemon-state.mjs`
- Modify: `scripts/ralphctl.mjs`
- Modify: `tests/ralphd/server.test.mjs`
- Create: `tests/ralphd/runtime-loop.test.mjs`

- [ ] **Step 1: Write failing integration tests for pause, resume, input, cancel, and restart recovery**

Cover these cases:

- cooperative `pause` moves the run to `paused` after the current round boundary
- `resume` reuses the same managed thread id
- `input` advances `awaiting_user` without creating a new run
- daemon restart preserves the active run state from `.superpowers/runtime/`

Use the fake Codex fixture for deterministic round progression before enabling real CLI execution in the same suite.

- [ ] **Step 2: Run the runtime-loop integration suite and confirm it fails**

Run:

```powershell
cd C:\Users\01467304\.codex\superpowers
node --test tests/ralphd/runtime-loop.test.mjs tests/ralphd/server.test.mjs
```

Expected:

- FAIL because pause, resume, input, and recovery behavior are not fully wired yet

- [ ] **Step 3: Implement the real round loop inside the daemon**

The loop must follow this shape:

```js
for (;;) {
  if (run.phase === 'paused' || run.phase === 'awaiting_user' || run.phase === 'blocked') break;
  const contract = buildPhaseContract({ phase: run.phase, snapshot, ledgers });
  const turn = await bridge.runTurn({ run, contract });
  const outcome = evaluateCompletion({ ...turn, ...ledgers });
  ledgers = updateLedgers(ledgers, turn);
  run = registry.updateRun(run.runId, {
    round: run.round + 1,
    phase: outcome.decision === 'completed' ? 'completed' : nextPhaseFromOutcome(run.phase, outcome.decision),
    lastActivityAt: new Date().toISOString()
  });
  persistRun(run, ledgers);
  if (run.phase === 'completed' || run.phase === 'failed' || run.phase === 'cancelled') break;
}
```

Rules:

- one Ralph round equals one managed Codex turn
- only `verifying` may transition to `completed`
- command shell remains a control surface only

- [ ] **Step 4: Re-run the runtime-loop integration suite and perform a local smoke test with real Codex**

Run:

```powershell
cd C:\Users\01467304\.codex\superpowers
node --test tests/ralphd/runtime-loop.test.mjs tests/ralphd/server.test.mjs tests/ralphd/codex-bridge.test.mjs tests/ralphd/completion-gate.test.mjs
```

Expected:

- PASS across registry, server, bridge, gate, and runtime-loop suites

Then perform one local smoke test:

```powershell
cd C:\Users\01467304\.codex\superpowers
node scripts/ralphctl.mjs start --workspace "C:\Users\01467304\.codex\superpowers" --source codex --source-session smoke-thread --snapshot-file ".superpowers/runtime/snapshots/smoke.json"
node scripts/ralphctl.mjs status --workspace "C:\Users\01467304\.codex\superpowers"
```

Expected:

- daemon starts
- one run is created or attached
- status shows phase and round instead of raw transport output

- [ ] **Step 5: Commit the real Ralph loop**

```bash
git add lib/ralphd/server.mjs lib/ralphd/run-registry.mjs lib/ralphd/daemon-state.mjs scripts/ralphctl.mjs tests/ralphd/runtime-loop.test.mjs tests/ralphd/server.test.mjs
git commit -m "Add Ralph managed loop and recovery"
```

## Final Verification

- [ ] **Step 1: Run the full Ralph runtime test set**

Run:

```powershell
cd C:\Users\01467304\.codex\superpowers
node --test tests/ralphd/*.test.mjs
```

Expected:

- PASS for all Ralph runtime tests

- [ ] **Step 2: Perform a manual Codex smoke test**

Manual checklist:

- restart Codex so new command shims are discovered
- invoke the Ralph command surface
- confirm start or attach behavior for the current workspace
- confirm repeated invocation re-attaches instead of creating a second active run
- confirm `pause`, `resume`, and `cancel` report daemon-owned state transitions

- [ ] **Step 3: Confirm docs match shipped behavior**

Verify:

- `README.md` describes Ralph as a daemon-backed runtime, not a skill
- `docs/README.codex.md` documents command usage and daemon behavior
- no doc claims WebCode or Feishu integration is already implemented in this repository

## Follow-On Work After This Plan

This plan intentionally stops at a stable Superpowers runtime surface. The following must be implemented separately:

- WebCode daemon client and UI integration
- Feishu card actions mapped onto daemon control verbs
- external-host snapshot builders specific to those codebases
