# Superpowers Plan Quick Actions Design

Date: 2026-04-30
Status: approved in discussion, pending implementation planning

## Goal

Replace the current "low interruption continue" affordance under streaming assistant replies with a new `SuperpowersQuickAction` surface centered on Superpowers plan execution and skill prompting.

The new interaction must:

- replace the existing streaming-card bottom action label and behavior with `执行 plan`
- add a second adjacent button `子代理执行 plan`
- show the streaming-card quick-action area only when the workspace has at least one Superpowers plan file and the current session history contains `superpowers`
- add a quick-input field under the streaming reply card
- add the same quick-input field to the bottom of the `feishuhelp` help card
- automatically prepend `使用superpowers技能，` when the user submits quick-input text without that prefix
- send a fixed prompt `使用superpowers的executing-plans技能执行计划` when the user clicks `执行 plan`
- send a fixed prompt `使用superpowers的executing-plans技能执行计划,并且使用superpowers的subagent-driven-development技能` when the user clicks `子代理执行 plan`
- keep desktop Web, mobile Web, and Feishu help card behavior aligned through shared eligibility and prompt-building rules

This change is a semantic replacement, not a skin-deep label swap. The repository should stop modeling this affordance as CLI thread resume and instead model it as a Superpowers quick-action surface.

## Scope

In scope:

- rename the current low-interruption surface to a new `SuperpowersQuickAction` semantic domain
- change streaming reply card visibility logic
- change streaming reply card button text and click behavior
- add streaming reply card quick-input UI and submit behavior
- add `feishuhelp` help card bottom quick-input UI and both plan-execution buttons
- share prompt construction and eligibility logic across Web and Feishu
- keep desktop and mobile layouts consistent in behavior

Out of scope:

- selecting a specific plan file before execution
- parsing plan files to derive a plan target
- changing the main chat composer behavior
- changing unrelated session resume mechanics elsewhere in the product
- changing Superpowers plan authoring workflows

## Problem

The current bottom action on streaming assistant replies is built around a "low interruption continue" concept. That no longer matches the desired workflow.

The desired workflow is:

- use the presence of Superpowers plans in the workspace as the gating signal
- use session history containing `superpowers` as the conversational relevance signal
- expose an explicit `执行 plan` action that delegates plan discovery to the model
- expose an explicit `子代理执行 plan` action that routes to a combined executing-plans plus subagent-driven-development prompt
- expose a compact text field that can quickly route follow-up requests through Superpowers skills

The current implementation has several mismatches:

- the button is semantically tied to resuming the existing CLI thread
- the visibility logic depends on thread-reuse and unfinished-work heuristics
- the quick follow-up text field is named and structured around continuation rather than Superpowers skill routing
- `feishuhelp` does not expose the same quick Superpowers entry point

This creates both user-facing confusion and implementation drift. The interaction should instead become a single concept with one naming system, one visibility model, and one prompt-construction rule set.

## User Experience

### Streaming Reply Card

When eligibility conditions are met, the latest eligible completed assistant reply shows a `SuperpowersQuickAction` block at the bottom.

The block layout is:

1. one-line instructional text
2. one-line text input
3. one action row with `执行 plan` and `子代理执行 plan`

Recommended instructional text:

`可直接输入 superpowers 指令；未填写前缀时，会自动补成“使用superpowers技能，”。`

Button text:

`执行 plan`

Secondary button text:

`子代理执行 plan`

Button behavior:

- clicking the button submits the fixed prompt `使用superpowers的executing-plans技能执行计划`
- clicking the secondary button submits the fixed prompt `使用superpowers的executing-plans技能执行计划,并且使用superpowers的subagent-driven-development技能`
- no plan picker, modal, or file selector is shown
- the model is expected to inspect the workspace and choose the relevant plan itself

Input behavior:

- pressing Enter submits the text
- blank or whitespace-only input is ignored
- if the input already starts with `使用superpowers技能，`, submit it unchanged
- otherwise submit `使用superpowers技能，<user input>`

### Mobile Web

Mobile behavior must match desktop behavior exactly:

- same visibility rules
- same instruction text
- same prefixing rules
- same fixed `执行 plan` action

Only layout differs to fit the smaller viewport.

### Feishu Help Card

The bottom of the `feishuhelp` help card adds the same Superpowers quick-action block:

1. one-line instructional text
2. one-line text input
3. one action row with `执行 plan` and `子代理执行 plan`

The Feishu card behavior must match Web semantics:

- input submission auto-prefixes with `使用superpowers技能，` when needed
- button click sends `使用superpowers的executing-plans技能执行计划`
- secondary button click sends `使用superpowers的executing-plans技能执行计划,并且使用superpowers的subagent-driven-development技能`
- the action routes through the active chat session rather than requiring a separate plan selection step

## Eligibility Rules

The `SuperpowersQuickAction` block is shown only when all of the following are true:

1. the workspace contains at least one file matching `docs/superpowers/plans/*.md`
2. any message in the current session history contains `superpowers`
3. there is no in-flight execution currently running

Additional placement rule:

- on streaming assistant replies, render the block only under the latest eligible completed assistant message, not under every assistant message

This replaces the old heuristics based on CLI-thread reuse, unfinished todo detection, or plain-text plan/task signal scanning.

## Functional Requirements

### Prompt Construction

The system needs three canonical prompt builders:

1. `BuildExecutePlanPrompt()`
   Returns exactly:
   `使用superpowers的executing-plans技能执行计划`

2. `BuildSubagentExecutePlanPrompt()`
   Returns exactly:
   `使用superpowers的executing-plans技能执行计划,并且使用superpowers的subagent-driven-development技能`

3. `BuildQuickSkillPrompt(input)`
   Rules:
   - trim surrounding whitespace
   - reject empty results
   - if the trimmed input already starts with `使用superpowers技能，`, return it unchanged
   - otherwise return `使用superpowers技能，<trimmed input>`

These rules must be shared between Web and Feishu implementations.

### Execution Routing

For Web:

- both quick-action submissions reuse the existing normal chat send pipeline
- they must not invoke a separate low-interruption resume pipeline

For Feishu:

- both quick-action submissions route through the active session command execution path already used by help-card command submission
- they must not require the caller to specify a plan file

### Busy-State Behavior

When the session is currently executing:

- the input is disabled
- the `执行 plan` button is disabled
- the `子代理执行 plan` button is disabled
- the instructional text remains visible if the block itself is visible

### History Matching

The session-history condition uses any message, regardless of role, as long as the message content contains `superpowers`.

This intentionally follows the approved user rule:

- user message match counts
- assistant message match counts
- system-derived or persisted chat messages count if they are part of session history

## Design Principles

1. Model the feature around Superpowers intent, not CLI thread-resume semantics.
2. Keep the quick-action surface compact and direct.
3. Reuse one eligibility model and one prompt-building model across Web and Feishu.
4. Avoid plan selection UI when the approved behavior is to let the model find the plan.
5. Prefer explicit renaming over compatibility shims for this feature area.

## Architecture

### 1. Semantic Replacement

Replace the current `LowInterruptionContinue` naming domain with `SuperpowersQuickAction`.

Recommended conceptual naming:

- `SuperpowersQuickActionHelper`
- `SuperpowersQuickActionEligibility`
- `SuperpowersPromptBuilder`
- `StartSuperpowersExecutePlanAsync`
- `SubmitSuperpowersQuickInputAsync`

This is intentionally a full semantic rename. New behavior should not live behind old low-interruption names.

### 2. Shared Eligibility Helper

Create a helper that computes a single eligibility result for the current session and workspace.

Recommended conceptual fields:

```json
{
  "targetMessageId": "latest-completed-assistant-id",
  "showQuickActions": true,
  "isDisabled": false,
  "hasPlanFiles": true,
  "historyContainsSuperpowers": true
}
```

The helper should:

- find the latest eligible completed assistant message for placement
- test for `docs/superpowers/plans/*.md`
- test whether any message content contains `superpowers`
- disable actions while execution is in progress

### 3. Shared Prompt Builder

Create a small prompt-building component responsible only for canonical prompt shaping.

Responsibilities:

- produce the fixed `执行 plan` prompt
- produce the fixed `子代理执行 plan` prompt
- normalize quick-input text with the approved prefix rule

This keeps prefix logic out of Razor event handlers and Feishu action handlers.

The selected subagent skill name is based on the current Superpowers skill catalog:

- the formal skill name is `superpowers:subagent-driven-development`
- `superpowers:executing-plans` explicitly advises switching to `superpowers:subagent-driven-development` when subagents are available

The approved button prompt intentionally references both skills in one instruction:

- `superpowers:executing-plans` remains the plan-execution anchor
- `superpowers:subagent-driven-development` is added explicitly to bias the model toward subagent-backed execution
- this combined wording follows the user-approved prompt contract even though the skill guidance alone would otherwise prefer the direct subagent-driven path

### 4. Web UI Integration

Desktop Web:

- replace the current bottom action block under [WebCodeCli/Components/ChatMessageListPanel.razor](D:/VSWorkshop/WebCode/WebCodeCli/Components/ChatMessageListPanel.razor:87)
- rename component parameters away from `LowInterruptionContinue*`
- wire Enter submission for the quick-input box
- route both plan buttons and the quick-input action into the standard message-send path

Desktop page state:

- replace the current `CurrentLowInterruptionContinueEligibility` and related fields in [WebCodeCli/Pages/CodeAssistant.razor.cs](D:/VSWorkshop/WebCode/WebCodeCli/Pages/CodeAssistant.razor.cs:1897)
- remove dependence on `SupportsLowInterruptionContinue()` and reusable CLI-thread checks for this UI feature

Mobile Web:

- perform the same semantic replacement in [WebCodeCli/Pages/CodeAssistantMobile.razor](D:/VSWorkshop/WebCode/WebCodeCli/Pages/CodeAssistantMobile.razor:612)
- mirror the same new page-state logic in the mobile code-behind

### 5. Feishu Help Card Integration

Card rendering:

- extend [WebCodeCli.Domain/Domain/Service/Channels/FeishuHelpCardBuilder.cs](D:/VSWorkshop/WebCode/WebCodeCli.Domain/Domain/Service/Channels/FeishuHelpCardBuilder.cs:13) to append the instructional text, text input, `执行 plan` button, and `子代理执行 plan` button to the bottom of the help card

Card actions:

- extend [WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs](D:/VSWorkshop/WebCode/WebCodeCli.Domain/Domain/Service/Channels/FeishuCardActionService.cs:118) to handle:
  - quick-input submit
  - `执行 plan` click
- `子代理执行 plan` click
- both actions should use the shared prompt builder before handing off to the existing active-session execution path

### 6. Compatibility Cleanup

The repository should remove or rename the old low-interruption UI affordance rather than keeping a parallel alias.

Expected cleanup areas include:

- helper names
- page fields
- component parameters
- button labels
- comments that describe the feature as "low interruption continue"

This reduces the risk of future contributors reintroducing thread-resume assumptions into the new Superpowers behavior.

## Edge Cases

### No Plan Files

If `docs/superpowers/plans/*.md` has no matches:

- do not render the quick-action block at all

### No `superpowers` in Session History

If the workspace has plan files but session history does not contain `superpowers`:

- do not render the quick-action block at all

### Duplicate Prefix

If the user enters text already starting with `使用superpowers技能，`:

- do not add the prefix again

### Empty Input

If the submitted input is empty after trimming:

- do not send a message
- keep focus in the input if practical on the given surface

### Execution Already Running

If the session is currently running:

- keep the block visible when otherwise eligible
- disable the input and button

## Verification

### Build Verification

- desktop Web page compiles after parameter and state renaming
- mobile Web page compiles after parameter and state renaming
- domain project compiles after Feishu card action changes

### Web Behavior Verification

1. No plan files in workspace:
   - quick-action block is hidden
2. Plan files exist but no `superpowers` in session history:
   - quick-action block is hidden
3. Plan files exist and session history contains `superpowers`:
   - quick-action block is visible under the latest eligible completed assistant reply
4. Click `执行 plan`:
   - the sent message is exactly `使用superpowers的executing-plans技能执行计划`
5. Click `子代理执行 plan`:
   - the sent message is exactly `使用superpowers的executing-plans技能执行计划,并且使用superpowers的subagent-driven-development技能`
6. Submit quick input without prefix:
   - the sent message is prefixed with `使用superpowers技能，`
7. Submit quick input with prefix already present:
   - the sent message is unchanged
8. While execution is in progress:
   - input and both buttons are disabled

### Feishu Behavior Verification

1. `feishuhelp` card bottom shows the new instructional text, input, `执行 plan`, and `子代理执行 plan`
2. Submitting input without prefix auto-prefixes correctly
3. Submitting input with existing prefix leaves the text unchanged
4. Clicking `执行 plan` sends the fixed plan-execution prompt into the active session flow
5. Clicking `子代理执行 plan` sends the fixed combined plan-and-subagent prompt into the active session flow
6. Busy state disables the interactive controls

## Open Questions

None. The following decisions were explicitly resolved in discussion:

- any matching plan file in `docs/superpowers/plans/*.md` is sufficient
- any session-history message containing `superpowers` is sufficient
- quick input auto-prefixes only when the prefix is not already present
- `执行 plan` does not target a specific file and instead lets the model find the plan
- `子代理执行 plan` uses the combined prompt `使用superpowers的executing-plans技能执行计划,并且使用superpowers的subagent-driven-development技能`
- `feishuhelp` should expose the same quick-action capability at the bottom

## Recommended Next Step

Create an implementation plan that:

- renames the low-interruption UI feature area to `SuperpowersQuickAction`
- introduces shared plan-file and history matching logic
- introduces shared prompt-building logic
- updates desktop and mobile Web UI
- updates Feishu help card rendering and callback handling
- adds the adjacent `子代理执行 plan` action on both Web and Feishu surfaces
- verifies all three surfaces end-to-end
