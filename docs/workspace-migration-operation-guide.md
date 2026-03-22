# 统一工作区管理架构 - 运维任务执行指南

## 概述

本文档描述统一工作区管理架构的运维任务执行步骤，包括数据库迁移、数据迁移和定期清理任务的配置。

## 目录

- [执行前准备](#执行前准备)
- [任务1: 数据库备份](#任务1-数据库备份)
- [任务2: 执行数据库迁移](#任务2-执行数据库迁移)
- [任务3: 运行数据迁移脚本](#任务3-运行数据迁移脚本)
- [任务4: 配置定期清理任务](#任务4-配置定期清理任务)
- [验证与回滚](#验证与回滚)

---

## 执行前准备

### 1.1 检查清单

在开始执行前，请确认以下项目：

- [ ] 已通知用户系统将进行维护
- [ ] 当前时间是低峰期（建议凌晨2-4点）
- [ ] 已有足够的磁盘空间（至少当前数据库大小的2倍）
- [ ] 已准备好回滚方案
- [ ] 日志记录已启用

### 1.2 环境检查

```bash
# 检查项目状态
cd D:\VSWorkshop\WebCode
git status

# 检查数据库文件位置
# SQLite数据库通常位于: WebCodeCli.db 或 /app/data/WebCodeCli.db
```

### 1.3 配置检查

确认 `appsettings.json` 中的配置：

```json
{
  "CliTools": {
    "WorkspaceExpirationHours": 24
  }
}
```

---

## 任务1: 数据库备份

### 1.1 备份数据库

**⚠️ 危险操作检测！**
- **操作类型**: 数据库文件备份
- **影响范围**: SQLite数据库文件
- **风险评估**: 低风险（创建副本，不修改原文件）

请确认是否继续？[需要明确的"是"、"确认"、"继续"]

---

### 1.2 手动备份步骤

```bash
# 进入项目目录
cd D:\VSWorkshop\WebCode

# 备份数据库文件（SQLite）
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
Copy-Item "WebCodeCli.db" "WebCodeCli.db.backup_$timestamp"

# 验证备份文件
Test-Path "WebCodeCli.db.backup_$timestamp"

# 记录备份日志
"备份完成: WebCodeCli.db.backup_$timestamp" | Out-File -Append "migration-log.txt"
```

### 1.3 保留现有数据的代码备份

同时备份项目代码：

```bash
# 创建git标签（如果使用git）
git tag -a before-workspace-migration -m "备份：工作区迁移前状态"
git push origin before-workspace-migration
```

---

## 任务2: 执行数据库迁移

### 2.1 迁移概述

系统已内置自动数据库迁移机制，通过 SqlSugar 的 CodeFirst 模式在应用启动时自动创建表结构。

涉及的表：
- `WorkspaceOwner` - 工作区所有者表
- `WorkspaceAuthorization` - 工作区授权表
- `ChatSession`（新增字段）- 聊天会话表

### 2.2 自动迁移执行

启动应用程序，自动执行迁移：

```bash
# 构建项目
dotnet build WebCodeCli.sln

# 运行应用（会自动创建表结构）
dotnet run --project WebCodeCli
```

### 2.3 验证表结构

应用启动后，验证表是否创建成功：

```csharp
// 在应用启动日志中查找以下信息：
// "开始初始化工作区授权相关表..."
// "工作区授权相关表初始化成功"
```

### 2.4 表结构说明

**WorkspaceOwner 表：**
| 字段 | 类型 | 说明 |
|------|------|------|
| Id | INTEGER | 主键，自增 |
| DirectoryPath | TEXT | 目录路径（规范化）|
| OwnerUsername | TEXT | 所有者用户名 |
| CreatedAt | TEXT | 创建时间 |
| UpdatedAt | TEXT | 更新时间 |

**WorkspaceAuthorization 表：**
| 字段 | 类型 | 说明 |
|------|------|------|
| Id | INTEGER | 主键，自增 |
| DirectoryPath | TEXT | 目录路径 |
| AuthorizedUsername | TEXT | 被授权用户名 |
| GrantedBy | TEXT | 授权人用户名 |
| GrantedAt | TEXT | 授权时间 |

**ChatSession 表新增字段：**
| 字段 | 类型 | 说明 |
|------|------|------|
| WorkspacePath | TEXT | 工作区路径 |
| IsCustomWorkspace | bit | 是否自定义工作区 |
| DirectoryId | varchar(64) | 目录ID（新架构）|
| DirectoryType | int | 目录类型（新架构）|
| FeishuChatKey | varchar(128) | 飞书聊天键 |
| IsFeishuActive | bit | 是否飞书活跃 |

---

## 任务3: 运行数据迁移脚本

### 3.1 数据迁移服务

系统已提供 `IWorkspaceMigrationService` 服务来处理数据迁移：

```csharp
public interface IWorkspaceMigrationService
{
    // 执行数据迁移
    Task MigrateAsync();

    // 清理孤立数据
    Task CleanupOrphanedDataAsync();
}
```

### 3.2 创建迁移执行API

创建一个临时的 API 端点来触发迁移：

```csharp
// 在 WorkspaceController.cs 中添加
[HttpPost("migrate")]
public async Task<IActionResult> TriggerMigration()
{
    try
    {
        await _workspaceMigrationService.MigrateAsync();
        return Ok(new { Success = true, Message = "迁移完成" });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { Success = false, Message = ex.Message });
    }
}
```

### 3.3 手动触发迁移

使用 PowerShell 调用迁移 API：

```powershell
# 确保应用正在运行
# 调用迁移API
$response = Invoke-RestMethod -Uri "http://localhost:5000/api/workspace/migrate" -Method Post
$response | ConvertTo-Json -Depth 10

# 检查日志
Get-Content "logs\feishu-$(Get-Date -Format 'yyyyMMdd').log" -Tail 50
```

### 3.4 迁移内容说明

数据迁移服务执行以下操作：

1. **迁移会话工作区信息**
   - 扫描现有 `ChatSession` 表中的 `WorkspacePath` 字段
   - 为每个唯一路径创建 `WorkspaceDirectory` 记录
   - 更新会话的 `DirectoryId` 字段

2. **创建默认工作区目录**
   - `workspaces/default`
   - `workspaces/temp`
   - `~/WebCode/workspaces`

3. **记录迁移日志**
   - 每个迁移操作都会记录到应用日志中

---

## 任务4: 配置定期清理任务

### 4.1 现有后台服务

系统已内置以下后台服务：

1. **WorkspaceCleanupBackgroundService**
   - 执行频率：每小时
   - 功能：清理过期的会话工作区

2. **QuartzHostedService**
   - 功能：管理定时任务调度器

### 4.2 在 appsettings.json 中配置

```json
{
  "CliTools": {
    // 工作区过期时间（小时）
    "WorkspaceExpirationHours": 24,

    // 允许的工作区路径
    "AllowedWorkspacePaths": [
      "D:\\VSWorkshop",
      "D:\\WMS4"
    ]
  }
}
```

### 4.3 启用工作区清理后台服务

在 `Program.cs` 中已注册：

```csharp
// 添加工作区清理后台服务
builder.Services.AddHostedService<WorkspaceCleanupBackgroundService>();

// 添加 Quartz 定时任务后台服务
builder.Services.AddSingleton<QuartzHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<QuartzHostedService>());
```

### 4.4 创建定时清理任务（使用 Quartz）

通过系统定时任务功能创建：

```csharp
// 清理任务的 Cron 表达式示例：
// "0 0 2 * * ?" - 每天凌晨2点执行
// "0 0 */6 * * ?" - 每6小时执行一次
```

### 4.5 清理任务内容

定期清理任务执行以下操作：

1. **清理过期工作区目录**
   - 删除超过 `WorkspaceExpirationHours` 未访问的工作区

2. **清理孤立数据**
   - 删除已结束的会话映射
   - 删除过期的授权记录

3. **更新目录访问时间**
   - 记录目录的最后访问时间

---

## 验证与回滚

### 5.1 迁移验证清单

- [ ] 数据库表结构正确创建
- [ ] 索引正确创建
- [ ] 现有数据迁移完成
- [ ] 没有数据丢失
- [ ] 应用正常启动
- [ ] 工作区功能正常
- [ ] 授权功能正常
- [ ] 日志记录正常

### 5.2 功能验证测试

```bash
# 1. 启动应用
dotnet run --project WebCodeCli

# 2. 访问 Web 界面
# 浏览器打开: http://localhost:5000

# 3. 测试创建会话
# 创建新会话，使用自定义工作目录

# 4. 测试授权功能
# 测试目录授权和撤销

# 5. 检查日志
# 确认没有错误日志
```

### 5.3 回滚方案

如果迁移出现问题，按以下步骤回滚：

```bash
# 1. 停止应用
# Ctrl+C 或停止服务

# 2. 恢复数据库备份
$backupFile = "WebCodeCli.db.backup_YYYYMMDD_HHmmss"
Copy-Item $backupFile "WebCodeCli.db" -Force

# 3. 回滚代码（如果需要）
git checkout before-workspace-migration

# 4. 重新启动应用
dotnet run --project WebCodeCli
```

---

## 监控与日志

### 6.1 日志位置

- **应用日志**: `logs/feishu-YYYYMMDD.log`
- **迁移日志**: 记录在应用日志中
- **错误日志**: 单独的错误日志条目

### 6.2 关键日志标记

迁移相关的日志标记：

```
"开始初始化工作区授权相关表..."
"工作区授权相关表初始化成功"
"开始执行工作区数据迁移..."
"工作区数据迁移完成"
"开始清理孤立数据..."
"清理完成: 删除 X 个结束映射, Y 个过期授权"
```

### 6.3 错误监控

监控以下错误类型：

- 数据库连接错误
- 表创建失败
- 数据迁移异常
- 权限检查错误

---

## 附录

### A. 手动SQL脚本（备用）

如果自动迁移失败，可以使用以下SQL脚本手动创建表：

```sql
-- WorkspaceOwner 表
CREATE TABLE WorkspaceOwner (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DirectoryPath TEXT NOT NULL,
    OwnerUsername TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE UNIQUE INDEX idx_directory_path ON WorkspaceOwner(DirectoryPath);

-- WorkspaceAuthorization 表
CREATE TABLE WorkspaceAuthorization (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DirectoryPath TEXT NOT NULL,
    AuthorizedUsername TEXT NOT NULL,
    GrantedBy TEXT NOT NULL,
    GrantedAt TEXT NOT NULL
);

CREATE UNIQUE INDEX idx_directory_user ON WorkspaceAuthorization(DirectoryPath, AuthorizedUsername);
```

### B. 联系支持

如遇到问题，请查看：
- 项目文档: `docs/`
- 架构设计: `docs/plans/`
- GitHub Issues: 提交问题报告
