# Feishu Streaming Card Top Switch Design

Date: 2026-04-29
Status: approved in discussion, pending implementation planning

## Goal

Make model and reasoning switching available directly on the Feishu streaming reply card so users do not need to enter session management for common per-session launch changes.

The new interaction must:

- display all available model options at the top of the streaming reply card
- display all supported reasoning-effort options at the top of the streaming reply card for tools that support reasoning effort
- keep those options visible during streaming but disabled until the current streaming reply completes
- allow one-tap switching after completion
- show a toast after each switch
- apply the change only to the current session's launch-override state

This feature is a direct-action surface for session launch configuration. It must reuse the existing session launch-override mechanism, must not replace `cc-switch` provider authority, and must not mutate chat-global defaults.

## Scope

In scope:

- Feishu streaming reply card top chrome enhancement
- top-level chip rendering for model options
- top-level chip rendering for reasoning-effort options when supported
- click-to-switch behavior after streaming completion
- session launch-override write for managed tools
- toast feedback and card-top refresh after successful switching

Out of scope:

- switching provider directly from the streaming card
- changing tool type inside an existing session
- changing the model for the already-running in-flight reply
- adding a separate settings card or modal for this flow
- changing Web UI behavior outside the Feishu card surface

## Problem

Current Feishu model switching for managed sessions is routed through session management. That path is functionally complete but too heavy for the most common operation:

- inspect the just-finished streaming reply
- decide the next turn should use a different model or different reasoning effort
- switch immediately on the current card

The current flow creates avoidable friction because the user must leave the active reply context, enter session management, locate the session, edit settings, and then return to the conversation.

The desired interaction is much tighter:

- keep configuration options visible on the current reply card
- prevent switching while the current response is still running
- enable immediate post-completion switching for the next turn

This design must fit the existing architecture:

- `cc-switch` remains the authority for provider selection
- session-local launch overrides remain stored on the `ChatSession`
- each managed tool continues to consume the same launch-override merge rules already used by session launch settings

## User Experience

### Top Card Layout

The streaming card top area keeps the current status text and overflow menu, but grows a second top-level section for quick switches.

The top section becomes:

1. status row
2. option-chip row

Status row:

- current workspace label
- session label
- tool label
- running or completed state
- existing overflow actions such as session switching and session management

Option-chip row:

- all available model options for the current tool and current provider context
- all supported reasoning-effort options for tools that expose reasoning effort
- current active values shown with highlighted state
- unsupported groups omitted entirely

### Behavior While Streaming

While the reply is still streaming:

- all chips are visible
- all chips are disabled
- visual style indicates read-only state

This preserves discoverability without allowing mid-stream mutation.

### Behavior After Completion

After the reply is complete:

- all eligible chips become clickable
- clicking a chip immediately submits the switch action
- no extra form, modal, or secondary card is opened
- the user receives a toast
- the same card refreshes its top-chip highlight state

Example toasts:

- `已切换到 gpt-5.4，下次执行生效`
- `已切换思考等级为 high，下次执行生效`

## Requirements

### Session Boundaries

The switch applies only to the current session.

It must not:

- mutate another session
- mutate chat-global defaults
- mutate machine-global live config
- change provider selection globally

### Provider Authority

Provider authority remains unchanged:

- `cc-switch` still determines provider selection for managed tools
- this feature must not edit `cc-switch` global provider selection

### Session Override Rules

Per tool, the switch writes to the existing session launch-override store on `ChatSession.ToolLaunchOverridesJson`.

That means:

- `codex` stores `model` and `reasoningEffort`
- `claude-code` stores `model`
- `opencode` stores `model`

The quick-switch path must reuse the same normalization, validation, serialization, and runtime-reset rules already used by the existing session launch-settings flow.

### Tool Capability Rules

- `Codex` supports `model` and `reasoning effort`
- `Claude Code` supports `model`
- `OpenCode` supports `model`

Reasoning-effort chips are rendered only for tools that support them.

### Completion Gate

Switching is allowed only when the current streaming reply is complete.

The client UI should disable chips during streaming, but the server must also enforce this rule. A stale or replayed click must not bypass it.

## Design Principles

1. Keep the interaction on the current card.
2. Prefer direct action over secondary forms for the post-completion switch path.
3. Preserve existing provider authority boundaries.
4. Reuse current session launch-override storage and session reset infrastructure.
5. Keep the feature additive to existing card overflow and session-management flows.

## Architecture

The feature extends the existing Feishu streaming card chrome instead of introducing a parallel card type.

### 1. Chrome Data Model

Extend `FeishuStreamingCardChrome` to carry a top-chip payload in addition to status markdown and overflow options.

Recommended conceptual shape:

```json
{
  "statusMarkdown": "当前会话：workspace · 会话标题 · Codex · 已完成",
  "topChipGroups": [
    {
      "kind": "model",
      "label": "模型",
      "isEnabled": true,
      "items": [
        { "id": "gpt-5.4", "text": "gpt-5.4", "isActive": true, "isEnabled": true },
        { "id": "gpt-5.2", "text": "gpt-5.2", "isActive": false, "isEnabled": true }
      ]
    },
    {
      "kind": "reasoning_effort",
      "label": "思考",
      "isEnabled": true,
      "items": [
        { "id": "low", "text": "low", "isActive": false, "isEnabled": true },
        { "id": "medium", "text": "medium", "isActive": false, "isEnabled": true },
        { "id": "high", "text": "high", "isActive": true, "isEnabled": true },
        { "id": "xhigh", "text": "xhigh", "isActive": false, "isEnabled": true }
      ]
    }
  ]
}
```

The exact DTO shape may differ, but the chrome must carry:

- group kind
- item text
- item value
- active state
- enabled state
- enough action payload to resolve session and target value on click

### 2. Feishu Card Rendering

`FeishuCardKitClient` should render the new top chips below the status row and above the main markdown body.

Rendering rules:

- chips may wrap to multiple lines when needed
- the current active chip uses highlighted style
- disabled chips use muted style
- model and reasoning groups appear in a stable order
- the overflow menu remains available on the status row

### 3. Action Routing

Add explicit card actions for:

- `switch_streaming_card_model`
- `switch_streaming_card_reasoning_effort`

Each action must carry enough context to validate and perform the switch:

- `session_id`
- `chat_key`
- `tool_id`
- target `model` or `reasoning_effort`
- source card context if needed for refresh

### 4. Option Discovery

The top-chip data source should reuse the same model catalog and tool-capability logic already used by session launch settings.

Model options:

- resolve through the current managed tool id
- resolve through the current provider context for the session
- include all available models for the current tool/provider catalog

Reasoning options:

- use the supported fixed set for tools that expose reasoning effort
- omit the group for tools that do not support it

### 5. Current Active Value Resolution

The active chip highlight must reflect the effective session launch override for the current session when present. If no override exists for the current tool, the card may fall back to the current default/provider-derived value already surfaced by the existing session launch-settings flow.

The important rule is consistency with session launch settings: the top-card quick-switch path and the session launch-settings path must always describe the same effective override state.

## Click Flow

### Model Switch

1. user clicks a model chip on a completed streaming card
2. Feishu posts the action payload
3. server validates session, chat ownership, tool capability, and completion state
4. server writes the selected model to `ToolLaunchOverridesJson` for the current tool
6. server resets only the current session runtime so the next execution starts cleanly
7. server returns:
   - updated card chrome with new active chip highlight
   - success toast

### Reasoning Switch

1. user clicks a reasoning-effort chip on a completed streaming card
2. Feishu posts the action payload
3. server validates session, chat ownership, tool capability, and completion state
4. server writes the selected reasoning setting to `ToolLaunchOverridesJson` for the current tool
6. server resets only the current session runtime
7. server returns:
   - updated card chrome with new active chip highlight
   - success toast

## Runtime Reset Semantics

Any successful switch must reset the current session runtime so the next execution uses the new launch override deterministically.

This reset must:

- clear current runtime/thread reuse state for the session
- keep workspace files intact
- keep message history intact
- keep provider snapshot authority intact

This rule is consistent with the existing session launch override behavior and avoids ambiguity about whether a reused native thread still carries old launch configuration.

## Error Handling

### Validation Failures

Return toast-only or toast-plus-refresh responses for:

- session missing or stale
- chat/session mismatch
- unsupported tool capability
- invalid target model
- invalid target reasoning effort
- current reply not yet completed

Recommended messages:

- `会话不存在或已失效，请重新打开会话`
- `当前回复尚未完成，暂时不能切换`
- `当前会话工具不支持该切换`

### Config and Bootstrap Failures

If override serialization or persistence fails:

- log the exception with session and tool context
- return a concise failure toast
- do not optimistically refresh the active chip highlight

Recommended messages:

- `切换模型失败: ...`
- `切换思考等级失败: ...`

## Concurrency and Consistency

The UI should prevent clicks during streaming, but the server remains authoritative.

For repeated fast clicks on a completed card:

- requests should be handled safely in sequence
- the last successful write wins
- card refresh should always reflect the persisted result returned by the server

The card must not claim a switch succeeded before the session override write and session runtime reset complete successfully.

## Testing Strategy

Write tests before implementation changes.

At minimum, cover:

- completed streaming card renders all model chips from the current catalog
- tools with reasoning support render all reasoning chips
- tools without reasoning support omit the reasoning group
- running streaming card marks all chips disabled
- switching model on a completed card writes the current tool override and returns success toast
- switching reasoning effort on a completed card writes the current tool override and returns success toast
- successful switch resets only the current session runtime
- server rejects switch attempts when the current reply is not completed
- server rejects switch attempts for stale sessions or unsupported capabilities

## Compatibility With Existing Designs

This design builds on, rather than replaces, the existing session launch override and provider authority work:

- `2026-04-17-cc-switch-provider-authority-design.md`
- `2026-04-22-session-launch-overrides-design.md`

The key distinction is interaction surface:

- session launch overrides remain the general settings path
- streaming-card top switches become the fast path for immediate next-turn adjustments

Both paths must converge on the same `ToolLaunchOverridesJson` storage and session reset semantics.

## Implementation Notes

Implementation should prefer extending the existing Feishu streaming-card chrome and action routing over introducing new card types or duplicated configuration logic.

The implementation should also avoid making `Codex` a special one-off UI path. The card should support all managed tools consistently, while rendering only the controls each tool actually supports.
