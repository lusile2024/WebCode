# 统一工作区管理架构使用指南

## 概述

本系统实现了统一的工作区管理架构，将所有类型的工作目录（自定义目录、Git 项目、ZIP 上传项目）统一管理，支持会话绑定、目录授权和共享功能。

## 核心特性

### 1. 统一目录管理
- 支持四种目录类型：
  - `Default`：默认会话目录（按会话ID命名）
  - `Custom`：用户自定义工作目录
  - `Project`：Git/ZIP 导入的项目目录
  - `Shared`：共享目录

### 2. 目录授权系统
- 支持三级权限：
  - `Read`：只读访问
  - `ReadWrite`：读写权限
  - `Owner`：所有者权限
- 支持设置授权过期时间
- 过期授权自动清理

### 3. 会话与目录关联
- 支持将会话绑定到指定工作目录
- 同一会话可以切换工作目录
- 同一目录可以被多个会话使用
- 目录删除时自动检查活跃会话

### 4. 项目集成
- Git 克隆的项目自动创建 Project 类型目录
- ZIP 上传的项目自动创建 Project 类型目录
- 项目目录可以被多个会话复用
- 支持项目来源信息记录

## 核心组件

### 领域模型

| 实体 | 说明 |
|------|------|
| `WorkspaceDirectory` | 工作区目录实体 |
| `WorkspaceAuthorization` | 目录授权实体 |
| `SessionWorkspaceMapping` | 会话-目录映射实体 |
| `ChatSessionEntity` | 聊天会话实体（已扩展） |

### 服务层

| 服务 | 说明 |
|------|------|
| `IWorkspaceRegistryService` | 核心注册表服务，提供目录和授权管理 |
| `ISessionDirectoryService` | 会话与目录关联服务 |
| `IWorkspaceMigrationService` | 数据迁移服务 |

### API 接口

所有接口路径前缀：`/api/workspace-registry`

#### 目录管理
- `POST /directories` - 创建目录
- `GET /directories` - 获取用户目录
- `GET /directories/authorized` - 获取被授权的目录
- `GET /directories/{directoryId}` - 获取目录详情
- `PUT /directories/{directoryId}` - 更新目录
- `DELETE /directories/{directoryId}` - 删除目录
- `GET /paths/{path}/exists` - 检查路径是否已被使用

#### 授权管理
- `POST /directories/{directoryId}/authorize` - 授权用户访问
- `DELETE /directories/{directoryId}/authorize/{username}` - 撤销授权
- `GET /directories/{directoryId}/authorizations` - 获取目录授权列表
- `GET /authorizations` - 获取用户所有授权
- `GET /directories/{directoryId}/access` - 检查目录访问权限

#### 会话管理
- `POST /sessions` - 创建新会话并关联目录
- `PUT /sessions/{sessionId}/directory` - 切换会话工作目录
- `GET /sessions/available-directories` - 获取用户可用目录（包含被授权的）
- `GET /sessions/{sessionId}/directory` - 获取会话目录信息

#### 工具方法
- `GET /paths/{path}/access` - 检查路径访问权限
- `GET /directories/{directoryId}/stats` - 获取目录使用统计
- `POST /migrate` - 执行数据迁移（管理员）

## 使用示例

### 1. 创建自定义工作目录
```csharp
var directory = await _workspaceRegistry.CreateDirectoryAsync(
    name: "我的项目目录",
    path: @"D:\Projects\MyProject",
    ownerUsername: "user@example.com",
    type: DirectoryType.Custom);
```

### 2. 授权其他用户访问
```csharp
var authorization = await _workspaceRegistry.GrantDirectoryAccessAsync(
    directoryId: directory.DirectoryId,
    ownerUsername: "user@example.com",
    authorizedUsername: "collaborator@example.com",
    permission: PermissionLevel.ReadWrite,
    expiresAt: DateTime.Now.AddDays(7));
```

### 3. 创建会话并关联目录
```csharp
var sessionId = await _sessionDirectoryService.CreateSessionWithDirectoryAsync(
    username: "collaborator@example.com",
    directoryId: directory.DirectoryId,
    title: "协作开发会话");
```

### 4. 切换会话工作目录
```csharp
var success = await _sessionDirectoryService.SwitchSessionDirectoryAsync(
    sessionId: existingSessionId,
    newDirectoryId: otherDirectoryId,
    username: "user@example.com");
```

### 5. 获取用户可用目录
```csharp
var availableDirectories = await _sessionDirectoryService.GetUserAvailableDirectoriesAsync(
    username: "user@example.com");
```

## 数据迁移

系统提供自动数据迁移功能，将现有的会话工作区迁移到新的统一管理架构：

1. 执行数据迁移接口（管理员）：
   ```http
   POST /api/workspace-registry/migrate
   ```

2. 迁移内容：
   - 为现有会话的工作区路径创建目录记录
   - 自动建立会话与目录的关联
   - 创建默认的系统目录
   - 清理孤立数据

## 前端集成

### 显示会话工作目录
在会话名称下方显示当前关联的工作目录信息：
```razor
@if (session.DirectoryId != null)
{
    <div class="workspace-info">
        <span class="directory-type">@session.DirectoryType</span>
        <span class="directory-name">@session.Directory?.Name</span>
        <span class="directory-path">@session.Directory?.Path</span>
    </div>
}
```

### 新建会话选择目录
提供目录选择下拉框：
```razor
<select @bind="SelectedDirectoryId">
    <option value="">创建新临时目录</option>
    @foreach (var dir in AvailableDirectories)
    {
        <option value="@dir.DirectoryId">@dir.Name (@dir.Path)</option>
    }
</select>
```

## 数据库表结构

### WorkspaceDirectories 表
| 字段 | 类型 | 说明 |
|------|------|------|
| DirectoryId | varchar(64) | 主键，目录ID |
| Name | varchar(256) | 目录名称 |
| Path | varchar(512) | 目录路径 |
| OwnerUsername | varchar(128) | 所有者用户名 |
| Type | int | 目录类型 |
| ProjectId | varchar(64) | 关联的项目ID |
| SourceType | varchar(64) | 来源类型（git/zip等） |
| SourceUrl | varchar(512) | 来源URL |
| IsManaged | bit | 是否由系统管理 |
| CreatedAt | datetime | 创建时间 |
| LastAccessedAt | datetime | 最后访问时间 |

### WorkspaceAuthorizations 表
| 字段 | 类型 | 说明 |
|------|------|------|
| AuthorizationId | varchar(64) | 主键，授权ID |
| DirectoryId | varchar(64) | 目录ID |
| AuthorizedUsername | varchar(128) | 被授权用户名 |
| Permission | int | 权限级别 |
| GrantedAt | datetime | 授权时间 |
| ExpiresAt | datetime | 过期时间（可选） |
| IsExpired | bit | 是否已过期 |

### SessionWorkspaceMappings 表
| 字段 | 类型 | 说明 |
|------|------|------|
| SessionId | varchar(64) | 会话ID |
| DirectoryId | varchar(64) | 目录ID |
| CreatedAt | datetime | 创建时间 |
| EndedAt | datetime | 结束时间（可选） |

## 最佳实践

1. **目录类型选择**：
   - 临时任务使用 Default 类型
   - 长期项目使用 Custom 类型
   - Git/ZIP 导入使用 Project 类型
   - 需要团队共享使用 Shared 类型

2. **授权策略**：
   - 最小权限原则，只授予必要的权限
   - 临时授权设置过期时间
   - 定期清理过期和不再需要的授权

3. **会话管理**：
   - 对于长期项目，使用固定目录关联多个会话
   - 结束会话时主动解除目录关联
   - 定期清理结束的会话映射

4. **性能优化**：
   - 目录路径使用绝对路径
   - 避免频繁切换会话目录
   - 定期执行过期授权和孤立数据清理