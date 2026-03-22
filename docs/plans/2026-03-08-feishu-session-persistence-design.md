# 飞书会话管理持久化设计方案
## 版本信息
- 版本：v1.0
- 日期：2026-03-08
- 作者：Claude Code
- 状态：已批准
## 背景
飞书渠道的会话管理目前仅存储在内存中，存在以下问题：
1. 应用重启后所有会话映射丢失，用户需要重新创建会话
2. 不支持分布式部署，多实例之间会话数据无法共享
3. 长期运行存在内存泄漏风险
## 需求确认
1. **持久化范围**：仅持久化飞书聊天与会话的映射关系，会话消息内容继续使用现有 `ChatSessionService` 存储方案
2. **迁移策略**：不迁移旧数据，服务重启后用户需要重新创建会话
3. **分布式支持**：需要支持多实例部署，保证数据强一致性
## 方案选择
采用**数据库优先方案**：
- 完全以数据库作为唯一数据源，内存仅作为可选缓存层
- 所有会话操作先写数据库，再更新缓存
- 通过数据库事务和唯一索引保证数据一致性
## 详细设计
### 1. 数据库表结构调整
在 `ChatSession` 表中新增字段：
| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| FeishuChatKey | varchar(128) | 可空，普通索引 | 飞书聊天唯一标识（小写ChatId） |
| IsFeishuActive | tinyint(1) | 非空，默认0 | 是否为当前聊天的活跃会话 |
**索引设计**：
- 普通索引：`idx_feishu_chatkey (FeishuChatKey)` - 快速查询某个聊天的所有会话
- 唯一索引：`ux_feishu_active (FeishuChatKey, IsFeishuActive)` - 保证每个聊天只能有一个活跃会话
### 2. 核心逻辑改造
#### 2.1 FeishuChannelService 字段调整
移除原有三个内存字典：
- `_chatSessionList`
- `_chatCurrentSession`
- `_sessionLastActiveTime`
新增依赖：
- `IChatSessionRepository _chatSessionRepository`
#### 2.2 接口实现逻辑
| 方法 | 实现逻辑 |
|------|----------|
| `GetCurrentSession(chatKey)` | 查询数据库中 `FeishuChatKey = chatKey AND IsFeishuActive = true` 的会话 |
| `GetChatSessions(chatKey)` | 查询数据库中 `FeishuChatKey = chatKey` 的所有会话，按 `UpdatedAt` 倒序 |
| `GetSessionLastActiveTime(sessionId)` | 查询指定会话的 `UpdatedAt` 字段 |
| `SwitchCurrentSession(chatKey, sessionId)` | 开启事务：<br>1. 将该聊天下所有会话的 `IsFeishuActive` 设为 `false`<br>2. 将目标会话的 `IsFeishuActive` 设为 `true`<br>3. 更新目标会话的 `UpdatedAt` 为当前时间 |
| `CloseSession(chatKey, sessionId)` | 开启事务：<br>1. 删除指定会话记录<br>2. 如果关闭的是活跃会话，将该聊天下最新的会话设为活跃（如果有）<br>3. 调用 `_cliExecutor.CleanupSessionWorkspace()` 清理工作目录 |
| `CreateNewSession(message)` | 开启事务：<br>1. 将该聊天下所有会话的 `IsFeishuActive` 设为 `false`<br>2. 创建新会话记录，设置 `FeishuChatKey = chatKey`, `IsFeishuActive = true`<br>3. 返回新会话ID |
### 3. 分布式一致性保证
1. **事务原子性**：所有修改操作使用数据库事务保证原子性
2. **唯一索引约束**：通过 `ux_feishu_active` 唯一索引防止并发设置活跃会话导致的数据冲突
3. **乐观锁**：更新会话时使用 `UpdatedAt` 作为乐观锁标记，防止并发修改冲突
### 4. 缓存优化（可选）
增加本地内存缓存层，缓存过期时间5分钟：
```csharp
// 缓存结构：<ChatKey, (会话列表, 当前活跃会话ID, 缓存时间)>
private readonly ConcurrentDictionary<string, (List<string> Sessions, string? CurrentSessionId, DateTime CacheTime)> _sessionCache = new();
```
### 5. 错误处理
- 数据库操作失败：重试3次，失败返回友好提示
- 并发冲突：捕获唯一索引冲突异常，返回"操作太频繁，请稍后重试"
- 会话不存在：引导用户使用 `/feishusessions` 命令创建新会话
## 实施计划
### 阶段1：数据模型改造
1. 修改 `ChatSessionEntity` 添加新字段
2. 创建数据库迁移脚本
3. 更新 `IChatSessionRepository` 添加飞书相关查询方法
### 阶段2：业务逻辑改造
1. 修改 `FeishuChannelService` 移除内存字典，替换为数据库操作
2. 实现所有会话接口的数据库版本
3. 添加事务处理和错误重试机制
### 阶段3：测试验证
1. 单实例功能测试：会话创建、切换、关闭功能正常
2. 分布式测试：多实例部署下会话数据一致性验证
3. 性能测试：验证并发操作下的稳定性
## 风险评估
| 风险 | 概率 | 影响 | 应对措施 |
|------|------|------|----------|
| 服务重启后用户丢失历史会话 | 高 | 低（需求明确要求不迁移） | 发布公告告知用户重启后需要重新创建会话 |
| 数据库性能瓶颈 | 低 | 中 | 添加缓存层优化查询性能 |
| 并发操作冲突 | 中 | 低 | 数据库唯一索引+重试机制 |
## 验收标准
1. 飞书会话创建、切换、关闭功能正常
2. 应用重启后新建的会话不会丢失
3. 多实例部署下会话数据保持一致
4. 并发操作不会产生脏数据
