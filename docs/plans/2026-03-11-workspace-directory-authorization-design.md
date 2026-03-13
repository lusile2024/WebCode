# 工作区目录授权功能设计文档

## 一、功能概述
实现自定义工作目录与用户、会话的绑定授权机制，满足以下需求：
1. 用户名、会话ID与自定义工作目录绑定存储
2. 切换会话时自动使用该会话之前设置的自定义工作目录
3. 目录首次被使用时自动注册当前用户为所有者
4. 非目录所有者使用目录时提示未授权，需要所有者授权
5. 目录所有者可以对授权用户进行取消授权操作
6. 系统敏感目录默认禁止普通用户使用

## 二、数据结构设计

### 2.1 工作区所有者表（WorkspaceOwner）
存储目录与所有者的对应关系：

```csharp
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

### 2.2 工作区授权表（WorkspaceAuthorization）
存储目录的授权访问关系：

```csharp
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

## 三、核心业务逻辑

### 3.1 路径规范化处理
所有路径在存储和比较前都需要经过规范化处理：
1. 转换为绝对路径
2. 统一路径分隔符为 `/`
3. 去除末尾的路径分隔符
4. 转换为小写（不区分大小写比较）
5. 解析所有 `../` 和 `./` 相对路径

### 3.2 目录注册逻辑
当用户在新建会话时输入自定义目录：
1. 对输入路径进行规范化处理
2. 检查路径是否在系统敏感目录黑名单中，如果是则直接拒绝
3. 检查路径是否已经在 `WorkspaceOwner` 表中存在
   - 如果不存在：将当前用户注册为该目录的所有者
   - 如果存在：继续检查权限
4. 检查当前用户是否为目录所有者或已被授权
   - 如果是：将目录路径存储到会话的 `WorkspacePath` 字段
   - 如果不是：返回错误提示，引导用户申请授权

### 3.3 访问检查逻辑
在任何需要访问工作目录的操作前（包括命令执行、文件读写等）：
1. 获取当前会话的工作目录路径
2. 对路径进行规范化处理
3. 查询 `WorkspaceOwner` 表确认目录是否已注册
   - 如果未注册：自动注册当前用户为所有者
   - 如果已注册：检查当前用户是否为所有者或在授权列表中
4. 权限验证失败时抛出 `UnauthorizedAccessException`，包含提示信息

### 3.4 授权逻辑
目录所有者可以对其他用户进行授权：
1. 验证操作人是否为目录所有者
2. 检查被授权用户是否已存在授权记录
3. 新增授权记录到 `WorkspaceAuthorization` 表

### 3.5 取消授权逻辑
目录所有者可以取消已授权用户的访问权限：
1. 验证操作人是否为目录所有者
2. 删除 `WorkspaceAuthorization` 表中对应的授权记录
3. 所有已使用该目录的被授权用户会话将在下一次操作时被拒绝访问

### 3.6 敏感目录保护
系统内置敏感目录黑名单，默认禁止使用：
- Windows：`C:\Windows`、`C:\Program Files`、`C:\Program Files (x86)`、`C:\ProgramData`、`C:\Users\Public` 等
- Linux/macOS：`/etc`、`/root`、`/bin`、`/sbin`、`/usr/bin`、`/usr/sbin`、`/var` 等
- 支持在 `appsettings.json` 中配置额外的禁止目录

## 四、API接口设计

### 4.1 目录授权接口
```http
POST /api/workspace/authorize
Content-Type: application/json

{
  "directoryPath": "D:/Projects/MyProject",
  "authorizedUsername": "user001"
}
```

### 4.2 取消授权接口
```http
POST /api/workspace/revoke-authorization
Content-Type: application/json

{
  "directoryPath": "D:/Projects/MyProject",
  "authorizedUsername": "user001"
}
```

### 4.3 获取我的授权目录接口
```http
GET /api/workspace/my-authorized-directories
```

### 4.4 获取我拥有的目录接口
```http
GET /api/workspace/my-owned-directories
```

### 4.5 获取目录授权列表接口（仅所有者可调用）
```http
GET /api/workspace/directory-authorizations?directoryPath=D:/Projects/MyProject
```

## 五、现有代码改造点

### 5.1 领域层改造
1. `WebCodeCli.Domain/Repositories/Base/` 新增两个实体对应的仓储接口和实现
2. `WebCodeCli.Domain/Domain/Service/IWorkspaceService.cs` 新增授权相关方法
3. `WorkspaceService.cs` 中修改 `ValidatePath` 方法，增加权限检查逻辑
4. 修改 `GetOrCreateWorkspace` 方法，增加目录注册和权限验证逻辑

### 5.2 应用层改造
1. `WebCodeCli/Controllers/WorkspaceController.cs` 新增授权相关API接口
2. 会话创建时保存自定义目录路径到 `ChatSessionEntity.WorkspacePath`
3. 切换会话时自动加载该会话的 `WorkspacePath` 到当前上下文

### 5.3 前端改造
1. 新建会话时增加目录权限提示
2. 新增目录授权管理界面
3. 未授权目录访问时显示申请授权提示

## 六、安全设计
1. **路径穿越防护**：所有路径访问前都进行规范化处理，确保不会跳出工作目录
2. **最小权限原则**：默认用户只能访问自己创建或被授权的目录
3. **敏感目录隔离**：系统目录默认禁止访问，防止误操作
4. **操作审计**：所有授权、取消授权操作都记录日志
5. **会话隔离**：不同会话之间的工作目录相互隔离，即使是同一用户的不同会话也不会自动共享目录权限
