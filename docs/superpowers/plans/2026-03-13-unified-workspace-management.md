# 统一工作区管理架构 Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现完整的统一工作区管理架构，支持目录授权、多会话共享、权限控制，完全替换旧的WorkspaceService，不影响现有功能正常运行。

**重要约束：**
- 会话创建后**不允许切换**工作目录（工作区与会话绑定）
- 授权管理入口在**会话列表**中，通过会话项的操作按钮访问

**Architecture:** 采用三层核心架构设计：
1. 目录注册表层：统一管理所有工作区目录的元数据、所有者信息
2. 授权服务层：管理目录的用户授权、权限校验、授权生命周期
3. 会话目录关联层：管理会话与工作区目录的绑定关系（创建时绑定，不允许切换）

**完全丢弃旧的WorkspaceService实现，所有现有功能迁移到新架构。**

**Tech Stack:** .NET 10.0 + Blazor Server + SqlSugar ORM + MySQL + Playwright

---

## File Structure Mapping

### 新增文件
| 文件路径 | 职责 |
|---------|------|
| `WebCodeCli.Domain/Repositories/Base/Workspace/WorkspaceDirectoryEntity.cs` | 工作区目录实体 |
| `WebCodeCli.Domain/Repositories/Base/Workspace/WorkspaceAuthorizationEntity.cs` | 目录授权实体 |
| `WebCodeCli.Domain/Repositories/Base/Workspace/SessionDirectoryMappingEntity.cs` | 会话-目录映射实体 |
| `WebCodeCli.Domain/Repositories/Base/Workspace/IWorkspaceDirectoryRepository.cs` | 目录仓储接口 |
| `WebCodeCli.Domain/Repositories/Base/Workspace/WorkspaceDirectoryRepository.cs` | 目录仓储实现 |
| `WebCodeCli.Domain/Repositories/Base/Workspace/IWorkspaceAuthorizationRepository.cs` | 授权仓储接口 |
| `WebCodeCli.Domain/Repositories/Base/Workspace/WorkspaceAuthorizationRepository.cs` | 授权仓储实现 |
| `WebCodeCli.Domain/Repositories/Base/Workspace/ISessionDirectoryMappingRepository.cs` | 映射仓储接口 |
| `WebCodeCli.Domain/Repositories/Base/Workspace/SessionDirectoryMappingRepository.cs` | 映射仓储实现 |
| `WebCodeCli.Domain/Domain/Service/IWorkspaceRegistryService.cs` | 目录注册表服务接口 |
| `WebCodeCli.Domain/Domain/Service/WorkspaceRegistryService.cs` | 目录注册表服务实现 |
| `WebCodeCli.Domain/Domain/Service/IWorkspaceAuthorizationService.cs` | 授权服务接口 |
| `WebCodeCli.Domain/Domain/Service/WorkspaceAuthorizationService.cs` | 授权服务实现 |
| `WebCodeCli.Domain/Domain/Service/ISessionDirectoryService.cs` | 会话目录服务接口 |
| `WebCodeCli.Domain/Domain/Service/SessionDirectoryService.cs` | 会话目录服务实现 |
| `WebCodeCli/Components/Dialogs/WorkspaceAuthorizationModal.razor` | 目录授权模态框 |
| `tests/web-workspace-management.spec.ts` | Playwright端到端测试用例 |

### 修改文件
| 文件路径 | 修改内容 |
|---------|----------|
| `WebCodeCli.Domain/Repositories/Base/ChatSession/ChatSessionEntity.cs` | 扩展字段支持目录关联 |
| `WebCodeCli.Domain/Common/Extensions/DatabaseInitializer.cs` | 添加新表的CodeFirst初始化 |
| `WebCodeCli.Domain/Domain/Service/CliExecutorService.cs` | 迁移到新工作区架构 |
| `WebCodeCli.Domain/Domain/Service/SessionHistoryManager.cs` | 迁移到新工作区架构 |
| `WebCodeCli.Domain/Domain/Service/WorkspaceCleanupBackgroundService.cs` | 迁移到新工作区架构 |
| `WebCodeCli/Controllers/WorkspaceController.cs` | 扩展API支持新架构 |
| `WebCodeCli/Controllers/SessionController.cs` | 扩展API支持新架构 |
| `WebCodeCli/Components/SessionListPanel.razor` | 显示工作区信息，添加授权管理入口 |
| `WebCodeCli/Components/Dialogs/CreateSessionModal.razor` | 支持目录选择 |
| `WebCodeCli/Components/Dialogs/CreateSessionModal.razor.cs` | 添加目录选择逻辑 |

### 删除文件
| 文件路径 | 原因 |
|---------|------|
| `WebCodeCli.Domain/Domain/Service/IWorkspaceService.cs` | 旧接口废弃 |
| `WebCodeCli.Domain/Domain/Service/WorkspaceService.cs` | 旧实现废弃 |

---

## Chunk 1: 数据层实现

### Task 1: 创建工作区相关实体

**Files:**
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/WorkspaceDirectoryEntity.cs`
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/WorkspaceAuthorizationEntity.cs`
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/SessionDirectoryMappingEntity.cs`
- Modify: `WebCodeCli.Domain/Repositories/Base/ChatSession/ChatSessionEntity.cs`

- [ ] **Step 1: 创建WorkspaceDirectoryEntity**

```csharp
using SqlSugar;
using System;

namespace WebCodeCli.Domain.Repositories.Base.Workspace
{
    /// <summary>
    /// 工作区目录实体
    /// </summary>
    [SugarTable("WorkspaceDirectory")]
    [SugarIndex("idx_directory_path", nameof(DirectoryPath), OrderByType.Asc, IsUnique = true)]
    public class WorkspaceDirectoryEntity
    {
        /// <summary>
        /// 目录ID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long DirectoryId { get; set; }

        /// <summary>
        /// 规范化后的绝对路径
        /// </summary>
        [SugarColumn(Length = 512, IsNullable = false)]
        public string DirectoryPath { get; set; } = string.Empty;

        /// <summary>
        /// 目录名称
        /// </summary>
        [SugarColumn(Length = 128, IsNullable = false)]
        public string DirectoryName { get; set; } = string.Empty;

        /// <summary>
        /// 目录类型：Default/Custom/Project/Shared
        /// </summary>
        [SugarColumn(Length = 32, IsNullable = false)]
        public string DirectoryType { get; set; } = "Custom";

        /// <summary>
        /// 所有者用户名
        /// </summary>
        [SugarColumn(Length = 128, IsNullable = false)]
        public string OwnerUsername { get; set; } = string.Empty;

        /// <summary>
        /// 是否有效
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public bool IsValid { get; set; } = true;

        /// <summary>
        /// 创建时间
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
```

- [ ] **Step 2: 创建WorkspaceAuthorizationEntity**

```csharp
using SqlSugar;
using System;

namespace WebCodeCli.Domain.Repositories.Base.Workspace
{
    /// <summary>
    /// 工作区授权实体
    /// </summary>
    [SugarTable("WorkspaceAuthorization")]
    [SugarIndex("idx_directory_user", nameof(DirectoryId), nameof(AuthorizedUsername), OrderByType.Asc, IsUnique = true)]
    public class WorkspaceAuthorizationEntity
    {
        /// <summary>
        /// 授权ID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long AuthorizationId { get; set; }

        /// <summary>
        /// 目录ID
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public long DirectoryId { get; set; }

        /// <summary>
        /// 被授权用户名
        /// </summary>
        [SugarColumn(Length = 128, IsNullable = false)]
        public string AuthorizedUsername { get; set; } = string.Empty;

        /// <summary>
        /// 权限级别：ReadOnly/ReadWrite/Owner
        /// </summary>
        [SugarColumn(Length = 32, IsNullable = false)]
        public string PermissionLevel { get; set; } = "ReadWrite";

        /// <summary>
        /// 授权人用户名
        /// </summary>
        [SugarColumn(Length = 128, IsNullable = false)]
        public string GrantedBy { get; set; } = string.Empty;

        /// <summary>
        /// 授权时间
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public DateTime GrantedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 过期时间（null表示永不过期）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? ExpiresAt { get; set; }
    }
}
```

- [ ] **Step 3: 创建SessionDirectoryMappingEntity**

```csharp
using SqlSugar;
using System;

namespace WebCodeCli.Domain.Repositories.Base.Workspace
{
    /// <summary>
    /// 会话-目录映射实体
    /// </summary>
    [SugarTable("SessionDirectoryMapping")]
    [SugarIndex("idx_session_id", nameof(SessionId), OrderByType.Asc, IsUnique = true)]
    public class SessionDirectoryMappingEntity
    {
        /// <summary>
        /// 映射ID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long MappingId { get; set; }

        /// <summary>
        /// 会话ID
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = false)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 目录ID
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public long DirectoryId { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
```

- [ ] **Step 4: 扩展ChatSessionEntity**
在现有ChatSessionEntity中添加字段：

```csharp
/// <summary>
/// 关联的目录ID（null表示使用默认临时目录）
/// </summary>
[SugarColumn(IsNullable = true)]
public long? DirectoryId { get; set; }
```

- [ ] **Step 5: 编译验证实体定义**
Run: `dotnet build WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 编译成功，无错误

---

### Task 2: 实现仓储层

**Files:**
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/IWorkspaceDirectoryRepository.cs`
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/WorkspaceDirectoryRepository.cs`
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/IWorkspaceAuthorizationRepository.cs`
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/WorkspaceAuthorizationRepository.cs`
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/ISessionDirectoryMappingRepository.cs`
- Create: `WebCodeCli.Domain/Repositories/Base/Workspace/SessionDirectoryMappingRepository.cs`

- [ ] **Step 1: 创建IWorkspaceDirectoryRepository**

```csharp
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.Workspace
{
    public interface IWorkspaceDirectoryRepository : IBaseRepository<WorkspaceDirectoryEntity>
    {
    }
}
```

- [ ] **Step 2: 创建WorkspaceDirectoryRepository**

```csharp
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.Workspace
{
    [ServiceDescription(typeof(IWorkspaceDirectoryRepository), ServiceLifetime.Scoped)]
    public class WorkspaceDirectoryRepository : BaseRepository<WorkspaceDirectoryEntity>, IWorkspaceDirectoryRepository
    {
        public WorkspaceDirectoryRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}
```

- [ ] **Step 3: 创建IWorkspaceAuthorizationRepository**

```csharp
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.Workspace
{
    public interface IWorkspaceAuthorizationRepository : IBaseRepository<WorkspaceAuthorizationEntity>
    {
    }
}
```

- [ ] **Step 4: 创建WorkspaceAuthorizationRepository**

```csharp
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.Workspace
{
    [ServiceDescription(typeof(IWorkspaceAuthorizationRepository), ServiceLifetime.Scoped)]
    public class WorkspaceAuthorizationRepository : BaseRepository<WorkspaceAuthorizationEntity>, IWorkspaceAuthorizationRepository
    {
        public WorkspaceAuthorizationRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}
```

- [ ] **Step 5: 创建ISessionDirectoryMappingRepository**

```csharp
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.Workspace
{
    public interface ISessionDirectoryMappingRepository : IBaseRepository<SessionDirectoryMappingEntity>
    {
    }
}
```

- [ ] **Step 6: 创建SessionDirectoryMappingRepository**

```csharp
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base;

namespace WebCodeCli.Domain.Repositories.Base.Workspace
{
    [ServiceDescription(typeof(ISessionDirectoryMappingRepository), ServiceLifetime.Scoped)]
    public class SessionDirectoryMappingRepository : BaseRepository<SessionDirectoryMappingEntity>, ISessionDirectoryMappingRepository
    {
        public SessionDirectoryMappingRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}
```

- [ ] **Step 7: 编译验证仓储实现**
Run: `dotnet build WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 编译成功，无错误

---

### Task 3: 配置数据库自动初始化

**Files:**
- Modify: `WebCodeCli.Domain/Common/Extensions/DatabaseInitializer.cs`

- [ ] **Step 1: 添加新表初始化逻辑**
在InitDatabase方法中添加：

```csharp
// 初始化工作区相关表
db.CodeFirst.InitTables<WorkspaceDirectoryEntity>();
db.CodeFirst.InitTables<WorkspaceAuthorizationEntity>();
db.CodeFirst.InitTables<SessionDirectoryMappingEntity>();

// 更新ChatSession表结构
db.CodeFirst.InitTables<ChatSessionEntity>();
```

- [ ] **Step 2: 添加必要的using引用**
```csharp
using WebCodeCli.Domain.Repositories.Base.Workspace;
```

- [ ] **Step 3: 编译验证数据库初始化配置**
Run: `dotnet build WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 编译成功，无错误

---

## Chunk 2: 核心服务实现

### Task 4: 实现目录注册表服务

**Files:**
- Create: `WebCodeCli.Domain/Domain/Service/IWorkspaceRegistryService.cs`
- Create: `WebCodeCli.Domain/Domain/Service/WorkspaceRegistryService.cs`

- [ ] **Step 1: 创建IWorkspaceRegistryService接口**

```csharp
using WebCodeCli.Domain.Repositories.Base.Workspace;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebCodeCli.Domain.Domain.Service
{
    public interface IWorkspaceRegistryService
    {
        /// <summary>
        /// 注册新目录
        /// </summary>
        Task<WorkspaceDirectoryEntity> RegisterDirectoryAsync(string directoryPath, string directoryName, string directoryType, string ownerUsername);

        /// <summary>
        /// 根据路径获取目录信息
        /// </summary>
        Task<WorkspaceDirectoryEntity?> GetDirectoryByPathAsync(string directoryPath);

        /// <summary>
        /// 根据ID获取目录信息
        /// </summary>
        Task<WorkspaceDirectoryEntity?> GetDirectoryByIdAsync(long directoryId);

        /// <summary>
        /// 获取用户拥有的所有目录
        /// </summary>
        Task<List<WorkspaceDirectoryEntity>> GetUserOwnedDirectoriesAsync(string username);

        /// <summary>
        /// 更新目录信息
        /// </summary>
        Task UpdateDirectoryAsync(WorkspaceDirectoryEntity directory);

        /// <summary>
        /// 删除目录
        /// </summary>
        Task DeleteDirectoryAsync(long directoryId);

        /// <summary>
        /// 规范化路径
        /// </summary>
        string NormalizePath(string path);

        /// <summary>
        /// 检查是否为敏感系统目录
        /// </summary>
        bool IsSensitiveDirectory(string path);
    }
}
```

- [ ] **Step 2: 创建WorkspaceRegistryService实现**

```csharp
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base.Workspace;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WebCodeCli.Domain.Domain.Service
{
    [ServiceDescription(typeof(IWorkspaceRegistryService), ServiceLifetime.Singleton)]
    public class WorkspaceRegistryService : IWorkspaceRegistryService
    {
        private readonly ILogger<WorkspaceRegistryService> _logger;
        private readonly IWorkspaceDirectoryRepository _directoryRepository;

        // 敏感系统目录列表
        private readonly List<string> _sensitiveDirectories = new()
        {
            "/windows", "/program files", "/program files (x86)", "/system32",
            "/etc", "/root", "/bin", "/sbin", "/usr/bin", "/usr/sbin"
        };

        public WorkspaceRegistryService(
            ILogger<WorkspaceRegistryService> logger,
            IWorkspaceDirectoryRepository directoryRepository)
        {
            _logger = logger;
            _directoryRepository = directoryRepository;
        }

        public async Task<WorkspaceDirectoryEntity> RegisterDirectoryAsync(string directoryPath, string directoryName, string directoryType, string ownerUsername)
        {
            var normalizedPath = NormalizePath(directoryPath);

            // 检查是否为敏感目录
            if (IsSensitiveDirectory(normalizedPath))
            {
                throw new UnauthorizedAccessException("禁止访问系统敏感目录");
            }

            // 检查目录是否已存在
            var existingDir = await GetDirectoryByPathAsync(normalizedPath);
            if (existingDir != null)
            {
                return existingDir;
            }

            // 创建新目录记录
            var directory = new WorkspaceDirectoryEntity
            {
                DirectoryPath = normalizedPath,
                DirectoryName = string.IsNullOrEmpty(directoryName) ? Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar)) : directoryName,
                DirectoryType = directoryType,
                OwnerUsername = ownerUsername,
                IsValid = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            await _directoryRepository.InsertAsync(directory);
            _logger.LogInformation("目录 {Path} 已注册，所有者：{Owner}", normalizedPath, ownerUsername);

            return directory;
        }

        public async Task<WorkspaceDirectoryEntity?> GetDirectoryByPathAsync(string directoryPath)
        {
            var normalizedPath = NormalizePath(directoryPath);
            return await _directoryRepository.GetFirstAsync(d => d.DirectoryPath == normalizedPath && d.IsValid);
        }

        public async Task<WorkspaceDirectoryEntity?> GetDirectoryByIdAsync(long directoryId)
        {
            return await _directoryRepository.GetByIdAsync(directoryId);
        }

        public async Task<List<WorkspaceDirectoryEntity>> GetUserOwnedDirectoriesAsync(string username)
        {
            return await _directoryRepository.GetListAsync(d => d.OwnerUsername == username && d.IsValid);
        }

        public async Task UpdateDirectoryAsync(WorkspaceDirectoryEntity directory)
        {
            directory.UpdatedAt = DateTime.Now;
            await _directoryRepository.UpdateAsync(directory);
        }

        public async Task DeleteDirectoryAsync(long directoryId)
        {
            var directory = await GetDirectoryByIdAsync(directoryId);
            if (directory != null)
            {
                directory.IsValid = false;
                directory.UpdatedAt = DateTime.Now;
                await _directoryRepository.UpdateAsync(directory);
            }
        }

        public string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            // 统一路径分隔符
            path = path.Replace('\\', '/').Trim();

            // Windows路径处理
            if (Path.IsPathRooted(path) && path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
            {
                // 转换为小写盘符
                path = char.ToLower(path[0]) + path.Substring(1);
            }

            // 解析相对路径
            path = Path.GetFullPath(path).Replace('\\', '/');

            // 去除末尾分隔符
            path = path.TrimEnd('/');

            return path.ToLowerInvariant();
        }

        public bool IsSensitiveDirectory(string path)
        {
            var normalizedPath = NormalizePath(path).ToLowerInvariant();

            foreach (var sensitiveDir in _sensitiveDirectories)
            {
                if (normalizedPath.StartsWith(sensitiveDir))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
```

- [ ] **Step 3: 编译验证目录服务**
Run: `dotnet build WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 编译成功，无错误

---

### Task 5: 实现授权服务

**Files:**
- Create: `WebCodeCli.Domain/Domain/Service/IWorkspaceAuthorizationService.cs`
- Create: `WebCodeCli.Domain/Domain/Service/WorkspaceAuthorizationService.cs`

- [ ] **Step 1: 创建IWorkspaceAuthorizationService接口**

```csharp
using WebCodeCli.Domain.Repositories.Base.Workspace;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebCodeCli.Domain.Domain.Service
{
    public interface IWorkspaceAuthorizationService
    {
        /// <summary>
        /// 检查用户对目录的权限
        /// </summary>
        Task<bool> CheckPermissionAsync(long directoryId, string username, string requiredPermission = "ReadWrite");

        /// <summary>
        /// 授权用户访问目录
        /// </summary>
        Task GrantPermissionAsync(long directoryId, string ownerUsername, string authorizedUsername, string permissionLevel = "ReadWrite", DateTime? expiresAt = null);

        /// <summary>
        /// 撤销用户授权
        /// </summary>
        Task RevokePermissionAsync(long directoryId, string ownerUsername, string authorizedUsername);

        /// <summary>
        /// 获取目录的授权用户列表
        /// </summary>
        Task<List<WorkspaceAuthorizationEntity>> GetDirectoryAuthorizationsAsync(long directoryId);

        /// <summary>
        /// 获取用户被授权的所有目录
        /// </summary>
        Task<List<WorkspaceDirectoryEntity>> GetUserAuthorizedDirectoriesAsync(string username);

        /// <summary>
        /// 清理过期授权
        /// </summary>
        Task CleanupExpiredAuthorizationsAsync();
    }
}
```

- [ ] **Step 2: 创建WorkspaceAuthorizationService实现**

```csharp
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base.Workspace;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WebCodeCli.Domain.Domain.Service
{
    [ServiceDescription(typeof(IWorkspaceAuthorizationService), ServiceLifetime.Singleton)]
    public class WorkspaceAuthorizationService : IWorkspaceAuthorizationService
    {
        private readonly ILogger<WorkspaceAuthorizationService> _logger;
        private readonly IWorkspaceAuthorizationRepository _authorizationRepository;
        private readonly IWorkspaceRegistryService _workspaceRegistryService;
        private readonly IWorkspaceDirectoryRepository _directoryRepository;

        // 权限级别优先级
        private readonly Dictionary<string, int> _permissionLevels = new()
        {
            { "ReadOnly", 1 },
            { "ReadWrite", 2 },
            { "Owner", 3 }
        };

        public WorkspaceAuthorizationService(
            ILogger<WorkspaceAuthorizationService> logger,
            IWorkspaceAuthorizationRepository authorizationRepository,
            IWorkspaceRegistryService workspaceRegistryService,
            IWorkspaceDirectoryRepository directoryRepository)
        {
            _logger = logger;
            _authorizationRepository = authorizationRepository;
            _workspaceRegistryService = workspaceRegistryService;
            _directoryRepository = directoryRepository;
        }

        public async Task<bool> CheckPermissionAsync(long directoryId, string username, string requiredPermission = "ReadWrite")
        {
            var directory = await _workspaceRegistryService.GetDirectoryByIdAsync(directoryId);
            if (directory == null)
                return false;

            // 所有者拥有所有权限
            if (directory.OwnerUsername == username)
                return true;

            // 检查授权记录
            var authorization = await _authorizationRepository.GetFirstAsync(a =>
                a.DirectoryId == directoryId &&
                a.AuthorizedUsername == username &&
                (a.ExpiresAt == null || a.ExpiresAt > DateTime.Now));

            if (authorization == null)
                return false;

            // 检查权限级别是否足够
            return _permissionLevels.TryGetValue(authorization.PermissionLevel, out var userLevel) &&
                   _permissionLevels.TryGetValue(requiredPermission, out var requiredLevel) &&
                   userLevel >= requiredLevel;
        }

        public async Task GrantPermissionAsync(long directoryId, string ownerUsername, string authorizedUsername, string permissionLevel = "ReadWrite", DateTime? expiresAt = null)
        {
            // 验证操作人是否为目录所有者
            var directory = await _workspaceRegistryService.GetDirectoryByIdAsync(directoryId);
            if (directory == null || directory.OwnerUsername != ownerUsername)
            {
                throw new UnauthorizedAccessException("仅目录所有者可以授权");
            }

            // 不能授权给自己
            if (ownerUsername == authorizedUsername)
            {
                throw new ArgumentException("不能给自己授权");
            }

            // 检查授权是否已存在
            var existingAuth = await _authorizationRepository.GetFirstAsync(a =>
                a.DirectoryId == directoryId &&
                a.AuthorizedUsername == authorizedUsername);

            if (existingAuth != null)
            {
                // 更新现有授权
                existingAuth.PermissionLevel = permissionLevel;
                existingAuth.ExpiresAt = expiresAt;
                existingAuth.GrantedBy = ownerUsername;
                existingAuth.GrantedAt = DateTime.Now;
                await _authorizationRepository.UpdateAsync(existingAuth);
                _logger.LogInformation("更新目录 {DirectoryId} 对用户 {User} 的授权：{Permission}", directoryId, authorizedUsername, permissionLevel);
            }
            else
            {
                // 创建新授权
                var authorization = new WorkspaceAuthorizationEntity
                {
                    DirectoryId = directoryId,
                    AuthorizedUsername = authorizedUsername,
                    PermissionLevel = permissionLevel,
                    GrantedBy = ownerUsername,
                    GrantedAt = DateTime.Now,
                    ExpiresAt = expiresAt
                };
                await _authorizationRepository.InsertAsync(authorization);
                _logger.LogInformation("授予目录 {DirectoryId} 对用户 {User} 的授权：{Permission}", directoryId, authorizedUsername, permissionLevel);
            }
        }

        public async Task RevokePermissionAsync(long directoryId, string ownerUsername, string authorizedUsername)
        {
            // 验证操作人是否为目录所有者
            var directory = await _workspaceRegistryService.GetDirectoryByIdAsync(directoryId);
            if (directory == null || directory.OwnerUsername != ownerUsername)
            {
                throw new UnauthorizedAccessException("仅目录所有者可以撤销授权");
            }

            var authorization = await _authorizationRepository.GetFirstAsync(a =>
                a.DirectoryId == directoryId &&
                a.AuthorizedUsername == authorizedUsername);

            if (authorization != null)
            {
                await _authorizationRepository.DeleteAsync(authorization);
                _logger.LogInformation("撤销目录 {DirectoryId} 对用户 {User} 的授权", directoryId, authorizedUsername);
            }
        }

        public async Task<List<WorkspaceAuthorizationEntity>> GetDirectoryAuthorizationsAsync(long directoryId)
        {
            return await _authorizationRepository.GetListAsync(a => a.DirectoryId == directoryId);
        }

        public async Task<List<WorkspaceDirectoryEntity>> GetUserAuthorizedDirectoriesAsync(string username)
        {
            var authorizations = await _authorizationRepository.GetListAsync(a =>
                a.AuthorizedUsername == username &&
                (a.ExpiresAt == null || a.ExpiresAt > DateTime.Now));

            var directoryIds = authorizations.Select(a => a.DirectoryId).Distinct().ToList();
            if (!directoryIds.Any())
                return new List<WorkspaceDirectoryEntity>();

            return await _directoryRepository.GetListAsync(d => directoryIds.Contains(d.DirectoryId) && d.IsValid);
        }

        public async Task CleanupExpiredAuthorizationsAsync()
        {
            var expiredAuths = await _authorizationRepository.GetListAsync(a =>
                a.ExpiresAt != null &&
                a.ExpiresAt <= DateTime.Now);

            if (expiredAuths.Any())
            {
                await _authorizationRepository.DeleteAsync(expiredAuths);
                _logger.LogInformation("清理了 {Count} 条过期授权记录", expiredAuths.Count);
            }
        }
    }
}
```

- [ ] **Step 3: 编译验证授权服务**
Run: `dotnet build WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 编译成功，无错误

---

### Task 6: 实现会话目录服务

**Files:**
- Create: `WebCodeCli.Domain/Domain/Service/ISessionDirectoryService.cs`
- Create: `WebCodeCli.Domain/Domain/Service/SessionDirectoryService.cs`

- [ ] **Step 1: 创建ISessionDirectoryService接口**

**注意：** 移除切换目录方法，会话创建后不允许切换

```csharp
using WebCodeCli.Domain.Repositories.Base.Workspace;
using System.Threading.Tasks;

namespace WebCodeCli.Domain.Domain.Service
{
    public interface ISessionDirectoryService
    {
        /// <summary>
        /// 将会话关联到目录
        /// </summary>
        Task AssociateSessionWithDirectoryAsync(string sessionId, long directoryId);

        /// <summary>
        /// 获取会话关联的目录
        /// </summary>
        Task<WorkspaceDirectoryEntity?> GetSessionDirectoryAsync(string sessionId);

        /// <summary>
        /// 解除会话与目录的关联
        /// </summary>
        Task DisassociateSessionAsync(string sessionId);

        /// <summary>
        /// 获取会话的工作区路径
        /// </summary>
        Task<string> GetSessionWorkspacePathAsync(string sessionId, string defaultPath);
    }
}
```

- [ ] **Step 2: 创建SessionDirectoryService实现**

```csharp
using Microsoft.Extensions.Logging;
using WebCodeCli.Domain.Common.Extensions;
using WebCodeCli.Domain.Repositories.Base.Workspace;
using System;
using System.Threading.Tasks;

namespace WebCodeCli.Domain.Domain.Service
{
    [ServiceDescription(typeof(ISessionDirectoryService), ServiceLifetime.Singleton)]
    public class SessionDirectoryService : ISessionDirectoryService
    {
        private readonly ILogger<SessionDirectoryService> _logger;
        private readonly ISessionDirectoryMappingRepository _mappingRepository;
        private readonly IWorkspaceRegistryService _workspaceRegistryService;

        public SessionDirectoryService(
            ILogger<SessionDirectoryService> logger,
            ISessionDirectoryMappingRepository mappingRepository,
            IWorkspaceRegistryService workspaceRegistryService)
        {
            _logger = logger;
            _mappingRepository = mappingRepository;
            _workspaceRegistryService = workspaceRegistryService;
        }

        public async Task AssociateSessionWithDirectoryAsync(string sessionId, long directoryId)
        {
            // 检查目录是否存在
            var directory = await _workspaceRegistryService.GetDirectoryByIdAsync(directoryId);
            if (directory == null)
            {
                throw new ArgumentException("目录不存在");
            }

            // 检查是否已有映射
            var existingMapping = await _mappingRepository.GetFirstAsync(m => m.SessionId == sessionId);
            if (existingMapping != null)
            {
                // 更新现有映射
                existingMapping.DirectoryId = directoryId;
                existingMapping.UpdatedAt = DateTime.Now;
                await _mappingRepository.UpdateAsync(existingMapping);
                _logger.LogInformation("更新会话 {SessionId} 的关联目录：{DirectoryId}", sessionId, directoryId);
            }
            else
            {
                // 创建新映射
                var mapping = new SessionDirectoryMappingEntity
                {
                    SessionId = sessionId,
                    DirectoryId = directoryId,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                await _mappingRepository.InsertAsync(mapping);
                _logger.LogInformation("会话 {SessionId} 关联到目录：{DirectoryId}", sessionId, directoryId);
            }
        }

        public async Task<WorkspaceDirectoryEntity?> GetSessionDirectoryAsync(string sessionId)
        {
            var mapping = await _mappingRepository.GetFirstAsync(m => m.SessionId == sessionId);
            if (mapping == null)
                return null;

            return await _workspaceRegistryService.GetDirectoryByIdAsync(mapping.DirectoryId);
        }

        public async Task DisassociateSessionAsync(string sessionId)
        {
            var mapping = await _mappingRepository.GetFirstAsync(m => m.SessionId == sessionId);
            if (mapping != null)
            {
                await _mappingRepository.DeleteAsync(mapping);
                _logger.LogInformation("会话 {SessionId} 解除目录关联", sessionId);
            }
        }

        public async Task<string> GetSessionWorkspacePathAsync(string sessionId, string defaultPath)
        {
            var directory = await GetSessionDirectoryAsync(sessionId);
            if (directory == null)
                return defaultPath;

            return directory.DirectoryPath;
        }
    }
}
```

- [ ] **Step 3: 编译验证会话目录服务**
Run: `dotnet build WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 编译成功，无错误

---

## Chunk 3: 现有服务迁移

### Task 7: 迁移CliExecutorService到新架构

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/CliExecutorService.cs`
- Delete: `WebCodeCli.Domain/Domain/Service/IWorkspaceService.cs`
- Delete: `WebCodeCli.Domain/Domain/Service/WorkspaceService.cs`

- [ ] **Step 1: 更新依赖注入**
修改构造函数，替换IWorkspaceService为新的服务：
```csharp
public CliExecutorService(
    ILogger<CliExecutorService> logger,
    IOptions<CliToolsOption> cliToolsOptions,
    IServiceProvider serviceProvider,
    IUserContextService userContextService,
    IWorkspaceRegistryService workspaceRegistryService,
    ISessionDirectoryService sessionDirectoryService,
    IWorkspaceAuthorizationService workspaceAuthorizationService)
{
    _logger = logger;
    _cliToolsOptions = cliToolsOptions.Value;
    _serviceProvider = serviceProvider;
    _userContextService = userContextService;
    _workspaceRegistryService = workspaceRegistryService;
    _sessionDirectoryService = sessionDirectoryService;
    _workspaceAuthorizationService = workspaceAuthorizationService;
    _userAgent = $"WebCodeCli/{typeof(CliExecutorService).Assembly.GetName().Version}";
}
```

- [ ] **Step 2: 更新工作区路径获取逻辑**
修改GetWorkingDirectory方法，使用新架构获取路径：
```csharp
private async Task<string> GetWorkingDirectoryAsync(string sessionId)
{
    // 获取会话关联的目录
    var sessionDir = await _sessionDirectoryService.GetSessionDirectoryAsync(sessionId);
    if (sessionDir != null)
    {
        // 检查权限
        var hasPermission = await _workspaceAuthorizationService.CheckPermissionAsync(
            sessionDir.DirectoryId,
            _userContextService.CurrentUsername,
            "ReadWrite");

        if (!hasPermission)
        {
            throw new UnauthorizedAccessException("您没有权限访问该工作目录");
        }

        return sessionDir.DirectoryPath;
    }

    // 使用默认临时目录
    var workingDir = Path.Combine(Path.GetTempPath(), "WebCodeCli", "workspaces", sessionId);
    Directory.CreateDirectory(workingDir);
    return workingDir;
}
```

- [ ] **Step 3: 添加自定义工作区支持**
在CreateSession方法中添加目录关联逻辑：
```csharp
public async Task<ChatSessionEntity> CreateSessionAsync(string sessionName, string? customDirectoryPath = null)
{
    var session = await _chatSessionRepository.InsertAsync(new ChatSessionEntity
    {
        SessionId = Guid.NewGuid().ToString("N"),
        SessionName = sessionName,
        Username = _userContextService.CurrentUsername,
        CreatedAt = DateTime.Now,
        UpdatedAt = DateTime.Now
    });

    // 如果指定了自定义目录，注册并关联
    if (!string.IsNullOrEmpty(customDirectoryPath))
    {
        // 检查敏感目录
        if (_workspaceRegistryService.IsSensitiveDirectory(customDirectoryPath))
        {
            throw new UnauthorizedAccessException("禁止使用系统敏感目录作为工作区");
        }

        // 注册目录（当前用户自动成为所有者）
        var directory = await _workspaceRegistryService.RegisterDirectoryAsync(
            customDirectoryPath,
            Path.GetFileName(customDirectoryPath.TrimEnd(Path.DirectorySeparatorChar)),
            "Custom",
            _userContextService.CurrentUsername);

        // 关联到会话
        await _sessionDirectoryService.AssociateSessionWithDirectoryAsync(session.SessionId, directory.DirectoryId);
        session.DirectoryId = directory.DirectoryId;
        await _chatSessionRepository.UpdateAsync(session);
    }

    // 初始化会话输出目录
    var outputDir = Path.Combine(Path.GetTempPath(), "WebCodeCli", "outputs", session.SessionId);
    Directory.CreateDirectory(outputDir);

    _logger.LogInformation("新会话已创建: {SessionId}", session.SessionId);
    return session;
}
```

- [ ] **Step 4: 删除旧的WorkspaceService相关文件**
Run:
```bash
rm WebCodeCli.Domain/Domain/Service/IWorkspaceService.cs
rm WebCodeCli.Domain/Domain/Service/WorkspaceService.cs
```

- [ ] **Step 5: 编译验证迁移**
Run: `dotnet build WebCodeCli.Domain/WebCodeCli.Domain.csproj`
Expected: 编译成功，无错误

---

### Task 8: 迁移其他服务到新架构

**Files:**
- Modify: `WebCodeCli.Domain/Domain/Service/SessionHistoryManager.cs`
- Modify: `WebCodeCli.Domain/Domain/Service/WorkspaceCleanupBackgroundService.cs`

- [ ] **Step 1: 迁移SessionHistoryManager**
修改LoadSession方法，处理目录关联：
```csharp
public async Task<ChatSessionEntity?> LoadSessionAsync(string sessionId)
{
    var session = await _chatSessionRepository.GetFirstAsync(s => s.SessionId == sessionId);
    if (session == null)
        return null;

    // 如果会话有关联目录，确保映射存在
    if (session.DirectoryId.HasValue)
    {
        await _sessionDirectoryService.AssociateSessionWithDirectoryAsync(sessionId, session.DirectoryId.Value);
    }

    session.UpdatedAt = DateTime.Now;
    await _chatSessionRepository.UpdateAsync(session);
    return session;
}
```

- [ ] **Step 2: 迁移WorkspaceCleanupBackgroundService**
修改ExecuteAsync方法，集成新的清理逻辑：
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    _logger.LogInformation("工作区清理后台服务已启动");

    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            _logger.LogInformation("开始执行工作区清理任务");

            // 清理过期授权
            await _workspaceAuthorizationService.CleanupExpiredAuthorizationsAsync();

            // 清理临时工作区（原有逻辑保留）
            var tempWorkspaces = Path.Combine(Path.GetTempPath(), "WebCodeCli", "workspaces");
            if (Directory.Exists(tempWorkspaces))
            {
                var directories = Directory.GetDirectories(tempWorkspaces);
                foreach (var dir in directories)
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.LastAccessTime < DateTime.Now.AddDays(-7))
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                            _logger.LogInformation("删除过期临时工作区: {Dir}", dir);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "删除工作区失败: {Dir}", dir);
                        }
                    }
                }
            }

            _logger.LogInformation("工作区清理任务完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "工作区清理任务执行失败");
        }

        // 等待下一次清理
        await Task.Delay(_cleanupInterval, stoppingToken);
    }

    _logger.LogInformation("工作区清理后台服务已停止");
}
```

- [ ] **Step 3: 编译验证服务迁移**
Run: `dotnet build WebCodeCli.sln`
Expected: 整个解决方案编译成功，无错误

---

## Chunk 4: API扩展

### Task 9: 扩展WorkspaceController

**Files:**
- Modify: `WebCodeCli/Controllers/WorkspaceController.cs`

- [ ] **Step 1: 添加新API接口**
```csharp
/// <summary>
/// 获取我拥有的目录
/// </summary>
[HttpGet("my-owned-directories")]
public async Task<IActionResult> GetMyOwnedDirectories()
{
    var directories = await _workspaceRegistryService.GetUserOwnedDirectoriesAsync(_userContextService.CurrentUsername);
    return Ok(directories);
}

/// <summary>
/// 获取我被授权的目录
/// </summary>
[HttpGet("my-authorized-directories")]
public async Task<IActionResult> GetMyAuthorizedDirectories()
{
    var directories = await _workspaceAuthorizationService.GetUserAuthorizedDirectoriesAsync(_userContextService.CurrentUsername);
    return Ok(directories);
}

/// <summary>
/// 授权用户访问目录
/// </summary>
[HttpPost("authorize")]
public async Task<IActionResult> AuthorizeDirectory([FromBody] AuthorizeRequest request)
{
    try
    {
        await _workspaceAuthorizationService.GrantPermissionAsync(
            request.DirectoryId,
            _userContextService.CurrentUsername,
            request.AuthorizedUsername,
            request.PermissionLevel,
            request.ExpiresAt);

        return Ok();
    }
    catch (Exception ex)
    {
        return BadRequest(ex.Message);
    }
}

/// <summary>
/// 撤销用户授权
/// </summary>
[HttpPost("revoke-authorization")]
public async Task<IActionResult> RevokeAuthorization([FromBody] RevokeAuthorizationRequest request)
{
    try
    {
        await _workspaceAuthorizationService.RevokePermissionAsync(
            request.DirectoryId,
            _userContextService.CurrentUsername,
            request.AuthorizedUsername);

        return Ok();
    }
    catch (Exception ex)
    {
        return BadRequest(ex.Message);
    }
}

/// <summary>
/// 获取目录授权列表
/// </summary>
[HttpGet("directory-authorizations")]
public async Task<IActionResult> GetDirectoryAuthorizations(long directoryId)
{
    // 检查是否为目录所有者
    var directory = await _workspaceRegistryService.GetDirectoryByIdAsync(directoryId);
    if (directory == null || directory.OwnerUsername != _userContextService.CurrentUsername)
    {
        return Unauthorized("仅目录所有者可以查看授权列表");
    }

    var authorizations = await _workspaceAuthorizationService.GetDirectoryAuthorizationsAsync(directoryId);
    return Ok(authorizations);
}

// 请求模型
public class AuthorizeRequest
{
    public long DirectoryId { get; set; }
    public string AuthorizedUsername { get; set; } = string.Empty;
    public string PermissionLevel { get; set; } = "ReadWrite";
    public DateTime? ExpiresAt { get; set; }
}

public class RevokeAuthorizationRequest
{
    public long DirectoryId { get; set; }
    public string AuthorizedUsername { get; set; } = string.Empty;
}
```

- [ ] **Step 2: 编译验证API扩展**
Run: `dotnet build WebCodeCli/WebCodeCli.csproj`
Expected: 编译成功，无错误

---

### Task 10: 扩展SessionController

**注意：** 移除切换目录API，会话创建后不允许切换

**Files:**
- Modify: `WebCodeCli/Controllers/SessionController.cs`

- [ ] **Step 1: 扩展创建会话接口**
支持自定义工作目录：
```csharp
/// <summary>
/// 创建新会话
/// </summary>
[HttpPost]
public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequest request)
{
    var session = await _cliExecutorService.CreateSessionAsync(request.SessionName, request.CustomDirectoryPath);
    return Ok(session);
}

// 请求模型
public class CreateSessionRequest
{
    public string SessionName { get; set; } = string.Empty;
    public string? CustomDirectoryPath { get; set; }
}
```

- [ ] **Step 2: 编译验证API扩展**
Run: `dotnet build WebCodeCli/WebCodeCli.csproj`
Expected: 编译成功，无错误

---

## Chunk 5: 前端实现

### Task 11: 实现目录授权模态框（仅授权管理）

**注意：** 会话创建后不允许切换工作目录，授权管理入口放在会话列表中

**Files:**
- Create: `WebCodeCli/Components/Dialogs/WorkspaceAuthorizationModal.razor`

- [ ] **Step 1: 创建WorkspaceAuthorizationModal组件**

```razor
@using WebCodeCli.Domain.Domain.Model
@using WebCodeCli.Models
@inject HttpClient Http
@inject IUserContextService UserContextService
@inject IJSRuntime JSRuntime

@if (Show)
{
    <div class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
        <div class="bg-white rounded-xl shadow-2xl w-full max-w-2xl max-h-[90vh] flex flex-col">
            <!-- 头部 -->
            <div class="p-6 border-b border-gray-200 flex items-center justify-between">
                <h3 class="text-xl font-bold text-gray-800">目录授权管理</h3>
                <button @onclick="Close" class="p-2 hover:bg-gray-100 rounded-lg transition-colors">
                    <svg class="w-6 h-6 text-gray-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"></path>
                    </svg>
                </button>
            </div>

            <!-- 内容区域 -->
            <div class="flex-1 overflow-y-auto p-6 space-y-6">
                <!-- 当前目录信息 -->
                <div class="p-4 bg-blue-50 border border-blue-200 rounded-lg">
                    <h4 class="font-semibold text-blue-800 mb-2">当前目录</h4>
                    <p class="text-sm text-blue-700">@WorkspacePath</p>
                </div>

                <!-- 新增授权表单 -->
                <div class="p-4 border border-gray-200 rounded-lg">
                    <h4 class="text-sm font-semibold text-gray-700 mb-4 flex items-center gap-2">
                        <svg class="w-4 h-4 text-blue-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4"></path>
                        </svg>
                        新增用户授权
                    </h4>

                    <div class="space-y-4">
                        <div>
                            <label class="block text-sm font-medium text-gray-700 mb-2">用户名</label>
                            <input type="text"
                                   @bind="NewAuthorization.Username"
                                   class="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                                   placeholder="输入要授权的用户名" />
                        </div>

                        <div>
                            <label class="block text-sm font-medium text-gray-700 mb-2">权限级别</label>
                            <select @bind="NewAuthorization.Permission"
                                    class="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent">
                                <option value="read">只读（仅查看文件）</option>
                                <option value="write">读写（可编辑文件）</option>
                                <option value="admin">管理员（可授权其他用户）</option>
                            </select>
                        </div>

                        <div>
                            <label class="block text-sm font-medium text-gray-700 mb-2">过期时间（可选）</label>
                            <input type="datetime-local"
                                   @bind="NewAuthorization.ExpireTime"
                                   @bind:format="yyyy-MM-ddTHH:mm"
                                   class="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent" />
                            <p class="text-xs text-gray-500 mt-1">留空则永久有效</p>
                        </div>

                        @if (!string.IsNullOrEmpty(FormError))
                        {
                            <p class="text-red-500 text-sm">@FormError</p>
                        }

                        <div class="flex justify-end">
                            <button @onclick="AddAuthorization"
                                    disabled="@IsSubmitting"
                                    class="px-6 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center gap-2">
                                @if (IsSubmitting)
                                {
                                    <div class="inline-block animate-spin rounded-full h-4 w-4 border-2 border-white border-t-transparent"></div>
                                    <span>授权中...</span>
                                }
                                else
                                {
                                    <span>添加授权</span>
                                }
                            </button>
                        </div>
                    </div>
                </div>

                <!-- 已授权用户列表 -->
                <div>
                    <h4 class="text-sm font-semibold text-gray-700 mb-4 flex items-center gap-2">
                        <svg class="w-4 h-4 text-green-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z"></path>
                        </svg>
                        已授权用户
                    </h4>

                    @if (IsLoading)
                    {
                        <div class="text-center py-8">
                            <div class="inline-block animate-spin rounded-full h-8 w-8 border-4 border-gray-300 border-t-blue-600 mb-4"></div>
                            <p class="text-gray-500">加载授权列表中...</p>
                        </div>
                    }
                    else if (AuthorizedUsers.Any())
                    {
                        <div class="space-y-2">
                            @foreach (var user in AuthorizedUsers)
                            {
                                <div class="p-4 border border-gray-200 rounded-lg flex items-center justify-between">
                                    <div class="flex items-center gap-3">
                                        <div class="flex-shrink-0 w-10 h-10 bg-blue-100 rounded-full flex items-center justify-center">
                                            <span class="text-blue-600 font-medium">@user.Username[0].ToString().ToUpper()</span>
                                        </div>
                                        <div>
                                            <div class="font-medium text-gray-800">@user.Username</div>
                                            <div class="text-xs text-gray-500 mt-0.5">
                                                权限：<span class="px-2 py-0.5 rounded-full @GetPermissionBadgeClass(user.Permission)">@GetPermissionText(user.Permission)</span>
                                                @if (!string.IsNullOrEmpty(user.ExpireTimeDisplay))
                                                {
                                                    <span class="ml-2">· 过期时间：@user.ExpireTimeDisplay</span>
                                                }
                                            </div>
                                        </div>
                                    </div>
                                    <button @onclick="() => RevokeAuthorization(user.Username)"
                                            class="p-2 text-red-600 hover:bg-red-50 rounded-lg transition-colors"
                                            title="取消授权">
                                        <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"></path>
                                        </svg>
                                    </button>
                                </div>
                            }
                        </div>
                    }
                    else
                    {
                        <div class="text-center py-8 border border-gray-200 rounded-lg bg-gray-50">
                            <svg class="w-12 h-12 text-gray-300 mx-auto mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z"></path>
                            </svg>
                            <p class="text-gray-500">暂无授权用户</p>
                        </div>
                    }
                </div>
            </div>

            <!-- 底部按钮 -->
            <div class="p-6 border-t border-gray-200 flex justify-end">
                <button @onclick="Close" class="px-6 py-2 border border-gray-300 rounded-lg text-gray-700 hover:bg-gray-50 transition-colors">
                    关闭
                </button>
            </div>
        </div>
    </div>
}

@code {
    [Parameter] public bool Show { get; set; }
    [Parameter] public string WorkspacePath { get; set; } = string.Empty;
    [Parameter] public EventCallback OnClose { get; set; }

    private bool IsLoading { get; set; } = false;
    private bool IsSubmitting { get; set; } = false;
    private string FormError { get; set; } = string.Empty;
    private List<AuthorizedUserDto> AuthorizedUsers { get; set; } = new();
    private NewAuthorizationDto NewAuthorization { get; set; } = new();

    public class AuthorizedUserDto
    {
        public string Username { get; set; } = string.Empty;
        public string Permission { get; set; } = string.Empty;
        public DateTime? ExpireTime { get; set; }
        public string ExpireTimeDisplay => ExpireTime?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
    }

    public class NewAuthorizationDto
    {
        public string Username { get; set; } = string.Empty;
        public string Permission { get; set; } = "read";
        public DateTime? ExpireTime { get; set; }
    }

    public class AuthorizationRequest
    {
        public string WorkspacePath { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Permission { get; set; } = string.Empty;
        public DateTime? ExpireTime { get; set; }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Show && !string.IsNullOrEmpty(WorkspacePath))
        {
            await LoadAuthorizedUsersAsync();
        }
    }

    /// <summary>
    /// 显示对话框
    /// </summary>
    public async Task ShowAsync(string workspacePath)
    {
        Show = true;
        WorkspacePath = workspacePath;
        NewAuthorization = new NewAuthorizationDto();
        FormError = string.Empty;
        await LoadAuthorizedUsersAsync();
        StateHasChanged();
    }

    private async Task LoadAuthorizedUsersAsync()
    {
        IsLoading = true;
        try
        {
            var response = await Http.GetFromJsonAsync<ApiResponse<List<AuthorizedUserDto>>>($"/api/workspace/authorized-users?path={Uri.EscapeDataString(WorkspacePath)}");
            AuthorizedUsers = response?.Data ?? new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载授权列表失败: {ex.Message}");
            AuthorizedUsers = new();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void AddAuthorization()
    {
        if (string.IsNullOrWhiteSpace(NewAuthorization.Username))
        {
            FormError = "请输入用户名";
            return;
        }

        IsSubmitting = true;
        FormError = string.Empty;

        try
        {
            var request = new AuthorizationRequest
            {
                WorkspacePath = WorkspacePath,
                Username = NewAuthorization.Username,
                Permission = NewAuthorization.Permission
            };

            if (NewAuthorization.ExpireTime.HasValue)
            {
                request.ExpireTime = NewAuthorization.ExpireTime.Value;
            }

            var response = await Http.PostAsJsonAsync("/api/workspace/authorize", request);
            if (response.IsSuccessStatusCode)
            {
                // 重置表单
                NewAuthorization = new NewAuthorizationDto();
                // 重新加载授权列表
                await LoadAuthorizedUsersAsync();
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                FormError = $"授权失败: {errorContent}";
            }
        }
        catch (Exception ex)
        {
            FormError = $"授权失败: {ex.Message}";
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private async void RevokeAuthorization(string username)
    {
        if (!await JSRuntime.InvokeAsync<bool>("confirm", $"确定要取消用户 {username} 的授权吗？"))
        {
            return;
        }

        try
        {
            var response = await Http.DeleteAsync($"/api/workspace/revoke-authorization?path={Uri.EscapeDataString(WorkspacePath)}&username={Uri.EscapeDataString(username)}");
            if (response.IsSuccessStatusCode)
            {
                await LoadAuthorizedUsersAsync();
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                FormError = $"取消授权失败: {errorContent}";
            }
        }
        catch (Exception ex)
        {
            FormError = $"取消授权失败: {ex.Message}";
        }
    }

    private void Close()
    {
        Show = false;
        WorkspacePath = string.Empty;
        NewAuthorization = new NewAuthorizationDto();
        FormError = string.Empty;
        AuthorizedUsers.Clear();
        OnClose.InvokeAsync();
    }

    private string GetPermissionText(string permission)
    {
        return permission switch
        {
            "read" => "只读",
            "write" => "读写",
            "admin" => "管理员",
            _ => "未知"
        };
    }

    private string GetPermissionBadgeClass(string permission)
    {
        return permission switch
        {
            "read" => "bg-gray-100 text-gray-700",
            "write" => "bg-blue-100 text-blue-700",
            "admin" => "bg-purple-100 text-purple-700",
            _ => "bg-gray-100 text-gray-700"
        };
    }
}
```

---

### Task 12: 实现目录授权模态框

**Files:**
- Create: `WebCodeCli/Components/WorkspaceAuthorizationModal.razor`

- [ ] **Step 1: 创建WorkspaceAuthorizationModal组件**

```razor
@inject IWorkspaceAuthorizationService WorkspaceAuthorizationService
@inject ToastService ToastService

<div class="modal-overlay" @onclick="HandleClose">
    <div class="modal-content" @onclick:stopPropagation>
        <div class="modal-header">
            <h3>目录授权管理</h3>
            <button class="close-btn" @onclick="HandleClose">×</button>
        </div>

        <div class="modal-body">
            @if (DirectoryId == 0)
            {
                <div class="empty-state">
                    <p>请先选择一个目录</p>
                </div>
            }
            else
            {
                <div class="add-authorization-section">
                    <h4>添加授权</h4>
                    <div class="input-group">
                        <input type="text"
                               placeholder="输入用户名"
                               @bind-value="NewAuthorizedUsername" />
                        <select @bind-value="NewPermissionLevel">
                            <option value="ReadOnly">只读</option>
                            <option value="ReadWrite">读写</option>
                        </select>
                        <button class="add-btn" @onclick="AddAuthorization" disabled="@string.IsNullOrEmpty(NewAuthorizedUsername)">
                            添加
                        </button>
                    </div>
                </div>

                <div class="authorization-list">
                    <h4>已授权用户</h4>
                    @if (Authorizations.Any())
                    {
                        foreach (var auth in Authorizations)
                        {
                            <div class="authorization-item">
                                <div class="user-info">
                                    <span class="username">@auth.AuthorizedUsername</span>
                                    <span class="permission-badge @auth.PermissionLevel.ToLower()">
                                        @auth.PermissionLevel switch
                                        {
                                            "ReadOnly" => "只读",
                                            "ReadWrite" => "读写",
                                            _ => auth.PermissionLevel
                                        }
                                    </span>
                                    <span class="granted-by">由 @auth.GrantedBy 授权于 @auth.GrantedAt.ToString("yyyy-MM-dd")</span>
                                </div>
                                <button class="revoke-btn" @onclick="() => RevokeAuthorization(auth)">
                                    撤销
                                </button>
                            </div>
                        }
                    }
                    else
                    {
                        <div class="empty-state">
                            <p>暂无授权用户</p>
                        </div>
                    }
                </div>
            }
        </div>
    </div>
</div>

@code {
    [Parameter]
    public long DirectoryId { get; set; }

    [Parameter]
    public Action OnClose { get; set; } = () => { };

    private string NewAuthorizedUsername { get; set; } = string.Empty;
    private string NewPermissionLevel { get; set; } = "ReadWrite";
    private List<WorkspaceAuthorizationEntity> Authorizations { get; set; } = new();

    protected override async Task OnInitializedAsync()
    {
        if (DirectoryId > 0)
        {
            await LoadAuthorizations();
        }
    }

    private async Task LoadAuthorizations()
    {
        Authorizations = await WorkspaceAuthorizationService.GetDirectoryAuthorizationsAsync(DirectoryId);
    }

    private async Task AddAuthorization()
    {
        if (string.IsNullOrEmpty(NewAuthorizedUsername))
            return;

        try
        {
            await WorkspaceAuthorizationService.GrantPermissionAsync(
                DirectoryId,
                UserContextService.CurrentUsername,
                NewAuthorizedUsername,
                NewPermissionLevel);

            await LoadAuthorizations();
            NewAuthorizedUsername = string.Empty;
            ToastService.Success("授权添加成功");
        }
        catch (Exception ex)
        {
            ToastService.Error(ex.Message);
        }
    }

    private async Task RevokeAuthorization(WorkspaceAuthorizationEntity auth)
    {
        try
        {
            await WorkspaceAuthorizationService.RevokePermissionAsync(
                DirectoryId,
                UserContextService.CurrentUsername,
                auth.AuthorizedUsername);

            await LoadAuthorizations();
            ToastService.Success("授权已撤销");
        }
        catch (Exception ex)
        {
            ToastService.Error(ex.Message);
        }
    }

    private void HandleClose()
    {
        OnClose?.Invoke();
    }
}

<style>
    .modal-overlay {
        position: fixed;
        top: 0;
        left: 0;
        right: 0;
        bottom: 0;
        background: rgba(0, 0, 0, 0.6);
        display: flex;
        align-items: center;
        justify-content: center;
        z-index: 2000;
    }

    .modal-content {
        background: #1e1e1e;
        border: 1px solid #3e3e3e;
        border-radius: 8px;
        width: 500px;
        max-width: 90vw;
        max-height: 80vh;
        overflow: hidden;
    }

    .modal-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: 16px 20px;
        border-bottom: 1px solid #3e3e3e;
    }

    .modal-header h3 {
        margin: 0;
        color: white;
    }

    .close-btn {
        background: none;
        border: none;
        color: #888;
        font-size: 24px;
        cursor: pointer;
        padding: 0;
        width: 32px;
        height: 32px;
        display: flex;
        align-items: center;
        justify-content: center;
    }

    .close-btn:hover {
        color: white;
        background: rgba(255, 255, 255, 0.1);
        border-radius: 4px;
    }

    .modal-body {
        padding: 20px;
        max-height: calc(80vh - 70px);
        overflow-y: auto;
    }

    .add-authorization-section {
        margin-bottom: 24px;
    }

    .add-authorization-section h4 {
        margin: 0 0 12px 0;
        color: white;
        font-size: 14px;
    }

    .input-group {
        display: flex;
        gap: 8px;
    }

    .input-group input,
    .input-group select {
        flex: 1;
        padding: 8px 12px;
        background: #2d2d2d;
        border: 1px solid #3e3e3e;
        border-radius: 4px;
        color: white;
    }

    .add-btn {
        padding: 8px 16px;
        background: #1976d2;
        border: none;
        border-radius: 4px;
        color: white;
        cursor: pointer;
    }

    .add-btn:hover:not(:disabled) {
        background: #1565c0;
    }

    .add-btn:disabled {
        opacity: 0.5;
        cursor: not-allowed;
    }

    .authorization-list h4 {
        margin: 0 0 12px 0;
        color: white;
        font-size: 14px;
    }

    .authorization-item {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: 12px;
        background: #2d2d2d;
        border-radius: 4px;
        margin-bottom: 8px;
    }

    .user-info {
        display: flex;
        flex-direction: column;
        gap: 4px;
    }

    .username {
        font-weight: 500;
        color: white;
    }

    .permission-badge {
        display: inline-block;
        padding: 2px 8px;
        border-radius: 12px;
        font-size: 12px;
        font-weight: 500;
        margin-right: 8px;
    }

    .permission-badge.readonly {
        background: rgba(76, 175, 80, 0.2);
        color: #4caf50;
    }

    .permission-badge.readwrite {
        background: rgba(255, 152, 0, 0.2);
        color: #ff9800;
    }

    .granted-by {
        font-size: 12px;
        color: #888;
    }

    .revoke-btn {
        padding: 4px 12px;
        background: rgba(244, 67, 54, 0.2);
        border: 1px solid rgba(244, 67, 54, 0.3);
        border-radius: 4px;
        color: #f44336;
        cursor: pointer;
        font-size: 12px;
    }

    .revoke-btn:hover {
        background: rgba(244, 67, 54, 0.3);
    }

    .empty-state {
        text-align: center;
        padding: 40px 20px;
        color: #888;
    }
</style>
```

---

### Task 13: 更新CreateSessionModal支持目录选择

**注意：** 会话创建时选择工作目录，创建后不允许切换

**Files:**
- Modify: `WebCodeCli/Components/Dialogs/CreateSessionModal.razor`

- [ ] **Step 1: 更新CreateSessionModal支持目录选择**
添加自定义目录输入框和已有目录选择Tab：

```razor
<div class="modal-body">
    <div class="form-group">
        <label>会话名称</label>
        <input type="text" @bind-value="SessionName" placeholder="输入会话名称" />
    </div>

    <div class="form-group">
        <label>工作目录</label>
        <div class="tabs">
            <button class="tab @(SelectedTab == "default" ? "active" : "")" @onclick="() => SelectedTab = "default"">
                默认目录
            </button>
            <button class="tab @(SelectedTab == "custom" ? "active" : "")" @onclick="() => SelectedTab = "custom"">
                自定义路径
            </button>
            <button class="tab @(SelectedTab == "existing" ? "active" : "")" @onclick="() => SelectedTab = "existing"">
                选择已有目录
            </button>
        </div>

        @if (SelectedTab == "custom")
        {
            <input type="text" @bind-value="CustomDirectoryPath" placeholder="输入本地目录绝对路径" />
        }
        else if (SelectedTab == "existing")
        {
            <select @bind-value="SelectedDirectoryId">
                <option value="">请选择目录</option>
                @foreach (var dir in AvailableDirectories)
                {
                    <option value="@dir.DirectoryId">@dir.DirectoryName (@dir.DirectoryPath)</option>
                }
            </select>
        }
    </div>
</div>
```

- [ ] **Step 2: 添加目录选择相关的State和回调**

在 `@code` 部分添加：

```csharp
private string SelectedTab { get; set; } = "default";
private string CustomDirectoryPath { get; set; } = string.Empty;
private long SelectedDirectoryId { get; set; }
private List<AccessibleDirectoryDto> AvailableDirectories { get; set; } = new();

[Parameter] public EventCallback<CreateSessionRequest> OnCreateSession { get; set; }

// 在 OnInitializedAsync 中加载可用目录
protected override async Task OnInitializedAsync()
{
    await LoadAvailableDirectoriesAsync();
}

private async Task LoadAvailableDirectoriesAsync()
{
    // 调用API获取用户可用目录
    var response = await Http.GetFromJsonAsync<ApiResponse<List<AccessibleDirectoryDto>>("/api/workspace/my-accessible-directories");
    AvailableDirectories = response?.Data ?? new();
}
```

---

---

## Chunk 6: 测试与验证

### Task 14: 编写Playwright测试用例

**Files:**
- Modify: `tests/web-workspace-management.spec.ts`

- [ ] **Step 1: 扩展测试用例，添加Git导入和授权管理测试**
```typescript
test('测试Git项目导入功能', async ({ page }) => {
  const TEST_GIT_URL = 'https://gh.llkk.cc/https://github.com/lusile2024/skills-hub.git';

  // 打开项目导入页面
  await page.click('[data-menu-item="projects"]');
  await page.waitForLoadState('networkidle');

  // 点击导入Git项目按钮
  await page.click('button:has-text("导入Git项目")');
  await expect(page.locator('text="导入Git仓库"')).toBeVisible();

  // 填写Git仓库地址
  await page.fill('input[placeholder="请输入Git仓库地址"]', TEST_GIT_URL);

  // 确认导入
  await page.click('button:has-text("开始导入")');

  // 等待导入完成（最长等待60秒）
  await page.waitForSelector('text="导入成功"', { timeout: 60000 });
  await expect(page.locator('text="导入成功"')).toBeVisible();

  // 验证项目出现在可用目录列表中
  await page.click('[data-menu-item="sessions"]');
  await page.click('button:has-text("新建会话")');
  await page.click('button:has-text("选择已有目录")');

  const projectName = 'skills-hub';
  await expect(page.locator(`text="${projectName}"`)).toBeVisible();

  // 选择导入的项目创建会话
  await page.click(`text="${projectName}"`);
  await page.click('button:has-text("创建会话")');
  await page.waitForLoadState('networkidle');
});

test('测试目录授权管理功能', async ({ page }) => {
  // 打开会话列表
  await page.click('[data-menu-item="sessions"]');
  await page.waitForLoadState('networkidle');

  // 找到一个有工作区的会话
  const sessionWithWorkspace = page.locator('.session-item').filter({ hasText: 'D:' }).first();
  await expect(sessionWithWorkspace).toBeVisible();

  // 点击授权管理按钮
  await sessionWithWorkspace.getByRole('button', { name: '管理目录授权' }).click();
  await expect(page.locator('text="目录授权管理"')).toBeVisible();

  // 添加授权用户
  await page.fill('input[placeholder="输入要授权的用户名"]', 'testuser');
  await page.selectOption('select', 'write');
  await page.click('button:has-text("添加授权")');

  // 等待授权成功提示
  await expect(page.locator('text=testuser')).toBeVisible();

  // 撤销授权
  await page.click('button:has-text("取消授权")');

  // 等待撤销完成
  await expect(page.locator('text=testuser')).not.toBeVisible();
});
```

### Task 15: 执行测试验证

- [ ] **Step 1: 启动应用**
Run: `dotnet run --project WebCodeCli`
Expected: 应用正常启动，数据库自动创建新表

- [ ] **Step 2: 运行单元测试**
Run: `dotnet test`
Expected: 所有测试通过

- [ ] **Step 3: 运行Playwright测试**
Run: `npx playwright test tests/web-workspace-management.spec.ts`
Expected: 所有端到端测试通过

---

Plan complete and saved to `docs/superpowers/plans/2026-03-13-unified-workspace-management.md`. Ready to execute?