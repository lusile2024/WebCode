# Low-Interruption Native Continue Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `少打断执行` action for web, mobile web, and Feishu that launches one new native continue/resume run for the current CLI tool without adding a continuation prompt or introducing WebCode-owned task/loop state.

**Architecture:** Keep native continue semantics inside each CLI adapter and expose them through a dedicated executor entry path instead of overloading normal send-message execution. Compute button visibility from current output only, create a new assistant reply or new Feishu streaming card for each low-interruption run, and reuse existing process-streaming behavior without adding a WebCode loop state machine.

**Tech Stack:** ASP.NET Core, Blazor Server, domain services in `WebCodeCli.Domain`, Feishu card kit integration, xUnit, existing Playwright/manual UI smoke paths.

Depends on: `docs/superpowers/specs/2026-04-19-low-interruption-native-continue-design.md`

---

## File Map

### Native continue plumbing

- Modify: `WebCodeCli.Domain/Domain/Model/CliToolConfig.cs`
  Add an optional low-interruption argument template field.
- Modify: `WebCodeCli/appsettings.json`
  Add low-interruption template configuration and comments for Codex, Claude Code, and OpenCode.
- Modify: `WebCodeCli.Domain/Domain/Service/Adapters/ICliToolAdapter.cs`
  Add a dedicated native low-interruption argument builder contract.
- Modify: `WebCodeCli.Domain/Domain/Service/Adapters/CodexAdapter.cs`
  Implement the Codex default low-interruption continue command shape with `resume`, `--json`, `--full-auto`, and `--dangerously-bypass-approvals-and-sandbox`.
- Modify: `WebCodeCli.Domain/Domain/Service/Adapters/ClaudeCodeAdapter.cs`
  Implement the Claude Code default low-interruption continue command shape.
- Modify: `WebCodeCli.Domain/Domain/Service/Adapters/OpenCodeAdapter.cs`
  Implement the OpenCode default low-interruption continue command shape.
- Modify: `WebCodeCli.Domain/Domain/Service/ICliExecutorService.cs`
  Add capability-query and execution methods for native low-interruption continue.
- Modify: `WebCodeCli.Domain/Domain/Service/CliExecutorService.cs`
  Add the dedicated low-interruption execution path, reuse current streaming behavior, and require an existing native thread/session id.

### Button eligibility and shared UI logic

- Create: `WebCodeCli/Helpers/LowInterruptionContinueHelper.cs`
  Keep button eligibility logic out of page files; only inspect current output plus executor/tool availability.
- Create: `tests/WebCodeCli.Tests/LowInterruptionContinueHelperTests.cs`
  Cover latest-reply-only behavior, structured `todo_list` preference, text fallback, missing thread id, and disabled-while-running behavior.

### Desktop web

- Modify: `WebCodeCli/Components/ChatMessageListPanel.razor`
  Render the `少打断执行` action at the bottom of the latest eligible assistant message card.
- Modify: `WebCodeCli/Pages/CodeAssistant.razor`
  Pass eligibility, disabled state, and callback parameters into `ChatMessageListPanel`.
- Modify: `WebCodeCli/Pages/CodeAssistant.razor.cs`
  Add the desktop low-interruption command handler and reuse the existing new-assistant-message streaming flow.

### Mobile web

- Modify: `WebCodeCli/Pages/CodeAssistantMobile.razor`
  Render `少打断执行` on the latest eligible assistant bubble and disable it while a run is active.
- Modify: `WebCodeCli/Pages/CodeAssistantMobile.razor.cs`
  Add the mobile low-interruption command handler and reuse the existing new-assistant-message streaming flow.

### Feishu

- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuStreamingCardChrome.cs`
  Add a bottom-action payload model for streaming and completed cards.
- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs`
  Add a new action name and any fields needed to launch low-interruption continue.
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardKitClient.cs`
  Render bottom actions below the content block.
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs`
  Handle the new Feishu action by starting one new native low-interruption run as a new streaming card.
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs`
  Attach the action only when the just-finished latest card is eligible and no session execution is already active.

### Tests

- Modify: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`
  Cover native low-interruption command selection and no-prompt behavior.
- Modify: `WebCodeCli.Domain.Tests/FeishuCardKitClientTests.cs`
  Cover bottom-action rendering.
- Modify: `WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs`
  Cover the new Feishu action and disabled/no-thread-id paths.

## Chunk 1: Native Continue Plumbing

### Task 1: Add low-interruption template support to tool configuration and adapters

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Model/CliToolConfig.cs`
- Modify: `WebCodeCli/appsettings.json`
- Modify: `WebCodeCli.Domain/Domain/Service/Adapters/ICliToolAdapter.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Adapters/CodexAdapter.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Adapters/ClaudeCodeAdapter.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Adapters/OpenCodeAdapter.cs`
- Test: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`

- [ ] **Step 1: Write failing tests for adapter-driven low-interruption command building**

Add or extend tests so the suite asserts the following:

```csharp
[Fact]
public async Task ExecuteLowInterruptionContinue_UsesCodexResumeWithoutPrompt()
{
    // Arrange a session with CliThreadId and a codex tool config that supports low interruption.
    // Act by running the new executor entry point.
    // Assert the launched command contains:
    //   exec resume
    //   --dangerously-bypass-approvals-and-sandbox
    //   --json
    //   --full-auto
    // and does not append a user prompt.
}
```

- [ ] **Step 2: Run the focused domain test and confirm it fails for the expected missing API**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CliExecutorServiceTests"
```

Expected:

- FAIL because no dedicated low-interruption executor path or adapter contract exists yet

- [ ] **Step 3: Add `LowInterruptionArgumentTemplate` to `CliToolConfig` and appsettings**

Use this exact field shape:

```csharp
public string LowInterruptionArgumentTemplate { get; set; } = string.Empty;
```

Seed config comments and initial values so:

- Codex has a native low-interruption continue template
- Claude Code has a native low-interruption continue template
- OpenCode has a native low-interruption continue template

Do not change the existing `ArgumentTemplate`.

- [ ] **Step 4: Extend `ICliToolAdapter` with a dedicated builder**

Add an explicit method so low-interruption behavior stays tool-native:

```csharp
string BuildLowInterruptionArguments(CliToolConfig tool, CliSessionContext context);
```

Rules:

- no `{prompt}` placeholder
- require `context.CliThreadId`
- allow config override through `tool.LowInterruptionArgumentTemplate`
- otherwise use a tool-specific default

- [ ] **Step 5: Implement tool-specific low-interruption defaults in the three adapters**

Codex default must preserve:

```bash
exec resume --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox --json --full-auto <cliThreadId>
```

Claude Code and OpenCode defaults should keep native continue semantics in adapter code and avoid any synthetic continuation prompt.

- [ ] **Step 6: Re-run the focused domain test and confirm it now reaches the next missing execution-path failure or passes**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CliExecutorServiceTests"
```

Expected:

- either PASS for adapter-level assertions
- or FAIL only on the still-missing executor entry point added in Task 2

- [ ] **Step 7: Commit the adapter/config chunk**

```bash
git add WebCodeCli.Domain/Domain/Model/CliToolConfig.cs WebCodeCli/appsettings.json WebCodeCli.Domain/Domain/Service/Adapters/ICliToolAdapter.cs WebCodeCli.Domain/Domain/Service/Adapters/CodexAdapter.cs WebCodeCli.Domain/Domain/Service/Adapters/ClaudeCodeAdapter.cs WebCodeCli.Domain/Domain/Service/Adapters/OpenCodeAdapter.cs WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs
git commit -m "Add native low-interruption CLI templates"
```

### Task 2: Add the dedicated executor entry point for one native continue run

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/ICliExecutorService.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/CliExecutorService.cs`
- Test: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`

- [ ] **Step 1: Write failing tests for executor capability queries and no-thread-id behavior**

Add tests for:

- `SupportsLowInterruptionContinue(toolId)` returns `true` only when the tool has a template or adapter default
- `CanStartLowInterruptionContinue(sessionId, toolId)` requires a reusable CLI thread/session id
- the executor yields an error chunk instead of faking continuation when no native thread id exists

- [ ] **Step 2: Run the focused executor test and confirm it fails**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CliExecutorServiceTests"
```

Expected:

- FAIL because the new interface methods and executor path do not exist yet

- [ ] **Step 3: Add executor interface methods**

Add explicit methods instead of overloading `ExecuteStreamAsync`:

```csharp
bool SupportsLowInterruptionContinue(string toolId);
bool CanStartLowInterruptionContinue(string sessionId, string toolId);
IAsyncEnumerable<StreamOutputChunk> ExecuteLowInterruptionContinueStreamAsync(string sessionId, string toolId, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement the new executor path without prompt injection**

Implementation rules:

- resolve the same session workspace and environment as normal execution
- reuse the existing native thread/session id from `GetCliThreadId(sessionId)`
- build arguments through `adapter.BuildLowInterruptionArguments(...)`
- stream output using the same process/read loop infrastructure already used by one-shot execution
- do not append any prompt text
- if the session has no native thread/session id, yield one completed error chunk and stop

- [ ] **Step 5: Keep current execution semantics intact**

Do not change:

- normal `ExecuteStreamAsync`
- process exit handling
- current `--dangerously-bypass-approvals-and-sandbox` use for Codex

- [ ] **Step 6: Re-run the domain tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CliExecutorServiceTests"
```

Expected:

- PASS

- [ ] **Step 7: Commit the executor chunk**

```bash
git add WebCodeCli.Domain/Domain/Service/ICliExecutorService.cs WebCodeCli.Domain/Domain/Service/CliExecutorService.cs WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs
git commit -m "Add one-shot native low-interruption continue execution"
```

## Chunk 2: Web And Mobile UI

### Task 3: Add a shared eligibility helper for latest-reply-only button display

**Files:**
- Create: `WebCodeCli/Helpers/LowInterruptionContinueHelper.cs`
- Test: `tests/WebCodeCli.Tests/LowInterruptionContinueHelperTests.cs`

- [ ] **Step 1: Write failing helper tests**

Cover these cases:

- latest assistant reply is eligible when current output has structured `todo_list`
- text fallback works when output contains `plan` or `backlog`
- old assistant replies are not actionable
- no button when `_isLoading` is `true`
- no button when the tool lacks native low-interruption support
- no button when the session lacks a native CLI thread/session id

- [ ] **Step 2: Run the helper tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\tests\WebCodeCli.Tests\WebCodeCli.Tests.csproj --filter "FullyQualifiedName~LowInterruptionContinueHelperTests"
```

Expected:

- FAIL because the helper does not exist yet

- [ ] **Step 3: Implement the helper as pure presentation logic**

Suggested shape:

```csharp
public static LowInterruptionContinueEligibility Evaluate(
    IReadOnlyList<ChatMessage> messages,
    string latestAssistantContent,
    bool hasStructuredTodoList,
    bool isToolSupported,
    bool hasCliThreadId,
    bool isProcessRunning)
```

Rules:

- no persistence
- no backlog aggregation across history
- latest reply only
- structured `todo_list` beats plain text

- [ ] **Step 4: Re-run the helper tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\tests\WebCodeCli.Tests\WebCodeCli.Tests.csproj --filter "FullyQualifiedName~LowInterruptionContinueHelperTests"
```

Expected:

- PASS

- [ ] **Step 5: Commit the helper chunk**

```bash
git add WebCodeCli/Helpers/LowInterruptionContinueHelper.cs tests/WebCodeCli.Tests/LowInterruptionContinueHelperTests.cs
git commit -m "Add low-interruption continue eligibility helper"
```

### Task 4: Wire desktop web to create a new assistant reply for low-interruption continue

**Files:**
- Modify: `WebCodeCli/Components/ChatMessageListPanel.razor`
- Modify: `WebCodeCli/Pages/CodeAssistant.razor`
- Modify: `WebCodeCli/Pages/CodeAssistant.razor.cs`
- Test: `tests/WebCodeCli.Tests/LowInterruptionContinueHelperTests.cs`

- [ ] **Step 1: Add the desktop UI parameters before editing the page logic**

Add component parameters such as:

- current latest eligible message id
- per-message button visibility predicate
- per-message disabled predicate
- `EventCallback<ChatMessage>` for `少打断执行`
- localized label text

- [ ] **Step 2: Render the button at the bottom of the latest eligible assistant card**

Render rules:

- assistant reply only
- latest eligible reply only
- disabled while `_isLoading`
- no in-place continuation of old content

- [ ] **Step 3: Add the desktop page handler**

Create a dedicated method, for example:

```csharp
private async Task StartLowInterruptionContinueAsync(ChatMessage sourceMessage)
```

Implementation requirements:

- bail out if `_isLoading`
- create a new in-progress assistant message
- call `CliExecutorService.ExecuteLowInterruptionContinueStreamAsync(_sessionId, _selectedToolId, default)`
- stream into the new reply exactly like normal send-message flow
- save the session when the run completes

- [ ] **Step 4: Reuse the current output-state machinery**

Do not add a second output transport path.

Reuse:

- `InitializeJsonlState(...)`
- `ProcessJsonlChunk(...)`
- `_currentAssistantMessage`
- existing completion/error handling

- [ ] **Step 5: Run focused tests and a local build**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\tests\WebCodeCli.Tests\WebCodeCli.Tests.csproj --filter "FullyQualifiedName~LowInterruptionContinueHelperTests"
dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
```

Expected:

- helper tests PASS
- solution builds

- [ ] **Step 6: Commit the desktop chunk**

```bash
git add WebCodeCli/Components/ChatMessageListPanel.razor WebCodeCli/Pages/CodeAssistant.razor WebCodeCli/Pages/CodeAssistant.razor.cs
git commit -m "Add desktop low-interruption continue action"
```

### Task 5: Wire mobile web to the same execution semantics

**Files:**
- Modify: `WebCodeCli/Pages/CodeAssistantMobile.razor`
- Modify: `WebCodeCli/Pages/CodeAssistantMobile.razor.cs`
- Test: `tests/WebCodeCli.Tests/LowInterruptionContinueHelperTests.cs`

- [ ] **Step 1: Add the mobile button to the latest eligible assistant bubble**

Rules:

- same eligibility as desktop
- same disabled behavior while `_isLoading`
- create a new bubble for the new run

- [ ] **Step 2: Add the mobile handler**

Mirror desktop behavior with a dedicated method that calls:

```csharp
CliExecutorService.ExecuteLowInterruptionContinueStreamAsync(_sessionId, _selectedToolId)
```

Do not route this through normal `SendMessage()`.

- [ ] **Step 3: Reuse the current mobile streaming flow**

Keep:

- `_currentAssistantMessage`
- current JSONL parsing
- session save behavior

- [ ] **Step 4: Run the helper tests and solution build again**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\tests\WebCodeCli.Tests\WebCodeCli.Tests.csproj --filter "FullyQualifiedName~LowInterruptionContinueHelperTests"
dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
```

Expected:

- PASS

- [ ] **Step 5: Commit the mobile chunk**

```bash
git add WebCodeCli/Pages/CodeAssistantMobile.razor WebCodeCli/Pages/CodeAssistantMobile.razor.cs
git commit -m "Add mobile low-interruption continue action"
```

## Chunk 3: Feishu And Verification

### Task 6: Add Feishu bottom actions and launch a new streaming card for low-interruption continue

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuStreamingCardChrome.cs`
- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardKitClient.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuCardKitClientTests.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs`

- [ ] **Step 1: Write failing Feishu tests first**

Cover:

- bottom action rendering in `FeishuCardKitClient`
- `low_interruption_continue` action routing in `FeishuCardActionService`
- no action when there is already an active session execution
- no action when the session has no native CLI thread/session id
- clicking the action starts a new streaming card instead of modifying the old completed card

- [ ] **Step 2: Run the Feishu test slice and confirm it fails**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuCardKitClientTests|FullyQualifiedName~FeishuCardActionServiceTests|FullyQualifiedName~FeishuChannelServiceTests"
```

Expected:

- FAIL because the bottom action model and handler do not exist yet

- [ ] **Step 3: Extend card chrome and renderer for bottom actions**

Add a dedicated action model rather than overloading overflow options.

Render placement:

- below content
- one primary button labeled `少打断执行`
- no button when the card is not the latest eligible result

- [ ] **Step 4: Add the new action payload and handler**

Add a new Feishu action value such as:

```json
{ "action": "low_interruption_continue", "session_id": "...", "chat_key": "...", "tool_id": "..." }
```

Handler requirements:

- reject when another execution is active for the session
- launch one new streaming handle/card
- call `ExecuteLowInterruptionContinueStreamAsync(...)`
- add the resulting assistant message to the session history the same way current Feishu execution does

- [ ] **Step 5: Update channel-side card eligibility**

At the end of a just-finished Feishu run:

- inspect current run output for structured `todo_list` first, text fallback second
- only the latest eligible completed card gets the button
- old cards stay historical

- [ ] **Step 6: Re-run the Feishu tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuCardKitClientTests|FullyQualifiedName~FeishuCardActionServiceTests|FullyQualifiedName~FeishuChannelServiceTests"
```

Expected:

- PASS

- [ ] **Step 7: Commit the Feishu chunk**

```bash
git add WebCodeCli.Domain/Domain/Model/Channels/FeishuStreamingCardChrome.cs WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs WebCodeCli.Domain/Domain/Service/Channels/FeishuCardKitClient.cs WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs WebCodeCli.Domain.Tests/FeishuCardKitClientTests.cs WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs
git commit -m "Add Feishu low-interruption continue action"
```

### Task 7: Final verification and manual smoke checklist

**Files:**
- Modify: `docs/superpowers/plans/2026-04-19-low-interruption-native-continue-plan.md`
- Test: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuCardKitClientTests.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs`
- Test: `tests/WebCodeCli.Tests/LowInterruptionContinueHelperTests.cs`

- [ ] **Step 1: Run the full automated test set touched by this feature**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~CliExecutorServiceTests|FullyQualifiedName~FeishuCardKitClientTests|FullyQualifiedName~FeishuCardActionServiceTests|FullyQualifiedName~FeishuChannelServiceTests"
dotnet test D:\VSWorkshop\WebCode\tests\WebCodeCli.Tests\WebCodeCli.Tests.csproj --filter "FullyQualifiedName~LowInterruptionContinueHelperTests"
dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
```

Expected:

- all filtered tests PASS
- solution build PASS

- [ ] **Step 2: Run manual desktop smoke**

Checklist:

- latest eligible assistant card shows `少打断执行`
- button is disabled while a run is already active
- clicking it creates a new assistant reply
- normal send-message behavior is unchanged

- [ ] **Step 3: Run manual mobile smoke**

Checklist:

- latest eligible assistant bubble shows `少打断执行`
- clicking it creates a new assistant bubble
- no duplicate button on older assistant replies

- [ ] **Step 4: Run manual Feishu smoke**

Checklist:

- latest eligible completed card shows `少打断执行`
- clicking it creates a new streaming card
- card has no button when the session lacks a CLI thread/session id
- card has no button while another execution is active

- [ ] **Step 5: Record any deviations in the plan or implementation notes**

If a tool-specific CLI flag differs from the expected template, update:

- tool config comments
- adapter defaults
- implementation notes in the final report

- [ ] **Step 6: Commit the verification chunk**

```bash
git add .
git commit -m "Verify low-interruption native continue flow"
```
