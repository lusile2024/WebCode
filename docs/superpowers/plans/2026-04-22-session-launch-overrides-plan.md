# Session Launch Overrides Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add session-scoped launch overrides for managed CLI tools so each WebCode session can pin its own model selection, and Codex sessions can also pin reasoning effort, without changing other sessions or cc-switch provider authority.

**Architecture:** Persist tool-scoped launch overrides on the `ChatSession` record as JSON and round-trip them through `SessionHistory`. At launch time, keep the existing provider snapshot flow intact, then apply the session override for the effective tool: patch the session-local Codex snapshot config for `model` and `model_reasoning_effort`, and append `--model` for Claude Code and OpenCode. Saving or clearing overrides must call a session-only runtime reset that clears CLI thread/process state without deleting workspace files, messages, or provider snapshot metadata.

**Tech Stack:** ASP.NET Core, Blazor Server, SqlSugar, `WebCodeCli.Domain` services/adapters, Feishu card kit integration, xUnit

Depends on: `docs/superpowers/specs/2026-04-22-session-launch-overrides-design.md`

---

## File Map

### Shared session override model and persistence

- Create: `WebCodeCli.Domain/Domain/Model/SessionToolLaunchOverride.cs`
  Represent one tool-scoped override entry (`model`, optional `reasoningEffort`).
- Modify: `WebCodeCli.Domain/Domain/Model/SessionHistory.cs`
  Expose typed `ToolLaunchOverrides` data on the domain session object.
- Modify: `WebCodeCli.Domain/Repositories/Base/ChatSession/ChatSessionEntity.cs`
  Add the persisted JSON column/property for launch overrides.
- Modify: `WebCodeCli.Domain/Domain/Service/SessionHistoryManager.cs`
  Serialize and deserialize `ToolLaunchOverridesJson` and keep existing cc-switch snapshot merge behavior intact.
- Modify: `WebCodeCli.Domain/Common/Extensions/DatabaseInitializer.cs`
  Add incremental schema backfill for the new `ToolLaunchOverridesJson` column.
- Create: `WebCodeCli.Domain/Domain/Service/SessionLaunchOverrideHelper.cs`
  Centralize tool normalization, validation, blank-field clearing, effective-tool lookup, and display-ready override extraction.
- Create: `WebCodeCli.Domain.Tests/SessionHistoryManagerTests.cs`
  Cover persistence round-trip and JSON cleanup behavior.
- Create: `WebCodeCli.Domain.Tests/SessionLaunchOverrideHelperTests.cs`
  Cover validation, normalization, clearing, and effective override lookup.

### Session-only runtime reset and launch merge

- Modify: `WebCodeCli.Domain/Repositories/Base/ChatSession/IChatSessionRepository.cs`
  Allow clearing persisted `CliThreadId` for a session.
- Modify: `WebCodeCli.Domain/Repositories/Base/ChatSession/ChatSessionRepository.cs`
  Implement nullable/reset thread-id persistence without touching unrelated session data.
- Modify: `WebCodeCli.Domain/Domain/Service/ICliExecutorService.cs`
  Add a session-only runtime reset API for launch-setting changes.
- Modify: `WebCodeCli.Domain/Domain/Service/CliExecutorService.cs`
  Resolve the effective session launch override, apply it after provider snapshot materialization, and reset only the current session runtime when settings change.
- Modify: `WebCodeCli.Domain/Domain/Service/Adapters/CliOutputEvent.cs`
  Extend `CliSessionContext` so adapters can receive resolved launch overrides without learning about session storage.
- Modify: `WebCodeCli.Domain/Domain/Service/Adapters/ClaudeCodeAdapter.cs`
  Append `--model <value>` when a session-scoped override exists.
- Modify: `WebCodeCli.Domain/Domain/Service/Adapters/OpenCodeAdapter.cs`
  Append `--model <value>` when a session-scoped override exists.
- Modify: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`
  Cover runtime reset semantics, Codex snapshot patching, Claude/OpenCode argument merge, and provider-sync preservation.

### Desktop web

- Create: `WebCodeCli/Components/Dialogs/SessionLaunchOverrideDialog.razor`
  Dedicated session-level settings dialog with tool-specific fields and clear/save actions.
- Modify: `WebCodeCli/Components/SessionListPanel.razor`
  Add `模型设置` action and show override summary under the pinned provider summary.
- Modify: `WebCodeCli/Pages/CodeAssistant.razor`
  Mount the new dialog and pass the new session-list callbacks/labels.
- Modify: `WebCodeCli/Pages/CodeAssistant.razor.cs`
  Manage dialog state, load/save/clear overrides for the target session, call the shared helper, and trigger session-only runtime reset.

### Mobile web

- Modify: `WebCodeCli/Pages/CodeAssistantMobile.razor`
  Add override summary and `模型设置` action to the mobile session drawer.
- Modify: `WebCodeCli/Pages/CodeAssistantMobile.razor.cs`
  Reuse the same load/save/clear/reset flow as desktop.

### Localization

- Modify: `WebCodeCli/Resources/Localization/zh-CN.json`
- Modify: `WebCodeCli/Resources/Localization/en-US.json`
- Modify: `WebCodeCli/Resources/Localization/ja-JP.json`
- Modify: `WebCodeCli/Resources/Localization/ko-KR.json`
- Modify: `WebCodeCli/wwwroot/Resources/Localization/zh-CN.json`
- Modify: `WebCodeCli/wwwroot/Resources/Localization/en-US.json`
- Modify: `WebCodeCli/wwwroot/Resources/Localization/ja-JP.json`
- Modify: `WebCodeCli/wwwroot/Resources/Localization/ko-KR.json`
  Add labels, helper text, validation text, reset-success text, and summary labels for model/reasoning.

### Feishu session manager and status cards

- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs`
  Add action payload fields for expand/collapse state and launch-settings form/save/clear actions.
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs`
  Default the session manager to 3 sessions, implement expand/collapse, add launch-settings form cards, save/clear handlers, and show override summaries.
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs`
  Show the active override summary in the current execution chrome/status text.
- Modify: `WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs`
  Cover folded session manager, expand/collapse, form actions, save/clear behavior, and runtime reset.
- Modify: `WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs`
  Cover current-status override summary display.

### Explicit non-goals for this implementation

- Do not add a new controller or API surface just for launch settings; `SessionHistory` round-trip already covers the web persistence path.
- Do not add bUnit or any new UI test dependency.
- Do not let launch overrides write to live cc-switch config or any machine-global provider state.
- Do not reuse `CleanupSessionWorkspace` for settings changes.

## Chunk 1: Persist Tool-Scoped Launch Overrides

### Task 1: Add the persisted session override shape and round-trip it through `SessionHistory`

**Files:**
- Create: `WebCodeCli.Domain/Domain/Model/SessionToolLaunchOverride.cs`
- Modify: `WebCodeCli.Domain/Domain/Model/SessionHistory.cs`
- Modify: `WebCodeCli.Domain/Repositories/Base/ChatSession/ChatSessionEntity.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/SessionHistoryManager.cs`
- Modify: `WebCodeCli.Domain/Common/Extensions/DatabaseInitializer.cs`
- Test: `WebCodeCli.Domain.Tests/SessionHistoryManagerTests.cs`

- [ ] **Step 1: Write failing persistence round-trip tests first**

Add tests that prove:

- a session can save `codex` override data with both `model` and `reasoningEffort`
- a session can save `claude-code` and `opencode` overrides with `model` only
- saving an empty override payload removes the JSON column content instead of leaving `{}` noise
- loading old sessions with `ToolLaunchOverridesJson == null` still returns an empty typed object

Use a typed shape like:

```csharp
public sealed class SessionToolLaunchOverride
{
    public string? Model { get; set; }
    public string? ReasoningEffort { get; set; }
}
```

- [ ] **Step 2: Run the focused persistence tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~SessionHistoryManagerTests"
```

Expected:

- FAIL because `SessionHistory`, `ChatSessionEntity`, and the manager mapping do not know about launch overrides yet

- [ ] **Step 3: Add the entity/domain properties and JSON mapping**

Use these shapes:

```csharp
public string? ToolLaunchOverridesJson { get; set; }
```

```csharp
public Dictionary<string, SessionToolLaunchOverride> ToolLaunchOverrides { get; set; }
    = new(StringComparer.OrdinalIgnoreCase);
```

Serialization rules:

- use camelCase JSON keys to match the approved design (`reasoningEffort`, not `ReasoningEffort`)
- store `null` when the dictionary is empty
- deserialize invalid or blank JSON as an empty dictionary instead of throwing on session load

- [ ] **Step 4: Add the incremental schema backfill**

In `DatabaseInitializer.EnsureChatSessionSchema(...)`, add:

```csharp
EnsureColumnIfNotExists(db, "ChatSession", "ToolLaunchOverridesJson", "TEXT NULL", logger);
```

Do not add a separate migration framework or a second table.

- [ ] **Step 5: Re-run the persistence tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~SessionHistoryManagerTests"
```

Expected:

- PASS

- [ ] **Step 6: Commit the persistence shape**

```powershell
$commitMessage = @"
Persist session-scoped launch overrides on chat sessions

Constraint: Provider authority must remain owned by cc-switch
Confidence: medium
Scope-risk: moderate
Directive: Keep launch overrides tool-scoped; do not collapse them into a single session-global model field
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter FullyQualifiedName~SessionHistoryManagerTests
Not-tested: Web, mobile, and Feishu interaction paths
"@
git add WebCodeCli.Domain/Domain/Model/SessionToolLaunchOverride.cs WebCodeCli.Domain/Domain/Model/SessionHistory.cs WebCodeCli.Domain/Repositories/Base/ChatSession/ChatSessionEntity.cs WebCodeCli.Domain/Domain/Service/SessionHistoryManager.cs WebCodeCli.Domain/Common/Extensions/DatabaseInitializer.cs WebCodeCli.Domain.Tests/SessionHistoryManagerTests.cs
git commit -m $commitMessage
```

### Task 2: Add one shared helper for validation, clearing, and effective-tool lookup

**Files:**
- Create: `WebCodeCli.Domain/Domain/Service/SessionLaunchOverrideHelper.cs`
- Test: `WebCodeCli.Domain.Tests/SessionLaunchOverrideHelperTests.cs`

- [ ] **Step 1: Write failing helper tests**

Cover:

- tool normalization: `claude` -> `claude-code`, `opencode-cli` -> `opencode`
- `codex` accepts `model` and `reasoningEffort`
- `claude-code` and `opencode` reject non-blank `reasoningEffort`
- `reasoningEffort` only accepts `low`, `medium`, `high`, `xhigh`
- blank input clears a field, clearing the last field removes the tool entry
- effective lookup uses `session.CcSwitchSnapshotToolId ?? session.ToolId`

- [ ] **Step 2: Run the helper tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~SessionLaunchOverrideHelperTests"
```

Expected:

- FAIL because the helper does not exist yet

- [ ] **Step 3: Implement the helper as the single normalization/validation entry point**

Include methods along these lines:

```csharp
public static string NormalizeToolId(string? toolId);
public static bool SupportsLaunchOverrides(string? toolId);
public static SessionToolLaunchOverride? GetEffectiveOverride(SessionHistory session, string? toolId = null);
public static Dictionary<string, SessionToolLaunchOverride> ApplyOverride(
    IReadOnlyDictionary<string, SessionToolLaunchOverride>? currentOverrides,
    string toolId,
    string? model,
    string? reasoningEffort);
```

Rules:

- preserve unrelated tool entries
- trim whitespace before saving
- throw validation errors before persistence rather than letting each UI surface invent its own rules

- [ ] **Step 4: Re-run the helper tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~SessionLaunchOverrideHelperTests"
```

Expected:

- PASS

- [ ] **Step 5: Commit the helper chunk**

```powershell
$commitMessage = @"
Centralize launch override validation and tool normalization

Constraint: Web and Feishu must enforce identical rules for the same session data
Rejected: Duplicate validation per UI surface | would drift and break parity
Confidence: high
Scope-risk: narrow
Directive: Reuse SessionLaunchOverrideHelper from every save path; do not re-encode validation in pages or card handlers
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter FullyQualifiedName~SessionLaunchOverrideHelperTests
Not-tested: End-to-end save flows
"@
git add WebCodeCli.Domain/Domain/Service/SessionLaunchOverrideHelper.cs WebCodeCli.Domain.Tests/SessionLaunchOverrideHelperTests.cs
git commit -m $commitMessage
```

## Chunk 2: Reset Runtime And Merge Overrides Into Launches

### Task 3: Add a session-only runtime reset path for launch-setting changes

**Files:**
- Modify: `WebCodeCli.Domain/Repositories/Base/ChatSession/IChatSessionRepository.cs`
- Modify: `WebCodeCli.Domain/Repositories/Base/ChatSession/ChatSessionRepository.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/ICliExecutorService.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/CliExecutorService.cs`
- Test: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`

- [ ] **Step 1: Write failing tests for session-only runtime reset**

Cover these expectations:

- `ResetSessionRuntimeAsync(sessionId)` clears the in-memory CLI thread id
- the same call clears persisted `ChatSession.CliThreadId`
- the session workspace directory still exists after reset
- the reset path does not call `CleanupSessionWorkspace`

- [ ] **Step 2: Run the focused executor tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CliExecutorServiceTests"
```

Expected:

- FAIL because the repository cannot clear a thread id and the executor reset method does not exist yet

- [ ] **Step 3: Allow clearing persisted thread ids**

Change one of these contracts:

```csharp
Task<bool> UpdateCliThreadIdAsync(string sessionId, string? cliThreadId);
```

or

```csharp
Task<bool> ClearCliThreadIdAsync(string sessionId);
```

Prefer the smaller diff that keeps callers obvious.

- [ ] **Step 4: Implement a session-only runtime reset method in the executor**

Add an explicit API:

```csharp
Task ResetSessionRuntimeAsync(string sessionId, CancellationToken cancellationToken = default);
```

Implementation rules:

- call `_processManager.CleanupSessionProcesses(sessionId)`
- remove the session entry from `_cliThreadIds`
- clear persisted `CliThreadId` in the repository
- do not delete workspace directories
- do not modify chat messages
- do not modify provider snapshot metadata

- [ ] **Step 5: Re-run the executor tests and confirm the reset behavior passes**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CliExecutorServiceTests"
```

Expected:

- PASS for the reset-specific assertions

- [ ] **Step 6: Commit the runtime reset chunk**

```powershell
$commitMessage = @"
Reset only the current session runtime when launch settings change

Constraint: Changing model settings must not delete workspace files or touch sibling sessions
Rejected: Reuse CleanupSessionWorkspace | too destructive for a settings save
Confidence: high
Scope-risk: moderate
Directive: Keep runtime reset scoped to process and thread state only
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter FullyQualifiedName~CliExecutorServiceTests
Not-tested: Feishu-triggered reset flow
"@
git add WebCodeCli.Domain/Repositories/Base/ChatSession/IChatSessionRepository.cs WebCodeCli.Domain/Repositories/Base/ChatSession/ChatSessionRepository.cs WebCodeCli.Domain/Domain/Service/ICliExecutorService.cs WebCodeCli.Domain/Domain/Service/CliExecutorService.cs WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs
git commit -m $commitMessage
```

### Task 4: Merge the effective override into managed-tool launch configuration

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Adapters/CliOutputEvent.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/CliExecutorService.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Adapters/ClaudeCodeAdapter.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Adapters/OpenCodeAdapter.cs`
- Test: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`

- [ ] **Step 1: Write failing launch-merge tests**

Add tests for:

- managed Codex session with override patches the session-local `.codex/config.toml` so `model` and `model_reasoning_effort` match the session override while provider-related settings remain intact
- Claude Code launch arguments append exactly one `--model <value>` when the session override exists
- OpenCode launch arguments append exactly one `--model <value>` when the session override exists
- `SyncSessionCcSwitchSnapshotAsync(...)` preserves the stored launch overrides for the session

- [ ] **Step 2: Run the focused executor tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CliExecutorServiceTests"
```

Expected:

- FAIL because launch override data is not resolved or applied yet

- [ ] **Step 3: Extend `CliSessionContext` with resolved launch override fields**

Add fields like:

```csharp
public string? LaunchModelOverride { get; set; }
public string? LaunchReasoningEffortOverride { get; set; }
```

Do not put raw JSON or whole `SessionHistory` objects into adapter context.

- [ ] **Step 4: Resolve the effective override after session snapshot preparation**

In `CliExecutorService`, after `EnsureManagedToolSessionSnapshotAsync(...)` succeeds:

- load the current session entity/domain object
- compute the effective tool id using the shared helper
- get the stored override for that tool only
- keep provider snapshot handling unchanged

- [ ] **Step 5: Apply Codex overrides by patching the session-local snapshot file**

Implementation rule:

- for managed Codex sessions, patch `<workspace>\.codex\config.toml` after snapshot materialization and before launch
- update only `model = "..."` and `model_reasoning_effort = "..."`
- preserve provider id, base URL, wire API, and other snapshot-derived fields
- if the keys do not exist, add them; do not rewrite the file from scratch unless the test proves the snapshot format is already normalized

- [ ] **Step 6: Append `--model` for Claude Code and OpenCode only when needed**

Use the adapter context rather than hard-coding session access in the adapters:

```csharp
if (!string.IsNullOrWhiteSpace(context.LaunchModelOverride))
{
    // append --model <value>
}
```

Rules:

- do not append `--model` twice
- do not override the provider selection mechanism
- low-interruption and normal launch paths must both see the same model override

- [ ] **Step 7: Re-run the executor tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CliExecutorServiceTests"
```

Expected:

- PASS

- [ ] **Step 8: Commit the launch-merge chunk**

```powershell
$commitMessage = @"
Apply session-scoped launch overrides after provider snapshot resolution

Constraint: cc-switch remains the only authority for provider selection
Rejected: Store launch settings in live provider config | would leak across sessions
Confidence: medium
Scope-risk: moderate
Directive: Always merge overrides after the session snapshot is prepared; never mutate live provider state
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter FullyQualifiedName~CliExecutorServiceTests
Not-tested: Browser and Feishu form save flows
"@
git add WebCodeCli.Domain/Domain/Service/Adapters/CliOutputEvent.cs WebCodeCli.Domain/Domain/Service/CliExecutorService.cs WebCodeCli.Domain/Domain/Service/Adapters/ClaudeCodeAdapter.cs WebCodeCli.Domain/Domain/Service/Adapters/OpenCodeAdapter.cs WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs
git commit -m $commitMessage
```

## Chunk 3: Desktop And Mobile Web UI

### Task 5: Add desktop session override summary, settings action, and dialog flow

**Files:**
- Create: `WebCodeCli/Components/Dialogs/SessionLaunchOverrideDialog.razor`
- Modify: `WebCodeCli/Components/SessionListPanel.razor`
- Modify: `WebCodeCli/Pages/CodeAssistant.razor`
- Modify: `WebCodeCli/Pages/CodeAssistant.razor.cs`
- Modify: `WebCodeCli/Resources/Localization/zh-CN.json`
- Modify: `WebCodeCli/Resources/Localization/en-US.json`
- Modify: `WebCodeCli/Resources/Localization/ja-JP.json`
- Modify: `WebCodeCli/Resources/Localization/ko-KR.json`
- Modify: `WebCodeCli/wwwroot/Resources/Localization/zh-CN.json`
- Modify: `WebCodeCli/wwwroot/Resources/Localization/en-US.json`
- Modify: `WebCodeCli/wwwroot/Resources/Localization/ja-JP.json`
- Modify: `WebCodeCli/wwwroot/Resources/Localization/ko-KR.json`

- [ ] **Step 1: Add the dialog component using the existing rename-dialog visual pattern**

Required fields:

- current tool label
- pinned provider summary
- model text input with a small suggested-values dropdown/datalist
- reasoning-effort select for Codex only
- helper note that provider still comes from `cc-switch`
- buttons for save, clear, and cancel

Do not place launch settings into rename or global settings.

- [ ] **Step 2: Add the new action and summary to `SessionListPanel`**

Display rules:

- only show `模型设置` for managed tools (`codex`, `claude-code`, `opencode`)
- show summary only when an override exists for the effective tool
- keep the summary under the pinned-provider line

Summary examples:

- `模型: gpt-5.4 · 思考: high`
- `模型: sonnet`

- [ ] **Step 3: Add desktop page state and save/clear handlers**

In `CodeAssistant.razor.cs`, add a flow like:

```csharp
private async Task SaveSessionLaunchOverrideAsync(SessionHistory session, string? model, string? reasoningEffort)
{
    // validate via helper
    // update session.ToolLaunchOverrides
    // persist via SessionHistoryManager
    // reset runtime via CliExecutorService.ResetSessionRuntimeAsync(session.SessionId)
    // reload sessions and refresh the current session if needed
}
```

Rules:

- saving another session only resets that other session runtime
- saving the current session shows that the next execution uses the new settings
- clearing all fields removes the tool entry rather than storing blanks

- [ ] **Step 4: Add and mirror localization keys in both resource roots**

Add strings for:

- `模型设置`
- `模型`
- `思考等级`
- `提供方仍由 cc-switch 决定`
- `保存后会重置当前会话运行时，下次执行生效`
- validation errors
- clear/save success text

Keep the JSON keys identical between `Resources/Localization` and `wwwroot/Resources/Localization`.

- [ ] **Step 5: Run build and JSON syntax verification**

Run:

```powershell
$jsonFiles = @(
  "D:\VSWorkshop\WebCode\WebCodeCli\Resources\Localization\zh-CN.json",
  "D:\VSWorkshop\WebCode\WebCodeCli\Resources\Localization\en-US.json",
  "D:\VSWorkshop\WebCode\WebCodeCli\Resources\Localization\ja-JP.json",
  "D:\VSWorkshop\WebCode\WebCodeCli\Resources\Localization\ko-KR.json",
  "D:\VSWorkshop\WebCode\WebCodeCli\wwwroot\Resources\Localization\zh-CN.json",
  "D:\VSWorkshop\WebCode\WebCodeCli\wwwroot\Resources\Localization\en-US.json",
  "D:\VSWorkshop\WebCode\WebCodeCli\wwwroot\Resources\Localization\ja-JP.json",
  "D:\VSWorkshop\WebCode\WebCodeCli\wwwroot\Resources\Localization\ko-KR.json"
)
$jsonFiles | ForEach-Object { Get-Content $_ -Raw | ConvertFrom-Json | Out-Null }
dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
```

Expected:

- JSON parsing succeeds
- solution build PASS

- [ ] **Step 6: Run manual desktop smoke**

Checklist:

- session list shows `模型设置` for managed tools only
- existing provider summary remains visible
- saved override summary appears immediately in the list
- saving settings for the current session keeps messages/workspace but forces the next run onto a fresh CLI thread
- session B remains unchanged when session A settings are edited

- [ ] **Step 7: Commit the desktop web chunk**

```powershell
$commitMessage = @"
Expose session launch settings in the desktop session list

Constraint: The UI must edit one session at a time and must not imply provider switching authority
Confidence: medium
Scope-risk: moderate
Directive: Keep summary rendering read-only; all validation stays in SessionLaunchOverrideHelper
Tested: dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
Not-tested: Mobile and Feishu surfaces
"@
git add WebCodeCli/Components/Dialogs/SessionLaunchOverrideDialog.razor WebCodeCli/Components/SessionListPanel.razor WebCodeCli/Pages/CodeAssistant.razor WebCodeCli/Pages/CodeAssistant.razor.cs WebCodeCli/Resources/Localization/zh-CN.json WebCodeCli/Resources/Localization/en-US.json WebCodeCli/Resources/Localization/ja-JP.json WebCodeCli/Resources/Localization/ko-KR.json WebCodeCli/wwwroot/Resources/Localization/zh-CN.json WebCodeCli/wwwroot/Resources/Localization/en-US.json WebCodeCli/wwwroot/Resources/Localization/ja-JP.json WebCodeCli/wwwroot/Resources/Localization/ko-KR.json
git commit -m $commitMessage
```

### Task 6: Add the same launch-settings semantics to mobile session management

**Files:**
- Modify: `WebCodeCli/Pages/CodeAssistantMobile.razor`
- Modify: `WebCodeCli/Pages/CodeAssistantMobile.razor.cs`

- [ ] **Step 1: Add override summary and `模型设置` action to the mobile session drawer**

Rules:

- use the same managed-tool gate as desktop
- show the same summary line under the pinned provider summary
- do not invent a second settings model for mobile

- [ ] **Step 2: Reuse the same dialog and save/clear flow from desktop**

The mobile page should open the same `SessionLaunchOverrideDialog` component and call the same validation/persistence/runtime-reset path.

- [ ] **Step 3: Run build verification again**

Run:

```powershell
dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
```

Expected:

- PASS

- [ ] **Step 4: Run manual mobile smoke**

Checklist:

- the drawer shows summary text for sessions with overrides
- the dialog exposes the correct fields for Codex vs Claude/OpenCode
- saving from mobile resets only the selected session runtime

- [ ] **Step 5: Commit the mobile chunk**

```powershell
$commitMessage = @"
Match mobile session management to desktop launch settings

Constraint: Mobile and desktop must save the same underlying session data
Confidence: medium
Scope-risk: narrow
Directive: Reuse the desktop dialog/state flow rather than introducing mobile-only validation
Tested: dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
Not-tested: Feishu card flows
"@
git add WebCodeCli/Pages/CodeAssistantMobile.razor WebCodeCli/Pages/CodeAssistantMobile.razor.cs
git commit -m $commitMessage
```

## Chunk 4: Feishu Session Manager And Status Cards

### Task 7: Fold the Feishu session manager to 3 sessions by default and add launch-setting entry points

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs`

- [ ] **Step 1: Write failing Feishu session-manager tests first**

Cover:

- default session manager card renders only the most recent 3 sessions
- folded view shows `更多会话` when more sessions exist
- expanded view shows all sessions and exposes `收起`
- each managed session row shows `模型设置`
- existing actions (`切换`, `重命名`, `同步 Provider`, `关闭`) still render

- [ ] **Step 2: Run the Feishu card-action tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuCardActionServiceTests"
```

Expected:

- FAIL because the card builder still uses `Take(10)` and there is no expand/collapse state in the action payload

- [ ] **Step 3: Extend the Feishu action payload with session-manager view state**

Add fields such as:

```csharp
[JsonPropertyName("show_all_sessions")]
public bool ShowAllSessions { get; set; }
```

Reuse `open_session_manager` instead of inventing a second open action.

- [ ] **Step 4: Update `BuildSessionManagerCardAsync(...)` to support folded and expanded rendering**

Rules:

- folded mode: `Take(3)`
- expanded mode: render the full list (pagination only if card size limits force it)
- show `更多会话` in folded mode when hidden sessions exist
- show `收起` in expanded mode
- add the override summary under the provider summary when an override exists

- [ ] **Step 5: Re-run the Feishu card-action tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuCardActionServiceTests"
```

Expected:

- PASS for folded/expanded session manager rendering

- [ ] **Step 6: Commit the Feishu folding chunk**

```powershell
$commitMessage = @"
Fold Feishu session management by default and expose launch settings

Constraint: Feishu cards must stay readable without hiding access to older sessions
Confidence: medium
Scope-risk: moderate
Directive: Preserve the product semantics of 3 by default, explicit expand, explicit collapse
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter FullyQualifiedName~FeishuCardActionServiceTests
Not-tested: Live Feishu card size limits
"@
git add WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs
git commit -m $commitMessage
```

### Task 8: Add Feishu launch-settings form/save/clear actions and show the active summary during execution

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs`

- [ ] **Step 1: Write failing tests for the Feishu launch-settings form and save flow**

Cover:

- `show_session_launch_settings_form` renders `Model` for Claude/OpenCode and `Model + Reasoning Effort` for Codex
- `save_session_launch_settings` persists the override and clears the target session thread id
- `clear_session_launch_settings` removes the override entry and also clears the target session runtime
- current streaming status markdown shows the active override summary when one exists

- [ ] **Step 2: Run the focused Feishu tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuCardActionServiceTests|FullyQualifiedName~FeishuChannelServiceTests"
```

Expected:

- FAIL because the form actions and status-summary logic do not exist yet

- [ ] **Step 3: Add form-card builders and handlers in `FeishuCardActionService`**

Add actions:

- `show_session_launch_settings_form`
- `save_session_launch_settings`
- `clear_session_launch_settings`

Use the shared helper plus executor reset flow:

- validate and normalize form values
- persist on the session record
- call `ResetSessionRuntimeAsync(sessionId)`
- rebuild the session manager card with updated summary lines

- [ ] **Step 4: Update `FeishuChannelService` status chrome to show the active override**

Append a summary line to the current status chrome when present, for example:

- `🤖 模型: gpt-5.4`
- `🧠 思考: high`

Keep the existing current-session/workspace/title/tool line intact.

- [ ] **Step 5: Re-run the focused Feishu tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuCardActionServiceTests|FullyQualifiedName~FeishuChannelServiceTests"
```

Expected:

- PASS

- [ ] **Step 6: Commit the Feishu launch-settings chunk**

```powershell
$commitMessage = @"
Bring session launch settings to Feishu cards and execution status

Constraint: Feishu must match Web session semantics instead of becoming a second-class configuration path
Confidence: medium
Scope-risk: moderate
Directive: Reuse the same helper and runtime reset path as Web; do not fork validation rules
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter FullyQualifiedName~FeishuCardActionServiceTests|FullyQualifiedName~FeishuChannelServiceTests
Not-tested: Live Feishu operator UX with large cards
"@
git add WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs
git commit -m $commitMessage
```

## Chunk 5: Final Verification

### Task 9: Run full targeted verification and the cross-session smoke checklist

**Files:**
- Test: `WebCodeCli.Domain.Tests/SessionHistoryManagerTests.cs`
- Test: `WebCodeCli.Domain.Tests/SessionLaunchOverrideHelperTests.cs`
- Test: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs`
- Test: `tests/WebCodeCli.Tests/WebCodeCli.Tests.csproj`

- [ ] **Step 1: Run the targeted domain/unit suite**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~SessionHistoryManagerTests|FullyQualifiedName~SessionLaunchOverrideHelperTests|FullyQualifiedName~CliExecutorServiceTests|FullyQualifiedName~FeishuCardActionServiceTests|FullyQualifiedName~FeishuChannelServiceTests"
```

Expected:

- PASS

- [ ] **Step 2: Run the web-project test project and solution build**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\tests\WebCodeCli.Tests\WebCodeCli.Tests.csproj
dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
```

Expected:

- PASS

- [ ] **Step 3: Run cross-session manual smoke on desktop web**

Checklist:

- session A and session B can use different Codex models without affecting each other
- session A Codex reasoning change does not affect Claude/OpenCode sessions
- reloading the page preserves the saved summary
- syncing provider does not remove the saved launch override
- saving settings does not delete workspace files or historical messages

- [ ] **Step 4: Run manual mobile and Feishu smoke**

Checklist:

- mobile shows the same summary and field set as desktop
- Feishu session manager defaults to 3 sessions
- `更多会话` expands and `收起` collapses
- Feishu save/clear actions apply on the next execution only to the targeted session
- current Feishu execution card shows the active override summary

- [ ] **Step 5: Record any implementation deviations before closing the work**

If implementation differs from the design, update:

- the final report
- the relevant tests
- this plan document if the execution handoff depends on the new behavior

- [ ] **Step 6: Commit any final verification fixes if needed**

Only create this commit if verification required follow-up edits:

```powershell
$commitMessage = @"
Verify session launch overrides across Web and Feishu flows

Constraint: Completion requires evidence for cross-session isolation and session-only runtime reset
Confidence: medium
Scope-risk: narrow
Directive: Do not mark this feature done without manual checks for Web, mobile, and Feishu parity
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter FullyQualifiedName~SessionHistoryManagerTests|FullyQualifiedName~SessionLaunchOverrideHelperTests|FullyQualifiedName~CliExecutorServiceTests|FullyQualifiedName~FeishuCardActionServiceTests|FullyQualifiedName~FeishuChannelServiceTests; dotnet test D:\VSWorkshop\WebCode\tests\WebCodeCli.Tests\WebCodeCli.Tests.csproj; dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
Not-tested: None beyond explicitly documented manual smoke
"@
git add .
git commit -m $commitMessage
```
