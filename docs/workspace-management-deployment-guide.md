# 统一工作区管理架构部署指南

## 部署步骤

### 1. 数据库迁移

#### 1.1 备份数据库
```bash
# MySQL备份示例
mysqldump -u root -p webcode > webcode_backup_$(date +%Y%m%d).sql
```

#### 1.2 执行表结构迁移
执行SQL脚本：`docs/workspace-database-migration.sql`
```bash
mysql -u root -p webcode < docs/workspace-database-migration.sql
```

#### 1.3 数据迁移
执行PowerShell脚本迁移现有会话数据：
```powershell
# 先模拟执行，查看迁移效果
.\docs\workspace-data-migration.ps1 -ConnectionString "server=localhost;database=webcode;uid=root;pwd=your_password;" -DryRun

# 确认无误后执行实际迁移
.\docs\workspace-data-migration.ps1 -ConnectionString "server=localhost;database=webcode;uid=root;pwd=your_password;"
```

### 2. 应用配置

#### 2.1 注册后台服务
在 `Program.cs` 中添加后台清理服务：
```csharp
// 工作区清理后台服务
builder.Services.AddHostedService<WorkspaceCleanupBackgroundService>();
```

#### 2.2 权限配置
确保以下服务已正确注册（通常会被自动扫描）：
- WorkspaceRegistryService
- WorkspaceAuthorizationService
- SessionDirectoryService
- WorkspaceCleanupBackgroundService

### 3. 验证部署

#### 3.1 功能验证
1. Web端测试：
   - 新建会话时可以看到目录选择选项
   - 会话列表显示目录信息
   - 可以切换会话的工作目录
   - 目录授权功能正常工作

2. 飞书端测试：
   - 新建会话时可以选择已有目录
   - 会话管理卡片显示目录信息
   - 飞书端和Web端目录数据同步

#### 3.2 后台任务验证
查看日志确认清理服务正常启动：
```
信息: WebCodeCli.Domain.Domain.Service.WorkspaceCleanupBackgroundService[0]
      工作区清理后台服务已启动
信息: WebCodeCli.Domain.Domain.Service.WorkspaceCleanupBackgroundService[0]
      下一次工作区清理时间: 2026-03-13 02:00:00
```

## 运维操作

### 手动触发清理
可以通过API手动触发清理任务：
```http
POST /api/workspace/cleanup
Authorization: Bearer <token>
```

### 查看清理日志
清理任务执行后会输出详细日志：
```
信息: WebCodeCli.Domain.Domain.Service.WorkspaceCleanupBackgroundService[0]
      === 开始执行工作区清理任务 ===
信息: WebCodeCli.Domain.Domain.Service.WorkspaceCleanupBackgroundService[0]
      1. 清理过期目录授权...
信息: WebCodeCli.Domain.Domain.Service.WorkspaceCleanupBackgroundService[0]
      ✅ 已清理过期授权记录数: 5
信息: WebCodeCli.Domain.Domain.Service.WorkspaceCleanupBackgroundService[0]
      2. 清理孤立会话目录映射...
信息: WebCodeCli.Domain.Domain.Service.WorkspaceCleanupBackgroundService[0]
      ✅ 已清理孤立映射记录数: 2
信息: WebCodeCli.Domain.Domain.Service.WorkspaceCleanupBackgroundService[0]
      3. 清理无效目录记录...
信息: WebCodeCli.Domain.Domain.Service.WorkspaceCleanupBackgroundService[0]
      ✅ 已清理无效目录记录数: 0
信息: WebCodeCli.Domain.Domain.Service.WorkspaceCleanupBackgroundService[0]
      === 工作区清理任务完成 ===
```

## 回滚方案

### 1. 数据库回滚
如果迁移出现问题，使用备份的数据库文件恢复：
```bash
mysql -u root -p webcode < webcode_backup_20260312.sql
```

### 2. 代码回滚
如果新版本出现问题，回滚到上一个版本即可，新功能不影响原有业务逻辑。

## 常见问题

### Q: 迁移后原有会话的工作区路径会变化吗？
A: 不会。迁移过程只是将现有工作区路径注册到目录表中，不会修改实际文件路径。

### Q: 飞书用户和Web用户的目录是隔离的吗？
A: 是的。目录按用户隔离，不同用户之间看不到对方的目录，除非被授权。

### Q: 清理任务会删除实际文件吗？
A: 不会。清理任务只会清理数据库中的过期记录，不会删除实际的文件目录。

### Q: 可以自定义清理时间吗？
A: 可以。修改 `WorkspaceCleanupBackgroundService.cs` 中的 `_runTime` 字段即可，默认是每日凌晨2点。
