# Web 端会话管理增强实现计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 实现 Web 端用户-会话绑定和自定义工作目录功能，支持用户隔离、自定义工作目录、桌面端和移动端友好的 UI

**Architecture:** 抽象共享 IWorkspaceService 服务层，统一 Web 端和飞书端工作目录管理，复用现有 PathSecurityHelper 安全校验逻辑，保持前后端 API 解耦

**Tech Stack:** .NET 10, Blazor Server, SqlSugar ORM, Tailwind CSS

---

## Task 1: 实现 IWorkspaceService 共享服务层

**Files:**
- Create: `WebCodeCli.Domain/Domain/Service/IWorkspaceService.cs`
- Create: `WebCodeCli.Domain/Domain/Service/WorkspaceService.cs`
- Modify: `WebCodeCli.Domain/Common/Extensions/ServiceCollectionExtensions.cs`
- Test: `WebCodeCli.Domain.Tests/Service/WorkspaceServiceTests.cs`

**Step 1: 定义 IWorkspaceService 接口**

```csharp
namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 工作目录管理服务接口
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// 验证路径是否安全
    /// </summary>
    /// <param name="path">要验证的路径</param>
    /// <param name="errorMessage">错误信息</param>
    /// <returns>是否安全</returns>
    bool ValidatePath(string path, out string errorMessage);

    /// <summary>
    /// 确保路径存在
    /// </summary>
    /// <param name="path">路径</param>
    /// <param name="allowCreate">是否允许创建</param>
    /// <param name="errorMessage">错误信息</param>
    /// <returns>是否存在或创建成功</returns>
    bool EnsurePathExists(string path, bool allowCreate, out string errorMessage);

    /// <summary>
    /// 获取允许的目录白名单
    /// </summary>
    /// <returns>允许的目录列表</returns>
    List<string> GetAllowedPaths();

    /// <summary>
    /// 获取或创建会话工作目录
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="customPath">自定义路径（可选）</param>
    /// <returns>工作目录路径</returns>
    string GetOrCreateWorkspace(string sessionId, string? customPath = null);

    /// <summary>
    /// 获取会话工作目录
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>工作目录路径</returns>
    string GetSessionWorkspacePath(string sessionId);

    /// <summary>
    /// 清理会话工作目录
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="isCustom">是否是自定义目录</param>
    void CleanupSessionWorkspace(string sessionId, bool isCustom = false);
}
```

**Step 2: 实现 WorkspaceService**

```csharp
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Helpers;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model;
using Microsoft.Extensions.Logging;

namespace WebCodeCli.Domain.Domain.Service;

/// <summary>
/// 工作目录管理服务
/// </summary>
public class WorkspaceService : IWorkspaceService
{
    private readonly FeishuOptions _options;
    private readonly ICliExecutorService _cliExecutor;
    private readonly ILogger<WorkspaceService> _logger;
    private static readonly object _lock = new();
    private static readonly Dictionary<string, string> _sessionWorkspaces = new();

    public WorkspaceService(
        IOptions<FeishuOptions> options,
        ICliExecutorService cliExecutor,
        ILogger<WorkspaceService> logger)
    {
        _options = options.Value;
        _cliExecutor = cliExecutor;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool ValidatePath(string path, out string errorMessage)
    {
        return PathSecurityHelper.IsPathSafe(path, _options.AllowedWorkspacePaths, out errorMessage);
    }

    /// <inheritdoc />
    public bool EnsurePathExists(string path, bool allowCreate, out string errorMessage)
    {
        return PathSecurityHelper.EnsurePathExists(path, allowCreate, out errorMessage);
    }

    /// <inheritdoc />
    public List<string> GetAllowedPaths()
    {
        return _options.AllowedWorkspacePaths ?? new List<string>();
    }

    /// <inheritdoc />
    public string GetOrCreateWorkspace(string sessionId, string? customPath = null)
    {
        lock (_lock)
        {
            if (_sessionWorkspaces.TryGetValue(sessionId, out var existingWorkspace))
            {
                return existingWorkspace;
            }

            string workspacePath;
            if (!string.IsNullOrEmpty(customPath))
            {
                // 验证自定义路径
                if (!ValidatePath(customPath, out var errorMessage))
                {
                    throw new InvalidOperationException(errorMessage);
                }

                // 确保路径存在
                if (!EnsurePathExists(customPath, _options.AllowCreateNonExistentPath, out errorMessage))
                {
                    throw new InvalidOperationException(errorMessage);
                }

                workspacePath = customPath;
            }
            else
            {
                // 使用默认路径
                workspacePath = _cliExecutor.GetSessionWorkspacePath(sessionId);
            }

            _sessionWorkspaces[sessionId] = workspacePath;
            return workspacePath;
        }
    }

    /// <inheritdoc />
    public string GetSessionWorkspacePath(string sessionId)
    {
        lock (_lock)
        {
            if (_sessionWorkspaces.TryGetValue(sessionId, out var existingWorkspace))
            {
                return existingWorkspace;
            }
            return _cliExecutor.GetSessionWorkspacePath(sessionId);
        }
    }

    /// <inheritdoc />
    public void CleanupSessionWorkspace(string sessionId, bool isCustom = false)
    {
        try
        {
            // 自定义目录不清理
            if (isCustom) return;

            lock (_lock)
            {
                _sessionWorkspaces.Remove(sessionId, out _);
            }

            _cliExecutor.CleanupSessionWorkspace(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理会话工作目录失败: {SessionId}", sessionId);
        }
    }
}
```

**Step 3: 注册服务到 DI 容器**

在 `ServiceCollectionExtensions.cs` 中添加：
```csharp
services.AddScoped<IWorkspaceService, WorkspaceService>();
```

**Step 4: 运行构建验证编译**

Run: `dotnet build WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 0 errors

**Step 5: Commit**

```bash
git add WebCodeCli.Domain/Domain/Service/IWorkspaceService.cs
git add WebCodeCli.Domain/Domain/Service/WorkspaceService.cs
git add WebCodeCli.Domain/Common/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat: add IWorkspaceService shared layer"
```

---

## Task 2: 扩展 SessionHistory 模型支持用户隔离和自定义目录

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Model/Chat/SessionHistory.cs`
- Modify: `WebCodeCli.Domain/Repositories/Base/ChatSession/ChatSessionRepository.cs`
- Modify: `WebCodeCli.Domain/Repositories/Base/ChatSession/IChatSessionRepository.cs`
- Test: `WebCodeCli.Domain.Tests/Repositories/ChatSessionRepositoryTests.cs`

**Step 1: 扩展 SessionHistory 模型**

```csharp
namespace WebCodeCli.Domain.Domain.Model.Chat;

/// <summary>
/// 会话历史记录
/// </summary>
public class SessionHistory
{
    /// <summary>
    /// 会话ID
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 会话标题
    /// </summary>
    public string Title { get; set; } = "新会话";

    /// <summary>
    /// 用户名（用户标识，用于多用户隔离）
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 工作目录路径
    /// </summary>
    public string? WorkspacePath { get; set; }

    /// <summary>
    /// 是否为自定义工作目录
    /// </summary>
    public bool IsCustomWorkspace { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 关联的项目ID（可选）
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// 关联的项目名称（可选）
    /// </summary>
    public string? ProjectName { get; set; }

    /// <summary>
    /// 使用的CLI工具ID
    /// </summary>
    public string ToolId { get; set; } = "claude-code";

    /// <summary>
    /// 会话消息列表
    /// </summary>
    public List<ChatMessage> Messages { get; set; } = new();

    /// <summary>
    /// 工作区是否有效
    /// </summary>
    public bool IsWorkspaceValid { get; set; } = true;
}
```

**Step 2: 更新 ChatSessionRepository - 添加用户隔离查询**

在 `IChatSessionRepository.cs` 中添加：
```csharp
/// <summary>
/// 获取指定用户的所有会话
/// </summary>
/// <param name="username">用户名</param>
/// <returns>会话列表</returns>
Task<List<ChatSessionEntity>> GetUserSessionsAsync(string username);

/// <summary>
/// 验证会话是否属于指定用户
/// </summary>
/// <param name="sessionId">会话ID</param>
/// <param name="username">用户名</param>
/// <returns>是否属于该用户</returns>
Task<bool> IsSessionOwnerAsync(string sessionId, string username);
```

在 `ChatSessionRepository.cs` 中实现：
```csharp
/// <inheritdoc />
public async Task<List<ChatSessionEntity>> GetUserSessionsAsync(string username)
{
    return await GetDB().Queryable<ChatSessionEntity>()
        .Where(s => s.Username == username && !s.IsDeleted)
        .OrderByDescending(s => s.UpdatedAt)
        .ToListAsync();
}

/// <inheritdoc />
public async Task<bool> IsSessionOwnerAsync(string sessionId, string username)
{
    return await GetDB().Queryable<ChatSessionEntity>()
        .AnyAsync(s => s.SessionId == sessionId && s.Username == username && !s.IsDeleted);
}
```

**Step 3: 运行构建验证编译**

Run: `dotnet build WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 0 errors

**Step 4: Commit**

```bash
git add WebCodeCli.Domain/Domain/Model/Chat/SessionHistory.cs
git add WebCodeCli.Domain/Repositories/Base/ChatSession/*
git commit -m "feat: extend SessionHistory for user isolation and custom workspace"
```

---

## Task 3: 实现 Web API 接口

**Files:**
- Create: `WebCodeCli/Controllers/WorkspaceController.cs`
- Modify: `WebCodeCli/Controllers/SessionController.cs` (or create if not exists)

**Step 1: 创建 WorkspaceController**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Model;

namespace WebCodeCli.Controllers;

/// <summary>
/// 工作目录管理 API
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkspaceController : ControllerBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ILogger<WorkspaceController> _logger;

    public WorkspaceController(
        IWorkspaceService workspaceService,
        ILogger<WorkspaceController> logger)
    {
        _workspaceService = workspaceService;
        _logger = logger;
    }

    /// <summary>
    /// 获取目录白名单
    /// </summary>
    /// <returns>允许的目录列表</returns>
    [HttpGet("allowed-paths")]
    public IActionResult GetAllowedPaths()
    {
        try
        {
            var paths = _workspaceService.GetAllowedPaths();
            return Ok(new { success = true, paths });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取目录白名单失败");
            return StatusCode(500, new { success = false, message = "获取目录白名单失败" });
        }
    }

    /// <summary>
    /// 验证路径是否合规
    /// </summary>
    /// <param name="request">路径验证请求</param>
    /// <returns>验证结果</returns>
    [HttpPost("validate-path")]
    public IActionResult ValidatePath([FromBody] ValidatePathRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Path))
            {
                return BadRequest(new { success = false, message = "路径不能为空" });
            }

            var isValid = _workspaceService.ValidatePath(request.Path, out var errorMessage);
            return Ok(new { success = isValid, message = errorMessage });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证路径失败");
            return StatusCode(500, new { success = false, message = "验证路径失败" });
        }
    }
}

/// <summary>
/// 路径验证请求
/// </summary>
public class ValidatePathRequest
{
    /// <summary>
    /// 要验证的路径
    /// </summary>
    public string Path { get; set; } = string.Empty;
}
```

**Step 2: 创建/更新 SessionController**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebCodeCli.Domain.Domain.Service;
using WebCodeCli.Domain.Domain.Model.Chat;
using WebCodeCli.Domain.Repositories.Base.ChatSession;

namespace WebCodeCli.Controllers;

/// <summary>
/// 会话管理 API
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SessionController : ControllerBase
{
    private readonly IChatSessionRepository _sessionRepository;
    private readonly ISessionHistoryManager _sessionHistoryManager;
    private readonly IUserContextService _userContext;
    private readonly ILogger<SessionController> _logger;

    public SessionController(
        IChatSessionRepository sessionRepository,
        ISessionHistoryManager sessionHistoryManager,
        IUserContextService userContext,
        ILogger<SessionController> logger)
    {
        _sessionRepository = sessionRepository;
        _sessionHistoryManager = sessionHistoryManager;
        _userContext = userContext;
        _logger = logger;
    }

    /// <summary>
    /// 获取当前用户的所有会话
    /// </summary>
    /// <returns>会话列表</returns>
    [HttpGet("user")]
    public async Task<IActionResult> GetUserSessions()
    {
        try
        {
            var username = _userContext.GetCurrentUsername();
            var sessions = await _sessionRepository.GetUserSessionsAsync(username);
            return Ok(new { success = true, sessions });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取用户会话失败");
            return StatusCode(500, new { success = false, message = "获取会话失败" });
        }
    }

    /// <summary>
    /// 删除会话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>操作结果</returns>
    [HttpDelete("{sessionId}")]
    public async Task<IActionResult> DeleteSession(string sessionId)
    {
        try
        {
            var username = _userContext.GetCurrentUsername();
            var isOwner = await _sessionRepository.IsSessionOwnerAsync(sessionId, username);
            if (!isOwner)
            {
                return Forbid("没有权限删除该会话");
            }

            var success = await _sessionRepository.CloseFeishuSessionAsync(username, sessionId);
            if (success)
            {
                await _sessionHistoryManager.DeleteSessionAsync(sessionId);
                return Ok(new { success = true, message = "会话已删除" });
            }
            return BadRequest(new { success = false, message = "删除失败" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除会话失败: {SessionId}", sessionId);
            return StatusCode(500, new { success = false, message = "删除失败" });
        }
    }
}
```

**Step 3: 运行构建验证编译**

Run: `dotnet build WebCodeCli/WebCodeCli.csproj`
Expected: 0 errors

**Step 4: Commit**

```bash
git add WebCodeCli/Controllers/WorkspaceController.cs
git add WebCodeCli/Controllers/SessionController.cs
git commit -m "feat: add web API for workspace and session management"
```

---

## Task 4: 实现前端 UI 组件

**Files:**
- Modify: `WebCodeCli/Pages/CodeAssistant.razor`
- Modify: `WebCodeCli/Pages/CodeAssistant.razor.cs`
- Create: `WebCodeCli/Components/Dialogs/CreateSessionModal.razor`
- Create: `WebCodeCli/Components/Dialogs/SessionManagementPanel.razor`
- Modify: `WebCodeCli/Components/SessionListPanel.razor`

**Step 1: 创建 CreateSessionModal 新建会话弹窗**

```razor
@inherits ComponentBase
@inject IWorkspaceService WorkspaceService
@inject IUserContextService UserContext
@inject ISessionHistoryManager SessionHistoryManager
@inject ICliExecutorService CliExecutor
@inject IToastService ToastService

<div class="modal @(IsVisible ? "show" : "")" style="display: @(IsVisible ? "block" : "none")">
    <div class="modal-dialog modal-lg">
        <div class="modal-content">
            <div class="modal-header">
                <h5 class="modal-title">🆕 新建会话</h5>
                <button type="button" class="btn-close" @onclick="Close"></button>
            </div>
            <div class="modal-body">
                <div class="mb-4">
                    <label class="form-label">工作目录</label>
                    <div class="space-y-3">
                        <div class="form-check">
                            <input class="form-check-input" type="radio" name="workspaceType" id="defaultWorkspace"
                                   checked="@(!UseCustomWorkspace)" @onchange="() => UseCustomWorkspace = false">
                            <label class="form-check-label" for="defaultWorkspace">
                                📄 使用默认目录 (推荐)
                                <div class="text-sm text-gray-500 mt-1">系统自动创建独立工作空间，会话关闭后自动清理</div>
                            </label>
                        </div>

                        <div class="form-check">
                            <input class="form-check-input" type="radio" name="workspaceType" id="customWorkspace"
                                   checked="@UseCustomWorkspace" @onchange="() => UseCustomWorkspace = true">
                            <label class="form-check-label" for="customWorkspace">
                                📂 自定义目录
                                <div class="text-sm text-gray-500 mt-1">使用指定目录作为工作空间，多个会话可共用</div>
                            </label>
                        </div>

                        @if (UseCustomWorkspace)
                        {
                            <div class="mt-3">
                                <input type="text" class="form-control" placeholder="请输入目录路径，例如：D:\Workspace\MyProject"
                                       @bind-value="CustomPath" @oninput="ValidatePath">
                                @if (!string.IsNullOrEmpty(ValidationMessage))
                                {
                                    <div class="text-danger text-sm mt-1">@ValidationMessage</div>
                                }
                                <div class="text-sm text-gray-500 mt-1">
                                    ℹ️ 使用 /webdir 命令查看允许的目录列表
                                </div>
                            </div>
                        }
                    </div>
                </div>
            </div>
            <div class="modal-footer">
                <button type="button" class="btn btn-secondary" @onclick="Close">取消</button>
                <button type="button" class="btn btn-primary" @onclick="CreateSession" disabled="@IsCreating">
                    @(IsCreating ? "创建中..." : "创建会话")
                </button>
            </div>
        </div>
    </div>
</div>

@if (IsVisible)
{
    <div class="modal-backdrop show"></div>
}

@code {
    [Parameter] public EventCallback OnSessionCreated { get; set; }

    private bool IsVisible { get; set; }
    private bool UseCustomWorkspace { get; set; }
    private string CustomPath { get; set; } = string.Empty;
    private string ValidationMessage { get; set; } = string.Empty;
    private bool IsCreating { get; set; }

    public async Task ShowAsync()
    {
        IsVisible = true;
        UseCustomWorkspace = false;
        CustomPath = string.Empty;
        ValidationMessage = string.Empty;
        await InvokeAsync(StateHasChanged);
    }

    private void Close()
    {
        IsVisible = false;
        InvokeAsync(StateHasChanged);
    }

    private async Task ValidatePath()
    {
        if (string.IsNullOrWhiteSpace(CustomPath))
        {
            ValidationMessage = "路径不能为空";
            return;
        }

        var isValid = WorkspaceService.ValidatePath(CustomPath, out var message);
        ValidationMessage = isValid ? string.Empty : message;
        await InvokeAsync(StateHasChanged);
    }

    private async Task CreateSession()
    {
        try
        {
            IsCreating = true;
            ValidationMessage = string.Empty;

            string? workspacePath = null;
            bool isCustom = false;

            if (UseCustomWorkspace)
            {
                if (string.IsNullOrWhiteSpace(CustomPath))
                {
                    ValidationMessage = "路径不能为空";
                    return;
                }

                if (!WorkspaceService.ValidatePath(CustomPath, out var errorMessage))
                {
                    ValidationMessage = errorMessage;
                    return;
                }

                workspacePath = CustomPath;
                isCustom = true;
            }

            // 创建新会话
            var sessionId = Guid.NewGuid().ToString();
            var username = UserContext.GetCurrentUsername();

            // 初始化工作区
            var path = WorkspaceService.GetOrCreateWorkspace(sessionId, workspacePath);

            // 保存会话
            var session = new SessionHistory
            {
                SessionId = sessionId,
                Title = "新会话",
                Username = username,
                WorkspacePath = path,
                IsCustomWorkspace = isCustom,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            await SessionHistoryManager.SaveSessionImmediateAsync(session);

            ToastService.Success("会话创建成功");
            Close();
            await OnSessionCreated.InvokeAsync();
        }
        catch (Exception ex)
        {
            ToastService.Error($"创建失败: {ex.Message}");
        }
        finally
        {
            IsCreating = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}
```

**Step 2: 创建 SessionManagementPanel 会话管理面板**

```razor
@inherits ComponentBase
@inject IUserContextService UserContext
@inject IChatSessionRepository SessionRepository
@inject ISessionHistoryManager SessionHistoryManager
@inject IToastService ToastService
@inject NavigationManager NavManager

<div class="modal @(IsVisible ? "show" : "")" style="display: @(IsVisible ? "block" : "none"); z-index: 1055">
    <div class="modal-dialog modal-xl">
        <div class="modal-content">
            <div class="modal-header">
                <h5 class="modal-title">📋 会话管理</h5>
                <button type="button" class="btn-close" @onclick="Close"></button>
            </div>
            <div class="modal-body">
                @if (IsLoading)
                {
                    <div class="text-center py-5">
                        <div class="spinner-border" role="status">
                            <span class="visually-hidden">加载中...</span>
                        </div>
                        <p class="mt-2">加载会话列表...</p>
                    </div>
                }
                else
                {
                    <div class="row">
                        <div class="col-md-8">
                            <h6 class="mb-3">我的会话 (@Sessions.Count)</h6>
                            <div class="space-y-3">
                                @foreach (var session in Sessions)
                                {
                                    <div class="card p-3 @(session.SessionId == CurrentSessionId ? "border-primary" : "")">
                                        <div class="d-flex justify-content-between align-items-start">
                                            <div class="flex-grow-1">
                                                <div class="fw-bold">
                                                    @(session.SessionId == CurrentSessionId ? "✅ " : "")@session.Title
                                                </div>
                                                <div class="text-sm text-gray-600 mt-1">
                                                    📂 @(string.IsNullOrEmpty(session.WorkspacePath) ? "默认目录" : session.WorkspacePath)
                                                </div>
                                                <div class="text-xs text-gray-500 mt-1">
                                                    ⏱️ @session.UpdatedAt.ToString("yyyy-MM-dd HH:mm")
                                                </div>
                                            </div>
                                            <div class="d-flex gap-2">
                                                @if (session.SessionId != CurrentSessionId)
                                                {
                                                    <button class="btn btn-sm btn-primary" @onclick="() => SwitchSession(session.SessionId)">
                                                        切换
                                                    </button>
                                                }
                                                <button class="btn btn-sm btn-danger" @onclick="() => DeleteSession(session.SessionId)">
                                                    删除
                                                </button>
                                            </div>
                                        </div>
                                    </div>
                                }
                            </div>
                            @if (Sessions.Count == 0)
                            {
                                <div class="text-center py-5 text-gray-500">
                                    暂无会话
                                </div>
                            }
                        </div>
                        <div class="col-md-4 border-start">
                            <h6 class="mb-3">操作</h6>
                            <button class="btn btn-primary w-100 mb-3" @onclick="OpenCreateSessionModal">
                                ➕ 新建会话
                            </button>
                            <button class="btn btn-secondary w-100 mb-3" @onclick="ShowDirectoryWhitelist">
                                📋 目录白名单
                            </button>
                            <div class="alert alert-info mt-4">
                                <small>
                                    <strong>💡 提示：</strong><br>
                                    • 自定义目录不会自动清理<br>
                                    • 多个会话可以共用同一个目录
                                </small>
                            </div>
                        </div>
                    </div>
                }
            </div>
        </div>
    </div>
</div>

@if (IsVisible)
{
    <div class="modal-backdrop show" style="z-index: 1054"></div>
}

<CreateSessionModal @ref="_createSessionModal" OnSessionCreated="LoadSessions"></CreateSessionModal>

@code {
    [Parameter] public string? CurrentSessionId { get; set; }
    [Parameter] public EventCallback<string> OnSessionSwitched { get; set; }

    private bool IsVisible { get; set; }
    private bool IsLoading { get; set; }
    private List<SessionHistory> Sessions { get; set; } = new();
    private CreateSessionModal? _createSessionModal;

    public async Task ShowAsync()
    {
        IsVisible = true;
        await LoadSessions();
        await InvokeAsync(StateHasChanged);
    }

    private void Close()
    {
        IsVisible = false;
        InvokeAsync(StateHasChanged);
    }

    private async Task LoadSessions()
    {
        try
        {
            IsLoading = true;
            var username = UserContext.GetCurrentUsername();
            var entities = await SessionRepository.GetUserSessionsAsync(username);
            Sessions = entities.Select(e => new SessionHistory
            {
                SessionId = e.SessionId,
                Title = e.Title,
                WorkspacePath = e.WorkspacePath,
                UpdatedAt = e.UpdatedAt
            }).ToList();
        }
        catch (Exception ex)
        {
            ToastService.Error($"加载失败: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task OpenCreateSessionModal()
    {
        if (_createSessionModal != null)
        {
            await _createSessionModal.ShowAsync();
        }
    }

    private async Task SwitchSession(string sessionId)
    {
        try
        {
            await OnSessionSwitched.InvokeAsync(sessionId);
            Close();
        }
        catch (Exception ex)
        {
            ToastService.Error($"切换失败: {ex.Message}");
        }
    }

    private async Task DeleteSession(string sessionId)
    {
        if (!await JSRuntime.InvokeAsync<bool>("confirm", "确定要删除该会话吗？"))
        {
            return;
        }

        try
        {
            var username = UserContext.GetCurrentUsername();
            var isOwner = await SessionRepository.IsSessionOwnerAsync(sessionId, username);
            if (!isOwner)
            {
                ToastService.Error("没有权限删除该会话");
                return;
            }

            var success = await SessionRepository.CloseFeishuSessionAsync(username, sessionId);
            if (success)
            {
                await SessionHistoryManager.DeleteSessionAsync(sessionId);
                await LoadSessions();
                ToastService.Success("会话已删除");
            }
            else
            {
                ToastService.Error("删除失败");
            }
        }
        catch (Exception ex)
        {
            ToastService.Error($"删除失败: {ex.Message}");
        }
    }

    private void ShowDirectoryWhitelist()
    {
        NavManager.NavigateTo("/webdir");
        Close();
    }
}
```

**Step 3: 实现移动端顶部下拉菜单**

在 `CodeAssistant.razor` 中添加顶部菜单：
```razor
<div class="header">
    <div class="d-flex align-items-center">
        <h1 class="app-title">CodeAssistant</h1>
        @* 移动端会话下拉菜单 *@
        <div class="dropdown ms-3 d-md-none">
            <button class="btn btn-sm btn-outline-secondary dropdown-toggle" type="button"
                    data-bs-toggle="dropdown" aria-expanded="false">
                会话 ▼
            </button>
            <ul class="dropdown-menu">
                @foreach (var session in RecentSessions.Take(5))
                {
                    <li><a class="dropdown-item @(session.SessionId == CurrentSessionId ? "active" : "")"
                           @onclick="() => SwitchSession(session.SessionId)">
                        @session.Title
                    </a></li>
                }
                <li><hr class="dropdown-divider"></li>
                <li><a class="dropdown-item" @onclick="OpenSessionManagement">
                    📋 会话管理
                </a></li>
                <li><a class="dropdown-item" @onclick="OpenCreateSession">
                    ➕ 新建会话
                </a></li>
            </ul>
        </div>
    </div>
    @* 其他头部元素 *@
</div>
```

**Step 4: 运行构建验证编译**

Run: `dotnet build WebCodeCli/WebCodeCli.csproj`
Expected: 0 errors

**Step 5: Commit**

```bash
git add WebCodeCli/Components/Dialogs/CreateSessionModal.razor
git add WebCodeCli/Components/Dialogs/SessionManagementPanel.razor
git add WebCodeCli/Pages/CodeAssistant.razor
git add WebCodeCli/Pages/CodeAssistant.razor.cs
git commit -m "feat: implement frontend UI for session management"
```

---

## Task 5: 实现 /webdir 和 /websessions 内置命令

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/CommandScannerService.cs`
- Modify: `WebCodeCli/Pages/CodeAssistant.razor.cs` (command handling)

**Step 1: 添加内置命令到 CommandScannerService**

在 `CommandScannerService.cs` 的内置命令列表中添加：
```csharp
new CommandInfo
{
    Name = "/webdir",
    Description = "查看目录白名单配置",
    Usage = "/webdir",
    Category = "内置命令",
    Official = true
},
new CommandInfo
{
    Name = "/websessions",
    Description = "打开会话管理面板",
    Usage = "/websessions",
    Category = "内置命令",
    Official = true
}
```

**Step 2: 在 CodeAssistant.razor.cs 中处理命令**

在命令处理逻辑中添加：
```csharp
if (userMessage.StartsWith("/webdir", StringComparison.OrdinalIgnoreCase))
{
    await HandleWebDirCommand();
    return;
}
else if (userMessage.StartsWith("/websessions", StringComparison.OrdinalIgnoreCase))
{
    await HandleWebSessionsCommand();
    return;
}
```

实现处理方法：
```csharp
private async Task HandleWebDirCommand()
{
    var allowedPaths = _workspaceService.GetAllowedPaths();
    var content = "## 📂 目录白名单\n\n";
    if (allowedPaths == null || allowedPaths.Count == 0)
    {
        content += "⚠️ 未配置目录白名单，无法使用自定义目录功能。";
    }
    else
    {
        content += "允许使用以下目录或其子目录：\n\n";
        for (int i = 0; i < allowedPaths.Count; i++)
        {
            content += $"{i + 1}. `{allowedPaths[i]}`\n";
        }
        content += $"\n**其他配置：**\n";
        content += $"- 自动创建不存在的目录：{(_options.AllowCreateNonExistentPath ? "✅ 允许" : "❌ 不允许")}";
    }

    // 添加到消息列表
    _messages.Add(new ChatMessage
    {
        Role = "assistant",
        Content = content,
        CreatedAt = DateTime.Now
    });

    StateHasChanged();
    await Task.CompletedTask;
}

private async Task HandleWebSessionsCommand()
{
    if (_sessionManagementPanel != null)
    {
        await _sessionManagementPanel.ShowAsync();
    }

    // 显示提示消息
    _messages.Add(new ChatMessage
    {
        Role = "assistant",
        Content = "已打开会话管理面板",
        CreatedAt = DateTime.Now
    });

    StateHasChanged();
    await Task.CompletedTask;
}
```

**Step 3: 运行构建验证编译**

Run: `dotnet build WebCodeCli.sln`
Expected: 0 errors

**Step 4: Commit**

```bash
git add WebCodeCli.Domain/Domain/Service/CommandScannerService.cs
git add WebCodeCli/Pages/CodeAssistant.razor.cs
git commit -m "feat: add /webdir and /websessions built-in commands"
```

---

## Task 6: 测试和验证

**Step 1: 运行应用**

Run: `dotnet run --project WebCodeCli`
Expected: Application starts successfully

**Step 2: 测试功能**
1. 测试用户隔离：使用不同用户登录，确认只能看到自己的会话
2. 测试自定义目录：输入合法路径创建会话，确认工作目录正确
3. 测试路径校验：输入根目录（如 `c:\`）确认被拒绝
4. 测试移动端点：在移动端浏览器测试顶部下拉菜单
5. 测试命令：输入 `/webdir` 和 `/websessions` 确认正常工作

**Step 3: Commit final changes**

```bash
git add .
git commit -m "feat: complete web session management feature"
```

---

Plan complete and saved to `docs/plans/2026-03-10-web-session-management-implementation.md`. Two execution options:

**1. Subagent-Driven (this session)** - I dispatch fresh subagent per task, review between tasks, fast iteration

**2. Parallel Session (separate)** - Open new session with executing-plans, batch execution with checkpoints

Which approach?
