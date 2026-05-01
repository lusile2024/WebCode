# Codex Thread Provider Sync Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a current-session, current-Codex-thread provider resync flow that rewrites only the local session copy of Codex rollout metadata and sqlite state, while leaving the global Codex home and unrelated threads untouched.

**Architecture:** Keep `CliExecutorService` as the session-level orchestration entrypoint, but move the thread-specific rewrite work into a dedicated `CodexThreadProviderSyncService`. The new public sync path resolves the active provider from cc-switch, resolves the current CLI thread via existing recovery logic, seeds the local session snapshot through the existing materialization flow, then rewrites only matching local rollout files and the local `state_5.sqlite` row for that thread. Desktop web, mobile web, and Feishu all call the same backend method and only differ in how they surface the returned result.

**Tech Stack:** ASP.NET Core, Blazor Server, `System.Data.SQLite.Core`, `SqlSugar`, xUnit v3, existing WebCode service DI scanning.

Depends on:
- `docs/superpowers/specs/2026-04-30-codex-thread-provider-sync-design.md`

---

## File Map

### Core sync surface

- Create: `WebCodeCli.Domain/Domain/Model/CodexThreadProviderSyncRequest.cs`
  Request contract for the session workspace, resolved thread id, and active provider id.
- Create: `WebCodeCli.Domain/Domain/Model/CodexThreadProviderSyncResult.cs`
  Result contract that carries the sync summary, updated files, sqlite outcome, and a user-facing message.
- Create: `WebCodeCli.Domain/Domain/Service/ICodexThreadProviderSyncService.cs`
  Focused interface for local rollout-file and sqlite rewrites.
- Create: `WebCodeCli.Domain/Domain/Service/CodexThreadProviderSyncService.cs`
  Thread-scoped rewrite engine for local `sessions` / `archived_sessions` files and `state_5.sqlite`.
- Modify: `WebCodeCli.Domain/WebCodeCli.Domain.csproj`
  Add the SQLite package used by the new local sqlite rewrite service.
- Modify: `WebCodeCli.Domain/Domain/Service/ICliExecutorService.cs`
  Add the new thread-provider sync entrypoint while keeping the existing snapshot-materialization method intact.
- Modify: `WebCodeCli.Domain/Domain/Service/CliExecutorService.cs`
  Resolve thread ids, seed the session-local Codex snapshot, delegate to the thread sync service, and return the rich result.

### UI entrypoints

- Modify: `WebCodeCli/Pages/CodeAssistant.razor.cs`
  Call the new sync entrypoint, reload sessions, and surface success vs partial-success messaging.
- Modify: `WebCodeCli/Pages/CodeAssistantMobile.razor.cs`
  Mirror the desktop sync handling and message surfacing.
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs`
  Route the Feishu sync action through the same backend entrypoint and map the returned result to success or warning toast.

### Tests

- Create: `WebCodeCli.Domain.Tests/CodexThreadProviderSyncServiceTests.cs`
  Focused tests for local rewrite, source seeding, thread identity checks, sqlite update, missing sqlite, and lock handling.
- Modify: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`
  Add orchestration tests for the new entrypoint and its thread-id resolution behavior.
- Modify: `WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs`
  Update the sync-action coverage to assert the new result-based flow and partial-success handling.
- Modify: `WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs`
  Update all `ICliExecutorService` test doubles to implement the new method.

### Explicit non-goals

- Do not mutate global `~/.codex/config.toml`.
- Do not rewrite other Codex threads.
- Do not switch providers inside cc-switch.
- Do not add a provider picker or a separate global sync screen.
- Do not create a new sqlite file if the local `state_5.sqlite` is absent.

---

## Chunk 1: Build the Thread Sync Engine

### Task 1: Add the request/result contracts and the local rewrite service

**Files:**
- Create: `WebCodeCli.Domain/Domain/Model/CodexThreadProviderSyncRequest.cs`
- Create: `WebCodeCli.Domain/Domain/Model/CodexThreadProviderSyncResult.cs`
- Create: `WebCodeCli.Domain/Domain/Service/ICodexThreadProviderSyncService.cs`
- Create: `WebCodeCli.Domain/Domain/Service/CodexThreadProviderSyncService.cs`
- Modify: `WebCodeCli.Domain/WebCodeCli.Domain.csproj`
- Test: `WebCodeCli.Domain.Tests/CodexThreadProviderSyncServiceTests.cs`

- [ ] **Step 1: Write the failing service tests first**

Add tests that prove:

- a local rollout file whose first line has `session_meta.payload.id == threadId` is rewritten to the active provider id
- filename matching is only a candidate filter, not the identity check
- a local rollout file for another thread remains unchanged
- when the local snapshot is missing but the source `~/.codex` has the current thread file, the service seeds the local copy first and then rewrites only the current thread
- `state_5.sqlite` updates only `threads.model_provider` for `WHERE id = @threadId`
- a missing local `state_5.sqlite` is a partial success, not a hard failure
- a locked rollout file or locked sqlite database is skipped and reported, not fatal
- a missing current-thread rollout after seeding fails fast instead of rewriting another thread

Use temp directories and real files. For sqlite, create a small `threads` table with at least `id`, `model_provider`, `cwd`, and `archived`, then verify that only the current row changes.

- [ ] **Step 2: Run the focused test command and confirm it fails**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CodexThreadProviderSyncServiceTests"
```

Expected:

- FAIL because the request/result contracts and the service do not exist yet

- [ ] **Step 3: Implement the new contracts and service**

Implement:

- a request object that carries the session workspace path, resolved CLI thread id, and target provider id
- a result object that carries the updated rollout files, skipped rollout files, sqlite outcome, `SeededFromSource`, `HasWarnings`, and a user-facing message
- a singleton `CodexThreadProviderSyncService` that:
  - resolves the source Codex home using the same environment precedence as the existing launch path
  - searches `sessions` and `archived_sessions` under both source and target roots
  - validates `session_meta.payload.id` before rewriting anything
  - rewrites only the first-line `payload.model_provider`
  - leaves the rest of each rollout file untouched
  - updates only `state_5.sqlite` when it exists
  - treats missing or locked files as warnings, not global failures

Keep the local rewrite logic isolated from cc-switch and from the global Codex home.

- [ ] **Step 4: Re-run the focused test command and confirm it passes**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CodexThreadProviderSyncServiceTests"
```

Expected:

- PASS

- [ ] **Step 5: Commit the thread-sync engine chunk**

```powershell
git add WebCodeCli.Domain/Domain/Model/CodexThreadProviderSyncRequest.cs WebCodeCli.Domain/Domain/Model/CodexThreadProviderSyncResult.cs WebCodeCli.Domain/Domain/Service/ICodexThreadProviderSyncService.cs WebCodeCli.Domain/Domain/Service/CodexThreadProviderSyncService.cs WebCodeCli.Domain/WebCodeCli.Domain.csproj WebCodeCli.Domain.Tests/CodexThreadProviderSyncServiceTests.cs
git commit -m "feat: add codex thread provider sync engine"
```

### Task 2: Add the new executor entrypoint without breaking the existing snapshot sync path

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/ICliExecutorService.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/CliExecutorService.cs`
- Modify: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`

- [ ] **Step 1: Write the failing orchestration tests**

Add tests that prove:

- the new entrypoint resolves the current thread through the existing `GetCliThreadId` recovery path
- the new entrypoint still seeds the session-local Codex snapshot before rewriting the thread artifacts
- the new entrypoint updates only the current thread, not any other local thread
- a missing thread id fails fast with a clear error
- existing snapshot-materialization tests keep working unchanged

Keep the old `SyncSessionCcSwitchSnapshotAsync` method available for snapshot-only callers. The new method should be additive, not a breaking rename.

- [ ] **Step 2: Run the focused orchestration tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CliExecutorServiceTests&FullyQualifiedName~SyncCodexThreadProviderAsync"
```

Expected:

- FAIL because the new entrypoint is not wired yet

- [ ] **Step 3: Implement the new public sync method**

Add `SyncCodexThreadProviderAsync(...)` to `ICliExecutorService` and `CliExecutorService`.

Implementation rules:

- resolve the thread id with the existing recovery path first
- call the existing snapshot sync method to materialize the session-local Codex snapshot if needed
- resolve `ICodexThreadProviderSyncService` from the existing service provider instead of changing the constructor signature
- pass the session workspace, current thread id, and the active provider id into the new service
- return the rich result object so callers can show success vs partial success

Do not remove or rename the existing snapshot-materialization method in this step.

- [ ] **Step 4: Re-run the focused orchestration tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CliExecutorServiceTests&FullyQualifiedName~SyncCodexThreadProviderAsync"
```

Expected:

- PASS

- [ ] **Step 5: Commit the executor chunk**

```powershell
git add WebCodeCli.Domain/Domain/Service/ICliExecutorService.cs WebCodeCli.Domain/Domain/Service/CliExecutorService.cs WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs
git commit -m "feat: wire codex thread provider sync into executor"
```

---

## Chunk 2: Route the Desktop, Mobile, and Feishu Entry Points

### Task 3: Update the desktop and mobile session sync handlers to use the new result

**Files:**
- Modify: `WebCodeCli/Pages/CodeAssistant.razor.cs`
- Modify: `WebCodeCli/Pages/CodeAssistantMobile.razor.cs`

- [ ] **Step 1: Write the compile-driving tests or assertions that expose the old call pattern**

There are no existing page-level unit tests in this repo, so use compile-driven TDD here and keep the validation focused on the sync handler behavior:

- the button handler should call `SyncCodexThreadProviderAsync`
- it should still reload the session list after the sync finishes
- it should surface the returned message to the user
- it should keep the current session merge logic intact

- [ ] **Step 2: Run a solution build and confirm the current code still compiles before the change**

Run:

```powershell
dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
```

Expected:

- PASS before the change, or compile errors only in the sync handlers after the new interface method is added

- [ ] **Step 3: Update the desktop and mobile handlers**

Change both `SyncSessionProviderAsync(SessionHistory session)` methods to:

- call `SyncCodexThreadProviderAsync(...)`
- keep the existing `_syncingSessionId` guard
- keep the existing `LoadSessionsAsync` / `LoadSessions` refresh
- keep the existing snapshot merge logic
- show the returned message to the user, with a warning style when `HasWarnings` is true

Keep the UI wiring simple. Do not add a separate provider picker or a second sync button.

- [ ] **Step 4: Re-run the solution build and confirm the pages compile**

Run:

```powershell
dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
```

Expected:

- PASS

- [ ] **Step 5: Commit the desktop/mobile wiring chunk**

```powershell
git add WebCodeCli/Pages/CodeAssistant.razor.cs WebCodeCli/Pages/CodeAssistantMobile.razor.cs
git commit -m "feat: route session sync through codex thread provider sync"
```

### Task 4: Update the Feishu sync action to use the same backend method and surface partial success

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs`
- Modify: `WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs`

- [ ] **Step 1: Write the failing Feishu sync tests**

Add or update tests that prove:

- the `sync_session_provider` action calls the new thread-provider sync method
- a full success still refreshes the session manager card and returns a success toast
- a partial success returns a warning toast while still refreshing the card
- missing thread id and unsupported-tool cases still return the existing error/warning patterns

Update the existing `HandleCardActionAsync_SyncSessionProvider_InvokesCliExecutorAndRefreshesSessionManagerCard` test rather than inventing a second path if possible.

- [ ] **Step 2: Run the focused Feishu tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuCardActionServiceTests&FullyQualifiedName~SyncSessionProvider"
```

Expected:

- FAIL because the handler still calls the old method and the stubs do not expose the new result yet

- [ ] **Step 3: Update the Feishu handler**

Change `HandleSyncSessionProviderAsync(...)` to:

- call `SyncCodexThreadProviderAsync(...)`
- choose `success` vs `warning` toast based on `HasWarnings`
- keep the session-manager-card refresh after the sync
- keep the existing error handling for missing chat/session binding

The Feishu path should not special-case global Codex state. It should reuse the same backend result as desktop and mobile.

- [ ] **Step 4: Re-run the focused Feishu tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuCardActionServiceTests&FullyQualifiedName~SyncSessionProvider"
```

Expected:

- PASS

- [ ] **Step 5: Commit the Feishu wiring chunk**

```powershell
git add WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs
git commit -m "feat: surface codex thread provider sync in feishu"
```

---

## Chunk 3: Update Test Doubles, Run the Regression Suite, and Smoke-Test the Result

### Task 5: Update the remaining `ICliExecutorService` test doubles

**Files:**
- Modify: `WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs`
- Modify: any other `ICliExecutorService` test double surfaced by a solution-wide search

- [ ] **Step 1: Search for every remaining `ICliExecutorService` stub**

Run:

```powershell
rg -n "class .*: ICliExecutorService|: ICliExecutorService" WebCodeCli.Domain.Tests WebCodeCli tests WebCodeCli.Domain -S
```

Expected:

- all stubs are accounted for before the build step

- [ ] **Step 2: Add the new method to every stub**

Each stub should return a canned successful `CodexThreadProviderSyncResult` unless a test needs a specific failure or warning case.

The stubs do not need to simulate sqlite or rollout rewriting unless the test is specifically about sync behavior.

- [ ] **Step 3: Run the build and both test projects**

Run:

```powershell
dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj
dotnet test D:\VSWorkshop\WebCode\tests\WebCodeCli.Tests\WebCodeCli.Tests.csproj
```

Expected:

- PASS for the build and both test projects

- [ ] **Step 4: Perform the manual smoke checks**

Check these flows on the real app:

- desktop web sync on an already-materialized session
- desktop web sync on a session that still needs local seeding
- mobile web sync on the same two cases
- Feishu `sync_session_provider` on the same two cases
- locked rollout file returns a partial-success warning
- missing `state_5.sqlite` returns a partial-success warning and does not create a new database

For each case, verify:

- only the current thread changes
- unrelated local threads remain untouched
- the global Codex home remains unchanged
- the user sees success when everything is rewritten and a warning when anything is skipped

- [ ] **Step 5: Commit the regression pass**

```powershell
git add WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs WebCodeCli.Domain.Tests WebCodeCli tests WebCodeCli
git commit -m "test: cover codex thread provider sync regression cases"
```

Plan complete and saved to `docs/superpowers/plans/2026-04-30-codex-thread-provider-sync-plan.md`. Ready to execute?
