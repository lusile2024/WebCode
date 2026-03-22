# 工作区目录授权功能 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 实现自定义工作目录的用户绑定、授权、访问控制机制，支持目录所有者授权其他用户访问，以及敏感目录保护。

**Architecture:** 在现有WorkspaceService基础上扩展，新增两个数据库实体存储目录所有权和授权关系，修改路径验证逻辑增加权限检查，新增API接口提供授权管理功能，前端增加授权管理界面。

**Tech Stack:** .NET 10.0, SqlSugar ORM, Blazor Server, Tailwind CSS

---

## 任务列表

### Task 1: 新增数据库实体类

**Files:**
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/WorkspaceOwnerEntity.cs`
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/WorkspaceAuthorizationEntity.cs`
- Modify: `WebCodeCli.Domain/Common/Extensions/SqlSugarExtensions.cs` (新增表注册)

**Step 1: 创建 WorkspaceOwnerEntity 实体**

```csharp
using SqlSugar;

namespace WebCodeCli.Domain.Repositories.Base.Workspace;

/// <summary>
/// 工作区所有者实体
/// </summary>
[SugarTable("WorkspaceOwner")]
[SugarIndex("idx_directory_path", nameof(DirectoryPath), OrderByType.Asc, IsUnique = true)]
public class WorkspaceOwnerEntity
{
    /// <summary>
    /// 主键ID
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 目录绝对路径（规范化后）
    /// </summary>
    [SugarColumn(Length = 512, IsNullable = false)]
    public string DirectoryPath { get; set; } = string.Empty;

    /// <summary>
    /// 所有者用户名
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = false)]
    public string OwnerUsername { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间（首次注册时间）
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
```

**Step 2: 创建 WorkspaceAuthorizationEntity 实体**

```csharp
using SqlSugar;

namespace WebCodeCli.Domain.Repositories.Base.Workspace;

/// <summary>
/// 工作区授权实体
/// </summary>
[SugarTable("WorkspaceAuthorization")]
[SugarIndex("idx_directory_user", nameof(DirectoryPath), nameof(AuthorizedUsername), OrderByType.Asc, IsUnique = true)]
public class WorkspaceAuthorizationEntity
{
    /// <summary>
    /// 主键ID
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>
    /// 目录绝对路径（规范化后）
    /// </summary>
    [SugarColumn(Length = 512, IsNullable = false)]
    public string DirectoryPath { get; set; } = string.Empty;

    /// <summary>
    /// 被授权用户名
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = false)]
    public string AuthorizedUsername { get; set; } = string.Empty;

    /// <summary>
    /// 授权人用户名（目录所有者）
    /// </summary>
    [SugarColumn(Length = 128, IsNullable = false)]
    public string GrantedBy { get; set; } = string.Empty;

    /// <summary>
    /// 授权时间
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime GrantedAt { get; set; } = DateTime.Now;
}
```

**Step 3: 在 SqlSugar 配置中注册新表**
找到 `SqlSugarExtensions.cs` 中的 `AddSqlSugar` 方法，添加新实体：

```csharp
// 新增在现有实体注册之后
.InitTables<WorkspaceOwnerEntity>()
.InitTables<WorkspaceAuthorizationEntity>()
```

**Step 4: 运行项目验证表自动创建**
Run: `dotnet run --project WebCodeCli`
Expected: 数据库自动创建 `WorkspaceOwner` 和 `WorkspaceAuthorization` 两张表

**Step 5: Commit**
```bash
git add WebCodeCli.Domain/Repositories/Base/Workspace/ WebCodeCli.Domain/Common/Extensions/SqlSugarExtensions.cs
git commit -m "feat: add workspace authorization database entities"
```

---

### Task 2: 新增仓储接口和实现

**Files:**
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/IWorkspaceOwnerRepository.cs`
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/WorkspaceOwnerRepository.cs`
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/IWorkspaceAuthorizationRepository.cs`
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/WorkspaceAuthorizationRepository.cs`

**Step 1: 创建 IWorkspaceOwnerRepository 接口**

```csharp
namespace WebCodeCli.Domain.Repositories.Base.Workspace;

public interface IWorkspaceOwnerRepository : IBaseRepository<WorkspaceOwnerEntity>
{
    /// <summary>
    /// 根据目录路径获取所有者信息
    /// </summary>
    Task<WorkspaceOwnerEntity?> GetByDirectoryPathAsync(string normalizedPath);

    /// <summary>
    /// 获取用户拥有的所有目录
    /// </summary>
    Task<List<WorkspaceOwnerEntity>> GetByOwnerUsernameAsync(string username);
}
```

**Step 2: 创建 WorkspaceOwnerRepository 实现**

```csharp
using SqlSugar;
using WebCodeCli.Domain.Common.Attributes;

namespace WebCodeCli.Domain.Repositories.Base.Workspace;

[ServiceDescription(typeof(IWorkspaceOwnerRepository), ServiceLifetime.Scoped)]
public class WorkspaceOwnerRepository : BaseRepository<WorkspaceOwnerEntity>, IWorkspaceOwnerRepository
{
    public WorkspaceOwnerRepository(ISqlSugarClient db) : base(db)
    {
    }

    public async Task<WorkspaceOwnerEntity?> GetByDirectoryPathAsync(string normalizedPath)
    {
        return await GetFirstAsync(x => x.DirectoryPath == normalizedPath);
    }

    public async Task<List<WorkspaceOwnerEntity>> GetByOwnerUsernameAsync(string username)
    {
        return await GetListAsync(x => x.OwnerUsername == username);
    }
}
```

**Step 3: 创建 IWorkspaceAuthorizationRepository 接口**

```csharp
namespace WebCodeCli.Domain.Repositories.Base.Workspace;

public interface IWorkspaceAuthorizationRepository : IBaseRepository<WorkspaceAuthorizationEntity>
{
    /// <summary>
    /// 检查用户是否有目录访问权限
    /// </summary>
    Task<bool> CheckAuthorizationAsync(string normalizedPath, string username);

    /// <summary>
    /// 获取目录的所有授权用户
    /// </summary>
    Task<List<WorkspaceAuthorizationEntity>> GetByDirectoryPathAsync(string normalizedPath);

    /// <summary>
    /// 获取用户被授权的所有目录
    /// </summary>
    Task<List<WorkspaceAuthorizationEntity>> GetByAuthorizedUsernameAsync(string username);

    /// <summary>
    /// 删除授权记录
    /// </summary>
    Task<bool> DeleteAuthorizationAsync(string normalizedPath, string authorizedUsername);
}
```

**Step 4: 创建 WorkspaceAuthorizationRepository 实现**

```csharp
using SqlSugar;
using WebCodeCli.Domain.Common.Attributes;

namespace WebCodeCli.Domain.Repositories.Base.Workspace;

[ServiceDescription(typeof(IWorkspaceAuthorizationRepository), ServiceLifetime.Scoped)]
public class WorkspaceAuthorizationRepository : BaseRepository<WorkspaceAuthorizationEntity>, IWorkspaceAuthorizationRepository
{
    public WorkspaceAuthorizationRepository(ISqlSugarClient db) : base(db)
    {
    }

    public async Task<bool> CheckAuthorizationAsync(string normalizedPath, string username)
    {
        return await IsAnyAsync(x => x.DirectoryPath == normalizedPath && x.AuthorizedUsername == username);
    }

    public async Task<List<WorkspaceAuthorizationEntity>> GetByDirectoryPathAsync(string normalizedPath)
    {
        return await GetListAsync(x => x.DirectoryPath == normalizedPath);
    }

    public async Task<List<WorkspaceAuthorizationEntity>> GetByAuthorizedUsernameAsync(string username)
    {
        return await GetListAsync(x => x.AuthorizedUsername == username);
    }

    public async Task<bool> DeleteAuthorizationAsync(string normalizedPath, string authorizedUsername)
    {
        return await DeleteAsync(x => x.DirectoryPath == normalizedPath && x.AuthorizedUsername == authorizedUsername);
    }
}
```

**Step 5: 编译项目验证**
Run: `dotnet build WebCodeCli.sln`
Expected: 编译成功

**Step 6: Commit**
```bash
git add WebCodeCli.Domain/Repositories/Base/Workspace/
git commit -m "feat: add workspace authorization repositories"
```

---

### Task 3: 扩展 IWorkspaceService 接口

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/IWorkspaceService.cs`

**Step 1: 添加授权相关方法定义**

```csharp
/// <summary>
/// 检查用户对目录是否有访问权限
/// </summary>
/// <param name="directoryPath">目录路径</param>
/// <param name="username">用户名</param>
/// <returns>(是否有权限, 错误信息)</returns>
Task<(bool hasPermission, string errorMessage)> CheckDirectoryPermissionAsync(string directoryPath, string username);

/// <summary>
/// 授权用户访问目录（仅目录所有者可调用）
/// </summary>
/// <param name="directoryPath">目录路径</param>
/// <param name="ownerUsername">所有者用户名</param>
/// <param name="authorizedUsername">被授权用户名</param>
/// <returns>(是否成功, 错误信息)</returns>
Task<(bool success, string errorMessage)> AuthorizeUserAsync(string directoryPath, string ownerUsername, string authorizedUsername);

/// <summary>
/// 取消用户对目录的访问授权（仅目录所有者可调用）
/// </summary>
/// <param name="directoryPath">目录路径</param>
/// <param name="ownerUsername">所有者用户名</param>
/// <param name="authorizedUsername">被授权用户名</param>
/// <returns>(是否成功, 错误信息)</returns>
Task<(bool success, string errorMessage)> RevokeAuthorizationAsync(string directoryPath, string ownerUsername, string authorizedUsername);

/// <summary>
/// 获取用户拥有的所有目录
/// </summary>
Task<List<string>> GetOwnedDirectoriesAsync(string username);

/// <summary>
/// 获取用户被授权的所有目录
/// </summary>
Task<List<string>> GetAuthorizedDirectoriesAsync(string username);

/// <summary>
/// 获取目录的所有授权用户（仅目录所有者可调用）
/// </summary>
Task<(List<string> authorizedUsers, string errorMessage)> GetDirectoryAuthorizedUsersAsync(string directoryPath, string ownerUsername);
```

**Step 2: 编译项目验证**
Run: `dotnet build WebCodeCli.Domain`
Expected: 编译成功

**Step 3: Commit**
```bash
git add WebCodeCli.Domain/Domain/Service/IWorkspaceService.cs
git commit -m "feat: extend IWorkspaceService with authorization methods"
```

---

### Task 4: 实现 WorkspaceService 授权逻辑

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/WorkspaceService.cs`

**Step 1: 添加依赖注入**
在构造函数中新增仓储依赖：

```csharp
private readonly IWorkspaceOwnerRepository _workspaceOwnerRepository;
private readonly IWorkspaceAuthorizationRepository _workspaceAuthorizationRepository;

public WorkspaceService(
    ILogger<WorkspaceService> logger,
    IUserContextService userContextService,
    IOptions<FeishuOptions> feishuOptions,
    IWorkspaceOwnerRepository workspaceOwnerRepository,
    IWorkspaceAuthorizationRepository workspaceAuthorizationRepository)
{
    _logger = logger;
    _userContextService = userContextService;
    _feishuOptions = feishuOptions;
    _workspaceOwnerRepository = workspaceOwnerRepository;
    _workspaceAuthorizationRepository = workspaceAuthorizationRepository;

    // ... 原有初始化逻辑 ...
}
```

**Step 2: 新增路径规范化私有方法**

```csharp
/// <summary>
/// 规范化路径：转为绝对路径、统一分隔符、小写、去除末尾分隔符
/// </summary>
private string NormalizePath(string path)
{
    if (string.IsNullOrWhiteSpace(path))
        return string.Empty;

    // 转为绝对路径
    var absolutePath = Path.GetFullPath(path);
    // 统一分隔符为 /
    var normalized = absolutePath.Replace('\\', '/').TrimEnd('/');
    // 转为小写
    return normalized.ToLowerInvariant();
}
```

**Step 3: 新增敏感目录检查私有方法**

```csharp
/// <summary>
/// 检查是否为敏感系统目录
/// </summary>
private bool IsSensitiveSystemDirectory(string normalizedPath)
{
    var sensitiveDirectories = new List<string>
    {
        // Windows 敏感目录
        "c:/windows",
        "c:/program files",
        "c:/program files (x86)",
        "c:/programdata",
        "c:/users/public",
        "c:/recovery",
        "c:/system volume information",

        // Linux/macOS 敏感目录
        "/etc",
        "/root",
        "/bin",
        "/sbin",
        "/usr/bin",
        "/usr/sbin",
        "/var",
        "/sys",
        "/proc"
    };

    // 检查路径是否以敏感目录开头
    return sensitiveDirectories.Any(dir =>
        normalizedPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase) &&
        (normalizedPath.Length == dir.Length || normalizedPath[dir.Length] == '/'));
}
```

**Step 4: 实现 CheckDirectoryPermissionAsync 方法**

```csharp
public async Task<(bool hasPermission, string errorMessage)> CheckDirectoryPermissionAsync(string directoryPath, string username)
{
    if (string.IsNullOrWhiteSpace(directoryPath))
        return (false, "目录路径不能为空");

    if (string.IsNullOrWhiteSpace(username))
        return (false, "用户名不能为空");

    var normalizedPath = NormalizePath(directoryPath);

    // 检查敏感目录
    if (IsSensitiveSystemDirectory(normalizedPath))
        return (false, "禁止访问系统敏感目录");

    // 查询目录所有者
    var owner = await _workspaceOwnerRepository.GetByDirectoryPathAsync(normalizedPath);

    // 目录未注册，允许首次使用，自动注册为所有者
    if (owner == null)
    {
        await _workspaceOwnerRepository.InsertAsync(new WorkspaceOwnerEntity
        {
            DirectoryPath = normalizedPath,
            OwnerUsername = username,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        });
        return (true, string.Empty);
    }

    // 是所有者，直接允许
    if (owner.OwnerUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
        return (true, string.Empty);

    // 检查是否已授权
    var isAuthorized = await _workspaceAuthorizationRepository.CheckAuthorizationAsync(normalizedPath, username);
    if (isAuthorized)
        return (true, string.Empty);

    return (false, $"您没有目录 {directoryPath} 的访问权限，请联系目录所有者 {owner.OwnerUsername} 授权");
}
```

**Step 5: 实现 AuthorizeUserAsync 方法**

```csharp
public async Task<(bool success, string errorMessage)> AuthorizeUserAsync(string directoryPath, string ownerUsername, string authorizedUsername)
{
    if (string.IsNullOrWhiteSpace(directoryPath))
        return (false, "目录路径不能为空");

    if (string.IsNullOrWhiteSpace(ownerUsername))
        return (false, "所有者用户名不能为空");

    if (string.IsNullOrWhiteSpace(authorizedUsername))
        return (false, "被授权用户名不能为空");

    if (ownerUsername.Equals(authorizedUsername, StringComparison.OrdinalIgnoreCase))
        return (false, "不能对自己进行授权");

    var normalizedPath = NormalizePath(directoryPath);

    // 检查是否为所有者
    var owner = await _workspaceOwnerRepository.GetByDirectoryPathAsync(normalizedPath);
    if (owner == null)
        return (false, "目录不存在或未注册");

    if (!owner.OwnerUsername.Equals(ownerUsername, StringComparison.OrdinalIgnoreCase))
        return (false, "只有目录所有者可以进行授权操作");

    // 检查是否已经授权
    var existingAuthorization = await _workspaceAuthorizationRepository.CheckAuthorizationAsync(normalizedPath, authorizedUsername);
    if (existingAuthorization)
        return (false, "该用户已经拥有此目录的访问权限");

    // 添加授权记录
    await _workspaceAuthorizationRepository.InsertAsync(new WorkspaceAuthorizationEntity
    {
        DirectoryPath = normalizedPath,
        AuthorizedUsername = authorizedUsername,
        GrantedBy = ownerUsername,
        GrantedAt = DateTime.Now
    });

    _logger.LogInformation("用户 {Owner} 授权 {User} 访问目录 {Path}", ownerUsername, authorizedUsername, directoryPath);
    return (true, string.Empty);
}
```

**Step 6: 实现 RevokeAuthorizationAsync 方法**

```csharp
public async Task<(bool success, string errorMessage)> RevokeAuthorizationAsync(string directoryPath, string ownerUsername, string authorizedUsername)
{
    if (string.IsNullOrWhiteSpace(directoryPath))
        return (false, "目录路径不能为空");

    if (string.IsNullOrWhiteSpace(ownerUsername))
        return (false, "所有者用户名不能为空");

    if (string.IsNullOrWhiteSpace(authorizedUsername))
        return (false, "被授权用户名不能为空");

    var normalizedPath = NormalizePath(directoryPath);

    // 检查是否为所有者
    var owner = await _workspaceOwnerRepository.GetByDirectoryPathAsync(normalizedPath);
    if (owner == null)
        return (false, "目录不存在或未注册");

    if (!owner.OwnerUsername.Equals(ownerUsername, StringComparison.OrdinalIgnoreCase))
        return (false, "只有目录所有者可以进行取消授权操作");

    // 删除授权记录
    var deleted = await _workspaceAuthorizationRepository.DeleteAuthorizationAsync(normalizedPath, authorizedUsername);
    if (!deleted)
        return (false, "该用户没有此目录的访问授权");

    _logger.LogInformation("用户 {Owner} 取消了 {User} 对目录 {Path} 的访问权限", ownerUsername, authorizedUsername, directoryPath);
    return (true, string.Empty);
}
```

**Step 7: 实现其他辅助方法**

```csharp
public async Task<List<string>> GetOwnedDirectoriesAsync(string username)
{
    if (string.IsNullOrWhiteSpace(username))
        return new List<string>();

    var owners = await _workspaceOwnerRepository.GetByOwnerUsernameAsync(username);
    return owners.Select(x => x.DirectoryPath).ToList();
}

public async Task<List<string>> GetAuthorizedDirectoriesAsync(string username)
{
    if (string.IsNullOrWhiteSpace(username))
        return new List<string>();

    var authorizations = await _workspaceAuthorizationRepository.GetByAuthorizedUsernameAsync(username);
    return authorizations.Select(x => x.DirectoryPath).ToList();
}

public async Task<(List<string> authorizedUsers, string errorMessage)> GetDirectoryAuthorizedUsersAsync(string directoryPath, string ownerUsername)
{
    if (string.IsNullOrWhiteSpace(directoryPath))
        return (new List<string>(), "目录路径不能为空");

    if (string.IsNullOrWhiteSpace(ownerUsername))
        return (new List<string>(), "所有者用户名不能为空");

    var normalizedPath = NormalizePath(directoryPath);

    // 检查是否为所有者
    var owner = await _workspaceOwnerRepository.GetByDirectoryPathAsync(normalizedPath);
    if (owner == null)
        return (new List<string>(), "目录不存在或未注册");

    if (!owner.OwnerUsername.Equals(ownerUsername, StringComparison.OrdinalIgnoreCase))
        return (new List<string>(), "只有目录所有者可以查看授权列表");

    var authorizations = await _workspaceAuthorizationRepository.GetByDirectoryPathAsync(normalizedPath);
    var authorizedUsers = authorizations.Select(x => x.AuthorizedUsername).ToList();

    return (authorizedUsers, string.Empty);
}
```

**Step 8: 修改 ValidatePath 方法，集成权限检查**

```csharp
public (bool isValid, string errorMessage) ValidatePath(string path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return (false, "路径不能为空");
    }

    // 原有路径安全检查
    var allowedPaths = GetAllowedPaths();
    if (!PathSecurityHelper.IsPathSafe(path, allowedPaths, out var errorMessage))
    {
        return (false, errorMessage);
    }

    // 同步调用权限检查（注意：这是临时方案，后续应该在调用处使用异步版本）
    var username = _userContextService.GetCurrentUsername();
    var checkTask = CheckDirectoryPermissionAsync(path, username);
    checkTask.Wait();

    if (!checkTask.Result.hasPermission)
    {
        return (false, checkTask.Result.errorMessage);
    }

    return (true, string.Empty);
}
```

**Step 9: 修改 GetOrCreateWorkspace 方法，保存自定义路径到会话**
在方法末尾添加：

```csharp
// 保存自定义路径到会话
if (!string.IsNullOrWhiteSpace(customPath))
{
    var sessionId = _userContextService.GetCurrentSessionId();
    // 这里需要调用 IChatSessionRepository 更新会话的 WorkspacePath
    // 后续Task会实现这部分逻辑
}
```

**Step 10: 编译项目验证**
Run: `dotnet build WebCodeCli.sln`
Expected: 编译成功

**Step 11: Commit**
```bash
git add WebCodeCli.Domain/Domain/Service/WorkspaceService.cs
git commit -m "feat: implement workspace authorization logic in WorkspaceService"
```

---

### Task 5: 实现 API 接口

**Files:**
- Modify: `WebCodeCli/Controllers/WorkspaceController.cs`

**Step 1: 添加授权相关接口**

```csharp
/// <summary>
/// 授权用户访问目录
/// </summary>
[HttpPost("authorize")]
public async Task<IActionResult> AuthorizeUser([FromBody] AuthorizeUserRequest request)
{
    var currentUsername = _userContextService.GetCurrentUsername();
    var (success, errorMessage) = await _workspaceService.AuthorizeUserAsync(
        request.DirectoryPath,
        currentUsername,
        request.AuthorizedUsername);

    if (!success)
        return BadRequest(new { Message = errorMessage });

    return Ok(new { Success = true });
}

/// <summary>
/// 取消用户访问授权
/// </summary>
[HttpPost("revoke-authorization")]
public async Task<IActionResult> RevokeAuthorization([FromBody] RevokeAuthorizationRequest request)
{
    var currentUsername = _userContextService.GetCurrentUsername();
    var (success, errorMessage) = await _workspaceService.RevokeAuthorizationAsync(
        request.DirectoryPath,
        currentUsername,
        request.AuthorizedUsername);

    if (!success)
        return BadRequest(new { Message = errorMessage });

    return Ok(new { Success = true });
}

/// <summary>
/// 获取我拥有的目录列表
/// </summary>
[HttpGet("my-owned-directories")]
public async Task<IActionResult> GetMyOwnedDirectories()
{
    var currentUsername = _userContextService.GetCurrentUsername();
    var directories = await _workspaceService.GetOwnedDirectoriesAsync(currentUsername);
    return Ok(directories);
}

/// <summary>
/// 获取我被授权的目录列表
/// </summary>
[HttpGet("my-authorized-directories")]
public async Task<IActionResult> GetMyAuthorizedDirectories()
{
    var currentUsername = _userContextService.GetCurrentUsername();
    var directories = await _workspaceService.GetAuthorizedDirectoriesAsync(currentUsername);
    return Ok(directories);
}

/// <summary>
/// 获取目录的授权用户列表
/// </summary>
[HttpGet("directory-authorizations")]
public async Task<IActionResult> GetDirectoryAuthorizations([FromQuery] string directoryPath)
{
    var currentUsername = _userContextService.GetCurrentUsername();
    var (authorizedUsers, errorMessage) = await _workspaceService.GetDirectoryAuthorizedUsersAsync(
        directoryPath,
        currentUsername);

    if (!string.IsNullOrEmpty(errorMessage))
        return BadRequest(new { Message = errorMessage });

    return Ok(authorizedUsers);
}

// 请求模型定义
public class AuthorizeUserRequest
{
    public string DirectoryPath { get; set; } = string.Empty;
    public string AuthorizedUsername { get; set; } = string.Empty;
}

public class RevokeAuthorizationRequest
{
    public string DirectoryPath { get; set; } = string.Empty;
    public string AuthorizedUsername { get; set; } = string.Empty;
}
```

**Step 2: 编译项目验证**
Run: `dotnet build WebCodeCli`
Expected: 编译成功

**Step 3: Commit**
```bash
git add WebCodeCli/Controllers/WorkspaceController.cs
git commit -m "feat: add workspace authorization API endpoints"
```

---

### Task 6: 会话工作目录关联实现

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/WorkspaceService.cs`
- Modify: `WebCodeCli/Controllers/SessionController.cs`
- Modify: `WebCodeCli/Pages/CodeAssistant.razor.cs`

**Step 1: 在 WorkspaceService 中注入 IChatSessionRepository**
构造函数添加：
```csharp
private readonly IChatSessionRepository _chatSessionRepository;
```

更新构造函数参数列表，在 GetOrCreateWorkspace 方法末尾添加：
```csharp
// 保存自定义路径到会话
if (!string.IsNullOrWhiteSpace(customPath))
{
    var session = await _chatSessionRepository.GetByIdAsync(sessionId);
    if (session != null)
    {
        session.WorkspacePath = normalizedPath;
        session.IsCustomWorkspace = true;
        session.UpdatedAt = DateTime.Now;
        await _chatSessionRepository.UpdateAsync(session);
    }
}
```

**Step 2: 在会话切换时加载工作目录**
在 SessionController 的切换会话接口中，添加加载 WorkspacePath 到用户上下文的逻辑。

**Step 3: 在 CodeAssistant.razor.cs 中加载会话工作目录**
在页面初始化和会话切换时，将会话的 WorkspacePath 传递给 WorkspaceService。

**Step 4: 编译项目验证**
Run: `dotnet build WebCodeCli.sln`
Expected: 编译成功

**Step 5: Commit**
```bash
git add WebCodeCli.Domain/Domain/Service/WorkspaceService.cs WebCodeCli/Controllers/SessionController.cs WebCodeCli/Pages/CodeAssistant.razor.cs
git commit -m "feat: link workspace directory with session"
```

---

### Task 7: 前端授权管理界面实现

**Files:**
- Create: `WebCodeCli/Components/WorkspaceAuthorizationModal.razor`
- Modify: `WebCodeCli/Pages/CodeAssistant.razor`

**Step 1: 创建授权管理弹窗组件**
实现目录授权、取消授权、查看授权列表的UI。

**Step 2: 在主界面集成授权管理入口**
在会话设置或工作区设置中添加授权管理按钮。

**Step 3: 测试前端界面**
Run: `dotnet run --project WebCodeCli`
Expected: 界面正常显示，授权功能可正常使用

**Step 4: Commit**
```bash
git add WebCodeCli/Components/WorkspaceAuthorizationModal.razor WebCodeCli/Pages/CodeAssistant.razor
git commit -m "feat: add workspace authorization UI"
```

---

### Task 8: 功能测试和验证

**Files:**
- Create: `tests/WebCodeCli.Domain.Tests/Service/WorkspaceServiceAuthorizationTests.cs`

**Step 1: 编写单元测试**
覆盖所有授权相关的业务逻辑，包括：
- 目录首次使用自动注册所有者
- 敏感目录访问拒绝
- 授权和取消授权功能
- 权限检查逻辑

**Step 2: 运行单元测试**
Run: `dotnet test tests/WebCodeCli.Domain.Tests/`
Expected: 所有测试通过

**Step 3: 集成测试**
手动测试完整流程：
1. 用户A创建会话使用自定义目录D:/TestProject
2. 用户B尝试使用同一目录，被拒绝访问
3. 用户A授权用户B访问该目录
4. 用户B可以正常使用该目录
5. 用户A取消用户B的授权
6. 用户B再次被拒绝访问
7. 尝试访问系统敏感目录，被拒绝

**Step 4: Commit**
```bash
git add tests/WebCodeCli.Domain.Tests/Service/WorkspaceServiceAuthorizationTests.cs
git commit -m "test: add workspace authorization unit tests"
```
