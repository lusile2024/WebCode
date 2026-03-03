# 敏捷开发Story拆解 - 飞书斜杠命令帮助功能

> **日期**: 2026-03-02
> **项目**: WebCodeCli - 飞书渠道集成
> **Epic**: 飞书机器人用户体验优化

---

## 1. Product Backlog

### Epic: 飞书斜杠命令帮助功能

作为飞书用户，我希望通过输入 `/feishuhelp` 快速查看和使用所有可用命令，以便更高效地与机器人交互。

---

## 2. User Story 拆解（按优先级排序）

### 🟢 P0 - 核心功能MVP

#### Story 1: 数据模型层实现
**As a** 开发者
**I want** 创建命令相关的数据模型
**So that** 后续功能可以基于稳定的模型进行开发

**验收标准**:
- [ ] 创建 `FeishuCommand` 模型（包含Id、Name、Description、Usage、Category）
- [ ] 创建 `FeishuCommandCategory` 模型（包含分组信息）
- [ ] 创建 `FeishuHelpCardAction` 模型（处理卡片回调动作）
- [ ] 编译通过，无错误

**预估工时**: 0.5小时
**依赖**: 无
**Tags**: 数据模型, 后端

---

#### Story 2: 插件路径助手
**As a** 开发者
**I want** 创建插件路径助手类
**So that** 可以跨平台正确找到技能和插件目录

**验收标准**:
- [ ] 实现 `GetPluginsDirectory()` 方法
- [ ] 实现 `GetSkillsDirectory()` 方法
- [ ] 实现 `GetProjectSkillsDirectory()` 方法（支持向上查找）
- [ ] 支持Windows、macOS、Linux平台
- [ ] 编译通过，无错误

**预估工时**: 0.5小时
**依赖**: Story 1
**Tags**: 工具类, 跨平台

---

#### Story 3: 命令服务基础结构
**As a** 系统
**I want** 有一个命令服务来管理所有可用命令
**So that** 可以高效地查询、过滤和刷新命令列表

**验收标准**:
- [ ] 实现 `GetCommandsAsync()` - 获取命令列表（带缓存）
- [ ] 实现 `RefreshCommandsAsync()` - 刷新命令列表
- [ ] 实现 `GetCommandAsync()` - 获取单个命令
- [ ] 实现 `GetCategorizedCommandsAsync()` - 按分组组织命令
- [ ] 实现 `FilterCommandsAsync()` - 关键字过滤
- [ ] 加载内置核心命令（硬编码版本）
- [ ] 编译通过，无错误

**预估工时**: 1小时
**依赖**: Story 1, Story 2
**Tags**: 后端服务, 缓存

---

### 🟡 P1 - 核心用户体验

#### Story 4: 卡片构建器 - 命令选择卡片
**As a** 飞书用户
**I want** 看到一个美观的命令选择卡片
**So that** 我可以浏览和选择可用的命令

**验收标准**:
- [ ] 实现 `BuildCommandListCard()` 方法
- [ ] 卡片包含"更新命令列表"按钮
- [ ] 每个命令分组使用 overflow 折叠组件
- [ ] 分组显示正确的emoji图标
- [ ] 按钮value包含正确的action JSON
- [ ] 卡片JSON格式符合飞书v2.0规范
- [ ] 编译通过，无错误

**预估工时**: 1小时
**依赖**: Story 3
**Tags**: 飞书卡片, UI/UX

---

#### Story 5: 卡片构建器 - 执行卡片
**As a** 飞书用户
**I want** 点击命令后看到执行卡片
**So that** 我可以编辑并执行命令

**验收标准**:
- [ ] 实现 `BuildExecuteCard()` 方法
- [ ] 显示命令说明和用法示例
- [ ] input组件预设命令内容
- [ ] 提供"取消"和"执行命令"按钮
- [ ] 卡片JSON格式符合飞书v2.0规范
- [ ] 编译通过，无错误

**预估工时**: 0.5小时
**依赖**: Story 4
**Tags**: 飞书卡片, UI/UX

---

#### Story 6: 卡片构建器 - 搜索过滤卡片
**As a** 飞书用户
**I want** 输入关键词后看到过滤结果
**So that** 我可以快速找到想要的命令

**验收标准**:
- [ ] 实现 `BuildFilteredCard()` 方法
- [ ] 显示搜索关键词和匹配数量
- [ ] 提供"显示全部命令"返回按钮
- [ ] 无匹配时显示友好提示
- [ ] 卡片JSON格式符合飞书v2.0规范
- [ ] 编译通过，无错误

**预估工时**: 0.5小时
**依赖**: Story 5
**Tags**: 飞书卡片, 搜索

---

#### Story 7: 消息处理器 - /feishuhelp 检测
**As a** 飞书用户
**I want** 输入 `/feishuhelp` 后收到帮助卡片
**So that** 我可以开始使用命令帮助功能

**验收标准**:
- [ ] 在 `FeishuMessageHandler` 中添加 `/feishuhelp` 检测
- [ ] 支持 `/feishuhelp <keyword>` 带参数形式
- [ ] 调用 `FeishuCommandService` 获取命令
- [ ] 调用 `FeishuHelpCardBuilder` 构建卡片
- [ ] 通过 `FeishuChannelService` 发送卡片
- [ ] 日志输出完整清晰
- [ ] 编译通过，无错误

**预估工时**: 0.5小时
**依赖**: Story 6
**Tags**: 消息处理, 集成

---

### 🟠 P2 - 完整交互体验

#### Story 8: 卡片回调处理器 - 基础结构
**As a** 系统
**I want** 能够处理飞书卡片的回调事件
**So that** 用户可以与卡片交互

**验收标准**:
- [ ] 查找或创建 `card.action.trigger` 事件处理器
- [ ] 解析 `action.value` JSON
- [ ] 实现action路由逻辑
- [ ] 日志输出完整清晰
- [ ] 编译通过，无错误

**预估工时**: 0.5小时
**依赖**: Story 7
**Tags**: 回调处理, 飞书集成

---

#### Story 9: 卡片回调 - 刷新和选择命令
**As a** 飞书用户
**I want** 点击"更新命令列表"或选择命令
**So that** 卡片会相应更新

**验收标准**:
- [ ] 实现 `refresh_commands` action - 刷新命令并返回新卡片
- [ ] 实现 `select_command` action - 显示执行卡片
- [ ] 实现 `back_to_list` action - 返回命令列表
- [ ] 返回正确的toast提示
- [ ] 错误处理友好
- [ ] 编译通过，无错误

**预估工时**: 0.5小时
**依赖**: Story 8
**Tags**: 回调处理, 用户交互

---

#### Story 10: 卡片回调 - 执行命令
**As a** 飞书用户
**I want** 点击"执行命令"后命令被执行
**So that** 我可以看到命令的执行结果

**验收标准**:
- [ ] 实现 `execute_command` action
- [ ] 从 `form_value` 获取命令输入
- [ ] 复用现有 `CliExecutorService` 执行命令
- [ ] 复用现有 `FeishuStreamingHandle` 流式输出
- [ ] 显示"开始执行"toast提示
- [ ] 错误处理友好
- [ ] 编译通过，无错误

**预估工时**: 1小时
**依赖**: Story 9
**Tags**: 回调处理, 命令执行

---

### 🔵 P3 - 增强功能

#### Story 11: 插件/技能扫描
**As a** 系统
**I want** 自动扫描项目技能、全局技能和插件
**So that** 用户可以使用所有可用的扩展功能

**验收标准**:
- [ ] 实现 `ScanSkillsDirectoryAsync()` - 扫描技能目录
- [ ] 实现 `ScanPluginsDirectoryAsync()` - 扫描插件目录
- [ ] 解析 `SKILL.md` 提取描述
- [ ] 创建 `FeishuPluginCommand` 实例
- [ ] 错误处理（目录不存在时跳过）
- [ ] 编译通过，无错误

**预估工时**: 0.5小时
**依赖**: Story 3
**Tags**: 插件系统, 扫描

---

#### Story 12: 服务注册与DI
**As a** 应用
**I want** 所有新服务正确注册到DI容器
**So that** 依赖注入可以正常工作

**验收标准**:
- [ ] 在服务注册文件中注册 `FeishuCommandService`
- [ ] 在服务注册文件中注册 `FeishuHelpCardBuilder`
- [ ] 注册卡片回调处理器（如需要）
- [ ] 编译通过，无错误
- [ ] 应用启动无异常

**预估工时**: 0.25小时
**依赖**: Story 10, Story 11
**Tags**: DI, 配置

---

#### Story 13: 端到端测试
**As a** 测试人员
**I want** 进行完整的端到端测试
**So that** 确保功能在真实场景中正常工作

**验收标准**:
- [ ] 测试 `/feishuhelp` - 显示命令选择卡片
- [ ] 测试 `/feishuhelp <keyword>` - 显示过滤结果
- [ ] 测试选择命令 - 显示执行卡片
- [ ] 测试取消 - 返回命令列表
- [ ] 测试刷新 - 更新命令列表
- [ ] 测试执行命令 - 正常执行并输出结果
- [ ] 所有测试用例通过

**预估工时**: 0.5小时
**依赖**: Story 12
**Tags**: 测试, E2E

---

#### Story 14: 错误处理与边界情况
**As a** 用户
**I want** 遇到异常时看到友好的错误提示
**So that** 我知道发生了什么以及如何处理

**验收标准**:
- [ ] 测试命令ID不存在 - 显示toast并返回列表
- [ ] 测试空命令输入 - 显示警告提示
- [ ] 测试插件目录不存在 - 跳过不报错
- [ ] 测试关键词无匹配 - 显示友好提示
- [ ] 所有错误都有日志记录
- [ ] 用户体验流畅，无崩溃

**预估工时**: 0.25小时
**依赖**: Story 13
**Tags**: 错误处理, 用户体验

---

## 3. Sprint 规划

### Sprint 1: MVP核心（2天）
**目标**: 实现基础命令选择和展示功能

**Story 分配**:
- Story 1: 数据模型层实现
- Story 2: 插件路径助手
- Story 3: 命令服务基础结构
- Story 4: 卡片构建器 - 命令选择卡片
- Story 7: 消息处理器 - /feishuhelp 检测

**Definition of Done**:
- [ ] 用户可以输入 `/feishuhelp` 看到命令列表
- [ ] 所有代码编译通过
- [ ] 代码Review通过

---

### Sprint 2: 交互体验（1.5天）
**目标**: 实现完整的卡片交互流程

**Story 分配**:
- Story 5: 卡片构建器 - 执行卡片
- Story 6: 卡片构建器 - 搜索过滤卡片
- Story 8: 卡片回调处理器 - 基础结构
- Story 9: 卡片回调 - 刷新和选择命令

**Definition of Done**:
- [ ] 用户可以点击命令进入执行卡片
- [ ] 用户可以搜索过滤命令
- [ ] 用户可以刷新命令列表
- [ ] 所有代码编译通过
- [ ] 代码Review通过

---

### Sprint 3: 完整功能（1.5天）
**目标**: 实现命令执行和插件扫描

**Story 分配**:
- Story 10: 卡片回调 - 执行命令
- Story 11: 插件/技能扫描
- Story 12: 服务注册与DI
- Story 13: 端到端测试
- Story 14: 错误处理与边界情况

**Definition of Done**:
- [ ] 用户可以执行命令并看到结果
- [ ] 插件和技能可以被扫描和显示
- [ ] 所有端到端测试通过
- [ ] 错误处理友好
- [ ] 文档完整

---

## 4. 依赖关系图

```
Story 1 (数据模型)
    ↓
Story 2 (路径助手) ←┐
    ↓                │
Story 3 (命令服务) ──┘
    ↓
Story 4 (选择卡片) ─┐
    ↓                │
Story 5 (执行卡片)   │
    ↓                │
Story 6 (搜索卡片)   │
    ↓                │
Story 7 (/feishuhelp)│
    ↓                │
Story 8 (回调基础)   │
    ↓                │
Story 9 (刷新选择) ──┤
    ↓                │
Story 10 (执行命令)  │
                     │
Story 11 (插件扫描) ←┘ (依赖Story 3)
    ↓
Story 12 (DI注册)
    ↓
Story 13 (E2E测试)
    ↓
Story 14 (错误处理)
```

---

## 5. 任务并行化策略

### 可并行的任务组

**Group A: 基础架构（串行）**
- Story 1 → Story 2 → Story 3

**Group B: 卡片构建（部分并行）**
- Story 4 (选择卡片) 可独立进行
- Story 5 (执行卡片) 依赖 Story 4
- Story 6 (搜索卡片) 依赖 Story 5

**Group C: 插件扫描（独立）**
- Story 11 仅依赖 Story 3，可与 Group B 并行

**Group D: 集成与测试（串行）**
- Story 7 → Story 8 → Story 9 → Story 10 → Story 12 → Story 13 → Story 14

---

## 6. 验收标准汇总

### 功能验收
- [ ] 用户输入 `/feishuhelp` 显示命令选择卡片
- [ ] 用户输入 `/feishuhelp <keyword>` 显示过滤结果
- [ ] 用户点击命令显示执行卡片，命令预填入
- [ ] 用户可以编辑命令并执行
- [ ] 用户可以取消返回命令列表
- [ ] 用户可以刷新命令列表
- [ ] 项目技能、全局技能、插件都能显示

### 质量验收
- [ ] 所有代码编译通过，无警告
- [ ] 代码风格与现有代码一致
- [ ] 日志输出完整清晰
- [ ] 错误处理友好，无崩溃
- [ ] 端到端测试全部通过

### 文档验收
- [ ] 数据模型有XML注释
- [ ] 公共方法有XML注释
- [ ] 复杂逻辑有 inline 注释
- [ ] README或wiki有使用说明

---

## 7. 风险与缓解

| 风险 | 影响 | 概率 | 缓解措施 |
|------|------|------|---------|
| 飞书卡片回调SDK不支持 | 高 | 中 | 提前调研SDK，准备备用方案 |
| 插件扫描性能问题 | 中 | 低 | 添加缓存，异步加载 |
| 现有代码重构影响 | 中 | 中 | 保持最小改动，充分测试 |
| 测试环境不足 | 高 | 低 | 准备模拟数据和单元测试 |

---

**文档完成**: 2026-03-02
**下次更新**: Sprint 1 开始前
