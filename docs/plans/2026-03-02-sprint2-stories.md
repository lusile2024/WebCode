# Sprint 2 - 交互体验 - User Stories

> **Sprint目标**: 实现完整的卡片交互流程，用户可以选择命令、搜索过滤、刷新列表
> **日期**: 2026-03-02
> **状态**: 进行中

---

## Story 8: 卡片回调处理器 - 基础结构

**As a** 系统
**I want** 能够处理飞书卡片的回调事件
**So that** 用户可以与卡片交互

### 验收标准
- [ ] 查找或创建 `card.action.trigger` 事件处理器
- [ ] 解析 `action.value` JSON
- [ ] 实现action路由逻辑
- [ ] 日志输出完整清晰
- [ ] 编译通过，无错误

### 技术实现要点
1. 使用 FeishuNetSdk 的 IEventHandler 模式
2. 注入 FeishuCommandService、FeishuHelpCardBuilder、ILogger
3. 解析 FeishuHelpCardAction 模型
4. 路由到不同的action处理方法

### 预估工时
0.5小时

---

## Story 9: 卡片回调 - 刷新和选择命令

**As a** 飞书用户
**I want** 点击"更新命令列表"或选择命令
**So that** 卡片会相应更新

### 验收标准
- [ ] 实现 `refresh_commands` action - 刷新命令并返回新卡片
- [ ] 实现 `select_command` action - 显示执行卡片
- [ ] 实现 `back_to_list` action - 返回命令列表
- [ ] 返回正确的toast提示
- [ ] 错误处理友好
- [ ] 编译通过，无错误

### 技术实现要点
1. HandleRefreshCommandsAsync - 调用 RefreshCommandsAsync，返回新卡片+toast
2. HandleSelectCommandAsync - 查找命令，返回执行卡片
3. HandleBackToListAsync - 返回命令选择卡片
4. 使用 BuildToastResponse 返回toast+卡片

### 预估工时
0.5小时

---

## Story 10: 卡片回调 - 执行命令

**As a** 飞书用户
**I want** 点击"执行命令"后命令被执行
**So that** 我可以看到命令的执行结果

### 验收标准
- [ ] 实现 `execute_command` action
- [ ] 从 `form_value` 获取命令输入
- [ ] 复用现有 `CliExecutorService` 执行命令
- [ ] 复用现有 `FeishuStreamingHandle` 流式输出
- [ ] 显示"开始执行"toast提示
- [ ] 错误处理友好
- [ ] 编译通过，无错误

### 技术实现要点
1. 从 event.Action.FormValue 获取 command_input
2. 需要从回调中获取 ChatId/Session信息
3. 复用 FeishuChannelService 的 ExecuteCliAndStreamAsync 逻辑
4. 返回 toast 确认开始执行

### 预估工时
1小时

---

## 依赖关系

```
Story 8 (回调基础结构)
    ↓
Story 9 (刷新和选择命令)
    ↓
Story 10 (执行命令)
```

---

## Definition of Done (DoD)

- [ ] 所有Story验收标准满足
- [ ] 代码编译通过，0错误
- [ ] 代码Review通过
- [ ] 日志输出完整清晰
- [ ] 端到端测试通过

---

## 风险与缓解

| 风险 | 影响 | 概率 | 缓解措施 |
|------|------|------|---------|
| FeishuNetSdk不支持card.action.trigger | 高 | 中 | 提前调研SDK，准备备用方案 |
| form_value结构不明确 | 中 | 高 | 参考飞书文档，添加详细日志 |
| 无法从回调获取ChatId | 高 | 中 | 检查事件结构，必要时调整 |
