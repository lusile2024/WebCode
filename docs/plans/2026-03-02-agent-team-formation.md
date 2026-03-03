# Agent Teams 组建与分工计划

> **日期**: 2026-03-02
> **项目**: WebCodeCli - 飞书斜杠命令帮助功能
> **模式**: Agent Teams 场景4 (Lead-Member)

---

## 1. 任务总览

| 字段 | 内容 |
|------|------|
| 任务目标 | 实现飞书斜杠命令帮助功能，支持双卡片交互模式 |
| 预期结果 | 完整可运行的 `/feishuhelp` 功能，包含14个User Story |
| 验收标准 | 所有Story验收条件满足，端到端测试通过，代码质量达标 |
| 范围界定 | 核心功能(P0,P1,P2)必须实现，增强功能(P3)按需实现 |
| 预计Agent数 | 5个（1个Lead + 4个Member） |
| 选定场景 | 场景4 (Lead-Member) - 明确分工协作 |
| 协作模式 | Agent Team + TaskList (成员间可通信) |

---

## 2. 团队蓝图

| 编号 | 角色 | 职责 | 模型 | subagent_type | Skill/Type |
|------|------|------|------|---------------|------------|
| 1 | **Team Lead** | 整体协调、进度跟踪、质量把关、最终验收 | opus | feature-dev:code-architect | [Type: general-purpose] |
| 2 | **Backend Developer** | 数据模型、命令服务、插件扫描 | sonnet | feature-dev:code-explorer | [Type: general-purpose] |
| 3 | **Card UI Developer** | 卡片构建器、飞书卡片JSON生成 | sonnet | frontend-design:frontend-design | [Skill: frontend-design] |
| 4 | **Integration Engineer** | 消息处理、回调处理、DI注册 | sonnet | feature-dev:code-explorer | [Type: general-purpose] |
| 5 | **QA Engineer** | 测试策略、端到端测试、错误处理验证 | haiku | feature-dev:code-reviewer | [Type: general-purpose] |

---

## 3. Sprint 1 任务分配（MVP核心）

### Sprint Goal
实现基础命令选择和展示功能，用户可以输入 `/feishuhelp` 看到命令列表。

#### Team Lead 任务
- [ ] 监控整体进度
- [ ] 协调各成员间依赖
- [ ] 代码质量Review
- [ ] Sprint 1验收

#### Backend Developer 任务
- [ ] **Story 1**: 数据模型层实现
  - 创建 `FeishuCommand.cs`
  - 创建 `FeishuCommandCategory.cs`
  - 创建 `FeishuHelpCardAction.cs`
  - 验证编译
- [ ] **Story 2**: 插件路径助手
  - 创建 `FeishuPluginPathHelper.cs`
  - 实现3个路径获取方法
  - 验证编译
- [ ] **Story 3**: 命令服务基础结构
  - 创建 `FeishuCommandService.cs`
  - 实现6个核心方法
  - 加载内置核心命令
  - 验证编译

#### Card UI Developer 任务
- [ ] **Story 4**: 卡片构建器 - 命令选择卡片
  - 创建 `FeishuHelpCardBuilder.cs`
  - 实现 `BuildCommandListCard()`
  - 验证飞书卡片JSON格式
  - 验证编译

#### Integration Engineer 任务
- [ ] **Story 7**: 消息处理器 - /feishuhelp 检测
  - 修改 `FeishuMessageHandler.cs`
  - 添加 `/feishuhelp` 检测逻辑
  - 集成命令服务和卡片构建器
  - 验证编译

#### QA Engineer 任务
- [ ] 准备Sprint 1测试用例
- [ ] 设计验收检查清单
- [ ] 参与Story 1-4, 7的代码Review

---

## 4. Sprint 2 任务分配（交互体验）

### Sprint Goal
实现完整的卡片交互流程，用户可以选择命令、搜索过滤、刷新列表。

#### Team Lead 任务
- [ ] Sprint 1回顾与总结
- [ ] 监控Sprint 2进度
- [ ] 解决阻塞问题
- [ ] Sprint 2验收

#### Backend Developer 任务
- [ ] 支持Card UI Developer的卡片数据需求
- [ ] 优化命令服务性能（如需要）

#### Card UI Developer 任务
- [ ] **Story 5**: 卡片构建器 - 执行卡片
  - 实现 `BuildExecuteCard()`
  - 验证卡片JSON格式
  - 验证编译
- [ ] **Story 6**: 卡片构建器 - 搜索过滤卡片
  - 实现 `BuildFilteredCard()`
  - 验证卡片JSON格式
  - 验证编译

#### Integration Engineer 任务
- [ ] **Story 8**: 卡片回调处理器 - 基础结构
  - 查找/创建回调处理器
  - 实现action解析和路由
  - 验证编译
- [ ] **Story 9**: 卡片回调 - 刷新和选择命令
  - 实现 `refresh_commands`
  - 实现 `select_command`
  - 实现 `back_to_list`
  - 验证编译

#### QA Engineer 任务
- [ ] 执行Sprint 1回归测试
- [ ] 准备Sprint 2测试用例
- [ ] 测试卡片交互流程
- [ ] 参与代码Review

---

## 5. Sprint 3 任务分配（完整功能）

### Sprint Goal
实现命令执行和插件扫描，完成所有功能并通过测试。

#### Team Lead 任务
- [ ] Sprint 2回顾与总结
- [ ] 监控Sprint 3进度
- [ ] 最终质量把关
- [ ] 项目交付总结

#### Backend Developer 任务
- [ ] **Story 11**: 插件/技能扫描
  - 实现 `ScanSkillsDirectoryAsync()`
  - 实现 `ScanPluginsDirectoryAsync()`
  - 解析 `SKILL.md`
  - 验证编译

#### Card UI Developer 任务
- [ ] 支持插件/技能命令的卡片显示
- [ ] 优化卡片UI（如需要）

#### Integration Engineer 任务
- [ ] **Story 10**: 卡片回调 - 执行命令
  - 实现 `execute_command`
  - 集成 `CliExecutorService`
  - 集成 `FeishuStreamingHandle`
  - 验证编译
- [ ] **Story 12**: 服务注册与DI
  - 注册 `FeishuCommandService`
  - 注册 `FeishuHelpCardBuilder`
  - 注册回调处理器
  - 验证应用启动

#### QA Engineer 任务
- [ ] **Story 13**: 端到端测试
  - 测试所有用户场景
  - 执行完整E2E测试
- [ ] **Story 14**: 错误处理与边界情况
  - 测试各种异常情况
  - 验证错误提示友好性
- [ ] 最终验收测试
- [ ] 生成测试报告

---

## 6. 并行执行策略

### 阶段1: 基础架构（串行）
```
Team Lead: 启动Sprint 1
    ↓
Backend Dev: Story 1 → Story 2 → Story 3
    ↓
(同时) Card UI Dev: Story 4 (依赖Story 3数据结构)
    ↓
(同时) Integration Dev: Story 7 (依赖Story 3, 4)
    ↓
QA: Sprint 1验收
```

### 阶段2: 交互体验（部分并行）
```
Team Lead: 启动Sprint 2
    ↓
Card UI Dev: Story 5 → Story 6
    ↓
(同时) Integration Dev: Story 8 → Story 9
    ↓
QA: Sprint 2验收
```

### 阶段3: 完整功能（部分并行）
```
Team Lead: 启动Sprint 3
    ↓
Backend Dev: Story 11 (仅依赖Story 3，可早开始)
    ↓
(同时) Integration Dev: Story 10 → Story 12
    ↓
QA: Story 13 → Story 14 → 最终验收
```

---

## 7. 通信协议

### Team Lead → Member
- 使用 `SendMessage` 发送任务分配
- 每日进度同步
- 阻塞问题协调

### Member → Team Lead
- 任务完成汇报
- 阻塞问题上报
- 需要Review的代码通知

### Member ↔ Member
- 依赖关系协调
- 接口定义确认
- 数据格式约定

### 消息格式示例
```json
{
  "type": "task_update",
  "task_id": "Story 1",
  "status": "completed",
  "output": "FeishuCommand.cs created",
  "next": "ready for Story 2"
}
```

---

## 8. 质量闸门

### Sprint 验收检查清单
- [ ] 所有分配的Story已完成
- [ ] 代码编译通过，无警告
- [ ] 代码Review已通过
- [ ] 单元测试通过（如适用）
- [ ] 日志输出完整清晰
- [ ] 文档注释齐全

### 最终验收检查清单
- [ ] 所有14个Story已完成
- [ ] 所有端到端测试通过
- [ ] 所有错误场景处理友好
- [ ] 代码风格一致
- [ ] 文档完整
- [ ] 用户体验流畅

---

## 9. 风险预案

| 风险 | 触发条件 | 应对措施 |
|------|---------|---------|
| 某成员执行受阻 | 超过1个工作日无进展 | Team Lead介入协调，必要时重新分配 |
| 依赖关系阻塞 | 上游任务延期 | 调整任务顺序，优先执行无依赖任务 |
| 代码质量问题 | Review发现重大问题 | 打回修改，最多2轮，仍不通过则升级 |
| 技能不匹配 | 任务超过Agent能力 | 更换subagent_type或简化任务 |

---

## 10. 时间线预估

| 阶段 | 预估时间 | 关键里程碑 |
|------|---------|-----------|
| Sprint 1 | 2天 | MVP可用，用户能看到命令列表 |
| Sprint 2 | 1.5天 | 完整交互体验 |
| Sprint 3 | 1.5天 | 功能完整，测试通过 |
| **总计** | **5天** | **项目交付** |

---

**文档完成**: 2026-03-02
**团队规模**: 5个Agent
**协作模式**: Lead-Member with TaskList
