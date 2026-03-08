# 飞书会话持久化实现计划
> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.
**Goal:** 实现飞书会话管理的数据库持久化，支持分布式部署，应用重启后会话不丢失
**Architecture:** 以数据库作为唯一数据源，移除内存字典存储，通过数据库事务和唯一索引保证数据一致性，可选本地缓存提升查询性能
**Tech Stack:** .NET 10.0, SqlSugar ORM, MySQL/SQLite
---
## 前置检查
### Task 0: 代码库状态检查
**Files:**
- Check: `WebCodeCli.Domain/Repositories/Base/ChatSession/ChatSessionEntity.cs`
- Check: `WebCodeCli.Domain/Repositories/Base/ChatSession/IChatSessionRepository.cs`
- Check: `WebCodeCli.Domain/Repositories/Base/ChatSession/ChatSessionRepository.cs`
- Check: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs`
**Step 1: 确认现有代码结构**
```bash
# 检查文件是否存在
ls WebCodeCli.Domain/Repositories/Base/ChatSession/
ls WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs
```
Expected: 所有文件都存在，编译正常
**Step 2: 备份当前代码**
```bash
git add .
git commit -m "backup: before feishu session persistence implementation"
```
---
## 阶段1：数据模型改造
### Task 1: 修改 ChatSessionEntity 添加飞书字段
**Files:**
- Modify: `WebCodeCli.Domain/Repositories/Base/ChatSession/ChatSessionEntity.cs`
**Step 1: 添加新字段**
在 `ChatSessionEntity` 类中添加以下属性：
```csharp
/// <summary>
/// 飞书聊天唯一标识（小写的ChatId，作为渠道关联键）
/// </summary>
[SugarColumn(Length = 128, IsNullable = true, IndexGroupName = "idx_feishu_chatkey")]
public string? FeishuChatKey { get; set; }
/// <summary>
/// 是否为当前聊天的活跃会话（每个ChatKey只能有一个活跃会话）
/// </summary>
[SugarColumn(IsNullable = false, DefaultValue = "0")]
public bool IsFeishuActive { get; set; } = false;
```
**Step 2: 添加索引配置**
在类上方添加索引配置：
```csharp
[SugarIndex("idx_feishu_chatkey", nameof(FeishuChatKey), OrderByType.Asc)]
[SugarIndex("ux_feishu_active", nameof(FeishuChatKey), nameof(IsFeishuActive), OrderByType.Asc, IsUnique = true)]
```
**Step 3: 编译验证**
```bash
dotnet build WebCodeCli.Domain
```
Expected: 编译成功
---
### Task 2: 扩展 IChatSessionRepository 接口
**Files:**
- Modify: `WebCodeCli.Domain/Repositories/Base/ChatSession/IChatSessionRepository.cs`
**Step 1: 添加飞书相关查询方法**
```csharp
/// <summary>
/// 根据飞书ChatKey获取所有会话
/// </summary>
/// <param name="feishuChatKey">飞书聊天键</param>
/// <returns>会话列表</returns>
Task<List<ChatSessionEntity>> GetByFeishuChatKeyAsync(string feishuChatKey);
/// <summary>
/// 根据飞书ChatKey获取当前活跃会话
/// </summary>
/// <param name="feishuChatKey">飞书聊天键</param>
/// <returns>活跃会话，如果不存在则返回null</returns>
Task<ChatSessionEntity?> GetActiveByFeishuChatKeyAsync(string feishuChatKey);
/// <summary>
/// 设置指定会话为飞书活跃会话
/// </summary>
/// <param name="feishuChatKey">飞书聊天键</param>
/// <param name="sessionId">要设置为活跃的会话ID</param>
/// <returns>是否成功</returns>
Task<bool> SetActiveSessionAsync(string feishuChatKey, string sessionId);
/// <summary>
/// 关闭飞书会话
/// </summary>
/// <param name="feishuChatKey">飞书聊天键</param>
/// <param name="sessionId">要关闭的会话ID</param>
/// <returns>是否成功</returns>
Task<bool> CloseFeishuSessionAsync(string feishuChatKey, string sessionId);
/// <summary>
/// 创建飞书新会话
/// </summary>
/// <param name="feishuChatKey">飞书聊天键</param>
/// <param name="username">用户名</param>
/// <param name="workspacePath">工作区路径</param>
/// <returns>新会话ID</returns>
Task<string> CreateFeishuSessionAsync(string feishuChatKey, string username, string? workspacePath = null);
```
**Step 2: 编译验证**
```bash
dotnet build WebCodeCli.Domain
```
Expected: 编译成功
---
### Task 3: 实现 ChatSessionRepository 扩展方法
**Files:**
- Modify: `WebCodeCli.Domain/Repositories/Base/ChatSession/ChatSessionRepository.cs`
**Step 1: 实现 GetByFeishuChatKeyAsync**
```csharp
public async Task<List<ChatSessionEntity>> GetByFeishuChatKeyAsync(string feishuChatKey)
{
    return await _db.Queryable<ChatSessionEntity>()
        .Where(s => s.FeishuChatKey == feishuChatKey)
        .OrderByDescending(s => s.UpdatedAt)
        .ToListAsync();
}
```
**Step 2: 实现 GetActiveByFeishuChatKeyAsync**
```csharp
public async Task<ChatSessionEntity?> GetActiveByFeishuChatKeyAsync(string feishuChatKey)
{
    return await _db.Queryable<ChatSessionEntity>()
        .Where(s => s.FeishuChatKey == feishuChatKey && s.IsFeishuActive)
        .FirstAsync();
}
```
**Step 3: 实现 SetActiveSessionAsync**
```csharp
public async Task<bool> SetActiveSessionAsync(string feishuChatKey, string sessionId)
{
    using var tran = _db.Ado.BeginTran();
    try
    {
        // 将该聊天下所有会话设为非活跃
        await _db.Updateable<ChatSessionEntity>()
            .Set(s => s.IsFeishuActive, false)
            .Where(s => s.FeishuChatKey == feishuChatKey)
            .ExecuteCommandAsync();
        // 设置目标会话为活跃
        var rowsAffected = await _db.Updateable<ChatSessionEntity>()
            .Set(s => s.IsFeishuActive, true)
            .Set(s => s.UpdatedAt, DateTime.Now)
            .Where(s => s.SessionId == sessionId && s.FeishuChatKey == feishuChatKey)
            .ExecuteCommandAsync();
        tran.Commit();
        return rowsAffected > 0;
    }
    catch
    {
        tran.Rollback();
        throw;
    }
}
```
**Step 4: 实现 CloseFeishuSessionAsync**
```csharp
public async Task<bool> CloseFeishuSessionAsync(string feishuChatKey, string sessionId)
{
    using var tran = _db.Ado.BeginTran();
    try
    {
        // 检查是否为活跃会话
        var session = await _db.Queryable<ChatSessionEntity>()
            .Where(s => s.SessionId == sessionId && s.FeishuChatKey == feishuChatKey)
            .FirstAsync();
        if (session == null)
        {
            tran.Rollback();
            return false;
        }
        var wasActive = session.IsFeishuActive;
        // 删除会话
        await _db.Deleteable<ChatSessionEntity>()
            .Where(s => s.SessionId == sessionId)
            .ExecuteCommandAsync();
        // 如果删除的是活跃会话，找最新的会话设为活跃
        if (wasActive)
        {
            var latestSession = await _db.Queryable<ChatSessionEntity>()
                .Where(s => s.FeishuChatKey == feishuChatKey)
                .OrderByDescending(s => s.UpdatedAt)
                .FirstAsync();
            if (latestSession != null)
            {
                await _db.Updateable<ChatSessionEntity>()
                    .Set(s => s.IsFeishuActive, true)
                    .Set(s => s.UpdatedAt, DateTime.Now)
                    .Where(s => s.SessionId == latestSession.SessionId)
                    .ExecuteCommandAsync();
            }
        }
        tran.Commit();
        return true;
    }
    catch
    {
        tran.Rollback();
        throw;
    }
}
```
**Step 5: 实现 CreateFeishuSessionAsync**
```csharp
public async Task<string> CreateFeishuSessionAsync(string feishuChatKey, string username, string? workspacePath = null)
{
    using var tran = _db.Ado.BeginTran();
    try
    {
        // 将该聊天下所有会话设为非活跃
        await _db.Updateable<ChatSessionEntity>()
            .Set(s => s.IsFeishuActive, false)
            .Where(s => s.FeishuChatKey == feishuChatKey)
            .ExecuteCommandAsync();
        // 创建新会话
        var newSessionId = Guid.NewGuid().ToString();
        var newSession = new ChatSessionEntity
        {
            SessionId = newSessionId,
            Username = username,
            FeishuChatKey = feishuChatKey,
            IsFeishuActive = true,
            WorkspacePath = workspacePath,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        await _db.Insertable(newSession).ExecuteCommandAsync();
        tran.Commit();
        return newSessionId;
    }
    catch
    {
        tran.Rollback();
        throw;
    }
}
```
**Step 6: 编译验证**
```bash
dotnet build WebCodeCli.Domain
```
Expected: 编译成功
---
## 阶段2：业务逻辑改造
### Task 4: 改造 FeishuChannelService 移除内存字典
**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs`
**Step 1: 移除内存字典字段**
删除以下三个字段：
```csharp
private readonly ConcurrentDictionary<string, List<string>> _chatSessionList = new();
private readonly ConcurrentDictionary<string, string> _chatCurrentSession = new();
private readonly ConcurrentDictionary<string, DateTime> _sessionLastActiveTime = new();
```
**Step 2: 新增 ChatSessionRepository 依赖**
在构造函数中添加参数：
```csharp
private readonly IChatSessionRepository _chatSessionRepository;
public FeishuChannelService(
    IOptions<FeishuOptions> options,
    ILogger<FeishuChannelService> logger,
    IFeishuCardKitClient cardKit,
    IServiceProvider serviceProvider,
    ICliExecutorService cliExecutor,
    IChatSessionService chatSessionService,
    IChatSessionRepository chatSessionRepository) // 新增
{
    _options = options.Value;
    _logger = logger;
    _cardKit = cardKit;
    _serviceProvider = serviceProvider;
    _cliExecutor = cliExecutor;
    _chatSessionService = chatSessionService;
    _chatSessionRepository = chatSessionRepository; // 新增
}
```
**Step 3: 编译验证**
```bash
dotnet build WebCodeCli.Domain
```
Expected: 编译成功（会有方法实现错误，下一步修复）
---
### Task 5: 重写会话相关方法实现
**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs`
**Step 1: 重写 GetCurrentSession**
```csharp
public string? GetCurrentSession(string chatKey)
{
    var session = _chatSessionRepository.GetActiveByFeishuChatKeyAsync(chatKey).GetAwaiter().GetResult();
    return session?.SessionId;
}
```
**Step 2: 重写 GetSessionLastActiveTime**
```csharp
public DateTime? GetSessionLastActiveTime(string sessionId)
{
    var session = _chatSessionRepository.GetByIdAsync(sessionId).GetAwaiter().GetResult();
    return session?.UpdatedAt;
}
```
**Step 3: 重写 GetChatSessions**
```csharp
public List<string> GetChatSessions(string chatKey)
{
    var sessions = _chatSessionRepository.GetByFeishuChatKeyAsync(chatKey).GetAwaiter().GetResult();
    return sessions.Select(s => s.SessionId).ToList();
}
```
**Step 4: 重写 SwitchCurrentSession**
```csharp
public bool SwitchCurrentSession(string chatKey, string sessionId)
{
    return _chatSessionRepository.SetActiveSessionAsync(chatKey, sessionId).GetAwaiter().GetResult();
}
```
**Step 5: 重写 CloseSession**
```csharp
public bool CloseSession(string chatKey, string sessionId)
{
    var success = _chatSessionRepository.CloseFeishuSessionAsync(chatKey, sessionId).GetAwaiter().GetResult();
    if (success)
    {
        // 清理工作目录
        _cliExecutor.CleanupSessionWorkspace(sessionId);
        _logger.LogInformation("Closed session {SessionId} for chat {ChatKey}", sessionId, chatKey);
    }
    return success;
}
```
**Step 6: 重写 CreateNewSession**
```csharp
public string CreateNewSession(FeishuIncomingMessage message, string? customWorkspacePath = null)
{
    var chatKey = message.ChatId.ToLowerInvariant();
    var newSessionId = _chatSessionRepository.CreateFeishuSessionAsync(
        chatKey,
        message.SenderName ?? "unknown",
        customWorkspacePath).GetAwaiter().GetResult();
    _logger.LogInformation(
        "Created new session: {SessionId} for chat: {ChatId} (user: {UserName})",
        newSessionId,
        message.ChatId,
        message.SenderName);
    return newSessionId;
}
```
**Step 7: 更新 GetCurrentSession 私有方法**
```csharp
private string GetCurrentSession(FeishuIncomingMessage message)
{
    var chatKey = message.ChatId.ToLowerInvariant();
    _logger.LogInformation("🔍 [会话匹配] 消息ChatId={ChatId}, 生成ChatKey={ChatKey}",
        message.ChatId, chatKey);
    var currentSession = _chatSessionRepository.GetActiveByFeishuChatKeyAsync(chatKey).GetAwaiter().GetResult();
    if (currentSession != null)
    {
        _logger.LogDebug("Using current session: {SessionId} for chat: {ChatId}", currentSession.SessionId, message.ChatId);
        // 更新最后活动时间
        _chatSessionRepository.UpdateAsync(new ChatSessionEntity
        {
            SessionId = currentSession.SessionId,
            UpdatedAt = DateTime.Now
        }, s => new { s.UpdatedAt }).GetAwaiter().GetResult();
        return currentSession.SessionId;
    }
    // 没有当前会话，抛出异常提示用户手动创建
    throw new InvalidOperationException("当前没有可用会话，请先发送 /feishusessions 命令创建或选择会话。");
}
```
**Step 8: 更新 ExecuteCliAndStreamAsync 中的会话更新逻辑**
在方法末尾，更新会话最后活动时间部分：
```csharp
// 更新会话最后活动时间
await _chatSessionRepository.UpdateAsync(new ChatSessionEntity
{
    SessionId = sessionId,
    UpdatedAt = DateTime.UtcNow
}, s => new { s.UpdatedAt });
```
**Step 9: 编译验证**
```bash
dotnet build WebCodeCli.Domain
```
Expected: 编译成功
---
## 阶段3：测试与验证
### Task 6: 本地功能测试
**Files:**
- Run: WebCodeCli application
**Step 1: 启动应用**
```bash
dotnet run --project WebCodeCli
```
Expected: 应用启动成功，飞书渠道正常初始化
**Step 2: 测试会话创建**
1. 在飞书中发送 `/feishusessions` 命令
2. 点击「新建会话」按钮
3. 验证会话创建成功，卡片显示新会话
**Step 3: 测试会话切换**
1. 创建多个会话
2. 点击「切换」按钮切换会话
3. 验证切换成功，当前会话标记正确
**Step 4: 测试会话关闭**
1. 点击「关闭」按钮关闭会话
2. 验证会话从列表中消失
**Step 5: 测试重启恢复**
1. 创建几个会话，记住当前活跃会话
2. 重启应用
3. 发送 `/feishusessions` 命令
4. 验证会话列表和活跃会话都正确保留
---
### Task 7: 并发与分布式测试
**Step 1: 并发创建会话测试**
使用工具模拟多个并发请求创建会话，验证唯一索引约束生效，不会产生多个活跃会话
**Step 2: 多实例测试**
启动两个应用实例，在一个实例创建会话，在另一个实例发送 `/feishusessions` 命令，验证会话数据同步
---
## 阶段4：性能优化（可选）
### Task 8: 添加本地缓存层（可选）
**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/Channels/FeishuChannelService.cs`
**Step 1: 添加缓存字段**
```csharp
// 缓存结构：<ChatKey, (会话列表, 当前活跃会话ID, 缓存时间)>
private readonly ConcurrentDictionary<string, (List<string> Sessions, string? CurrentSessionId, DateTime CacheTime)> _sessionCache = new();
private const int CacheExpirationMinutes = 5;
```
**Step 2: 封装缓存操作方法**
添加缓存读取和更新的辅助方法，在每次数据库操作后更新缓存，查询时先查缓存
---
## 验收标准
1. ✅ 飞书会话创建、切换、关闭功能正常
2. ✅ 应用重启后会话数据不丢失
3. ✅ 多实例部署下会话数据一致
4. ✅ 并发操作不会产生脏数据
5. ✅ 原有功能不受影响，消息处理正常
