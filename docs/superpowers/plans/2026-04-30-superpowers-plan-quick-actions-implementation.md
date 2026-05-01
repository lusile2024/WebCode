# Superpowers Plan Quick Actions Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current low-interruption streaming-card affordance with a unified `SuperpowersQuickAction` surface across desktop Web, mobile Web, and Feishu help cards, including `执行 plan`, `子代理执行 plan`, and auto-prefixed quick-input submission.

**Architecture:** Perform a semantic rename instead of a wrapper alias. Centralize eligibility and prompt-building rules in shared helpers, then update the desktop page, mobile page, and Feishu help-card action flow to consume the same rules. Remove the old low-interruption visibility heuristics from this feature path and gate rendering on workspace plan-file presence plus session-history `superpowers` presence.

**Tech Stack:** ASP.NET Core, Blazor Server, `WebCodeCli.Domain` services, Feishu card-kit JSON rendering, xUnit, existing solution/test projects.

Depends on:
- `docs/superpowers/specs/2026-04-30-superpowers-plan-quick-actions-design.md`

---

## File Map

### Shared quick-action semantics

- Create: `WebCodeCli/Helpers/SuperpowersQuickActionHelper.cs`
  Web-side eligibility helper for latest-message placement, plan-file detection, and session-history matching.
- Create: `WebCodeCli.Domain/Domain/Model/SuperpowersQuickActionDefaults.cs`
  Shared field names, button labels, instructional text, and canonical prompt strings for the Feishu/domain side.
- Create: `WebCodeCli.Domain/Domain/Service/Channels/SuperpowersQuickActionHelper.cs`
  Feishu-side helper for action names, bottom prompt/button payloads, and channel-side eligibility checks.
- Create: `WebCodeCli.Domain/Domain/Service/SuperpowersPromptBuilder.cs`
  Canonical builder for:
  - `使用superpowers的executing-plans技能执行计划`
  - `使用superpowers的executing-plans技能执行计划,并且使用superpowers的subagent-driven-development技能`
  - `使用superpowers技能，<input>`

### Web UI

- Modify: `WebCodeCli/Components/ChatMessageListPanel.razor`
  Replace `LowInterruptionContinue*` parameters and markup with `SuperpowersQuickAction*` parameters, instructional text, input, and two buttons.
- Modify: `WebCodeCli/Pages/CodeAssistant.razor`
  Pass renamed quick-action parameters into `ChatMessageListPanel`.
- Modify: `WebCodeCli/Pages/CodeAssistant.razor.cs`
  Replace low-interruption state and handlers with standard-send based Superpowers quick-action handlers.
- Modify: `WebCodeCli/Pages/CodeAssistantMobile.razor`
  Replace the mobile low-interruption block with the new instructional text, input, and two buttons.
- Modify: `WebCodeCli/Pages/CodeAssistantMobile.razor.cs`
  Mirror the desktop state and handler changes for mobile.

### Feishu help-card quick actions

- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs`
  Add action names and any payload fields needed for Superpowers quick-input submit and the two plan buttons.
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuHelpCardBuilder.cs`
  Append the instructional text, quick-input field, `执行 plan`, and `子代理执行 plan` to the bottom of `feishuhelp` cards.
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs`
  Handle the two plan buttons and quick-input submit by building the approved prompt and sending it through the normal active-session execution path.
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs`
  Replace low-interruption streaming-card bottom-action wiring with the new quick-action eligibility and payload generation.
- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuStreamingCardChrome.cs`
  Rename or replace bottom prompt/action DTO usage if necessary so the streaming-card chrome no longer exposes low-interruption-specific naming.

### Tests

- Create: `tests/WebCodeCli.Tests/SuperpowersQuickActionHelperTests.cs`
  Cover plan-file gating, session-history matching, latest-assistant placement, and disabled state.
- Modify or Replace: `tests/WebCodeCli.Tests/LowInterruptionContinueHelperTests.cs`
  Either migrate these tests to the new helper file or delete once the replacement tests exist.
- Create: `WebCodeCli.Domain.Tests/SuperpowersPromptBuilderTests.cs`
  Cover both fixed plan prompts and the auto-prefix rule.
- Modify: `WebCodeCli.Domain.Tests/FeishuHelpCardBuilderTests.cs`
  Cover the new help-card bottom instructional text, input, and two buttons.
- Modify: `WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs`
  Cover quick-input submit, `执行 plan`, `子代理执行 plan`, and busy-state rejection.
- Modify: `WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs`
  Cover streaming-card bottom quick-action eligibility and button payload construction.
- Modify: `WebCodeCli.Domain.Tests/FeishuCardKitClientTests.cs`
  Update bottom prompt/action JSON assertions if DTO/property names or field names change.
- Modify: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`
  Remove or update assertions that assume the Web/Feishu UI feature still uses low-interruption continue semantics if those tests are coupled through prompts/defaults.

### Explicit non-goals

- Do not change `CliExecutorService.ExecuteLowInterruptionContinueStreamAsync` in this plan unless a compile fix is required by shared constant extraction.
- Do not add plan-file selection UI.
- Do not change the main composer input behavior outside the new quick-action blocks.
- Do not add new dependencies.

## Chunk 1: Replace the Shared Semantics and Prompt Contracts

### Task 1: Introduce canonical prompt-building and defaults without touching UI yet

**Files:**
- Create: `WebCodeCli.Domain/Domain/Model/SuperpowersQuickActionDefaults.cs`
- Create: `WebCodeCli.Domain/Domain/Service/SuperpowersPromptBuilder.cs`
- Test: `WebCodeCli.Domain.Tests/SuperpowersPromptBuilderTests.cs`

- [ ] **Step 1: Write the failing domain tests for the three canonical prompt outputs**

Add tests that prove:

```csharp
[Fact]
public void BuildExecutePlanPrompt_ReturnsApprovedPrompt()
{
    Assert.Equal(
        "使用superpowers的executing-plans技能执行计划",
        SuperpowersPromptBuilder.BuildExecutePlanPrompt());
}

[Fact]
public void BuildSubagentExecutePlanPrompt_ReturnsApprovedCombinedPrompt()
{
    Assert.Equal(
        "使用superpowers的executing-plans技能执行计划,并且使用superpowers的subagent-driven-development技能",
        SuperpowersPromptBuilder.BuildSubagentExecutePlanPrompt());
}

[Theory]
[InlineData("写一个执行步骤", "使用superpowers技能，写一个执行步骤")]
[InlineData("使用superpowers技能，写一个执行步骤", "使用superpowers技能，写一个执行步骤")]
public void BuildQuickSkillPrompt_AppliesPrefixOnlyWhenMissing(string input, string expected)
{
    Assert.Equal(expected, SuperpowersPromptBuilder.BuildQuickSkillPrompt(input));
}
```

- [ ] **Step 2: Run the focused domain test and confirm it fails**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~SuperpowersPromptBuilderTests"
```

Expected:

- FAIL because `SuperpowersPromptBuilder` and `SuperpowersQuickActionDefaults` do not exist yet

- [ ] **Step 3: Create `SuperpowersQuickActionDefaults.cs` with only the shared constants needed by UI and Feishu**

Include constants for:

- two action/button labels
- the instructional text
- the quick-input field name
- the quick-input placeholder

Do not move unrelated low-interruption execution defaults into this new file.

- [ ] **Step 4: Create `SuperpowersPromptBuilder.cs` with the exact approved prompt strings**

Use a minimal API shape like:

```csharp
public static class SuperpowersPromptBuilder
{
    public static string BuildExecutePlanPrompt() => "...";
    public static string BuildSubagentExecutePlanPrompt() => "...";
    public static string? BuildQuickSkillPrompt(string? input) => ...;
}
```

Rules:

- trim whitespace
- return `null` or empty-safe sentinel for blank input
- do not double-prefix quick input

- [ ] **Step 5: Re-run the focused domain test and confirm it passes**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~SuperpowersPromptBuilderTests"
```

Expected:

- PASS

- [ ] **Step 6: Commit the prompt-contract chunk**

```powershell
$message = @"
Define canonical Superpowers quick-action prompt builders

Constraint: Prompt wording must match the approved Chinese strings exactly
Rejected: Build prompt strings ad hoc in each UI handler | would drift across Web and Feishu
Confidence: high
Scope-risk: narrow
Reversibility: clean
Directive: Route every quick-action prompt through SuperpowersPromptBuilder instead of duplicating literals
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter FullyQualifiedName~SuperpowersPromptBuilderTests
Not-tested: Web and Feishu integration
"@
git add WebCodeCli.Domain/Domain/Model/SuperpowersQuickActionDefaults.cs WebCodeCli.Domain/Domain/Service/SuperpowersPromptBuilder.cs WebCodeCli.Domain.Tests/SuperpowersPromptBuilderTests.cs
git commit -m $message
```

### Task 2: Replace the Web-side eligibility helper and remove low-interruption visibility heuristics

**Files:**
- Create: `WebCodeCli/Helpers/SuperpowersQuickActionHelper.cs`
- Create: `tests/WebCodeCli.Tests/SuperpowersQuickActionHelperTests.cs`
- Modify or Delete: `WebCodeCli/Helpers/LowInterruptionContinueHelper.cs`
- Modify or Delete: `tests/WebCodeCli.Tests/LowInterruptionContinueHelperTests.cs`

- [ ] **Step 1: Write the failing helper tests for plan-file gating and session-history matching**

Add tests that prove:

- no `docs/superpowers/plans/*.md` match means hidden
- plan-file match plus any message containing `superpowers` means visible
- latest eligible assistant message is the only placement target
- running state disables but does not hide

Use test data shaped like:

```csharp
var messages = new[]
{
    new ChatMessage { Role = "user", Content = "使用superpowers技能，先看一下计划", IsCompleted = true },
    new ChatMessage { Id = "a1", Role = "assistant", Content = "好的", IsCompleted = true }
};
```

- [ ] **Step 2: Run the focused Web helper tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\tests\WebCodeCli.Tests\WebCodeCli.Tests.csproj --filter "FullyQualifiedName~SuperpowersQuickActionHelperTests"
```

Expected:

- FAIL because the new helper does not exist yet

- [ ] **Step 3: Create `SuperpowersQuickActionHelper.cs` with a narrow eligibility record**

Use a minimal record like:

```csharp
public readonly record struct SuperpowersQuickActionEligibility(
    string? MessageId,
    bool ShowQuickActions,
    bool IsDisabled);
```

The helper should accept:

- current messages
- a boolean for `hasSuperpowersPlanFiles`
- a boolean for `isProcessRunning`

Do not retain the old `hasCliThreadId`, `isToolSupported`, `todo_list`, or plain-text `plan|todo` signal logic for this feature.

- [ ] **Step 4: Replace the old low-interruption helper file or remove it once all callers are migrated**

If keeping the old file temporarily to preserve compile order:

- mark it for deletion in a later chunk
- do not route new quick-action logic through it

- [ ] **Step 5: Re-run the focused Web helper tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\tests\WebCodeCli.Tests\WebCodeCli.Tests.csproj --filter "FullyQualifiedName~SuperpowersQuickActionHelperTests"
```

Expected:

- PASS

- [ ] **Step 6: Commit the helper-semantics chunk**

```powershell
$message = @"
Replace low-interruption visibility heuristics with Superpowers quick-action gating

Constraint: Visibility now depends on plan-file presence and session-history superpowers usage
Rejected: Preserve todo-list and plain-text task heuristics | no longer matches the approved UX contract
Confidence: high
Scope-risk: narrow
Reversibility: clean
Directive: Keep Superpowers quick-action eligibility separate from CLI resume capability checks
Tested: dotnet test D:\VSWorkshop\WebCode\tests\WebCodeCli.Tests\WebCodeCli.Tests.csproj --filter FullyQualifiedName~SuperpowersQuickActionHelperTests
Not-tested: Razor integration
"@
git add WebCodeCli/Helpers/SuperpowersQuickActionHelper.cs tests/WebCodeCli.Tests/SuperpowersQuickActionHelperTests.cs WebCodeCli/Helpers/LowInterruptionContinueHelper.cs tests/WebCodeCli.Tests/LowInterruptionContinueHelperTests.cs
git commit -m $message
```

## Chunk 2: Update Desktop and Mobile Web Quick-Action UI

### Task 3: Replace `ChatMessageListPanel` parameters and markup with the new quick-action surface

**Files:**
- Modify: `WebCodeCli/Components/ChatMessageListPanel.razor`

- [ ] **Step 1: Write or extend a component-level smoke test if an existing pattern exists; otherwise document the manual assertions in this task**

If there is no existing Razor component test harness in this repo, do not add a new test framework just for this component. Instead, record the exact manual assertions to validate after build:

- latest assistant message shows instructional text
- two buttons render side by side
- input reflects bound value
- disabled state affects input and both buttons

- [ ] **Step 2: Rename the component parameters away from `LowInterruptionContinue*`**

Use parameter names that reflect the approved semantics, for example:

```csharp
[Parameter] public string? SuperpowersQuickActionMessageId { get; set; }
[Parameter] public bool SuperpowersQuickActionDisabled { get; set; }
[Parameter] public string SuperpowersQuickInput { get; set; } = string.Empty;
[Parameter] public EventCallback<string> SuperpowersQuickInputChanged { get; set; }
[Parameter] public EventCallback<ChatMessage> OnExecuteSuperpowersPlan { get; set; }
[Parameter] public EventCallback<ChatMessage> OnExecuteSuperpowersSubagentPlan { get; set; }
[Parameter] public EventCallback<ChatMessage> OnSubmitSuperpowersQuickInput { get; set; }
```

- [ ] **Step 3: Replace the existing markup block with instructional text, one input, and two buttons**

Keep the bottom-of-message placement behavior, but replace the current low-interruption prompt/button layout.

Use the instructional copy from the approved spec:

```text
可直接输入 superpowers 指令；未填写前缀时，会自动补成“使用superpowers技能，”。
```

- [ ] **Step 4: Add Enter submission behavior for the quick-input field**

Pressing Enter in the quick-input box must invoke `OnSubmitSuperpowersQuickInput` for that message.

Do not route this input through the main composer component.

- [ ] **Step 5: Keep the latest-message-only logic but rename the predicate**

Replace `ShouldShowLowInterruptionContinue` with a new predicate that checks the renamed message id and delegate availability.

- [ ] **Step 6: Build the solution and confirm Razor compiles**

Run:

```powershell
dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
```

Expected:

- PASS, or compile errors only in page call sites that still reference old parameter names

- [ ] **Step 7: Commit the shared message-panel UI change**

```powershell
$message = @"
Rename streaming message quick actions to Superpowers semantics

Constraint: The message-card surface must expose two plan buttons plus quick input
Rejected: Keep low-interruption parameter names as aliases | would preserve the wrong mental model
Confidence: high
Scope-risk: moderate
Reversibility: clean
Directive: Do not reintroduce thread-resume wording in message-card quick-action parameters
Tested: dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
Not-tested: Interactive browser behavior
"@
git add WebCodeCli/Components/ChatMessageListPanel.razor
git commit -m $message
```

### Task 4: Rewire the desktop page to send normal chat messages for all three quick actions

**Files:**
- Modify: `WebCodeCli/Pages/CodeAssistant.razor`
- Modify: `WebCodeCli/Pages/CodeAssistant.razor.cs`

- [ ] **Step 1: Write failing tests if there are existing page-level tests; otherwise capture exact manual assertions and proceed with compile-driven TDD**

Manual assertions to validate later:

- `执行 plan` adds a new user message with the exact fixed prompt
- `子代理执行 plan` adds a new user message with the combined fixed prompt
- quick-input submit auto-prefixes only when missing
- block visibility depends on plan-file presence plus session-history match

- [ ] **Step 2: Replace `_lowInterruptionContinuePrompt` and related state with a Superpowers quick-input field**

Use a clearly named page field such as:

```csharp
private string _superpowersQuickInput = string.Empty;
```

- [ ] **Step 3: Replace `CurrentLowInterruptionContinueEligibility` with `CurrentSuperpowersQuickActionEligibility`**

Have it call `SuperpowersQuickActionHelper.Evaluate(...)`.

Compute `hasSuperpowersPlanFiles` using a dedicated workspace check against `docs/superpowers/plans/*.md`.

- [ ] **Step 4: Replace the low-interruption button callbacks with standard-send handlers**

Implement:

- `StartSuperpowersExecutePlanAsync(ChatMessage sourceMessage)`
- `StartSuperpowersSubagentExecutePlanAsync(ChatMessage sourceMessage)`
- `SubmitSuperpowersQuickInputAsync(ChatMessage sourceMessage)`

Each should:

- validate the latest eligible message
- build the prompt via `SuperpowersPromptBuilder`
- route through the normal send-message streaming flow instead of `ExecuteLowInterruptionContinueStreamAsync`

- [ ] **Step 5: Refactor the common send path so button-triggered prompts can reuse it without mutating the main composer text**

Prefer a private helper such as:

```csharp
private Task SendMessageAsync(string message, string? overrideToolId = null)
```

Then let the main `SendMessage()` call that helper with `_inputMessage`.

- [ ] **Step 6: Update the `.razor` bindings to the renamed component parameters**

Remove:

- `LowInterruptionContinueMessageId`
- `LowInterruptionContinueDisabled`
- `LowInterruptionContinuePrompt`
- `OnLowInterruptionContinue`

Add the new Superpowers quick-action parameters instead.

- [ ] **Step 7: Build the solution and confirm the desktop page compiles**

Run:

```powershell
dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
```

Expected:

- PASS, or remaining errors only in the mobile page that still references old names

- [ ] **Step 8: Commit the desktop page integration**

```powershell
$message = @"
Route desktop Superpowers quick actions through normal send flow

Constraint: Quick actions must send chat prompts, not trigger low-interruption native continue
Rejected: Keep ExecuteLowInterruptionContinueStreamAsync as the button path | wrong behavior for the approved design
Confidence: high
Scope-risk: moderate
Reversibility: clean
Directive: Keep all desktop quick-action submissions on the same chat-send path as normal user messages
Tested: dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
Not-tested: Browser interaction and Feishu surfaces
"@
git add WebCodeCli/Pages/CodeAssistant.razor WebCodeCli/Pages/CodeAssistant.razor.cs
git commit -m $message
```

### Task 5: Mirror the same quick-action behavior on mobile

**Files:**
- Modify: `WebCodeCli/Pages/CodeAssistantMobile.razor`
- Modify: `WebCodeCli/Pages/CodeAssistantMobile.razor.cs`

- [ ] **Step 1: Replace the mobile low-interruption block with the new instructional text, input, and two buttons**

Keep the mobile-first stacked layout, but ensure the same action semantics as desktop.

- [ ] **Step 2: Replace the mobile state and helper usage with the new `SuperpowersQuickActionHelper`**

Remove dependence on:

- `SupportsLowInterruptionContinue()`
- `LowInterruptionContinueDefaults`
- `StartLowInterruptionContinueAsync(...)`

for this UI feature.

- [ ] **Step 3: Implement the three mobile handlers using the same prompt builder and send helper strategy as desktop**

Use the same exact prompt strings; do not duplicate literals in the mobile page.

- [ ] **Step 4: Build the solution and confirm mobile compiles**

Run:

```powershell
dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
```

Expected:

- PASS for both desktop and mobile pages

- [ ] **Step 5: Commit the mobile integration**

```powershell
$message = @"
Mirror Superpowers quick actions on mobile streaming replies

Constraint: Mobile behavior must match desktop prompt and gating semantics exactly
Rejected: Keep a separate mobile-only quick-action interpretation | would drift from desktop and Feishu
Confidence: high
Scope-risk: moderate
Reversibility: clean
Directive: Keep mobile quick-action strings and prompt-building logic shared with desktop
Tested: dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
Not-tested: Manual mobile browser interaction
"@
git add WebCodeCli/Pages/CodeAssistantMobile.razor WebCodeCli/Pages/CodeAssistantMobile.razor.cs
git commit -m $message
```

## Chunk 3: Replace Feishu Quick Actions and Help-Card Entry Points

### Task 6: Replace low-interruption Feishu streaming-card payloads with Superpowers quick actions

**Files:**
- Create or Replace: `WebCodeCli.Domain/Domain/Service/Channels/SuperpowersQuickActionHelper.cs`
- Modify or Delete: `WebCodeCli.Domain/Domain/Service/Channels/LowInterruptionContinueHelper.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs`
- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuStreamingCardChrome.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuCardKitClientTests.cs`

- [ ] **Step 1: Write failing Feishu-channel tests for the new bottom action payload**

Add tests that prove:

- streaming-card quick actions appear only when `docs/superpowers/plans/*.md` exists and session history contains `superpowers`
- the prompt field uses the new field name from `SuperpowersQuickActionDefaults`
- both buttons are present with distinct action names
- running state disables both buttons and the input where the chrome supports disabled state

- [ ] **Step 2: Run the focused Feishu channel/card-kit tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuChannelServiceTests|FullyQualifiedName~FeishuCardKitClientTests"
```

Expected:

- FAIL because the old helper and payload model only know about low-interruption prompt plus one action

- [ ] **Step 3: Replace the old channel helper with a new `SuperpowersQuickActionHelper`**

Responsibilities:

- action names for quick input submit, `执行 plan`, and `子代理执行 plan`
- button labels
- prompt/input metadata
- Feishu-specific eligibility wrapper that mirrors the approved rules

Do not preserve the old `plan|todo` text-regex behavior in this helper.

- [ ] **Step 4: Update `FeishuStreamingCardChrome` only as much as needed to represent the new bottom quick-action payload**

If the existing `BottomPrompt` and `BottomActions` structures can express:

- instructional text
- one input
- two buttons

reuse them. If not, extend the chrome DTO minimally rather than introducing a parallel card type.

- [ ] **Step 5: Update `FeishuChannelService` to build the new quick-action block**

Replace calls to the old helper so the streaming-card footer is built from:

- plan-file presence
- session-history `superpowers` presence
- current running state

- [ ] **Step 6: Re-run the focused Feishu channel/card-kit tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuChannelServiceTests|FullyQualifiedName~FeishuCardKitClientTests"
```

Expected:

- PASS

- [ ] **Step 7: Commit the Feishu footer-payload change**

```powershell
$message = @"
Replace Feishu low-interruption footer payloads with Superpowers quick actions

Constraint: Feishu footer visibility must match the approved plan-file and session-history rules
Rejected: Reuse low-interruption action names and prompt metadata | would preserve obsolete semantics
Confidence: high
Scope-risk: moderate
Reversibility: clean
Directive: Keep Feishu footer action names and field names aligned with shared Superpowers quick-action defaults
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter FullyQualifiedName~FeishuChannelServiceTests|FullyQualifiedName~FeishuCardKitClientTests
Not-tested: End-to-end callback execution
"@
git add WebCodeCli.Domain/Domain/Service/Channels/SuperpowersQuickActionHelper.cs WebCodeCli.Domain/Domain/Service/Channels/LowInterruptionContinueHelper.cs WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs WebCodeCli.Domain/Domain/Model/Channels/FeishuStreamingCardChrome.cs WebCodeCli.Domain.Tests/FeishuChannelServiceTests.cs WebCodeCli.Domain.Tests/FeishuCardKitClientTests.cs
git commit -m $message
```

### Task 7: Add `feishuhelp` bottom quick input and both plan buttons, then route them through normal command execution

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuHelpCardBuilder.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuHelpCardBuilderTests.cs`
- Test: `WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs`

- [ ] **Step 1: Write failing tests for help-card rendering and action handling**

Add tests that prove:

- the help card bottom includes the instructional text, quick-input field, `执行 plan`, and `子代理执行 plan`
- quick-input submit without prefix sends `使用superpowers技能，<input>`
- quick-input submit with prefix leaves the input unchanged
- `执行 plan` sends `使用superpowers的executing-plans技能执行计划`
- `子代理执行 plan` sends `使用superpowers的executing-plans技能执行计划,并且使用superpowers的subagent-driven-development技能`
- busy-state or missing-active-session flows still return the existing warning patterns

- [ ] **Step 2: Run the focused Feishu help/action tests and confirm they fail**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuHelpCardBuilderTests|FullyQualifiedName~FeishuCardActionServiceTests"
```

Expected:

- FAIL because the help card and card-action handler do not yet know about the new quick-action actions

- [ ] **Step 3: Extend `FeishuHelpCardAction.cs` with action names/fields for the new footer actions**

Add only the payload needed for:

- quick-input submit
- execute-plan button
- subagent-execute-plan button

Reuse `session_id`, `chat_key`, and `tool_id` where possible.

- [ ] **Step 4: Update `FeishuHelpCardBuilder.cs` to append the new footer block**

Place it at the bottom of the command/help card so it is available in the same card view without navigating away.

Do not remove existing help-card actions unrelated to this feature.

- [ ] **Step 5: Update `FeishuCardActionService.cs` to map the three new actions to the normal execution path**

Implementation rules:

- quick input uses `SuperpowersPromptBuilder.BuildQuickSkillPrompt(...)`
- `执行 plan` uses `BuildExecutePlanPrompt()`
- `子代理执行 plan` uses `BuildSubagentExecutePlanPrompt()`
- all three route through the same active-session execution path already used by help-card command submission
- none of them call `ExecuteLowInterruptionContinueStreamAsync`

- [ ] **Step 6: Re-run the focused Feishu help/action tests and confirm they pass**

Run:

```powershell
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter "FullyQualifiedName~FeishuHelpCardBuilderTests|FullyQualifiedName~FeishuCardActionServiceTests"
```

Expected:

- PASS

- [ ] **Step 7: Commit the Feishu help-card quick-action integration**

```powershell
$message = @"
Add Superpowers quick actions to Feishu help cards

Constraint: Help-card plan actions must send the approved fixed prompts without selecting a concrete plan file
Rejected: Reuse low-interruption continue execution for help-card actions | would execute the wrong workflow
Confidence: high
Scope-risk: moderate
Reversibility: clean
Directive: Keep Feishu help-card prompt submission on the same command execution path as normal help-card command runs
Tested: dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj --filter FullyQualifiedName~FeishuHelpCardBuilderTests|FullyQualifiedName~FeishuCardActionServiceTests
Not-tested: Real Feishu callback round-trip
"@
git add WebCodeCli.Domain/Domain/Model/Channels/FeishuHelpCardAction.cs WebCodeCli.Domain/Domain/Service/Channels/FeishuHelpCardBuilder.cs WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs WebCodeCli.Domain.Tests/FeishuHelpCardBuilderTests.cs WebCodeCli.Domain.Tests/FeishuCardActionServiceTests.cs
git commit -m $message
```

## Chunk 4: Cleanup, Regression Pass, and Manual Verification

### Task 8: Remove obsolete low-interruption quick-action naming and update any coupled tests/constants

**Files:**
- Modify or Delete: `WebCodeCli.Domain/Domain/Model/LowInterruptionContinueDefaults.cs`
- Modify: `WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs`
- Modify: any remaining `LowInterruptionContinue*` references surfaced by solution-wide search

- [ ] **Step 1: Run a solution-wide search for remaining `LowInterruptionContinue` references**

Run:

```powershell
rg -n "LowInterruptionContinue" D:\VSWorkshop\WebCode
```

Expected:

- only true low-interruption executor functionality remains, not UI/Feishu quick-action naming

- [ ] **Step 2: Remove or narrow obsolete defaults/constants that only served the old quick-action UI**

If `LowInterruptionContinueDefaults` is now only needed by the executor path, keep only executor-relevant constants there.

If it is no longer needed at all, delete it and update references accordingly.

- [ ] **Step 3: Update any remaining tests that still assert old prompt labels, field names, or button text**

Keep true low-interruption execution tests only where they still cover executor behavior, not the replaced quick-action UX.

- [ ] **Step 4: Run both test projects plus a full solution build**

Run:

```powershell
dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln
dotnet test D:\VSWorkshop\WebCode\tests\WebCodeCli.Tests\WebCodeCli.Tests.csproj
dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj
```

Expected:

- PASS for build and both test projects

- [ ] **Step 5: Perform the required manual verification**

Desktop Web:

- open a session without `superpowers` in history and confirm the block is hidden
- add a session message containing `superpowers`, ensure a plan file exists under `docs/superpowers/plans/`, and confirm the block appears only under the latest eligible assistant message
- click `执行 plan` and verify the created user message is exactly `使用superpowers的executing-plans技能执行计划`
- click `子代理执行 plan` and verify the created user message is exactly `使用superpowers的executing-plans技能执行计划,并且使用superpowers的subagent-driven-development技能`
- submit quick input without prefix and verify auto-prefixing
- submit quick input with existing prefix and verify no duplicate prefix

Mobile Web:

- repeat the same prompt checks on the mobile layout

Feishu:

- open `feishuhelp`
- verify the footer shows the instructional text, quick-input field, and both buttons
- verify each action sends the expected prompt into the active session path

- [ ] **Step 6: Commit the cleanup and verification pass**

```powershell
$message = @"
Finish Superpowers quick-action migration and remove obsolete UI naming

Constraint: Low-interruption executor support may remain, but the quick-action UX must no longer use that naming or visibility model
Rejected: Leave mixed old and new names in place | would cause long-term maintenance drift
Confidence: medium
Scope-risk: moderate
Reversibility: clean
Directive: Keep future prompt and visibility changes centralized in Superpowers quick-action helpers and builders
Tested: dotnet build D:\VSWorkshop\WebCode\WebCodeCli.sln; dotnet test D:\VSWorkshop\WebCode\tests\WebCodeCli.Tests\WebCodeCli.Tests.csproj; dotnet test D:\VSWorkshop\WebCode\WebCodeCli.Domain.Tests\WebCodeCli.Domain.Tests.csproj; manual desktop/mobile/Feishu verification
Not-tested: Production Feishu tenant behavior outside local/staging validation
"@
git add WebCodeCli.Domain/Domain/Model/LowInterruptionContinueDefaults.cs WebCodeCli.Domain.Tests/CliExecutorServiceTests.cs WebCodeCli.Domain WebCodeCli tests
git commit -m $message
```

Plan complete and saved to `docs/superpowers/plans/2026-04-30-superpowers-plan-quick-actions-implementation.md`. Ready to execute?
