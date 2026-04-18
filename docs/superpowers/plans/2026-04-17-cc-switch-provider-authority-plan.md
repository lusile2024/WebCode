# CC Switch Provider Authority Implementation Plan

Date: 2026-04-17
Depends on: `docs/superpowers/specs/2026-04-17-cc-switch-provider-authority-design.md`

## Objective

Implement `cc-switch` as the only provider authority for Claude Code, Codex, and OpenCode in WebCode, while giving each managed-tool session a pinned provider snapshot and an explicit sync action.

## Phase 1: Session metadata and schema

Extend session persistence so a managed-tool session can describe the config snapshot it is pinned to.

Changes:

- add session-level provider snapshot fields to `ChatSessionEntity`
- update `SessionHistory` and mapping code to carry the same metadata
- add `ChatSession` schema migration in `DatabaseInitializer`
- add repository update helpers for snapshot metadata

## Phase 2: Snapshot materialization in execution path

Change `CliExecutorService` so managed tools use session-local snapshots instead of current global live config on every launch.

Changes:

- keep `ICcSwitchService` as the read-only source of current machine-active provider
- on first managed-tool execution, copy the current live config into the session workspace
- store pinned provider metadata and snapshot path on the session
- on later launches, inject the session-local snapshot instead of relying on the global live config
- if the pinned snapshot is missing, block execution and require explicit sync

## Phase 3: Manual sync flow

Expose a deliberate path for moving an existing session to the current `cc-switch` active provider.

Changes:

- add a service method that refreshes a session snapshot from current `cc-switch`
- update snapshot metadata and timestamp after sync
- keep sync explicit; never switch an existing session automatically

## Phase 4: Setup and runtime status copy cleanup

Refine UX copy so it matches the new semantics and fix reported untranslated text.

Changes:

- update setup review copy to stop saying WebCode always uses the current active provider at runtime
- keep setup as read-only `cc-switch` readiness review
- update the read-only status modal wording where needed
- add missing/incorrect localization keys for the setup review page and status labels

## Phase 5: Session UI integration

Make the pinned-session behavior visible and actionable.

Changes:

- show pinned provider information in the session list for managed-tool sessions
- add a sync action in session UI
- surface friendly sync-required errors when a snapshot is missing or broken

## Phase 6: Verification

Add coverage for the new semantics and verify build output.

Changes:

- add `CliExecutorService` tests for:
  - first-run snapshot creation
  - reusing the pinned snapshot after `cc-switch` changes
  - explicit sync updating the snapshot
  - blocking launch when a pinned snapshot is missing
- extend `CcSwitchService` tests if needed for richer metadata
- update localization tests for new Chinese copy
- build the solution
- run targeted tests
- generate publish output / installer after verification passes

## Expected File Groups

- Domain interfaces/models:
  - `WebCodeCli.Domain/Domain/Model/SessionHistory.cs`
  - `WebCodeCli.Domain/Domain/Service/ICliExecutorService.cs`
  - `WebCodeCli.Domain/Domain/Service/CliExecutorService.cs`
  - `WebCodeCli.Domain/Domain/Service/SessionHistoryManager.cs`
  - `WebCodeCli.Domain/Repositories/Base/ChatSession/*`
  - `WebCodeCli.Domain/Common/Extensions/DatabaseInitializer.cs`
- Web implementation:
  - `WebCodeCli/Services/CcSwitchService.cs`
- Setup and session UI:
  - `WebCodeCli/Pages/Setup.razor`
  - `WebCodeCli/Pages/Setup.razor.cs`
  - `WebCodeCli/Pages/CodeAssistant.razor`
  - `WebCodeCli/Pages/CodeAssistant.razor.cs`
  - `WebCodeCli/Components/SessionListPanel.razor`
  - `WebCodeCli/Components/EnvironmentVariableConfigModal.razor`
- Localization:
  - `WebCodeCli/Resources/Localization/*.json`
  - `WebCodeCli/wwwroot/Resources/Localization/*.json`
- Tests:
  - `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`
  - `tests/WebCodeCli.Tests/CcSwitchServiceTests.cs`
  - `tests/WebCodeCli.Tests/LocalizationResourceTests.cs`
