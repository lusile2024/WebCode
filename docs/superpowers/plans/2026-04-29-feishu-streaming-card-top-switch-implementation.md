# Feishu Streaming Card Top Switch Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add direct top-of-card model and reasoning switching to Feishu streaming reply cards so users can switch next-turn launch settings in place, without entering session management.

**Architecture:** Keep `cc-switch` as provider authority and keep `ToolLaunchOverridesJson` as the single session-level launch-setting authority for quick-switches and session launch settings alike. Build the Feishu streaming-card top chrome as a status row plus chip rows; chips are visible while streaming, disabled until completion, and become one-tap actions after completion. Clicking a chip updates the current tool entry in `ToolLaunchOverridesJson`, then resets only the current session runtime so the next execution starts with the new override.

**Tech Stack:** ASP.NET Core, Blazor Server, Feishu card kit JSON, `WebCodeCli.Domain` services, `cc-switch` model catalog, xUnit

Depends on:
- `docs/superpowers/specs/2026-04-29-feishu-streaming-card-top-switch-design.md`
- `docs/superpowers/specs/2026-04-22-session-launch-overrides-design.md`
- `docs/superpowers/specs/2026-04-17-cc-switch-provider-authority-design.md`

---

## File Map

### Session launch-override authority

- Modify: `WebCodeCli.Domain/Domain/Service/ICliExecutorService.cs`
  Expose a small API for reading effective session overrides if the Feishu surfaces need a single source of truth.
- Modify: `WebCodeCli.Domain/Domain/Service/CliExecutorService.cs`
  Continue to consume `ToolLaunchOverridesJson` on launch and keep reset behavior unchanged.
- Modify: `WebCodeCli.Domain/Domain/Service/SessionLaunchOverrideHelper.cs`
  Reuse the existing normalization/validation/apply logic for direct chip clicks.
- Modify: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`
  Cover that launch override consumption still behaves correctly after the quick-switch path is wired to the same storage.

### Feishu streaming-card chrome and rendering

- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuStreamingCardChrome.cs`
  Add top-chip group/item DTOs and a clear completed/disabled state model.
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardKitClient.cs`
  Render chip groups between the status row and body markdown, with wrapping layout, active styling, and disabled-state rendering.
- Modify: `WebCodeCli.Domain.Tests/FeishuCardKitClientTests.cs`
  Assert the exact card JSON shape for enabled/disabled chips and group omission.

### Feishu quick-switch action flow

- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs`
  Add payload fields for quick-switch target `model` and `reasoning_effort`.
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs`
  Build chip groups for completed/running cards, reuse the model catalog, gate clicks on completion state, write the current tool override, and return toast plus refreshed chrome.
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs`
  Build the same chip groups on the primary streaming path and flip them from disabled to enabled when the reply completes.
- Modify: `WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs`
  Cover quick-switch success, running-state rejection, stale session rejection, unsupported reasoning omission, and refreshed active-chip state.
- Modify: `WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs`
  Cover initial disabled chips during streaming and enabled chips after completion.

### Explicit non-goals for this implementation

- Do not change Web session-management UI or web dialog behavior in this plan.
- Do not introduce a second authority for launch settings alongside `ToolLaunchOverridesJson`.
- Do not add a new controller or HTTP API for this feature.
- Do not allow mid-stream switching.
- Do not add any new dependencies or test frameworks.

## Chunk 1: Make `ToolLaunchOverridesJson` the Explicit Quick-Switch Authority

### Task 1: Reuse session launch overrides as the only write path for top-card switching

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/ICliExecutorService.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/CliExecutorService.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/SessionLaunchOverrideHelper.cs`
- Test: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`

- [ ] **Step 1: Write the failing tests for launch override consumption and reset semantics**

Add tests that prove:

- `codex` still applies `model` and `model_reasoning_effort` from `ToolLaunchOverridesJson`
- `claude-code` still applies `model` from `ToolLaunchOverridesJson`
- `opencode` still applies `model` from `ToolLaunchOverridesJson`
- a quick-switch save path can update the session override for one tool without touching unrelated tool entries
- resetting the session runtime remains the only required post-save runtime behavior

- [ ] **Step 2: Run the focused executor tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CliExecutorServiceTests"
```

Expected:

- FAIL because the top-card quick-switch flow is not yet wired to the same helper/storage contract

- [ ] **Step 3: Add the smallest read helper needed by Feishu surfaces**

If `FeishuChannelService` or `FeishuCardActionService` need a single source of truth for active values, add only a narrow API to `ICliExecutorService`, for example:

```csharp
Task<SessionToolLaunchOverride?> GetEffectiveSessionLaunchOverrideAsync(
    string sessionId,
    string toolId,
    CancellationToken cancellationToken = default);
```

Do not add a second persistence path.

- [ ] **Step 4: Keep launch application logic unchanged except for any visibility needed by Feishu**

`CliExecutorService` should continue to:

- read `ToolLaunchOverridesJson`
- resolve the current tool override with `SessionLaunchOverrideHelper`
- apply `model`/`reasoningEffort` at launch time exactly as it does today

Only refactor if a test shows Feishu needs a cleaner accessor.

- [ ] **Step 5: Reset only the target session runtime after top-card saves**

Call:

```csharp
await ResetSessionRuntimeAsync(sessionId, clearCliThreadId: true, cancellationToken);
```

Do not:

- call `CleanupSessionWorkspace`
- clear message history
- mutate other sessions

- [ ] **Step 6: Re-run the executor tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CliExecutorServiceTests"
```

Expected:

- PASS

- [ ] **Step 7: Commit the launch-override authority layer**

```powershell
$message = @"
Keep Feishu quick-switches on session launch-override storage

Constraint: cc-switch must remain the only provider authority
Constraint: Quick-switch writes must share authority with existing session launch settings
Rejected: Introduce a project-config-only quick-switch path | would create dual authority with ToolLaunchOverridesJson
Confidence: high
Scope-risk: narrow
Reversibility: clean
Directive: Keep all per-session model/reasoning writes converged on SessionLaunchOverrideHelper and ToolLaunchOverridesJson
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter FullyQualifiedName~CliExecutorServiceTests
Not-tested: Feishu card rendering and click flow
"@
git add WebCodeCli.Domain/Domain/Service/ICliExecutorService.cs WebCodeCli.Domain/Domain/Service/CliExecutorService.cs WebCodeCli.Domain/Domain/Service/SessionLaunchOverrideHelper.cs WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs
git commit -m $message
```

## Chunk 2: Render Top Chips on the Streaming Card

### Task 2: Extend the chrome DTO and render top-chip groups in Feishu cards

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuStreamingCardChrome.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardKitClient.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuCardKitClientTests.cs`

- [ ] **Step 1: Write the failing rendering tests first**

Add tests that prove:

- a streaming card with model chips renders them between the status block and body markdown
- active chips are marked distinctly in the JSON payload
- disabled chips render during streaming without clickable actions
- reasoning chips are omitted entirely when no reasoning group exists
- overflow options still render on the status row

Use a chrome shape like:

```csharp
var chrome = new FeishuStreamingCardChrome
{
    StatusMarkdown = "当前会话"
};
chrome.TopChipGroups.Add(new FeishuStreamingCardTopChipGroup
{
    Kind = "model",
    Items =
    [
        new FeishuStreamingCardTopChipItem { Text = "gpt-5.4", IsActive = true, IsEnabled = false },
        new FeishuStreamingCardTopChipItem { Text = "gpt-5.2", IsActive = false, IsEnabled = false }
    ]
});
```

- [ ] **Step 2: Run the card-kit tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuCardKitClientTests"
```

Expected:

- FAIL because `FeishuStreamingCardChrome` does not yet expose top-chip groups and `FeishuCardKitClient` does not render them

- [ ] **Step 3: Add focused DTOs to `FeishuStreamingCardChrome.cs`**

Add only the chrome models required by rendering:

```csharp
public sealed class FeishuStreamingCardTopChipGroup
{
    public string Kind { get; set; } = string.Empty;
    public List<FeishuStreamingCardTopChipItem> Items { get; set; } = [];
}

public sealed class FeishuStreamingCardTopChipItem
{
    public string Text { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsEnabled { get; set; }
    public object? Value { get; set; }
}
```

And add:

```csharp
public List<FeishuStreamingCardTopChipGroup> TopChipGroups { get; set; } = [];
```

- [ ] **Step 4: Render top-chip groups in `FeishuCardKitClient`**

Add a helper such as:

```csharp
private object BuildTopChipGroup(FeishuStreamingCardTopChipGroup group)
```

Rendering rules:

- inject chip groups after the status module and before the main markdown module
- use wrapping `column_set` or equivalent button rows already supported by the card JSON contract
- active chips use a highlighted button style
- disabled chips omit action payloads or use the Feishu-supported disabled button mode
- keep status overflow rendering unchanged

- [ ] **Step 5: Re-run the card-kit tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuCardKitClientTests"
```

Expected:

- PASS

- [ ] **Step 6: Commit the chrome/rendering layer**

```powershell
$message = @"
Render direct model chips in Feishu streaming card chrome

Constraint: The quick-switch UI must stay on the current streaming reply card
Rejected: Open a second card or form for every switch | too much interaction overhead
Confidence: high
Scope-risk: narrow
Reversibility: clean
Directive: Keep chip rendering additive to the current status row and overflow menu
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter FullyQualifiedName~FeishuCardKitClientTests
Not-tested: End-to-end action routing
"@
git add WebCodeCli.Domain/Domain/Model/Channels/FeishuStreamingCardChrome.cs WebCodeCli.Domain/Domain/Service/Channels/FeishuCardKitClient.cs WebCodeCli.Domain.Tests/FeishuCardKitClientTests.cs
git commit -m $message
```

## Chunk 3: Build and Handle Feishu Quick-Switch Chips

### Task 3: Populate chips on the primary streaming path and toggle them on completion

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs`
- Modify: `WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs`

- [ ] **Step 1: Write the failing streaming-path tests first**

Add tests that prove:

- a running `codex` stream shows all model chips plus `low/medium/high/xhigh`, all disabled
- a running `claude-code` stream shows only model chips
- when the stream completes, the same chrome object refreshes to enabled chips
- the active chip matches the current effective session override returned by the shared helper, or the default/no-override state when none exists

- [ ] **Step 2: Run the Feishu channel tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuChannelServiceTests"
```

Expected:

- FAIL because `BuildStreamingCardChrome(...)` does not add chip groups and completion does not flip chip state

- [ ] **Step 3: Build chip groups in `FeishuChannelService.BuildStreamingCardChrome(...)`**

Use the existing executor and model catalog plumbing:

- resolve the current session and effective tool id
- ask the shared launch-override accessor for active values when present
- ask the same model-catalog source used by session launch settings for all model options
- build one `model` group
- build one `reasoning_effort` group only when the tool is `codex`

Initial state:

- `IsEnabled = false` for every chip while the stream is running

- [ ] **Step 4: Enable chips when the stream reaches completed state**

In the same completion path that already calls `SetCompletedStatus()`:

- rebuild or mutate `TopChipGroups` so `IsEnabled = true`
- keep the active chip unchanged
- do not enable chips on stopped/error states until a later explicit decision says otherwise

- [ ] **Step 5: Re-run the Feishu channel tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuChannelServiceTests"
```

Expected:

- PASS

- [ ] **Step 6: Commit the primary streaming-path chip population**

```powershell
$message = @"
Expose quick-switch chips on Feishu streaming cards

Constraint: Chips must stay visible during streaming but remain non-interactive until completion
Confidence: high
Scope-risk: moderate
Reversibility: clean
Directive: Keep active-chip state aligned with the existing session launch-override summary logic
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter FullyQualifiedName~FeishuChannelServiceTests
Not-tested: Action click handling and failure toasts
"@
git add WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs
git commit -m $message
```

### Task 4: Route chip clicks through `FeishuCardActionService`

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs`
- Modify: `WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs`

- [ ] **Step 1: Write the failing action-service tests first**

Cover:

- clicking a model chip on a completed card writes the current tool entry in `ToolLaunchOverridesJson` and returns success toast
- clicking a reasoning chip on a completed Codex card writes the same storage and returns success toast
- clicking while `_feishuChannel.IsSessionExecutionActive(sessionId)` is still true returns `当前回复尚未完成，暂时不能切换`
- clicking a reasoning chip for a non-Codex tool returns a warning/error toast
- stale session/chat mismatch returns the same session-invalid toast as other Feishu session actions
- refreshed chrome highlights the newly selected value

- [ ] **Step 2: Run the Feishu action-service tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuCardActionServiceTests"
```

Expected:

- FAIL because the quick-switch action names and handlers do not exist yet

- [ ] **Step 3: Add payload fields and action names**

Extend `FeishuHelpCardAction` with the minimum additional fields:

```csharp
[JsonPropertyName("model")]
public string? Model { get; set; }

[JsonPropertyName("reasoning_effort")]
public string? ReasoningEffort { get; set; }
```

If the file already has these fields for session-launch settings, reuse them instead of adding duplicates. The important part is introducing distinct action names:

- `switch_streaming_card_model`
- `switch_streaming_card_reasoning_effort`

- [ ] **Step 4: Handle model-chip and reasoning-chip actions**

In `FeishuCardActionService.HandleCardActionAsync(...)` and its private helpers:

- reuse existing session/chat validation flow from `HandleSyncSessionProviderAsync(...)`
- reject if `_feishuChannel.IsSessionExecutionActive(sessionId)` or the low-interruption map says the session is still running
- load `ToolLaunchOverridesJson`, apply the per-tool update through `SessionLaunchOverrideHelper.ApplyOverride(...)`, persist it, and then call `ResetSessionRuntimeAsync(...)`
- rebuild the current card chrome using the same helper as the primary streaming path
- return:
  - refreshed card response
  - success toast with “下次执行生效”

- [ ] **Step 5: Reuse the existing model catalog logic when building refreshed chips**

Do not invent a second model list source.

Reuse or extract the smallest helper from the session-launch form path:

- `LoadSessionLaunchModelOptionsAsync(...)`
- current tool capability checks

But do not reopen the session-launch form. The refreshed response must stay on the same card surface.

- [ ] **Step 6: Re-run the Feishu action-service tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuCardActionServiceTests"
```

Expected:

- PASS

- [ ] **Step 7: Commit the click-handling layer**

```powershell
$message = @"
Handle direct Feishu quick-switch actions on completed reply cards

Constraint: The current reply must be complete before switching is allowed
Rejected: Trust client-side disabled state alone | stale or replayed actions could bypass it
Confidence: high
Scope-risk: moderate
Reversibility: clean
Directive: Route all quick-switch writes through SessionLaunchOverrideHelper and the existing ChatSession persistence path rather than inventing a new storage layer
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter FullyQualifiedName~FeishuCardActionServiceTests
Not-tested: Full project test run
"@
git add WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs
git commit -m $message
```

## Chunk 4: Final Verification

### Task 5: Run the full relevant test suite and do a final consistency pass

**Files:**
- No new files
- Verify: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`
- Verify: `WebCodeCli.Domain.Tests/FeishuCardKitClientTests.cs`
- Verify: `WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs`
- Verify: `WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs`

- [ ] **Step 1: Run the full domain test project**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj
```

Expected:

- PASS

- [ ] **Step 2: Run the full solution tests if the domain project passes cleanly**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.sln
```

Expected:

- PASS, or any unrelated pre-existing failures are documented before merging

- [ ] **Step 3: Manual code review checklist**

Confirm:

- the quick-switch path writes only `ToolLaunchOverridesJson` on the current session
- provider selection still comes only from `cc-switch`
- chips are visible during streaming and clickable only after completion
- non-Codex tools never render reasoning chips
- the overflow session-switch menu still works

- [ ] **Step 4: Final commit if verification or cleanup produced additional changes**

```powershell
$message = @"
Finish Feishu streaming-card quick-switch implementation

Constraint: Quick-switch behavior must remain session-scoped and provider-safe
Confidence: medium
Scope-risk: moderate
Reversibility: clean
Directive: Keep session-management and top-card switching semantically aligned if either path changes later
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj; dotnet test D:\VSWorkshop\WebCode\WebCodeCli.sln
Not-tested: Live Feishu manual click-through against a real tenant
"@
git add -A
git commit -m $message
```

## Notes for Execution

- Start with the executor/project-config authority chunk. The Feishu UI work depends on that API existing.
- Keep `FeishuCardActionHandler.cs` unchanged unless a failing test proves the direct chip payload cannot flow through the current generic path.
- Prefer reusing the existing session-launch model-catalog helper over building a second provider/model discovery path.
- If Feishu button JSON does not support a true disabled state for the chosen chip element, fall back to rendering chips without action payloads while keeping the muted visual style. The behavior requirement is “visible but not switchable,” not any specific card-schema flag.
